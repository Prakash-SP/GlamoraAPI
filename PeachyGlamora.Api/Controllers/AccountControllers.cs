using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountController(AppDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // ---------- DTOs ----------

    public record ProfileDto(
        string FullName,
        string? Email,
        string? PhoneNumber,
        DateTime? DateOfBirth,
        string? ProfileImageUrl,
        string AuthProvider,
        int LoyaltyPoints,
        string? ReferralCode,
        DateTime CreatedAt);

    public record UpdateProfileDto(
        string FullName,
        string? PhoneNumber,
        DateTime? DateOfBirth,
        string? ProfileImageUrl);

    public record ChangePasswordDto(
        string CurrentPassword,
        string NewPassword);

    // ---------- GET /api/account/profile ----------

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var userId = User.FindFirst("sub")!.Value;

        // Projected via .Select() — never return the raw ApplicationUser entity,
        // since lazy-loading proxies + navigation collections (Addresses, Orders,
        // CartItems, WishlistItems, SupportTickets) will crash JSON serialization
        // on circular references.
        var profile = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => new ProfileDto(
                u.FullName,
                u.Email,
                u.PhoneNumber,
                u.DateOfBirth,
                u.ProfileImageUrl,
                u.AuthProvider,
                u.LoyaltyPoints,
                u.ReferralCode,
                u.CreatedAt))
            .FirstOrDefaultAsync();

        if (profile == null)
            return NotFound();

        return Ok(profile);
    }

    // ---------- PUT /api/account/profile ----------

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileDto dto)
    {
        var userId = User.FindFirst("sub")!.Value;

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return NotFound();

        // Email intentionally excluded — email changes need a separate
        // verification flow and are out of scope here.
        user.FullName = dto.FullName;
        user.PhoneNumber = dto.PhoneNumber;
        user.DateOfBirth = dto.DateOfBirth;
        user.ProfileImageUrl = dto.ProfileImageUrl;

        await _context.SaveChangesAsync();

        var profile = new ProfileDto(
            user.FullName,
            user.Email,
            user.PhoneNumber,
            user.DateOfBirth,
            user.ProfileImageUrl,
            user.AuthProvider,
            user.LoyaltyPoints,
            user.ReferralCode,
            user.CreatedAt);

        return Ok(profile);
    }

    // ---------- POST /api/account/change-password ----------

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        var userId = User.FindFirst("sub")!.Value;

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return NotFound();

        var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);

        if (!result.Succeeded)
        {
            var errorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
            return BadRequest(new { error = errorMessage });
        }

        return Ok(new { message = "Password updated successfully." });
    }
}

[ApiController]
[Route("api/addresses")]
[Authorize]
public class AddressesController : ControllerBase
{
    private readonly AppDbContext _db;
    public AddressesController(AppDbContext db) => _db = db;
    private string UserId => User.FindFirst("sub")!.Value;

    // Input DTO — used for both Create and Update.
    public record AddressDto(
        string FullName,
        string Phone,
        string Line1,
        string? Line2,
        string? Landmark,
        string City,
        string State,
        string Pincode,
        AddressType Type,
        bool IsDefault,
        string Country = "India");

    // Output DTO — projected, never the raw Address entity (it carries a
    // `User` nav property back to ApplicationUser, same circular-reference
    // risk already fixed elsewhere in this project).
    public record AddressResponseDto(
        int Id,
        string FullName,
        string Phone,
        string Line1,
        string? Line2,
        string? Landmark,
        string City,
        string State,
        string Pincode,
        string Country,
        AddressType Type,
        bool IsDefault);

    private static readonly Func<Address, AddressResponseDto> ToDto = a => new AddressResponseDto(
        a.Id, a.FullName, a.Phone, a.Line1, a.Line2, a.Landmark, a.City, a.State, a.Pincode, a.Country, a.Type, a.IsDefault);

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _db.Addresses
            .Where(a => a.UserId == UserId)
            .Select(a => new AddressResponseDto(
                a.Id, a.FullName, a.Phone, a.Line1, a.Line2, a.Landmark, a.City, a.State, a.Pincode, a.Country, a.Type, a.IsDefault))
            .ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(AddressDto dto)
    {
        if (dto.IsDefault)
            foreach (var a in _db.Addresses.Where(a => a.UserId == UserId && a.Type == dto.Type))
                a.IsDefault = false;

        var address = new Address
        {
            UserId = UserId, FullName = dto.FullName, Phone = dto.Phone, Line1 = dto.Line1,
            Line2 = dto.Line2, Landmark = dto.Landmark, City = dto.City, State = dto.State, Pincode = dto.Pincode,
            Country = dto.Country, Type = dto.Type, IsDefault = dto.IsDefault
        };
        _db.Addresses.Add(address);
        await _db.SaveChangesAsync();
        return Ok(ToDto(address));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, AddressDto dto)
    {
        var address = await _db.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == UserId);
        if (address == null) return NotFound();

