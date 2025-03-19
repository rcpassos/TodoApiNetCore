using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using TodoApi.Data;
using TodoApi.DTOs;

namespace TodoApi.Controllers
{
  [Authorize]
  [ApiController]
  [Route("api/[controller]")]
  public class StripeController(AppDbContext context) : ControllerBase
  {
    private readonly AppDbContext _context = context;

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe(SubscribeRequest request)
    {
      // Get the current user
      var userId = int.Parse(User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value!);
      var user = await _context.Users.FindAsync(userId);
      if (user == null) return Unauthorized();

      // Set Stripe API key (ideally, get from configuration)
      StripeConfiguration.ApiKey = "your_stripe_secret_key";

      // Create a Stripe customer if not exists
      var customerService = new CustomerService();
      Customer customer;
      if (string.IsNullOrEmpty(user.StripeCustomerId))
      {
        var customerOptions = new CustomerCreateOptions { Email = user.Email };
        customer = await customerService.CreateAsync(customerOptions);
        user.StripeCustomerId = customer.Id;
        await _context.SaveChangesAsync();
      }
      else
      {
        customer = await customerService.GetAsync(user.StripeCustomerId);
      }

      // Create a subscription for the customer
      var subscriptionOptions = new SubscriptionCreateOptions
      {
        Customer = customer.Id,
        Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions { Price = request.PriceId }
                },
        PaymentBehavior = "default_incomplete",
        Expand = new List<string> { "latest_invoice.payment_intent" }
      };
      var subscriptionService = new SubscriptionService();
      Subscription subscription = await subscriptionService.CreateAsync(subscriptionOptions);

      if (subscription.Status == "incomplete")
      {
        var paymentIntent = subscription.LatestInvoice?.PaymentIntent;
        return Ok(new { RequiresAction = true, PaymentIntentClientSecret = paymentIntent?.ClientSecret });
      }

      return Ok(new { Message = "Subscription created", SubscriptionId = subscription.Id });
    }
  }
}
