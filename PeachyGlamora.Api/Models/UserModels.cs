using Microsoft.AspNetCore.Identity;

namespace PeachyGlamora.Api.Models;

/// <summary>Extends ASP.NET Core Identity's user with jewellery-storefront-specific fields.
/// Identity already gives us PasswordHash, Email, PhoneNumber, EmailConfirmed etc.</summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = default!;
    public DateTime? DateOfBirth { get; set; }         // for birthday offers
    public string? ProfileImageUrl { get; set; }
    public string AuthProvider { get; set; } = "Email"; // Email | Google | Otp
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int LoyaltyPoints { get; set; }
    public string? ReferralCode { get; set; }
    public string? ReferredByCode { get; set; }

    public ICollection<Address> Addresses { get; set; } = new List<Address>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    public ICollection<WishlistItem> WishlistItems { get; set; } = new List<WishlistItem>();
    public ICollection<SupportTicket> SupportTickets { get; set; } = new List<SupportTicket>();
}

public enum AddressType { Shipping, Billing }

public class Address
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;

    public string FullName { get; set; } = default!;
    public string Phone { get; set; } = default!;
    public string Line1 { get; set; } = default!;
    public string? Line2 { get; set; }
    public string? Landmark { get; set; }
    public string City { get; set; } = default!;
    public string State { get; set; } = default!;
    public string Pincode { get; set; } = default!;
    public string Country { get; set; } = "India";
    public AddressType Type { get; set; } = AddressType.Shipping;
    public bool IsDefault { get; set; }
}

/// <summary>Short-lived OTP codes for phone-based login (SMS/WhatsApp delivered).</summary>
public class OtpCode
{
    public int Id { get; set; }
    public string PhoneNumber { get; set; } = default!;
    public string Code { get; set; } = default!;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum TicketStatus { Open, InProgress, Resolved, Closed }

public class SupportTicket
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;
    public string Subject { get; set; } = default!;
    public string Message { get; set; } = default!;
    public int? RelatedOrderId { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<SupportTicketReply> Replies { get; set; } = new List<SupportTicketReply>();
}

public class SupportTicketReply
{
    public int Id { get; set; }
    public int SupportTicketId { get; set; }
    public string AuthorId { get; set; } = default!;   // customer or admin/staff user id
    public bool IsFromStaff { get; set; }
    public string Message { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AppNotification
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Body { get; set; } = default!;
    public string? LinkUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
