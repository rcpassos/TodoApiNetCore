using System;
using System.Reflection;
using Stripe;

namespace TodoApi.Tests.Helpers
{
    public static class EventUtilityMock
    {
        private static Func<string, string, string, Event>? mockImplementation;

        public static void Setup(Func<string, string, string, Event> mockMethod)
        {
            mockImplementation = mockMethod;
        }

        public static void Reset()
        {
            mockImplementation = null;
        }

        public static Event ConstructEvent(string json, string signature, string secret)
        {
            if (mockImplementation != null)
            {
                return mockImplementation(json, signature, secret);
            }
            
            // Fall back to the original implementation
            return EventUtility.ConstructEvent(json, signature, secret);
        }
    }
}