        if (dto.IsDefault)
            foreach (var a in _db.Addresses.Where(a => a.UserId == UserId && a.Type == dto.Type && a.Id != id))
                a.IsDefault = false;

        address.FullName = dto.FullName; address.Phone = dto.Phone; address.Line1 = dto.Line1;
        address.Line2 = dto.Line2; address.Landmark = dto.Landmark; address.City = dto.City; address.State = dto.State;
        address.Pincode = dto.Pincode; address.Country = dto.Country;
        address.Type = dto.Type; address.IsDefault = dto.IsDefault;
        await _db.SaveChangesAsync();
        return Ok(ToDto(address));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var address = await _db.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == UserId);
        if (address == null) return NotFound();
        _db.Addresses.Remove(address);
        await _db.SaveChangesAsync();
        return Ok();
    }
}

[ApiController]
[Route("api/support-tickets")]
[Authorize]
public class SupportTicketsController : ControllerBase
{
    private readonly AppDbContext _db;
    public SupportTicketsController(AppDbContext db) => _db = db;
    private string UserId => User.FindFirst("sub")!.Value;

    public record CreateTicketDto(string Subject, string Message, int? RelatedOrderId);
    public record ReplyDto(string Message);

    // List view — no replies, keeps the payload light.
    public record SupportTicketListItemDto(
        int Id,
        string Subject,
        int? RelatedOrderId,
        TicketStatus Status,
        DateTime CreatedAt);

    // Reply projection — SupportTicketReply has no navigation property back
    // to SupportTicket (only the FK int), so this is safe to project as-is.
    public record SupportTicketReplyDto(
        int Id,
        string AuthorId,
        bool IsFromStaff,
        string Message,
        DateTime CreatedAt);

    // Detail view — replies projected as a nested list of DTOs, not entities.
    public record SupportTicketDetailDto(
        int Id,
        string Subject,
        string Message,
        int? RelatedOrderId,
        TicketStatus Status,
        DateTime CreatedAt,
        List<SupportTicketReplyDto> Replies);

    [HttpGet]
    public async Task<IActionResult> GetMyTickets() =>
        Ok(await _db.SupportTickets
            .Where(t => t.UserId == UserId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new SupportTicketListItemDto(t.Id, t.Subject, t.RelatedOrderId, t.Status, t.CreatedAt))
            .ToListAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetTicket(int id)
    {
        var ticket = await _db.SupportTickets
            .Where(t => t.Id == id && t.UserId == UserId)
            .Select(t => new SupportTicketDetailDto(
                t.Id,
                t.Subject,
                t.Message,
                t.RelatedOrderId,
                t.Status,
                t.CreatedAt,
                t.Replies
                    .OrderBy(r => r.CreatedAt)
                    .Select(r => new SupportTicketReplyDto(r.Id, r.AuthorId, r.IsFromStaff, r.Message, r.CreatedAt))
                    .ToList()))
            .FirstOrDefaultAsync();

        return ticket == null ? NotFound() : Ok(ticket);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateTicketDto dto)
    {
        var ticket = new SupportTicket { UserId = UserId, Subject = dto.Subject, Message = dto.Message, RelatedOrderId = dto.RelatedOrderId };
        _db.SupportTickets.Add(ticket);
        await _db.SaveChangesAsync();

        return Ok(new SupportTicketListItemDto(ticket.Id, ticket.Subject, ticket.RelatedOrderId, ticket.Status, ticket.CreatedAt));
    }

    [HttpPost("{id:int}/replies")]
    public async Task<IActionResult> Reply(int id, ReplyDto dto)
    {
        var ticket = await _db.SupportTickets.FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId);
        if (ticket == null) return NotFound();
        _db.SupportTicketReplies.Add(new SupportTicketReply { SupportTicketId = id, AuthorId = UserId, IsFromStaff = false, Message = dto.Message });
        ticket.Status = TicketStatus.InProgress;
        await _db.SaveChangesAsync();
        return Ok();
    }
}

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    public NotificationsController(AppDbContext db) => _db = db;
    private string UserId => User.FindFirst("sub")!.Value;

    // AppNotification has no navigation properties (scalar fields only), so
    // returning it raw wouldn't actually crash — but it's projected anyway
    // to stay consistent with the "never return raw entities" convention
    // and to stay safe if a nav property gets added later.
    public record NotificationDto(
        int Id,
        string Title,
        string Body,
        string? LinkUrl,
        bool IsRead,
        DateTime CreatedAt);

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _db.Notifications
            .Where(n => n.UserId == UserId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new NotificationDto(n.Id, n.Title, n.Body, n.LinkUrl, n.IsRead, n.CreatedAt))
            .ToListAsync());

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (n == null) return NotFound();
        n.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok();
    }
}
