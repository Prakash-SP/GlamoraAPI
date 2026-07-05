using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.DTOs;
using PeachyGlamora.Api.Services;

namespace PeachyGlamora.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize] // checkout and order history require a logged-in user (guest checkout still
            // creates a lightweight account behind the scenes — see CheckoutController)
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orders;
    private readonly AppDbContext _db;
    public OrdersController(IOrderService orders, AppDbContext db) { _orders = orders; _db = db; }

    private string UserId => User.FindFirst("sub")!.Value;

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(CheckoutRequest req)
    {
        try { return Ok(await _orders.CheckoutAsync(UserId, req)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // Powers the Account > Orders tab.
    [HttpGet]
    public async Task<IActionResult> GetMyOrders()
    {
        var orders = await _db.Orders
            .Where(o => o.UserId == UserId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.OrderNumber, o.Status, o.TotalAmount, o.CreatedAt, o.EstimatedDeliveryDate,
                ItemCount = o.Items.Count
            })
            .ToListAsync();

        return Ok(orders);
    }

    // Order tracking detail, including status history timeline.
    [HttpGet("{orderNumber}")]
    public async Task<IActionResult> GetOrder(string orderNumber)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistory.OrderBy(h => h.ChangedAt))
            .Include(o => o.ShippingAddress)
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.UserId == UserId);

        return order == null ? NotFound() : Ok(order);
    }

    [HttpPost("{orderNumber}/cancel")]
    public async Task<IActionResult> CancelOrder(string orderNumber)
    {
        var order = await _db.Orders.Include(o => o.Items).ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.UserId == UserId);
        if (order == null) return NotFound();
        if (order.Status is Models.OrderStatus.Shipped or Models.OrderStatus.Delivered)
            return BadRequest(new { error = "This order can no longer be cancelled — it has already shipped. Please request a return instead." });

        order.Status = Models.OrderStatus.Cancelled;
        foreach (var item in order.Items) item.ProductVariant.StockQuantity += item.Quantity; // release stock
        order.StatusHistory.Add(new Models.OrderStatusHistory { Status = Models.OrderStatus.Cancelled, Note = "Cancelled by customer" });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Order cancelled." });
    }
}

[ApiController]
[Route("api/returns")]
[Authorize]
public class ReturnsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ReturnsController(AppDbContext db) => _db = db;
    private string UserId => User.FindFirst("sub")!.Value;

    public record ReturnRequestDto(int OrderItemId, string Reason, bool IsExchange);

    [HttpPost]
    public async Task<IActionResult> RequestReturn(ReturnRequestDto req)
    {
        var orderItem = await _db.OrderItems.Include(i => i.Order)
            .FirstOrDefaultAsync(i => i.Id == req.OrderItemId && i.Order.UserId == UserId);
        if (orderItem == null) return NotFound();
        if (orderItem.Order.Status != Models.OrderStatus.Delivered)
            return BadRequest(new { error = "Returns can only be requested for delivered orders." });

        _db.ReturnRequests.Add(new Models.ReturnRequest
        {
            OrderItemId = req.OrderItemId,
            UserId = UserId,
            Reason = req.Reason,
            IsExchange = req.IsExchange
        });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Return request submitted. Our team will review it within 24 hours." });
    }
}
