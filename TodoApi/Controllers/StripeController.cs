using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout; // Add this for Events class
using TodoApi.Data;
using TodoApi.DTOs;
using TodoApi.Services;
using Microsoft.EntityFrameworkCore;
using TodoApi.Models; // Add this for WebhookEvent model

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

        public StripeController(
            AppDbContext context,
            IStripeService stripeService,
            IConfiguration configuration,
            ILogger<StripeController> logger)
        {
            _context = context;
            _stripeService = stripeService;
            _configuration = configuration;
            _logger = logger;
        }

        [Authorize]
        [HttpPost("subscribe")]
        public async Task<IActionResult> Subscribe(SubscribeRequest request)
        {
            // Get the current user
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return Unauthorized();

            try
            {
                // Get or create the Stripe customer
                var customer = await _stripeService.GetOrCreateCustomerAsync(user);

                // Create a subscription for the customer
                var (subscription, clientSecret) = await _stripeService.CreateSubscriptionAsync(customer.Id, request.PriceId);

                if (subscription.Status == "incomplete" && !string.IsNullOrEmpty(clientSecret))
                {
                    return Ok(new { RequiresAction = true, PaymentIntentClientSecret = clientSecret });
                }

                // Update user subscription data immediately if active
                if (subscription.Status == "active" || subscription.Status == "trialing")
                {
                    await _stripeService.HandleSubscriptionUpdatedAsync(subscription);
                }

                return Ok(new { Message = "Subscription created", SubscriptionId = subscription.Id });
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error occurred");
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            string endpointSecret = _configuration["Stripe:WebhookSecret"];

            try
            {
                // Verify the webhook signature
                var stripeEvent = EventUtility.ConstructEvent(
                    json,
                    Request.Headers["Stripe-Signature"],
                    endpointSecret
                );

                // Idempotency check - prevent duplicate event processing
                if (await EventAlreadyProcessed(stripeEvent.Id))
                {
                    return Ok();
                }

                // Handle the event based on its type
                switch (stripeEvent.Type)
                {
                    case Events.CustomerSubscriptionCreated:
                    case Events.CustomerSubscriptionUpdated:
                        var subscription = stripeEvent.Data.Object as Subscription;
                        if (subscription != null)
                            await _stripeService.HandleSubscriptionUpdatedAsync(subscription);
                        break;

                    case Events.CustomerSubscriptionDeleted:
                        var canceledSubscription = stripeEvent.Data.Object as Subscription;
                        if (canceledSubscription != null)
                            await _stripeService.HandleSubscriptionCanceledAsync(canceledSubscription);
                        break;

                    case Events.InvoicePaymentSucceeded:
                        // Handle successful payment, maybe extend subscription
                        var invoice = stripeEvent.Data.Object as Invoice;
                        if (invoice?.Subscription != null)
                        {
                            var subscriptionId = invoice.SubscriptionId; // Use the proper property
                            
                            if (!string.IsNullOrEmpty(subscriptionId))
                            {
                                var subscriptionService = new SubscriptionService();
                                var updatedSubscription = await subscriptionService.GetAsync(subscriptionId);
                                await _stripeService.HandleSubscriptionUpdatedAsync(updatedSubscription);
                            }
                        }
                        break;

                    case Events.InvoicePaymentFailed:
                        // Handle failed payment
                        _logger.LogWarning($"Payment failed for invoice: {((Invoice)stripeEvent.Data.Object).Id}");
                        break;

                    default:
                        _logger.LogInformation($"Unhandled event type: {stripeEvent.Type}");
                        break;
                }

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
                return StatusCode(500);
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
