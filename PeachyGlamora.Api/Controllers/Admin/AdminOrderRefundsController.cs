using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;
using PeachyGlamora.Api.Services;

namespace PeachyGlamora.Api.Controllers.Admin;

// Powers the "Cancelled items awaiting refund" tab on the admin
// Returns & Refunds page. Separate from AdminOrdersController /
// AdminReturnsController since this tracks a third, distinct thing:
// individual OrderItems that were Article-wise cancelled (not full orders,
// not customer return requests) and still owe a refund.
//
// Admin-only (not Admin,Support like most of AdminOrdersController) — this
// controller can view a customer's UPI ID and confirm money has actually
// been sent, which is a higher-trust action than order/status management.
[ApiController]
[Route("api/admin/order-item-refunds")]
[Authorize(Roles = "Admin")]
public class AdminOrderRefundsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminOrderRefundsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetPending()
    {
        var raw = await _db.OrderItems
            .Where(i => i.IsCancelled && i.RefundStatus == RefundStatus.Pending)
            .OrderByDescending(i => i.CancelledAt)
            .Select(i => new
            {
                i.Id,
                i.OrderId,
                i.Order.OrderNumber,
                i.ProductNameSnapshot,
                i.Quantity,
                i.UnitPriceSnapshot,
                i.TaxAmountSnapshot,
                i.CancellationReason,
                i.CancelledAt,
                CustomerName = i.Order.User.FullName,
                CustomerEmail = i.Order.User.Email,
            })
            .ToListAsync();

        // RefundAmount = UnitPriceSnapshot * Quantity + TaxAmountSnapshot — the
        // exact same formula as OrderCancellationCalculator.CalculateForItem,
        // computed here in-memory (post ToListAsync) purely because it's a
        // cheap arithmetic projection, not because it needs DB access.
        var items = raw.Select(i => new
        {
            i.Id,
            i.OrderId,
            i.OrderNumber,
            i.ProductNameSnapshot,
            i.Quantity,
            RefundAmount = (i.UnitPriceSnapshot * i.Quantity) + i.TaxAmountSnapshot,
            i.CancellationReason,
            i.CancelledAt,
            i.CustomerName,
            i.CustomerEmail,
        });

        return Ok(items);
    }

    [HttpGet("{itemId:int}")]
    public async Task<IActionResult> GetDetail(int itemId)
    {
        var item = await _db.OrderItems
            .Include(i => i.Order).ThenInclude(o => o.User)
            .Include(i => i.Order).ThenInclude(o => o.Items)
            .Include(i => i.Order).ThenInclude(o => o.ShippingAddress)
            .Include(i => i.Order).ThenInclude(o => o.Payment)
            .FirstOrDefaultAsync(i => i.Id == itemId);

        if (item == null) return NotFound();
        if (!item.IsCancelled) return BadRequest(new { error = "This item was not cancelled." });

        var order = item.Order;
        var refundResult = OrderCancellationCalculator.CalculateForItem(item, order.Payment);

        // Per your payout setup: customers only ever have a UPI ID on file
        // (no bank account flow), so this is always the source of truth for
        // where the refund QR should point.
        var activeUpi = await _db.UpiAccounts
            .Where(u => u.UserId == order.UserId && !u.IsDeleted && u.IsActive)
            .Select(u => new { u.UpiId })
            .FirstOrDefaultAsync();

        string? upiIntentUri = null;
        string? qrBase64 = null;

        // Only generate the QR while the refund is actually still Pending —
        // once it's marked Refunded there's nothing left to pay, so showing
        // a "scan to pay" code again would be actively misleading.
        if (activeUpi != null && item.RefundStatus == RefundStatus.Pending)
        {
            upiIntentUri = RefundQrGenerator.BuildUpiIntentUri(
                activeUpi.UpiId, order.User.FullName, refundResult.RefundAmount, $"Refund - {order.OrderNumber}");
            qrBase64 = RefundQrGenerator.GenerateBase64Png(upiIntentUri);
        }

        return Ok(new
        {
            item.Id,
            item.ProductNameSnapshot,
            item.Quantity,
            item.UnitPriceSnapshot,
            item.CancellationReason,
            item.CancelledAt,
            item.RefundStatus,
            item.RefundedAt,
            RefundAmount = refundResult.RefundAmount,
            Order = new
            {
                order.Id,
                order.OrderNumber,
                order.Status,
                order.CreatedAt,
                order.TotalAmount,
                CustomerName = order.User.FullName,
                CustomerEmail = order.User.Email,
                CustomerPhone = order.User.PhoneNumber,
                Items = order.Items.Select(oi => new
                {
                    oi.Id,
                    oi.ProductNameSnapshot,
                    oi.Quantity,
                    oi.UnitPriceSnapshot,
                    oi.IsCancelled,
                    oi.CancellationReason,
                }),
                ShippingAddress = new
                {
                    order.ShippingAddress.FullName,
                    order.ShippingAddress.Phone,
                    order.ShippingAddress.Line1,
                    order.ShippingAddress.Line2,
                    order.ShippingAddress.City,
                    order.ShippingAddress.State,
                    order.ShippingAddress.Pincode,
                },
            },
            UpiId = activeUpi?.UpiId,
            UpiIntentUri = upiIntentUri,
            QrCodeBase64Png = qrBase64,
            HasActiveUpi = activeUpi != null,
        });
    }

    public record MarkRefundedDto(string TransactionRef);

    [HttpPost("{itemId:int}/mark-refunded")]
    public async Task<IActionResult> MarkRefunded(int itemId, [FromBody] MarkRefundedDto dto)
    {
        // A transaction reference is mandatory — this is the whole point of
        // this endpoint's existence: no refund may be marked completed
        // without proof it actually happened. Same rule applies to whole-order
        // (AdminOrdersController.ConfirmRefund) and return (AdminReturnsController.UpdateStatus) refunds.
        if (string.IsNullOrWhiteSpace(dto?.TransactionRef))
            return BadRequest(new { error = "A transaction reference is required to mark this refund as completed." });

        // Includes the rest of the order's items + Payment now — needed so
        // RecalculatePaymentStatus below can see every item's RefundStatus
        // and Payment's current state, not just this one item in isolation.
        var item = await _db.OrderItems
            .Include(i => i.Order).ThenInclude(o => o.Items)
            .Include(i => i.Order).ThenInclude(o => o.Payment)
            .FirstOrDefaultAsync(i => i.Id == itemId);

        if (item == null) return NotFound();
        if (!item.IsCancelled)
            return BadRequest(new { error = "This item was not cancelled." });
        if (item.RefundStatus != RefundStatus.Pending)
            return BadRequest(new { error = "This item's refund is not currently pending." });

        item.RefundStatus = RefundStatus.Refunded;
        item.RefundedAt = DateTime.UtcNow;

        // This is the ONE place Payment.Status is ever allowed to advance to
        // PartiallyRefunded/Refunded — only once an admin has actually
        // confirmed the UPI transfer went out with a reference, never
        // eagerly at cancellation time.
        OrderCancellationCalculator.RecalculatePaymentStatus(item.Order);

        _db.Add(new OrderStatusHistory
        {
            OrderId = item.OrderId,
            Status = item.Order.Status,
            Note = $"Refund for item \"{item.ProductNameSnapshot}\" marked as completed by admin. Ref: {dto.TransactionRef}",
        });

        await _db.SaveChangesAsync();
        return Ok(new { message = "Refund marked as completed." });
    }
}
