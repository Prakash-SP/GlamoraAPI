using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.DTOs;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Services;

public interface IAuthService
{
    Task<(bool success, string? error, AuthResponse? result)> RegisterAsync(RegisterRequest req);
    Task<(bool success, string? error, AuthResponse? result)> LoginAsync(LoginRequest req);
    Task<(bool success, string? error, AuthResponse? result)> LoginWithGoogleAsync(string email, string fullName, string googleSubjectId);
    Task<bool> RequestOtpAsync(string phoneNumber);
    Task<(bool success, string? error, AuthResponse? result)> VerifyOtpAsync(string phoneNumber, string code);
}

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtService _jwt;
    private readonly AppDbContext _db;
    private readonly ISmsService _sms;

    public AuthService(UserManager<ApplicationUser> userManager, IJwtService jwt, AppDbContext db, ISmsService sms)
    {
        _userManager = userManager;
        _jwt = jwt;
        _db = db;
        _sms = sms;
    }

    public async Task<(bool, string?, AuthResponse?)> RegisterAsync(RegisterRequest req)
    {
        var existing = await _userManager.FindByEmailAsync(req.Email);
        if (existing != null) return (false, "An account with this email already exists.", null);

        var user = new ApplicationUser
        {
            UserName = req.Email,
            Email = req.Email,
            FullName = req.FullName,
            PhoneNumber = req.Phone,
            AuthProvider = "Email",
            ReferralCode = GenerateReferralCode(req.FullName)
        };

        var result = await _userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return (false, string.Join("; ", result.Errors.Select(e => e.Description)), null);

        await _userManager.AddToRoleAsync(user, "Customer");
        var (token, expires) = _jwt.GenerateToken(user, new[] { "Customer" });
        return (true, null, new AuthResponse(user.Id, user.FullName, user.Email!, token, expires));
    }

    public async Task<(bool, string?, AuthResponse?)> LoginAsync(LoginRequest req)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user == null || !await _userManager.CheckPasswordAsync(user, req.Password))
            return (false, "Invalid email or password.", null);

        var roles = await _userManager.GetRolesAsync(user);
        var (token, expires) = _jwt.GenerateToken(user, roles);
        return (true, null, new AuthResponse(user.Id, user.FullName, user.Email!, token, expires));
    }

    public async Task<(bool, string?, AuthResponse?)> LoginWithGoogleAsync(string email, string fullName, string googleSubjectId)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                AuthProvider = "Google",
                EmailConfirmed = true,
                ReferralCode = GenerateReferralCode(fullName)
            };
            await _userManager.CreateAsync(user);
            await _userManager.AddToRoleAsync(user, "Customer");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var (token, expires) = _jwt.GenerateToken(user, roles);
        return (true, null, new AuthResponse(user.Id, user.FullName, user.Email!, token, expires));
    }

    public async Task<bool> RequestOtpAsync(string phoneNumber)
    {
        var code = Random.Shared.Next(100000, 999999).ToString();
        _db.OtpCodes.Add(new OtpCode
        {
            PhoneNumber = phoneNumber,
            Code = code,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        });
        await _db.SaveChangesAsync();
        await _sms.SendSmsAsync(phoneNumber, $"Your Peachy Glamora OTP is {code}. It expires in 5 minutes. Do not share this with anyone.");
        return true;
    }

    public async Task<(bool, string?, AuthResponse?)> VerifyOtpAsync(string phoneNumber, string code)
    {
        var otp = await _db.OtpCodes
            .Where(o => o.PhoneNumber == phoneNumber && o.Code == code && !o.IsUsed)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otp == null || otp.ExpiresAt < DateTime.UtcNow)
            return (false, "Invalid or expired OTP.", null);

        otp.IsUsed = true;

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = phoneNumber,
                PhoneNumber = phoneNumber,
                FullName = "Peachy Customer",
                AuthProvider = "Otp",
                PhoneNumberConfirmed = true,
                Email = $"{phoneNumber}@otp.peachyglamora.local",
                ReferralCode = GenerateReferralCode(phoneNumber)
            };
            await _userManager.CreateAsync(user);
            await _userManager.AddToRoleAsync(user, "Customer");
        }

        await _db.SaveChangesAsync();
        var roles = await _userManager.GetRolesAsync(user);
        var (token, expires) = _jwt.GenerateToken(user, roles);
        return (true, null, new AuthResponse(user.Id, user.FullName, user.Email!, token, expires));
    }

    private static string GenerateReferralCode(string seed) =>
        (seed.Length >= 3 ? seed[..3] : seed).ToUpper() + Random.Shared.Next(1000, 9999);
}
