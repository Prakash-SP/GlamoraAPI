using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Controllers;

[ApiController]
[Route("api/addresses")]
[Authorize]
public class AddressesController : ControllerBase
{
    private readonly AppDbContext _db;
    public AddressesController(AppDbContext db) => _db = db;
    private string UserId => User.FindFirst("sub")!.Value;

    public record AddressDto(string FullName, string Phone, string Line1, string? Line2,
        string City, string State, string Pincode, AddressType Type, bool IsDefault);

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _db.Addresses.Where(a => a.UserId == UserId).ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(AddressDto dto)
    {
        if (dto.IsDefault)
            foreach (var a in _db.Addresses.Where(a => a.UserId == UserId && a.Type == dto.Type))
                a.IsDefault = false;

        var address = new Address
        {
            UserId = UserId, FullName = dto.FullName, Phone = dto.Phone, Line1 = dto.Line1,
            Line2 = dto.Line2, City = dto.City, State = dto.State, Pincode = dto.Pincode,
            Type = dto.Type, IsDefault = dto.IsDefault
        };
        _db.Addresses.Add(address);
        await _db.SaveChangesAsync();
        return Ok(address);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, AddressDto dto)
    {
        var address = await _db.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == UserId);
        if (address == null) return NotFound();

        address.FullName = dto.FullName; address.Phone = dto.Phone; address.Line1 = dto.Line1;
        address.Line2 = dto.Line2; address.City = dto.City; address.State = dto.State;
        address.Pincode = dto.Pincode; address.IsDefault = dto.IsDefault;
        await _db.SaveChangesAsync();
        return Ok(address);
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

    [HttpGet]
    public async Task<IActionResult> GetMyTickets() =>
        Ok(await _db.SupportTickets.Where(t => t.UserId == UserId)
            .OrderByDescending(t => t.CreatedAt).ToListAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetTicket(int id)
    {
        var ticket = await _db.SupportTickets.Include(t => t.Replies.OrderBy(r => r.CreatedAt))
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId);
        return ticket == null ? NotFound() : Ok(ticket);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateTicketDto dto)
    {
        var ticket = new SupportTicket { UserId = UserId, Subject = dto.Subject, Message = dto.Message, RelatedOrderId = dto.RelatedOrderId };
        _db.SupportTickets.Add(ticket);
        await _db.SaveChangesAsync();
        return Ok(ticket);
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

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _db.Notifications.Where(n => n.UserId == UserId)
            .OrderByDescending(n => n.CreatedAt).Take(50).ToListAsync());

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
