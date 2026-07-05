using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;
using PeachyGlamora.Api.Services;

namespace PeachyGlamora.Api.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPaymentGatewayService _gateway;
    private readonly IOrderNotificationService _notifications;

    public PaymentsController(AppDbContext db, IPaymentGatewayService gateway, IOrderNotificationService notifications)
    {
        _db = db;
        _gateway = gateway;
        _notifications = notifications;
    }

    private string UserId => User.FindFirst("sub")!.Value;

    /// <summary>Re-fetches the UPI QR/intent for an order — used if the customer's checkout
    /// session refreshed or they switch devices to scan the code.</summary>
    [HttpGet("{orderNumber}/qr")]
    public async Task<IActionResult> GetQr(string orderNumber)
    {
        var order = await _db.Orders.Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.UserId == UserId);
        if (order == null) return NotFound();
        if (order.Payment?.Method == PaymentMethod.CashOnDelivery)
            return BadRequest(new { error = "This order is being paid by Cash on Delivery." });

        var result = await _gateway.CreatePaymentOrderAsync(order.TotalAmount, order.OrderNumber);
        return Ok(new { result.UpiIntentUri, result.QrCodeBase64Png, paymentStatus = order.Payment?.Status.ToString() });
    }

    /// <summary>Lets the customer tell us "I've paid" right after completing the UPI transfer.
    /// This does NOT mark the order as paid — it flags it for priority manual verification,
    /// since a self-report alone isn't proof of payment.</summary>
    [HttpPost("{orderNumber}/mark-paid-by-customer")]
    public async Task<IActionResult> CustomerReportsPaid(string orderNumber)
    {
        var order = await _db.Orders.Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.UserId == UserId);
        if (order == null || order.Payment == null) return NotFound();

        order.StatusHistory.Add(new OrderStatusHistory
        {
            Status = order.Status,
            Note = "Customer reported UPI payment complete — awaiting verification"
        });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Thanks! We'll confirm your payment shortly and update your order." });
    }

    /// <summary>Staff-only: confirms a UPI payment after reconciling it against the bank
    /// statement / UPI app alert, then moves the order forward and notifies the customer.
    /// This is also the endpoint to point a future payment-aggregator webhook at if you
    /// upgrade from a plain VPA to Razorpay/Cashfree/PhonePe PG later.</summary>
    [HttpPost("{orderNumber}/confirm")]
    [Authorize(Roles = "Admin,Support")]
    public async Task<IActionResult> ConfirmPayment(string orderNumber, [FromBody] string? gatewayTransactionId)
    {
        var order = await _db.Orders.Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);
        if (order == null || order.Payment == null) return NotFound();

        order.Payment.Status = PaymentStatus.Paid;
        order.Payment.PaidAt = DateTime.UtcNow;
        order.Payment.GatewayTransactionId = gatewayTransactionId;
        order.Status = OrderStatus.Confirmed;
        order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Confirmed, Note = "UPI payment verified by staff" });
        await _db.SaveChangesAsync();

        await _notifications.SendOrderStatusUpdateAsync(order.Id, "Confirmed");
        return Ok(new { message = "Payment confirmed." });
    }
}
