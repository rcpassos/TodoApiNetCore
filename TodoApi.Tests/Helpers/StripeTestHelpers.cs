using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Stripe;

using System.Threading;

namespace TodoApi.Tests.Helpers
{
    public static class StripeTestHelpers
    {
        // Helper to create a mock HTTP context with Stripe signature
        public static HttpContext CreateMockHttpContext(string json, string stripeSignature)
        {
            var httpContext = new DefaultHttpContext();
            var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            httpContext.Request.Body = memoryStream;
            httpContext.Request.Headers["Stripe-Signature"] = stripeSignature;
            return httpContext;
        }
        
        // Helper to create a mock Event for testing
        public static Event CreateMockEvent(string eventType, IHasObject stripeObject)
        {
            return new Event
            {
                Id = $"evt_{Guid.NewGuid().ToString("N")}",
                Type = eventType,
                Data = new EventData
                {
                    Object = stripeObject
                }
            };
        }
        
        // Helper to create a mock Subscription
        public static Subscription CreateMockSubscription(string? id = null, string? customerId = null, string status = "active")
        {
            return new Subscription
            {
                Id = id ?? $"sub_{Guid.NewGuid().ToString("N")}",
                CustomerId = customerId ?? $"cus_{Guid.NewGuid().ToString("N")}",
                Status = status
            };
        }
        
        // Helper to create a mock Invoice
        public static Invoice CreateMockInvoice(string? id = null, string? subscriptionId = null)
        {
            return new Invoice
            {
                Id = id ?? $"in_{Guid.NewGuid().ToString("N")}",
                SubscriptionId = subscriptionId
            };
        }
    }
    
    // Mock extension for SubscriptionService to allow testing without actual Stripe API calls
    public static class SubscriptionService
    {
        public static Func<string, SubscriptionGetOptions, RequestOptions, CancellationToken, Task<Subscription>> GetAsync { get; set; } = 
            (id, options, requestOptions, cancellationToken) => Task.FromResult(new Subscription { Id = id });
            
        public static Func<string, SubscriptionCancelOptions, RequestOptions, CancellationToken, Task<Subscription>> CancelAsync { get; set; } = 
            (id, options, requestOptions, cancellationToken) => Task.FromResult(new Subscription { Id = id, Status = "canceled" });
    }
}
