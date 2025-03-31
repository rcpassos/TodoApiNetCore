using Microsoft.EntityFrameworkCore;
using Stripe;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Services
{
    public class StripeService : IStripeService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StripeService> _logger;

        public StripeService(
            AppDbContext context,
            IConfiguration configuration,
            ILogger<StripeService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
            
            // Configure Stripe with the API key from configuration
            StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
        }

        public async Task<Customer> GetOrCreateCustomerAsync(User user)
        {
            if (string.IsNullOrEmpty(user.StripeCustomerId))
            {
                // Create a new customer
                var customerOptions = new CustomerCreateOptions { Email = user.Email };
                var customerService = new CustomerService();
                var customer = await customerService.CreateAsync(customerOptions);
                
                user.StripeCustomerId = customer.Id;
                await _context.SaveChangesAsync();
                return customer;
            }
            else
            {
                // Get existing customer
                var customerService = new CustomerService();
                return await customerService.GetAsync(user.StripeCustomerId);
            }
        }

        public async Task<(Subscription, string?)> CreateSubscriptionAsync(string customerId, string priceId)
        {
            var subscriptionOptions = new SubscriptionCreateOptions
            {
                Customer = customerId,
                Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions { Price = priceId }
                },
                PaymentBehavior = "default_incomplete",
                Expand = new List<string> { "latest_invoice.payment_intent" }
            };

            var subscriptionService = new SubscriptionService();
            var subscription = await subscriptionService.CreateAsync(subscriptionOptions);
            
            string? clientSecret = null;
            if (subscription.Status == "incomplete" && 
                subscription.LatestInvoice?.PaymentIntent != null)
            {
                clientSecret = subscription.LatestInvoice.PaymentIntent.ClientSecret;
            }
            
            return (subscription, clientSecret);
        }

        public async Task HandleSubscriptionUpdatedAsync(Subscription subscription)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == subscription.CustomerId);
            if (user == null)
            {
                _logger.LogWarning($"User not found for Stripe customer {subscription.CustomerId}");
                return;
            }

            user.StripeSubscriptionId = subscription.Id;
            user.SubscriptionStatus = subscription.Status;
            user.SubscriptionEndDate = subscription.CurrentPeriodEnd;
            
            await _context.SaveChangesAsync();
            _logger.LogInformation($"Updated subscription status to {subscription.Status} for user {user.Id}");
        }

        public async Task HandleSubscriptionCanceledAsync(Subscription subscription)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == subscription.CustomerId);
            if (user == null)
            {
                _logger.LogWarning($"User not found for Stripe customer {subscription.CustomerId}");
                return;
            }

            user.SubscriptionStatus = subscription.Status;
            user.SubscriptionEndDate = subscription.CanceledAt;
            
            await _context.SaveChangesAsync();
            _logger.LogInformation($"Marked subscription as canceled for user {user.Id}");
        }
        
        public Event ConstructEvent(string json, string signature, string secret)
        {
            // Use the Stripe EventUtility to construct the event
            return EventUtility.ConstructEvent(json, signature, secret);
        }
        
        public async Task<Subscription> GetSubscriptionAsync(string subscriptionId)
        {
            // Use the Stripe SubscriptionService to get the subscription
            var subscriptionService = new SubscriptionService();
            return await subscriptionService.GetAsync(subscriptionId);
        }
    }

    // Moving IStripeService to its own file
}