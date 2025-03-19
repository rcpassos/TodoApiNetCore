using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TodoApi.Data;
using TodoApi.DTOs;
using TodoApi.Models;
using TodoApi.Services;

namespace TodoApi.Controllers
{
  [ApiController]
  [Route("api/[controller]")]
  public class AuthController : ControllerBase
  {
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;

    public AuthController(AppDbContext context, IConfiguration configuration, IEmailService emailService)
    {
      _context = context;
      _configuration = configuration;
      _emailService = emailService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
      if (await _context.Users.AnyAsync(u => u.Email == request.Email))
        return BadRequest("Email already exists.");

      var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
      var user = new User
      {
        Email = request.Email,
        PasswordHash = hashedPassword,
        Role = "User"
      };

      _context.Users.Add(user);
      await _context.SaveChangesAsync();
      return Ok("Registration successful.");
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
      var user = await _context.Users.SingleOrDefaultAsync(u => u.Email == request.Email);
      if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        return Unauthorized("Invalid credentials.");

      var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Email),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Role, user.Role)
            };

      var jwtSettings = _configuration.GetSection("JwtSettings");
      var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("Missing JWT configuration 'SecretKey'");
      var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
      var token = new JwtSecurityToken(
          issuer: jwtSettings["Issuer"],
          audience: jwtSettings["Audience"],
          expires: DateTime.UtcNow.AddHours(1),
          claims: authClaims,
          signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
      );

      return Ok(new { Token = new JwtSecurityTokenHandler().WriteToken(token) });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
      var user = await _context.Users.SingleOrDefaultAsync(u => u.Email == request.Email);
      if (user == null)
        return Ok(); // Do not reveal that the email does not exist

      user.PasswordResetToken = Guid.NewGuid().ToString();
      user.ResetTokenExpires = DateTime.UtcNow.AddHours(1);
      await _context.SaveChangesAsync();

      var resetLink = $"{Request.Scheme}://{Request.Host}/api/auth/reset-password?token={user.PasswordResetToken}";
      var subject = "Password Reset Request";
      var body = $"Please reset your password by clicking the following link: {resetLink}";
      await _emailService.SendEmailAsync(user.Email, subject, body);

      return Ok("Password reset email sent.");
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
      var user = await _context.Users.SingleOrDefaultAsync(u => u.PasswordResetToken == request.Token);
      if (user == null || user.ResetTokenExpires < DateTime.UtcNow)
        return BadRequest("Invalid or expired token.");

      user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
      user.PasswordResetToken = null;
      user.ResetTokenExpires = null;
      await _context.SaveChangesAsync();

      return Ok("Password has been reset successfully.");
    }
  }
}
