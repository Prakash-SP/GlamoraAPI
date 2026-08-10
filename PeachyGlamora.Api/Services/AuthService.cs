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
    Task RequestPasswordResetAsync(string email);
    Task<(bool success, string? error)> ResetPasswordAsync(string email, string token, string newPassword);
    Task<(bool success, string? error)> ConfirmEmailAsync(string email, string token);
    Task RequestEmailConfirmationAsync(string email);
}

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtService _jwt;
    private readonly AppDbContext _db;
    private readonly ISmsService _sms;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        IJwtService jwt,
        AppDbContext db,
        ISmsService sms,
        IEmailService email,
        IConfiguration config)
    {
        _userManager = userManager;
        _jwt = jwt;
        _db = db;
        _sms = sms;
        _email = email;
        _config = config;
    }

    public async Task<(bool, string?, AuthResponse?)> RegisterAsync(RegisterRequest req)
    {
        var validationError = ValidateRegisterRequest(req);
        if (validationError != null) return (false, validationError, null);

        var existing = await _userManager.FindByEmailAsync(req.Email);
        if (existing != null) return (false, "An account with this email already exists.", null);

        // Same "cheap early check, DB index is the real backstop" pattern as
        // email — someone may have already registered this phone number
        // through the OTP flow (VerifyOtpAsync) before ever hitting this
        // email/password path.
        var phoneInUse = await _userManager.Users.AnyAsync(u => u.PhoneNumber == req.Phone);
        if (phoneInUse) return (false, "An account with this phone number already exists.", null);

        var user = new ApplicationUser
        {
            UserName = req.Email,
            Email = req.Email,
            FullName = req.FullName,
            PhoneNumber = req.Phone,
            DateOfBirth = req.DateOfBirth,
            AuthProvider = "Email",
            ReferralCode = GenerateReferralCode(req.FullName)
        };

        IdentityResult result;
        try
        {
            result = await _userManager.CreateAsync(user, req.Password);
        }
        catch (DbUpdateException)
        {
            // The checks above are just an early, cheap rejection for the common
            // case — they can't see a second request registering the SAME email
            // or phone at nearly the same instant, since neither has committed
            // yet when the other checks. The unique indexes on
            // ApplicationUser.NormalizedEmail and .PhoneNumber (AppDbContext.cs)
            // are what actually prevent the duplicate row; this catches that
            // constraint violation. Deliberately generic (doesn't say which of
            // the two collided) — telling an attacker exactly which field
            // matched an existing account is a minor enumeration leak, and the
            // person retrying the form will see soon enough which field is the
            // problem once they change one and resubmit.
            return (false, "An account with this email or phone number already exists.", null);
        }

        if (!result.Succeeded)
            return (false, string.Join("; ", result.Errors.Select(e => e.Description)), null);

        await _userManager.AddToRoleAsync(user, "Customer");

        // New account, so EmailConfirmed is always false at this point (Identity
        // defaults it false; Register never sets it, unlike Google login which
        // sets it true since Google already verified that email). Doesn't block
        // returning success — the account exists and can log in immediately,
        // per the "allow login, show a banner" decision — this just kicks off
        // the confirmation email alongside it.
        await SendConfirmationEmailAsync(user);

        var (token, expires) = _jwt.GenerateToken(user, new[] { "Customer" });
        return (true, null, new AuthResponse(user.Id, user.FullName, user.Email!, token, expires, user.EmailConfirmed));
    }

    // Mirrors the frontend's register-form checks, but this is the copy that actually
    // matters — anyone can call this endpoint directly (Postman/curl) and skip the
    // Angular form entirely, so nothing here can rely on client-side validation alone.
    // Note: password *complexity* (length, digits, etc.) is intentionally left to
    // UserManager.CreateAsync below, which already enforces Identity's configured
    // PasswordOptions — duplicating that here would just risk the two drifting apart.
    private static string? ValidateRegisterRequest(RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FullName))
            return "Please enter your full name.";

        if (string.IsNullOrWhiteSpace(req.Email) || !IsValidEmail(req.Email))
            return "Please enter a valid email address.";

        if (string.IsNullOrWhiteSpace(req.Phone) || !System.Text.RegularExpressions.Regex.IsMatch(req.Phone, @"^\d{10}$"))
            return "Please enter a valid 10-digit mobile number.";

        if (string.IsNullOrEmpty(req.Password))
            return "Please enter a password.";

        if (!IsAtLeast14(req.DateOfBirth))
            return "You must be at least 14 years old to create an account.";

        return null;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAtLeast14(DateTime dateOfBirth)
    {
        var today = DateTime.UtcNow.Date;
        var dob = dateOfBirth.Date;
        if (dob > today) return false; // future DOB is never valid regardless of age math

        var age = today.Year - dob.Year;
        if (dob > today.AddYears(-age)) age--; // birthday hasn't occurred yet this year
        return age >= 14;
    }

    public async Task<(bool, string?, AuthResponse?)> LoginAsync(LoginRequest req)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user == null || !await _userManager.CheckPasswordAsync(user, req.Password))
            return (false, "Invalid email or password.", null);

        var roles = await _userManager.GetRolesAsync(user);
        var (token, expires) = _jwt.GenerateToken(user, roles);
        return (true, null, new AuthResponse(user.Id, user.FullName, user.Email!, token, expires, user.EmailConfirmed));
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

            try
            {
                await _userManager.CreateAsync(user);
                await _userManager.AddToRoleAsync(user, "Customer");
            }
            catch (DbUpdateException)
            {
                // Same race as RegisterAsync — two near-simultaneous first-time
                // Google logins for the same email. Whichever request loses the
                // race just re-fetches the row the winner created and logs into
                // that instead of failing outright; unlike a manual registration,
                // there's no reason to reject this one, since the person
                // genuinely does own this Google account either way.
                user = await _userManager.FindByEmailAsync(email)
                    ?? throw new InvalidOperationException("Unique-email conflict on Google login, but no matching user found afterward.");
            }
        }

        var roles = await _userManager.GetRolesAsync(user);
        var (token, expires) = _jwt.GenerateToken(user, roles);
        return (true, null, new AuthResponse(user.Id, user.FullName, user.Email!, token, expires, user.EmailConfirmed));
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

            try
            {
                await _userManager.CreateAsync(user);
                await _userManager.AddToRoleAsync(user, "Customer");
            }
            catch (DbUpdateException)
            {
                // Same race as RegisterAsync/LoginWithGoogleAsync — two OTP
                // verifications for the same phone number arriving close enough
                // together that both pass the FirstOrDefaultAsync check above
                // before either commits. An OTP code is single-use and this
                // whole flow requires having actually received it, so this is a
                // narrower window than the email cases, but not impossible (e.g.
                // the same device double-submitting). Re-fetch and log into the
                // account the winner created rather than failing outright — the
                // phone was verified either way.
                user = await _userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber)
                    ?? throw new InvalidOperationException("Unique-phone conflict on OTP verify, but no matching user found afterward.");
            }
        }

        await _db.SaveChangesAsync();
        var roles = await _userManager.GetRolesAsync(user);
        var (token, expires) = _jwt.GenerateToken(user, roles);
        return (true, null, new AuthResponse(user.Id, user.FullName, user.Email!, token, expires, user.EmailConfirmed));
    }

    // Deliberately returns nothing the caller can distinguish on — the
    // controller always sends back the same generic message regardless of
    // what happens in here. If the email doesn't exist, or belongs to a
    // Google/OTP account (no password to reset in the first place), this
    // just no-ops and no email goes out. Only a real Email/password account
    // actually gets a reset link.
    public async Task RequestPasswordResetAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null || user.AuthProvider != "Email") return;

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        // Identity's reset tokens contain characters (+, /, =) that aren't
        // safe unescaped in a URL query string — must percent-encode both
        // the token and the email before building the link.
        var encodedToken = Uri.EscapeDataString(token);
        var encodedEmail = Uri.EscapeDataString(email);

        // Falls back to local dev if not configured — set Frontend:BaseUrl
        // in appsettings.Production.json to the real deployed frontend URL.
        var frontendBaseUrl = _config["Frontend:BaseUrl"] ?? "http://localhost:4200";
        var resetLink = $"{frontendBaseUrl}/reset-password?email={encodedEmail}&token={encodedToken}";

        var html = $"""
            <p>Hi {user.FullName},</p>
            <p>We received a request to reset your Peachy Glamora password. Click the link below to choose a new one:</p>
            <p><a href="{resetLink}">Reset your password</a></p>
            <p>This link can only be used once. If you didn't request this, you can safely ignore this email — your password won't change.</p>
            """;

        await _email.SendAsync(email, "Reset your Peachy Glamora password", html);
    }

    public async Task<(bool, string?)> ResetPasswordAsync(string email, string token, string newPassword)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
            return (false, "This reset link is invalid or has expired. Please request a new one.");

        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            // Identity's own ResetPasswordAsync rejects an already-used or
            // expired token with an "InvalidToken" error — surfaced here in
            // plain language rather than the raw Identity error text.
            var isTokenError = result.Errors.Any(e => e.Code == "InvalidToken");
            return (false, isTokenError
                ? "This reset link is invalid or has expired. Please request a new one."
                : string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        return (true, null);
    }

    // Fire-and-forget from RegisterAsync's perspective (doesn't affect whether
    // registration itself succeeds) — a failed send here just means the
    // person can hit "Resend confirmation" later; SmtpEmailService already
    // swallows its own send failures rather than throwing.
    private async Task SendConfirmationEmailAsync(ApplicationUser user)
    {
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        // Same escaping reasoning as the password-reset link below — Identity's
        // tokens contain +, /, = which aren't safe unescaped in a query string.
        var encodedToken = Uri.EscapeDataString(token);
        var encodedEmail = Uri.EscapeDataString(user.Email!);
        var frontendBaseUrl = _config["Frontend:BaseUrl"] ?? "http://localhost:4200";
        var confirmLink = $"{frontendBaseUrl}/verify-email?email={encodedEmail}&token={encodedToken}";

        var html = $"""
            <p>Hi {user.FullName},</p>
            <p>Welcome to Peachy Glamora! Please confirm your email address by clicking the link below:</p>
            <p><a href="{confirmLink}">Confirm your email</a></p>
            <p>This link can only be used once. If you didn't create this account, you can safely ignore this email.</p>
            """;

        await _email.SendAsync(user.Email!, "Confirm your Peachy Glamora email", html);
    }

    // Same anti-enumeration shape as RequestPasswordResetAsync — the caller
    // (AuthController) always returns the same generic message regardless of
    // what happens in here, so this silently no-ops for an unknown email, a
    // non-Email-auth account (Google/OTP — nothing to confirm), or an
    // already-confirmed one, rather than telling the caller which case applied.
    public async Task RequestEmailConfirmationAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null || user.AuthProvider != "Email" || user.EmailConfirmed) return;

        await SendConfirmationEmailAsync(user);
    }

    public async Task<(bool, string?)> ConfirmEmailAsync(string email, string token)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
            return (false, "This confirmation link is invalid or has expired.");

        // Clicking an already-used link a second time (e.g. the person double
        // clicked, or opened the email twice) shouldn't show an error — the
        // outcome they wanted (a confirmed email) is already true.
        if (user.EmailConfirmed)
            return (true, null);

        var result = await _userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            // Same "translate Identity's raw error into plain language" pattern
            // as ResetPasswordAsync's InvalidToken handling.
            var isTokenError = result.Errors.Any(e => e.Code == "InvalidToken");
            return (false, isTokenError
                ? "This confirmation link is invalid or has expired. Please request a new one."
                : string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        return (true, null);
    }

    private static string GenerateReferralCode(string seed) =>
        (seed.Length >= 3 ? seed[..3] : seed).ToUpper() + Random.Shared.Next(1000, 9999);
}