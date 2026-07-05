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
}
