using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using TodoApi.Data;
using TodoApi.DTOs;
using TodoApi.Services;
using Microsoft.EntityFrameworkCore;
using TodoApi.Models;

namespace TodoApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StripeController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IStripeService _stripeService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StripeController> _logger;
        private readonly IEmailService _emailService;

        public StripeController(
            AppDbContext context,
            IStripeService stripeService,
            IConfiguration configuration,
            ILogger<StripeController> logger,
            IEmailService emailService)
        {
            _context = context;
            _stripeService = stripeService;
            _configuration = configuration;
            _logger = logger;
            _emailService = emailService;
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            string endpointSecret = _configuration["Stripe:WebhookSecret"];

            if (string.IsNullOrEmpty(endpointSecret))
            {
                _logger.LogError("Stripe webhook secret is not configured");
                return StatusCode(500, new { Error = "Webhook configuration error" });
            }

            try
            {
                // Verify the webhook signature
                var stripeSignature = Request.Headers["Stripe-Signature"];
                if (string.IsNullOrEmpty(stripeSignature))
                {
                    _logger.LogWarning("Missing Stripe signature header");
                    return BadRequest(new { Error = "Missing Stripe signature" });
                }

                // Use the IStripeService to construct the event
                var stripeEvent = _stripeService.ConstructEvent(
                    json,
                    stripeSignature,
                    endpointSecret
                );

                // Idempotency check - prevent duplicate event processing
                if (await EventAlreadyProcessed(stripeEvent.Id))
                {
                    _logger.LogInformation($"Event {stripeEvent.Id} already processed, skipping");
                    return Ok();
                }

                // Process the event
                await ProcessStripeEvent(stripeEvent);

                // Record that we've processed this event
                await RecordProcessedEvent(stripeEvent.Id);

                return Ok();
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error occurred");
                return BadRequest(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook");
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        private async Task ProcessStripeEvent(Event stripeEvent)
        {
            _logger.LogInformation($"Processing Stripe event: {stripeEvent.Type}");

            switch (stripeEvent.Type)
            {
                case "customer.subscription.created":
                case "customer.subscription.updated":
                    await HandleSubscriptionEvent(stripeEvent, _stripeService.HandleSubscriptionUpdatedAsync);
                    break;

                case "customer.subscription.deleted":
                    await HandleSubscriptionEvent(stripeEvent, _stripeService.HandleSubscriptionCanceledAsync);
                    break;

                case "invoice.payment_succeeded":
                    await HandleInvoicePaymentSucceeded(stripeEvent);
                    break;

                case "invoice.payment_failed":
                    await HandleInvoicePaymentFailed(stripeEvent);
                    break;

                default:
                    _logger.LogInformation($"Unhandled event type: {stripeEvent.Type}");
                    break;
            }
        }

        private async Task HandleSubscriptionEvent(Event stripeEvent, Func<Subscription, Task> handler)
        {
            var subscription = stripeEvent.Data.Object as Subscription;
            if (subscription != null)
            {
                await handler(subscription);
            }
            else
            {
                _logger.LogWarning($"Could not parse subscription from event {stripeEvent.Id}");
            }
        }

        private async Task HandleInvoicePaymentSucceeded(Event stripeEvent)
        {
            var invoice = stripeEvent.Data.Object as Invoice;
            if (invoice?.SubscriptionId != null)
            {
                try
                {
                    // Use the IStripeService to get the subscription instead of creating a new SubscriptionService
                    var subscription = await _stripeService.GetSubscriptionAsync(invoice.SubscriptionId);
                    await _stripeService.HandleSubscriptionUpdatedAsync(subscription);
                    _logger.LogInformation($"Updated subscription {subscription.Id} after successful payment");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error updating subscription after payment success: {invoice.SubscriptionId}");
                }
            }
            else
            {
                _logger.LogInformation($"Invoice {invoice?.Id} has no subscription ID");
            }
        }

        private async Task HandleInvoicePaymentFailed(Event stripeEvent)
        {
            var invoice = stripeEvent.Data.Object as Invoice;
            if (invoice != null && !string.IsNullOrEmpty(invoice.SubscriptionId))
            {
                _logger.LogWarning($"Payment failed for invoice: {invoice.Id}, subscription: {invoice.SubscriptionId}");
                
                try
                {
                    // Find the user associated with this subscription
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.StripeSubscriptionId == invoice.SubscriptionId);
                    
                    if (user != null)
                    {
                        // Update subscription status to canceled
                        user.SubscriptionStatus = "canceled";
                        user.SubscriptionEndDate = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                        
                        // Send email notification to the user
                        string subject = "Payment Failed - Subscription Canceled";
                        string body = $@"<html>
<body>
<h2>Payment Failed</h2>
<p>Dear {user.Email},</p>
<p>We were unable to process your payment for subscription: {invoice.SubscriptionId}.</p>
<p>As a result, your subscription has been canceled. To restore your subscription, please update your payment method and subscribe again.</p>
<p>If you have any questions, please contact our support team.</p>
<p>Thank you,<br>The Todo App Team</p>
</body>
</html>";
                        
                        await _emailService.SendEmailAsync(user.Email, subject, body);
                        
                        _logger.LogInformation($"User {user.Id} notified about payment failure and subscription canceled");
                    }
                    else
                    {
                        _logger.LogWarning($"User not found for subscription {invoice.SubscriptionId}");
                    }
                    
                    // Also cancel the subscription in Stripe if it's not already canceled
                    try
                    {
                        var subscriptionService = new SubscriptionService();
                        var subscription = await subscriptionService.GetAsync(invoice.SubscriptionId);
                        
                        if (subscription.Status != "canceled")
                        {
                            await subscriptionService.CancelAsync(invoice.SubscriptionId, new SubscriptionCancelOptions
                            {
                                InvoiceNow = false,
                                Prorate = false
                            });
                            _logger.LogInformation($"Subscription {invoice.SubscriptionId} canceled in Stripe");
                        }
                    }
                    catch (StripeException ex)
                    {
                        _logger.LogError(ex, $"Error canceling subscription {invoice.SubscriptionId} in Stripe");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing payment failure for invoice {invoice.Id}");
                }
            }
            else
            {
                _logger.LogWarning($"Invoice payment failed but no subscription ID found. Invoice ID: {invoice?.Id}");
            }
        }

        private async Task<bool> EventAlreadyProcessed(string eventId)
        {
            // Check if we've already processed this event
            return await _context.WebhookEvents.AnyAsync(e => e.EventId == eventId);
        }

        private async Task RecordProcessedEvent(string eventId)
        {
            // Record that we've processed this event
            _context.WebhookEvents.Add(new WebhookEvent 
            { 
                EventId = eventId,
                ProcessedAt = DateTime.UtcNow
            });
            
            await _context.SaveChangesAsync();
        }
    }
}
