using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Stripe;
using TodoApi.Controllers;
using TodoApi.Data;
using TodoApi.Models;
using TodoApi.Services;
using TodoApi.Tests.Helpers;
using Stripe.Checkout;
using Xunit;

namespace TodoApi.Tests
{
    public class StripeControllerTests
    {
        private readonly Mock<IStripeService> _mockStripeService;
        private readonly Mock<ILogger<StripeController>> _mockLogger;
        private readonly Mock<IEmailService> _mockEmailService;
        private readonly IConfiguration _configuration;

        public StripeControllerTests()
        {
            _mockStripeService = new Mock<IStripeService>();
            _mockLogger = new Mock<ILogger<StripeController>>();
            _mockEmailService = new Mock<IEmailService>();

            var inMemorySettings = new Dictionary<string, string>
            {
                {"Stripe:WebhookSecret", "whsec_test_secret"},
                {"Stripe:SecretKey", "sk_test_key"}
            };
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();
        }

        private AppDbContext GetDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private StripeController SetupController(AppDbContext? dbContext = null)
        {
            var context = dbContext ?? GetDbContext();
            return new StripeController(
                context,
                _mockStripeService.Object,
                _configuration,
                _mockLogger.Object,
                _mockEmailService.Object);
        }

        private void SetupControllerRequest(StripeController controller, string json, string stripeSignature = "test_signature")
        {
            var httpContext = new DefaultHttpContext();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            httpContext.Request.Body = stream;
            httpContext.Request.Headers["Stripe-Signature"] = stripeSignature;
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
            
            // Mock the IStripeService to handle the EventUtility.ConstructEvent call
            if (!string.IsNullOrEmpty(stripeSignature))
            {
                // Parse the JSON to determine the event type
                string eventType = "unknown";
                string eventId = "evt_test123";
                string subscriptionId = "sub_test123";
                
                if (json.Contains("evt_invoice_succeeded") || json.Contains("invoice.payment_succeeded"))
                    eventType = "invoice.payment_succeeded";
                else if (json.Contains("evt_invoice_failed") || json.Contains("invoice.payment_failed"))
                    eventType = "invoice.payment_failed";
                else if (json.Contains("evt_sub_created") || json.Contains("customer.subscription.created"))
                    eventType = "customer.subscription.created";
                else if (json.Contains("evt_sub_updated") || json.Contains("customer.subscription.updated"))
                    eventType = "customer.subscription.updated";
                else if (json.Contains("evt_sub_deleted") || json.Contains("customer.subscription.deleted"))
                    eventType = "customer.subscription.deleted";
                else if (json.Contains("evt_duplicate"))
                    eventId = "evt_duplicate";
                
                // Create the appropriate object based on the event type
                IHasObject stripeObject;
                if (eventType.StartsWith("invoice"))
                {
                    // For invoice events
                    var invoice = new Invoice { Id = "in_test123" };
                    if (eventType != "invoice.payment_succeeded" && json.Contains("in_test123") && !json.Contains("without_subscription"))
                    {
                        invoice.SubscriptionId = subscriptionId;
                    }
                    stripeObject = invoice;
                }
                else if (eventType.StartsWith("customer.subscription"))
                {
                    // For subscription events
                    stripeObject = new Subscription
                    {
                        Id = subscriptionId,
                        CustomerId = "cus_test123",
                        Status = eventType.Contains("deleted") ? "canceled" : "active"
                    };
                }
                else
                {
                    // Default to an invoice for unknown events
                    stripeObject = new Invoice { Id = "in_test123" };
                }
                
                // Create the event
                var mockEvent = new Event
                {
                    Id = eventId,
                    Type = eventType,
                    Data = new EventData
                    {
                        Object = stripeObject
                    }
                };
                
                _mockStripeService.Setup(s => s.ConstructEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(mockEvent);
            }
        }

        [Fact]
        public async Task Webhook_MissingSignature_ReturnsBadRequest()
        {
            // Arrange
            var controller = SetupController();
            var json = "{\"id\":\"evt_test123\"}";
            SetupControllerRequest(controller, json, ""); // Empty signature

            // Act
            var result = await controller.Webhook();

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("Missing Stripe signature", badRequestResult.Value.ToString());
            _mockLogger.Verify(l => l.Log(
                It.Is<LogLevel>(ll => ll == LogLevel.Warning),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Missing Stripe signature")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), 
                Times.Once);
        }

        [Fact]
        public async Task Webhook_MissingWebhookSecret_ReturnsServerError()
        {
            // Arrange
            var emptyConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> { { "Stripe:SecretKey", "sk_test" } })
                .Build();

            var context = GetDbContext();
            var controller = new StripeController(
                context,
                _mockStripeService.Object,
                emptyConfig,
                _mockLogger.Object,
                _mockEmailService.Object);

            SetupControllerRequest(controller, "{\"id\":\"evt_test123\"}");

            // Act
            var result = await controller.Webhook();

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
            Assert.Contains("Webhook configuration error", statusCodeResult.Value.ToString());
            _mockLogger.Verify(l => l.Log(
                It.Is<LogLevel>(ll => ll == LogLevel.Error),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("not configured")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), 
                Times.Once);
        }

        [Fact]
        public async Task Webhook_GeneralExceptionThrown_ReturnsServerError()
        {
            // Arrange
            var controller = SetupController();
            var json = "{\"id\":\"evt_test123\"}";
            SetupControllerRequest(controller, json);
            
            // Mock the StripeService to throw a general exception
            _mockStripeService.Setup(s => s.ConstructEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new Exception("Something went wrong"));
                
            // Act
            var result = await controller.Webhook();
            
            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
            Assert.Contains("Internal server error", statusCodeResult.Value.ToString());
            _mockLogger.Verify(l => l.Log(
                It.Is<LogLevel>(ll => ll == LogLevel.Error),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error processing webhook")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), 
                Times.Once);
        }

        [Fact]
        public async Task Webhook_StripeSignatureValidationFails_ReturnsBadRequest()
        {
            // Arrange
            var controller = SetupController();
            var json = "{\"id\":\"evt_test123\"}";
            SetupControllerRequest(controller, json, "invalid_signature");
            
            // Mock the StripeService to throw a StripeException
            _mockStripeService.Setup(s => s.ConstructEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new StripeException("Invalid signature"));
                
            // Act
            var result = await controller.Webhook();
            
            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("Invalid signature", badRequestResult.Value.ToString());
            _mockLogger.Verify(l => l.Log(
                It.Is<LogLevel>(ll => ll == LogLevel.Error),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Stripe error")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), 
                Times.Once);
        }

        [Fact]
        public async Task Webhook_SkipsProcessing_WhenEventAlreadyProcessed()
        {
            // Arrange
            var context = GetDbContext();
            
            // Add a processed event to the database
            var eventId = "evt_duplicate";
            context.WebhookEvents.Add(new WebhookEvent
            {
                EventId = eventId,
                ProcessedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            
            var controller = SetupController(context);
            var json = $"{{\"id\":\"{eventId}\", \"type\":\"customer.subscription.updated\"}}";
            
            // Create a custom setup for this test that doesn't call the service methods
            var httpContext = new DefaultHttpContext();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            httpContext.Request.Body = stream;
            httpContext.Request.Headers["Stripe-Signature"] = "test_signature";
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
            
            // Setup the mock service to return an event with the duplicate ID
            _mockStripeService.Setup(s => s.ConstructEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new Event
                {
                    Id = eventId,
                    Type = "customer.subscription.updated"
                });
            
            // Act
            var result = await controller.Webhook();
            
            // Assert
            Assert.IsType<OkResult>(result);
            // Verify that the service method was not called
            _mockStripeService.Verify(s => s.HandleSubscriptionUpdatedAsync(It.IsAny<Subscription>()), Times.Never);
            _mockLogger.Verify(l => l.Log(
                It.Is<LogLevel>(ll => ll == LogLevel.Information),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("already processed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), 
                Times.Once);
        }

        [Fact]
        public async Task Webhook_ProcessesSubscriptionEvent_CallsHandleSubscriptionUpdatedAsync()
        {
            // Arrange
            var controller = SetupController();
            var json = "{\"id\":\"evt_sub_created\",\"type\":\"customer.subscription.created\"}";
            SetupControllerRequest(controller, json);
            
            // Create a mock subscription object
            var subscription = StripeTestHelpers.CreateMockSubscription("sub_test123", "cus_test123");
            
            // Mock the EventUtility to return our test event
            try
            {
                // Use the EventUtilityMock helper to mock the static method
                var mockEvent = StripeTestHelpers.CreateMockEvent("customer.subscription.created", subscription);
                EventUtilityMock.Setup((json, signature, secret) => mockEvent);
                
                // Setup the mock service to verify the call
                _mockStripeService.Setup(s => s.HandleSubscriptionUpdatedAsync(It.IsAny<Subscription>()))
                    .Returns(Task.CompletedTask)
                    .Verifiable();
                
                // Act
                // We don't need to mock _mockStripeService.ConstructEvent as it's not used in this test
                // The controller directly uses EventUtility.ConstructEvent
                var result = await controller.Webhook();
                
                // Assert
                Assert.IsType<OkResult>(result);
                _mockStripeService.Verify(s => s.HandleSubscriptionUpdatedAsync(It.Is<Subscription>(s => s.Id == "sub_test123")), Times.Once);
            }
            finally
            {
                // Reset the EventUtilityMock to its original behavior
                EventUtilityMock.Reset();
            }
        }

        [Fact]
        public async Task Webhook_ProcessesSubscriptionDeletedEvent_CallsHandleSubscriptionCanceledAsync()
        {
            // Arrange
            var controller = SetupController();
            var json = "{\"id\":\"evt_sub_deleted\",\"type\":\"customer.subscription.deleted\"}";
            SetupControllerRequest(controller, json);
            
            // Setup the mock service to verify the call
            _mockStripeService.Setup(s => s.HandleSubscriptionCanceledAsync(It.IsAny<Subscription>()))
                .Returns(Task.CompletedTask)
                .Verifiable();
            
            // Act
            var result = await controller.Webhook();
            
            // Assert
            Assert.IsType<OkResult>(result);
            _mockStripeService.Verify(s => s.HandleSubscriptionCanceledAsync(It.Is<Subscription>(s => s.Id == "sub_test123")), Times.Once);
        }

        [Fact]
        public async Task Webhook_ProcessesInvoicePaymentSucceeded_UpdatesSubscription()
        {
            // Arrange
            var controller = SetupController();
            var json = "{\"id\":\"evt_invoice_succeeded\",\"type\":\"invoice.payment_succeeded\"}";
            
            // Create mock objects
            var invoice = StripeTestHelpers.CreateMockInvoice("in_test123", "sub_test123");
            var subscription = StripeTestHelpers.CreateMockSubscription("sub_test123", "cus_test123");
            
            // Create a custom HTTP context for this test
            var httpContext = new DefaultHttpContext();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            httpContext.Request.Body = stream;
            httpContext.Request.Headers["Stripe-Signature"] = "test_signature";
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
            
            // Mock the IStripeService to return our test event
            _mockStripeService.Setup(s => s.ConstructEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new Event
                {
                    Id = "evt_invoice_succeeded",
                    Type = "invoice.payment_succeeded",
                    Data = new EventData
                    {
                        Object = invoice
                    }
                });
                
            // Setup the mock service to verify the call
            _mockStripeService.Setup(s => s.HandleSubscriptionUpdatedAsync(It.IsAny<Subscription>()))
                .Returns(Task.CompletedTask)
                .Verifiable();
            
            // Mock the GetSubscriptionAsync method
            _mockStripeService.Setup(s => s.GetSubscriptionAsync(It.IsAny<string>()))
                .ReturnsAsync(subscription);
            
            // Act
            var result = await controller.Webhook();
            
            // Assert
            Assert.IsType<OkResult>(result);
            _mockStripeService.Verify(s => s.HandleSubscriptionUpdatedAsync(It.Is<Subscription>(s => s.Id == "sub_test123")), Times.Once);
        }

        [Fact]
        public async Task Webhook_HandlesInvoiceWithoutSubscription()
        {
            // Arrange
            var controller = SetupController();
            var json = "{\"id\":\"evt_invoice_no_sub\",\"type\":\"invoice.payment_succeeded\"}";
            SetupControllerRequest(controller, json);
            
            // Create mock invoice without subscription
            var invoice = StripeTestHelpers.CreateMockInvoice("in_test123", null);
            
            // Mock the EventUtility to return our test event
            try
            {
                // Create a mock event directly
                var mockEvent = StripeTestHelpers.CreateMockEvent("invoice.payment_succeeded", invoice);
                
                // Use the EventUtilityMock helper to mock the static method
                EventUtilityMock.Setup((json, signature, secret) => mockEvent);
                
                // Act
                // We don't need to mock _mockStripeService.ConstructEvent as it's not used in this test
                // The controller directly uses EventUtility.ConstructEvent
                var result = await controller.Webhook();
                
                // Assert
                Assert.IsType<OkResult>(result);
                // Verify that the service method was not called
                _mockStripeService.Verify(s => s.HandleSubscriptionUpdatedAsync(It.IsAny<Subscription>()), Times.Never);
                _mockLogger.Verify(l => l.Log(
                    It.Is<LogLevel>(ll => ll == LogLevel.Information),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("has no subscription ID")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), 
                    Times.Once);
            }
            finally
            {
                // Reset the EventUtilityMock to its original behavior
                EventUtilityMock.Reset();
            }
        }

        [Fact]
        public async Task Webhook_HandlesExceptionInSubscriptionRetrieval()
        {
            // Arrange
            var controller = SetupController();
            var json = "{\"id\":\"evt_invoice_error\",\"type\":\"invoice.payment_succeeded\"}";
            SetupControllerRequest(controller, json);
            
            // Create mock invoice with subscription
            var invoice = StripeTestHelpers.CreateMockInvoice("in_test123", "sub_test123");
            
            // Create a custom HTTP context for this test
            var httpContext = new DefaultHttpContext();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            httpContext.Request.Body = stream;
            httpContext.Request.Headers["Stripe-Signature"] = "test_signature";
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
            
            // Mock the IStripeService to return our test event
            _mockStripeService.Setup(s => s.ConstructEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new Event
                {
                    Id = "evt_invoice_error",
                    Type = "invoice.payment_succeeded",
                    Data = new EventData
                    {
                        Object = invoice
                    }
                });
                
            // Mock the GetSubscriptionAsync method to throw an exception
            _mockStripeService.Setup(s => s.GetSubscriptionAsync(It.IsAny<string>()))
                .ThrowsAsync(new StripeException("Subscription not found"));
                
            // Act
            var result = await controller.Webhook();
            
            // Assert
            Assert.IsType<OkResult>(result);
            // Verify that the service method was not called
            _mockStripeService.Verify(s => s.HandleSubscriptionUpdatedAsync(It.IsAny<Subscription>()), Times.Never);
            _mockLogger.Verify(l => l.Log(
                It.Is<LogLevel>(ll => ll == LogLevel.Error),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error updating subscription after payment success")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), 
                Times.Once);

        }

        [Fact]
        public async Task Webhook_ProcessesInvoicePaymentFailedEvent()
        {
            // Arrange
            var dbContext = GetDbContext();
            
            // Add a user with the subscription
            var user = new User
            {
                Email = "test@example.com",
                StripeSubscriptionId = "sub_test123",
                SubscriptionStatus = "active"
            };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
            
            var controller = SetupController(dbContext);
            var json = "{\"id\":\"evt_invoice_failed\",\"type\":\"invoice.payment_failed\"}";
            SetupControllerRequest(controller, json);
            
            // Create mock invoice
            var invoice = StripeTestHelpers.CreateMockInvoice("in_test123", "sub_test123");
            
            // Setup the mock event and subscription
            var mockSubscription = new Subscription
            {
                Id = "sub_test123",
                Status = "canceled", // Set to canceled to match the expected status
                CustomerId = "cus_test123"
            };
            
            // Mock the IStripeService to return our test event
            _mockStripeService.Setup(s => s.ConstructEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new Event
                {
                    Id = "evt_invoice_failed",
                    Type = "invoice.payment_failed",
                    Data = new EventData
                    {
                        Object = invoice
                    }
                });
                
            // Mock the subscription service to handle the subscription operations
            var subscriptionService = new Mock<Stripe.SubscriptionService>();
            subscriptionService.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSubscription);
                
            subscriptionService.Setup(s => s.CancelAsync(It.IsAny<string>(), It.IsAny<SubscriptionCancelOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSubscription);
                
                // Setup the email service mock
                _mockEmailService.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(Task.CompletedTask)
                    .Verifiable();
                
                // Act
                // We don't need to mock _mockStripeService.ConstructEvent as it's not used in this test
                // The controller directly uses EventUtility.ConstructEvent
                var result = await controller.Webhook();
                
                // Assert
                Assert.IsType<OkResult>(result);
                
                // Verify the user's subscription was updated
                var updatedUser = await dbContext.Users.FindAsync(user.Id);
                Assert.Equal("canceled", updatedUser.SubscriptionStatus);
                
                // Verify email was sent
                _mockEmailService.Verify(e => e.SendEmailAsync(
                    It.Is<string>(email => email == "test@example.com"),
                    It.Is<string>(subject => subject.Contains("Payment Failed")),
                    It.Is<string>(body => body.Contains("Payment Failed"))), 
                    Times.Once);
                
                // Verify logging occurred
                _mockLogger.Verify(l => l.Log(
                    It.Is<LogLevel>(ll => ll == LogLevel.Warning),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Payment failed for invoice")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), 
                    Times.Once);
        }

        [Fact]
        public async Task Webhook_HandlesUnknownEventType()
        {
            // Arrange
            var controller = SetupController();
            var json = "{\"id\":\"evt_unknown\",\"type\":\"unknown.event.type\"}";
            SetupControllerRequest(controller, json);
            
            // Create mock invoice as a placeholder object
            var invoice = StripeTestHelpers.CreateMockInvoice("unknown_event");
            
            // Mock the EventUtility to return our test event
            try
            {
                // Use the EventUtilityMock helper to mock the static method
                var mockEvent = StripeTestHelpers.CreateMockEvent("unknown.event.type", invoice);
                EventUtilityMock.Setup((json, signature, secret) => mockEvent);
                
                // Act
                // We don't need to mock _mockStripeService.ConstructEvent as it's not used in this test
                // The controller directly uses EventUtility.ConstructEvent
                var result = await controller.Webhook();
                
                // Assert
                Assert.IsType<OkResult>(result);
                _mockLogger.Verify(l => l.Log(
                    It.Is<LogLevel>(ll => ll == LogLevel.Information),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Unhandled event type")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), 
                    Times.Once);
            }
            finally
            {
                // Reset the EventUtilityMock to its original behavior
                EventUtilityMock.Reset();
            }
        }

        [Fact]
        public async Task Webhook_HandlesInvalidSubscriptionObject()
        {
            // Arrange
            var controller = SetupController();
            var json = "{\"id\":\"evt_invalid_sub\",\"type\":\"customer.subscription.updated\"}";
            SetupControllerRequest(controller, json);
            
            // Create mock invoice as a non-subscription object
            var invoice = StripeTestHelpers.CreateMockInvoice("not_a_subscription");
            
            // Create a custom HTTP context for this test
            var httpContext = new DefaultHttpContext();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            httpContext.Request.Body = stream;
            httpContext.Request.Headers["Stripe-Signature"] = "test_signature";
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
            
            // Mock the IStripeService to return our test event
            _mockStripeService.Setup(s => s.ConstructEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new Event
                {
                    Id = "evt_invalid_sub",
                    Type = "customer.subscription.updated",
                    Data = new EventData
                    {
                        Object = invoice
                    }
                });
            
            // Act
            var result = await controller.Webhook();
            
            // Assert
            Assert.IsType<OkResult>(result);
            // Verify that the service method was not called
            _mockStripeService.Verify(s => s.HandleSubscriptionUpdatedAsync(It.IsAny<Subscription>()), Times.Never);
            _mockLogger.Verify(l => l.Log(
                It.Is<LogLevel>(ll => ll == LogLevel.Warning),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Could not parse subscription from event")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), 
                Times.Once);
        }
    }
}
