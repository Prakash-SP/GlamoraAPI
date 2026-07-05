namespace PeachyGlamora.Api.DTOs;

public record RegisterRequest(string FullName, string Email, string Password, string? Phone);
public record LoginRequest(string Email, string Password);
public record GoogleLoginRequest(string IdToken); // ID token from Google Sign-In on the frontend
public record RequestOtpRequest(string PhoneNumber);
public record VerifyOtpRequest(string PhoneNumber, string Code);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Token, string NewPassword);

public record AuthResponse(string UserId, string FullName, string Email, string AccessToken, DateTime ExpiresAt);
