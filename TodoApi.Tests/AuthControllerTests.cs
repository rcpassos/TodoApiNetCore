using System.Collections.Generic;
using System.Threading.Tasks;
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
    public async Task Login_ReturnsToken_WhenCredentialsAreValid()
    {
      // Arrange
      var context = GetDbContext();
      var user = new User
      {
        Email = "test@example.com",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test@123"),
        Role = "User"
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
      Assert.NotNull(okResult.Value);
    }
  }
}
