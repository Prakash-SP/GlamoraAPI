using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PeachyGlamora.Api.DTOs;
using PeachyGlamora.Api.Services;

namespace PeachyGlamora.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")] // throttles brute-force login/OTP attempts, see Program.cs
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService auth, IConfiguration config, ILogger<AuthController> logger)
    {
        _auth = auth;
        _config = config;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var (success, error, result) = await _auth.RegisterAsync(req);
        return success ? Ok(result) : BadRequest(new { error });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var (success, error, result) = await _auth.LoginAsync(req);
        return success ? Ok(result) : Unauthorized(new { error });
    }

    // Frontend uses Google Identity Services to get an ID token, then posts it here. We verify
    // the token's signature, issuer, and audience server-side via Google's own library before
    // trusting any of its claims — never trust an ID token just because the client sent one.
    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin(GoogleLoginRequest req)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(req.IdToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _config["Google:ClientId"] }
            });
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Rejected invalid Google ID token");
            return Unauthorized(new { error = "Invalid Google sign-in token." });
        }

        if (!payload.EmailVerified)
            return Unauthorized(new { error = "Google account email is not verified." });

        var (success, error, result) = await _auth.LoginWithGoogleAsync(payload.Email, payload.Name ?? payload.Email, payload.Subject);
        return success ? Ok(result) : BadRequest(new { error });
    }

    [HttpPost("otp/request")]
    public async Task<IActionResult> RequestOtp(RequestOtpRequest req)
    {
        await _auth.RequestOtpAsync(req.PhoneNumber);
        return Ok(new { message = "OTP sent." });
    }

    [HttpPost("otp/verify")]
    public async Task<IActionResult> VerifyOtp(VerifyOtpRequest req)
    {
        var (success, error, result) = await _auth.VerifyOtpAsync(req.PhoneNumber, req.Code);
        return success ? Ok(result) : BadRequest(new { error });
    }

    public record ForgotPasswordRequest(string Email);
    public record ResetPasswordRequest(string Email, string Token, string NewPassword);

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { error = "Please enter your email address." });

        await _auth.RequestPasswordResetAsync(req.Email);

        // Always the exact same response whether or not the email is
        // registered — this is the actual anti-enumeration control.
        // RequestPasswordResetAsync silently no-ops below for unknown
        // emails or accounts that don't use Email/password auth, but the
        // caller can never tell the difference from this response alone.
        return Ok(new { message = "If an account exists for that email, we've sent a password reset link." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NewPassword))
            return BadRequest(new { error = "Please enter a new password." });

        var (success, error) = await _auth.ResetPasswordAsync(req.Email, req.Token, req.NewPassword);
        return success
            ? Ok(new { message = "Your password has been reset. You can now log in." })
            : BadRequest(new { error });
    }
}
