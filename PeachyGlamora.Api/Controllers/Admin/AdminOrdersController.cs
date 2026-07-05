using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;
using PeachyGlamora.Api.Services;

namespace PeachyGlamora.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = "Admin,Support")]
public class AdminOrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IOrderNotificationService _notifications;
    public AdminOrdersController(AppDbContext db, IOrderNotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] OrderStatus? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var query = _db.Orders.Include(o => o.User).AsQueryable();
        if (status.HasValue) query = query.Where(o => o.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(o => o.OrderNumber.Contains(search) || o.User.Email!.Contains(search));

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new { o.Id, o.OrderNumber, o.Status, o.TotalAmount, o.CreatedAt, CustomerName = o.User.FullName, CustomerEmail = o.User.Email })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOne(int id)
    {
        var order = await _db.Orders
            .Include(o => o.Items).Include(o => o.User).Include(o => o.ShippingAddress)
            .Include(o => o.BillingAddress).Include(o => o.Payment)
            .Include(o => o.StatusHistory.OrderBy(h => h.ChangedAt))
            .FirstOrDefaultAsync(o => o.Id == id);
        return order == null ? NotFound() : Ok(order);
    }

    public record UpdateStatusDto(OrderStatus Status, string? Note);

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateStatusDto dto)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        order.Status = dto.Status;
        _db.Add(new OrderStatusHistory { OrderId = id, Status = dto.Status, Note = dto.Note });
        await _db.SaveChangesAsync();

        await _notifications.SendOrderStatusUpdateAsync(id, dto.Status.ToString());
        return Ok(new { message = "Order status updated and customer notified." });
    }
}

[ApiController]
[Route("api/admin/returns")]
[Authorize(Roles = "Admin,Support")]
public class AdminReturnsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminReturnsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] ReturnStatus? status) =>
        Ok(await _db.ReturnRequests.Include(r => r.OrderItem).ThenInclude(i => i.Order)
            .Where(r => status == null || r.Status == status)
            .OrderByDescending(r => r.RequestedAt).ToListAsync());

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] ReturnStatus status)
    {
        var request = await _db.ReturnRequests.FindAsync(id);
        if (request == null) return NotFound();
        request.Status = status;
        await _db.SaveChangesAsync();
        return Ok(request);
    }
}

[ApiController]
[Route("api/admin/customers")]
[Authorize(Roles = "Admin")]
public class AdminCustomersController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminCustomersController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var query = _db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.Email!.Contains(search) || u.FullName.Contains(search));

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(u => u.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new
            {
                u.Id, u.FullName, u.Email, u.PhoneNumber, u.CreatedAt, u.LoyaltyPoints,
                OrderCount = u.Orders.Count,
                TotalSpent = u.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => (decimal?)o.TotalAmount) ?? 0
            }).ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOne(string id)
    {
        var user = await _db.Users.Include(u => u.Addresses)
            .Include(u => u.Orders.OrderByDescending(o => o.CreatedAt))
            .FirstOrDefaultAsync(u => u.Id == id);
        return user == null ? NotFound() : Ok(user);
    }
}
