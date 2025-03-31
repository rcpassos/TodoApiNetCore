using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using TodoApi.Controllers;
using TodoApi.Data;
using TodoApi.DTOs;
using TodoApi.Models;
using TodoApi.Services;
using Xunit;

namespace TodoApi.Tests
{
  public class AuthControllerTests
  {
    private AppDbContext GetDbContext()
    {
      var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;
      return new AppDbContext(options);
    }

    [Fact]
    public async Task Register_ReturnsOk_WhenRegistrationIsSuccessful()
    {
      // Arrange
      var context = GetDbContext();
      var inMemorySettings = new Dictionary<string, string>
            {
                {"JwtSettings:Issuer", "https://test.com"},
                {"JwtSettings:Audience", "https://test.com"},
                {"JwtSettings:SecretKey", "01234567890123456789012345678901"}
            };
      IConfiguration configuration = new ConfigurationBuilder()
          .AddInMemoryCollection(inMemorySettings)
          .Build();

      var emailServiceMock = new Mock<IEmailService>();
      var controller = new AuthController(context, configuration, emailServiceMock.Object);
      var request = new RegisterRequest { Email = "test@example.com", Password = "Test@123" };

      // Act
      var result = await controller.Register(request);

      // Assert
      Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Login_ReturnsTokenAndUserInfo_WhenCredentialsAreValid()
    {
      // Arrange
      var context = GetDbContext();
      var user = new User
      {
        Email = "test@example.com",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test@123"),
        Role = "User",
        StripeCustomerId = "cus_test456",
        StripeSubscriptionId = "sub_test456",
        SubscriptionStatus = "active",
        SubscriptionEndDate = DateTime.UtcNow.AddDays(30)
      };
      context.Users.Add(user);
      await context.SaveChangesAsync();

      var inMemorySettings = new Dictionary<string, string>
            {
                {"JwtSettings:Issuer", "https://test.com"},
                {"JwtSettings:Audience", "https://test.com"},
                {"JwtSettings:SecretKey", "01234567890123456789012345678901"}
            };
      IConfiguration configuration = new ConfigurationBuilder()
          .AddInMemoryCollection(inMemorySettings)
          .Build();

      var emailServiceMock = new Mock<IEmailService>();
      var controller = new AuthController(context, configuration, emailServiceMock.Object);
      var request = new LoginRequest { Email = "test@example.com", Password = "Test@123" };

      // Act
      var result = await controller.Login(request);

      // Assert
      var okResult = Assert.IsType<OkObjectResult>(result);
      var responseObj = okResult.Value;
      
      // Convert to dictionary to access properties
      var responseDict = responseObj.GetType().GetProperties()
          .ToDictionary(p => p.Name, p => p.GetValue(responseObj));
      
      // Verify token exists
      Assert.True(responseDict.ContainsKey("Token"));
      Assert.NotNull(responseDict["Token"]);
      
      // Verify user info exists and has correct properties
      Assert.True(responseDict.ContainsKey("User"));
      Assert.NotNull(responseDict["User"]);
      var userResponse = Assert.IsType<UserResponse>(responseDict["User"]);
      Assert.Equal(user.Email, userResponse.Email);
      Assert.Equal(user.Role, userResponse.Role);
      Assert.Equal(user.StripeCustomerId, userResponse.StripeCustomerId);
      Assert.Equal(user.StripeSubscriptionId, userResponse.StripeSubscriptionId);
      Assert.Equal(user.SubscriptionStatus, userResponse.SubscriptionStatus);
      Assert.Equal(user.SubscriptionEndDate, userResponse.SubscriptionEndDate);
    }
    
    [Fact]
    public async Task GetCurrentUser_ReturnsUserInfo_WhenUserIsAuthenticated()
    {
      // Arrange
      var context = GetDbContext();
      
      // Create a test user with subscription information
      var user = new User
      {
        Id = 1,
        Email = "test@example.com",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test@123"),
        Role = "User",
        StripeCustomerId = "cus_test123",
        StripeSubscriptionId = "sub_test123",
        SubscriptionStatus = "active",
        SubscriptionEndDate = DateTime.UtcNow.AddDays(30)
      };
      context.Users.Add(user);
      await context.SaveChangesAsync();

      var inMemorySettings = new Dictionary<string, string>
      {
          {"JwtSettings:Issuer", "https://test.com"},
          {"JwtSettings:Audience", "https://test.com"},
          {"JwtSettings:SecretKey", "01234567890123456789012345678901"}
      };
      IConfiguration configuration = new ConfigurationBuilder()
          .AddInMemoryCollection(inMemorySettings)
          .Build();

      var emailServiceMock = new Mock<IEmailService>();
      var controller = new AuthController(context, configuration, emailServiceMock.Object);
      
      // Mock the HttpContext with claims to simulate an authenticated user
      var claims = new List<Claim>
      {
          new Claim(ClaimTypes.NameIdentifier, "1"),
          new Claim(ClaimTypes.Name, "test@example.com"),
          new Claim(ClaimTypes.Role, "User")
      };
      var identity = new ClaimsIdentity(claims, "Test");
      var claimsPrincipal = new ClaimsPrincipal(identity);
      
      controller.ControllerContext = new ControllerContext
      {
          HttpContext = new DefaultHttpContext { User = claimsPrincipal }
      };

      // Act
      var result = await controller.GetCurrentUser();

      // Assert
      var okResult = Assert.IsType<OkObjectResult>(result);
      var userResponse = Assert.IsType<UserResponse>(okResult.Value);
      
      Assert.Equal(1, userResponse.Id);
      Assert.Equal("test@example.com", userResponse.Email);
      Assert.Equal("User", userResponse.Role);
      Assert.Equal("cus_test123", userResponse.StripeCustomerId);
      Assert.Equal("sub_test123", userResponse.StripeSubscriptionId);
      Assert.Equal("active", userResponse.SubscriptionStatus);
      Assert.NotNull(userResponse.SubscriptionEndDate);
    }
  }
}
