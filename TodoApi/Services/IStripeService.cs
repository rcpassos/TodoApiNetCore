using System.Threading.Tasks;
using Stripe;

namespace TodoApi.Services
{
    public interface IStripeService
    {
        Task HandleSubscriptionUpdatedAsync(Subscription subscription);
        Task HandleSubscriptionCanceledAsync(Subscription subscription);
        Event ConstructEvent(string json, string signature, string secret);
        Task<Subscription> GetSubscriptionAsync(string subscriptionId);
    }
}
