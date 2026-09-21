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
    private readonly IInvoicePdfService _invoicePdf;
    private readonly IShippingLabelPdfService _shippingLabelPdf;

    // NEW — " (Teal · Free)" style suffix built from an item's snapshot
    // fields, or "" if neither is set. Used anywhere an item name is
    // rendered as plain text (status-history notes) rather than a separate
    // JSON field a frontend can style itself.
    private static string VariantSuffix(OrderItem item)
    {
        var parts = new[] { item.ColorSnapshot, item.SizeSnapshot }.Where(p => !string.IsNullOrWhiteSpace(p));
        var joined = string.Join(" · ", parts);
        return string.IsNullOrEmpty(joined) ? "" : $" ({joined})";
    }

    public AdminOrdersController(AppDbContext db, IOrderNotificationService notifications,
        IInvoicePdfService invoicePdf, IShippingLabelPdfService shippingLabelPdf)
    {
        _db = db;
        _notifications = notifications;
        _invoicePdf = invoicePdf;
        _shippingLabelPdf = shippingLabelPdf;
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
            .Where(o => o.Id == id)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.Status,
                o.TotalAmount,
                o.CreatedAt,
                o.TrackingId,
                CustomerName = o.User.FullName,
                CustomerEmail = o.User.Email,
                CustomerPhone = o.User.PhoneNumber,
                Items = o.Items.Select(i => new
                {
                    i.Id,
                    i.ProductNameSnapshot,
                    i.ColorSnapshot,
                    i.SizeSnapshot,
                    i.UnitPriceSnapshot,
                    i.Quantity,
                    i.IsCancelled,
                    i.CancelledAt,
                    i.CancellationReason,
                    i.RefundStatus
                }),
                StatusHistory = o.StatusHistory.OrderBy(h => h.ChangedAt).Select(h => new
                {
                    h.Status,
                    h.Note,
                    h.ChangedAt
                }),
                ShippingAddress = new
                {
                    o.ShippingAddress.FullName,
                    o.ShippingAddress.Phone,
                    o.ShippingAddress.Line1,
                    o.ShippingAddress.Line2,
                    o.ShippingAddress.City,
                    o.ShippingAddress.State,
                    o.ShippingAddress.Pincode
                },
                BillingAddress = new
                {
                    o.BillingAddress.FullName,
                    o.BillingAddress.Phone,
                    o.BillingAddress.Line1,
                    o.BillingAddress.Line2,
                    o.BillingAddress.City,
                    o.BillingAddress.State,
                    o.BillingAddress.Pincode
                },
                Payment = o.Payment == null ? null : new
                {
                    o.Payment.Method,
                    o.Payment.Status,
                    o.Payment.Amount,
                    o.Payment.RefundTransactionRef,
                    o.Payment.RefundConfirmedAt
                }
            })
            .FirstOrDefaultAsync();

        return order == null ? NotFound() : Ok(order);
    }

    // ---------- Printable documents ----------

    // Admin/Support variant of the customer-facing invoice download — no
    // UserId restriction, since staff need to print any order's invoice,
    // not just their own. See IInvoicePdfService.GenerateInvoicePdfForAdminAsync.
    [HttpGet("{id:int}/invoice/pdf")]
    public async Task<IActionResult> DownloadInvoicePdf(int id)
    {
        var pdfBytes = await _invoicePdf.GenerateInvoicePdfForAdminAsync(id);
        if (pdfBytes == null) return NotFound();
        return File(pdfBytes, "application/pdf", $"invoice-{id}.pdf");
    }

    // The "SHIP TO" address slip meant to be printed and pasted onto the
    // parcel — see ShippingLabelPdfService for why this is a separate
    // document from the tax invoice above.
    [HttpGet("{id:int}/shipping-label/pdf")]
    public async Task<IActionResult> DownloadShippingLabelPdf(int id)
    {
        try
        {
            var pdfBytes = await _shippingLabelPdf.GenerateShippingLabelPdfAsync(id);
            if (pdfBytes == null) return NotFound();
            return File(pdfBytes, "application/pdf", $"shipping-label-{id}.pdf");
        }
        catch (ShippingLabelPdfService.ShippingLabelConfigException ex)
        {
            // Return address isn't configured — refuse to generate anything
            // rather than print a label with a fake/placeholder sender address.
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // labelsPerPage was previously missing here entirely — the DTO only had
    // OrderIds, so GenerateBatchShippingLabelsPdfAsync always ran with its
    // default of 2, no matter what the admin picked in the UI. The service
    // itself has always fully supported 2 or 4 (see ShippingLabelPdfService);
    // this was purely a controller-level gap.
    public record BatchShippingLabelsDto(List<int> OrderIds, int LabelsPerPage = 2);

    // Batch label printing for the Orders list "select + print" flow — one
    // combined PDF, LabelsPerPage labels per A4 page (dashed cut line
    // between them), in the order the admin selected them. POST (not GET)
    // since a batch of IDs doesn't belong cleanly in a query string.
    [HttpPost("labels/batch-pdf")]
    public async Task<IActionResult> DownloadBatchShippingLabelsPdf(BatchShippingLabelsDto dto)
    {
        if (dto.OrderIds == null || dto.OrderIds.Count == 0)
            return BadRequest(new { error = "Select at least one order to print labels for." });

        if (dto.LabelsPerPage != 2 && dto.LabelsPerPage != 4)
            return BadRequest(new { error = "labelsPerPage must be 2 or 4." });

        try
        {
            var pdfBytes = await _shippingLabelPdf.GenerateBatchShippingLabelsPdfAsync(dto.OrderIds, dto.LabelsPerPage);
            if (pdfBytes == null) return NotFound(new { error = "None of the selected orders could be found." });
            return File(pdfBytes, "application/pdf", $"shipping-labels-{DateTime.UtcNow:yyyyMMdd-HHmmss}.pdf");
        }
        catch (ShippingLabelPdfService.ShippingLabelConfigException ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    public record UpdateStatusDto(OrderStatus Status, string? Note, string? TrackingId);

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateStatusDto dto)
    {
        if (dto.Status == OrderStatus.Cancelled)
            return BadRequest(new { error = "Use the dedicated Cancel Order action instead — it calculates any refund/deduction first." });

        var order = await _db.Orders.Include(o => o.Payment).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        if (!OrderCancellationCalculator.CanChangeStatus(order))
            return BadRequest(new { error = "Payment must be confirmed before the order status can be updated." });

        string? historyNote = dto.Note;

        // Marking an order Shipped requires proof of dispatch: both a
        // comment AND a tracking/AWB ID, captured as a distinct field
        // (not parsed out of free text) so it can be reused reliably for
        // the customer email's tracking link.
        if (dto.Status == OrderStatus.Shipped)
        {
            if (string.IsNullOrWhiteSpace(dto.Note))
                return BadRequest(new { error = "A comment is required when marking an order as Shipped." });
            if (string.IsNullOrWhiteSpace(dto.TrackingId))
                return BadRequest(new { error = "An AWB / tracking (consignment) ID is required when marking an order as Shipped." });

            order.TrackingId = dto.TrackingId.Trim();
            historyNote = $"{dto.Note.Trim()} — AWB/Tracking ID: {order.TrackingId}";
        }

        order.Status = dto.Status;
        _db.Add(new OrderStatusHistory { OrderId = id, Status = dto.Status, Note = historyNote });
        await _db.SaveChangesAsync();

        await _notifications.SendOrderStatusUpdateAsync(id, dto.Status.ToString());
        return Ok(new { message = "Order status updated and customer notified." });
    }

    // ---------- Tracking ID correction ----------
    // Separate from UpdateStatus above on purpose: this is for fixing an AWB
    // that was already set (typo, courier reissued a number, etc.), not for
    // setting it the first time — that only ever happens as part of the
    // Shipped transition. Requires a tracking ID to already exist on the
    // order (i.e. it must have been Shipped at some point); the customer is
    // notified of the change with old → new shown, via
    // SendTrackingIdUpdatedEmailAsync — this is the notification you asked for.

    public record UpdateTrackingIdDto(string TrackingId);

    [HttpPut("{id:int}/tracking-id")]
    public async Task<IActionResult> UpdateTrackingId(int id, UpdateTrackingIdDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TrackingId))
            return BadRequest(new { error = "A tracking ID is required." });

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        if (string.IsNullOrWhiteSpace(order.TrackingId))
            return BadRequest(new { error = "This order doesn't have a tracking ID yet — set one via the Shipped status update first." });

        var newTrackingId = dto.TrackingId.Trim();
        if (newTrackingId == order.TrackingId)
            return BadRequest(new { error = "That's already the current tracking ID for this order." });

        var oldTrackingId = order.TrackingId;
        order.TrackingId = newTrackingId;

        _db.Add(new OrderStatusHistory
        {
            OrderId = id,
            Status = order.Status,
            Note = $"Tracking ID updated by admin: {oldTrackingId} → {newTrackingId}",
        });

        await _db.SaveChangesAsync();

        await _notifications.SendTrackingIdUpdatedEmailAsync(id, oldTrackingId, newTrackingId);

        return Ok(new { message = "Tracking ID updated and customer notified." });
    }

    [HttpGet("{id:int}/cancellation-preview")]
    public async Task<IActionResult> PreviewCancellation(int id)
    {
        var order = await _db.Orders.Include(o => o.Payment).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Delivered or OrderStatus.Returned or OrderStatus.Refunded)
            return BadRequest(new { error = $"This order is already {order.Status} and cannot be cancelled." });

        var preview = OrderCancellationCalculator.Calculate(order);
        return Ok(new
        {
            originalAmount = preview.OriginalAmount,
            shippingDeduction = preview.ShippingDeduction,
            refundAmount = preview.RefundAmount,
            paymentReceived = preview.PaymentReceived,
            deductionReason = preview.DeductionReason,
        });
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelOrder(int id)
    {
        var order = await _db.Orders.Include(o => o.Items).ThenInclude(i => i.ProductVariant)
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Delivered or OrderStatus.Returned or OrderStatus.Refunded)
            return BadRequest(new { error = $"This order is already {order.Status} and cannot be cancelled." });

        var preview = OrderCancellationCalculator.Calculate(order);

        // Pre-shipment vs already-shipped are handled differently below —
        // the per-item QR/Mark-Refunded flow was only ever designed for
        // pre-shipment cancellations (same window Article-wise cancellation
        // uses), so that's the only case unified with it here.
        var isPreShipment = order.Status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Processing;

        order.Status = OrderStatus.Cancelled;
        foreach (var item in order.Items) item.ProductVariant.StockQuantity += item.Quantity;

        if (isPreShipment)
        {
            // Route every item through the exact same bookkeeping as
            // Article-wise (per-item) cancellation, so a whole-order cancel
            // shows up identically on "Cancelled Items Awaiting Refund" and
            // requires the same manual QR-scan + Mark Refunded confirmation
            // (with a mandatory transaction reference) — one refund-honesty
            // model for the whole app, not two.
            foreach (var item in order.Items.Where(i => !i.IsCancelled))
            {
                item.IsCancelled = true;
                item.CancelledAt = DateTime.UtcNow;
                item.CancellationReason = "Whole order cancelled by admin.";
                item.RefundStatus = preview.PaymentReceived ? RefundStatus.Pending : RefundStatus.NotApplicable;
            }
            // Payment.Status is NOT touched here — RecalculatePaymentStatus
            // only ever advances it once items are actually marked Refunded
            // (with a reference) one at a time.
        }
        // Already-shipped: NO instant refund flag of any kind, on purpose.
        // Payment.Status stays exactly as it was (normally Paid) until an
        // admin explicitly calls ConfirmRefund below with a transaction
        // reference — this is the fix for the old behaviour where cancelling
        // a shipped order silently claimed "Refunded" the instant the button
        // was clicked, before any money had actually moved.

        // Reverse the coupon's usage against this order, if one was applied —
        // WHOLE-ORDER cancellation only, never per-item (CancelItem below
        // deliberately does not touch this, even if the remaining items
        // still clear the coupon's minimum order value — confirmed
        // intentional, not an oversight). The redemption never actually
        // completed for a cancelled order, so it shouldn't count against
        // the customer's UsageLimitPerUser or the coupon's TotalUsageLimit
        // anymore. Not done on a Return (AdminReturnsController below) —
        // a return means the sale genuinely happened, so that usage stays
        // counted. Mirrors OrdersController.CancelOrder's customer-facing
        // equivalent exactly.
        if (!string.IsNullOrWhiteSpace(order.CouponCode))
        {
            var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == order.CouponCode);
            if (coupon != null)
            {
                coupon.TimesUsed = Math.Max(0, coupon.TimesUsed - 1);

                var usage = await _db.CouponUsages
                    .FirstOrDefaultAsync(u => u.OrderId == order.Id && u.CouponId == coupon.Id && !u.IsReversed);
                if (usage != null)
                {
                    usage.IsReversed = true;
                    usage.ReversedAt = DateTime.UtcNow;
                }
            }
        }

        _db.Add(new OrderStatusHistory
        {
            OrderId = id,
            Status = OrderStatus.Cancelled,
            Note = preview.PaymentReceived
                ? (isPreShipment
                    ? $"Cancelled by admin. Refund of ₹{preview.RefundAmount:0.00} to be processed per item via Returns & Refunds."
                    : $"Cancelled by admin. Refund of ₹{preview.RefundAmount:0.00} due (₹{preview.ShippingDeduction:0.00} shipping deduction applied) — pending manual confirmation with a transaction reference once the returned item is received back.")
                : "Cancelled by admin. No payment had been received, so no refund is due.",
        });

        await _db.SaveChangesAsync();
        await _notifications.SendOrderStatusUpdateAsync(id, OrderStatus.Cancelled.ToString());

        return Ok(new { message = "Order cancelled.", refundAmount = preview.RefundAmount });
    }

    // ---------- Whole-order refund confirmation (already-shipped cancellations) ----------
    // Covers the one refund path that isn't per-item: an order that had
    // already shipped when it was cancelled. Admin-only, and — like every
    // other refund-completion path in the app — refuses to proceed without
    // a transaction reference.

    public record ConfirmRefundDto(string TransactionRef);

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:int}/confirm-refund")]
    public async Task<IActionResult> ConfirmRefund(int id, ConfirmRefundDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.TransactionRef))
            return BadRequest(new { error = "A transaction reference is required to confirm this refund." });

        var order = await _db.Orders.Include(o => o.Payment).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();
        if (order.Payment == null)
            return BadRequest(new { error = "This order has no payment record." });
        if (order.Payment.Status == PaymentStatus.Refunded)
            return BadRequest(new { error = "This order has already been marked as refunded." });
        if (order.Payment.Status != PaymentStatus.Paid)
            return BadRequest(new { error = "There is no paid amount pending refund for this order." });
        if (order.Status is not (OrderStatus.Cancelled or OrderStatus.Returned))
            return BadRequest(new { error = "This order isn't in a cancelled or returned state." });

        order.Payment.Status = PaymentStatus.Refunded;
        order.Payment.RefundTransactionRef = dto.TransactionRef;
        order.Payment.RefundConfirmedAt = DateTime.UtcNow;

        _db.Add(new OrderStatusHistory
        {
            OrderId = id,
            Status = order.Status,
            Note = $"Refund confirmed by admin. Ref: {dto.TransactionRef}",
        });

        await _db.SaveChangesAsync();
        return Ok(new { message = "Refund confirmed." });
    }

    // ---------- Article-wise (per-item) cancellation — Admin only ----------
    // Method-level [Authorize(Roles = "Admin")] below overrides the class-level
    // "Admin,Support" for these two actions specifically: combined with the
    // class attribute, ASP.NET Core requires BOTH to be satisfied, which in
    // practice means only Admin (never Support) can call these two endpoints.

    public record CancelItemDto(string Reason);

    [Authorize(Roles = "Admin")]
    [HttpGet("{orderId:int}/items/{itemId:int}/cancellation-preview")]
    public async Task<IActionResult> PreviewItemCancellation(int orderId, int itemId)
    {
        var order = await _db.Orders.Include(o => o.Items).Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) return NotFound();

        var item = order.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) return NotFound();

        if (order.Status is not (OrderStatus.Confirmed or OrderStatus.Processing))
            return BadRequest(new { error = "Individual items can only be cancelled while the order is Confirmed or Processing." });
        if (item.IsCancelled)
            return BadRequest(new { error = "This item has already been cancelled." });

        var result = OrderCancellationCalculator.CalculateForItem(item, order.Payment);
        return Ok(new { itemAmount = result.ItemAmount, refundAmount = result.RefundAmount, paymentReceived = result.PaymentReceived });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{orderId:int}/items/{itemId:int}/cancel")]
    // NOTE: deliberately does NOT reverse coupon usage, unlike whole-order
    // CancelOrder above. Confirmed: per-item cancellation never affects
    // Coupon.TimesUsed or CouponUsage, even if cancelling this item would
    // drop the remaining order below the coupon's minimum order value —
    // the coupon was validly applied to the order as it stood at checkout,
    // and stays applied regardless of later partial cancellations.
    public async Task<IActionResult> CancelItem(int orderId, int itemId, CancelItemDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest(new { error = "A reason is required to cancel this item." });

        var order = await _db.Orders.Include(o => o.Items).ThenInclude(i => i.ProductVariant)
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) return NotFound();

        if (order.Status is not (OrderStatus.Confirmed or OrderStatus.Processing))
            return BadRequest(new { error = "Individual items can only be cancelled while the order is Confirmed or Processing." });

        var item = order.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) return NotFound();
        if (item.IsCancelled)
            return BadRequest(new { error = "This item has already been cancelled." });

        var result = OrderCancellationCalculator.CalculateForItem(item, order.Payment);

        item.IsCancelled = true;
        item.CancelledAt = DateTime.UtcNow;
        item.CancellationReason = dto.Reason;
        item.ProductVariant.StockQuantity += item.Quantity; // release stock for this item only

        // Refund tracking: Pending if payment had actually been received for
        // this order (a real refund is owed), NotApplicable otherwise. This
        // is what powers the "Cancelled items awaiting refund" list on the
        // admin Returns & Refunds page.
        item.RefundStatus = result.PaymentReceived ? RefundStatus.Pending : RefundStatus.NotApplicable;

        // Rule: if every item in the order ends up cancelled, the whole order
        // auto-flips to Cancelled (same as the full-order cancel flow).
        var allCancelled = order.Items.All(i => i.IsCancelled);
        if (allCancelled)
            order.Status = OrderStatus.Cancelled;

        // Payment.Status is recomputed from actual per-item RefundStatus —
        // NOT set to Refunded here directly. Since this item (and possibly
        // every item, if allCancelled) just became Pending rather than
        // Refunded, this call is a no-op until an admin actually marks a
        // refund complete with a transaction reference via
        // AdminOrderRefundsController.MarkRefunded.
        OrderCancellationCalculator.RecalculatePaymentStatus(order);

        _db.Add(new OrderStatusHistory
        {
            OrderId = orderId,
            Status = allCancelled ? OrderStatus.Cancelled : order.Status,
            Note = (result.PaymentReceived
                ? $"Item \"{item.ProductNameSnapshot}{VariantSuffix(item)}\" (qty {item.Quantity}) cancelled by admin. Reason: {dto.Reason}. Refund of ₹{result.RefundAmount:0.00} initiated."
                : $"Item \"{item.ProductNameSnapshot}{VariantSuffix(item)}\" (qty {item.Quantity}) cancelled by admin. Reason: {dto.Reason}. No payment had been received, so no refund is due.")
              + (allCancelled ? " All items are now cancelled — order marked Cancelled." : ""),
        });

        await _db.SaveChangesAsync();

        // Always tell the customer directly: full order status + item list
        // (with this one marked cancelled), the admin's reason, and the
        // refund amount — regardless of whether this also tipped the whole
        // order into Cancelled below.
        await _notifications.SendItemCancellationEmailAsync(
            orderId, itemId, dto.Reason, result.RefundAmount, result.PaymentReceived);

        // On top of the item-specific email above, also fire the standard
        // whole-order "Cancelled" notification if this was the last item —
        // that one covers the order-status-change angle (tracking page,
        // generic copy) which the item-level email intentionally doesn't.
        if (allCancelled)
            await _notifications.SendOrderStatusUpdateAsync(orderId, OrderStatus.Cancelled.ToString());

        return Ok(new { message = "Item cancelled.", refundAmount = result.RefundAmount, orderCancelled = allCancelled });
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
    Ok(await _db.ReturnRequests
        .Where(r => status == null || r.Status == status)
        .OrderByDescending(r => r.RequestedAt)
        .Select(r => new
        {
            r.Id,
            r.Reason,
            r.IsExchange,
            r.Status,
            r.RequestedAt,
            r.RefundTransactionRef,
            r.RefundedAt,
            OrderItem = new
            {
                r.OrderItem.Id,
                r.OrderItem.ProductNameSnapshot,
                r.OrderItem.ColorSnapshot,
                r.OrderItem.SizeSnapshot,
                r.OrderItem.Quantity,
                Order = new
                {
                    r.OrderItem.Order.OrderNumber,
                    r.OrderItem.Order.Status
                }
            }
        })
        .ToListAsync());

    public record UpdateReturnStatusDto(ReturnStatus Status, string? TransactionRef);

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateReturnStatusDto dto)
    {
        // Same rule as every other refund-completion path in the app: moving
        // a return to Refunded requires a transaction reference, no exceptions.
        if (dto.Status == ReturnStatus.Refunded && string.IsNullOrWhiteSpace(dto.TransactionRef))
            return BadRequest(new { error = "A transaction reference is required to mark this return as refunded." });

        var request = await _db.ReturnRequests.FindAsync(id);
        if (request == null) return NotFound();

        request.Status = dto.Status;
        if (dto.Status == ReturnStatus.Refunded)
        {
            request.RefundTransactionRef = dto.TransactionRef;
            request.RefundedAt = DateTime.UtcNow;
        }

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
    private readonly BankAccountCrypto _crypto;
    public AdminCustomersController(AppDbContext db, BankAccountCrypto crypto)
    {
        _db = db;
        _crypto = crypto;
    }

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
                u.Id,
                u.FullName,
                u.Email,
                u.PhoneNumber,
                u.CreatedAt,
                u.LoyaltyPoints,
                OrderCount = u.Orders.Count,
                TotalSpent = u.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => (decimal?)o.TotalAmount) ?? 0
            }).ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOne(string id)
    {
        var user = await _db.Users
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id,
                u.FullName,
                u.Email,
                u.PhoneNumber,
                u.CreatedAt,
                u.LoyaltyPoints,
                Addresses = u.Addresses.Select(a => new
                {
                    a.Id,
                    a.FullName,
                    a.Phone,
                    a.Line1,
                    a.Line2,
                    a.City,
                    a.State,
                    a.Pincode,
                    a.Type,
                    a.IsDefault
                }),
                Orders = u.Orders.OrderByDescending(o => o.CreatedAt).Select(o => new
                {
                    o.Id,
                    o.OrderNumber,
                    o.Status,
                    o.TotalAmount,
                    o.CreatedAt
                })
            })
            .FirstOrDefaultAsync();

        if (user == null) return NotFound();

        // A user's Active payout method is a Bank Account OR a UPI ID —
        // never both (enforced in BankAccountsController.Activate /
        // UpiAccountsController.Activate) — so check both tables and
        // return whichever one is actually set. The bank account branch
        // stays masked here too: a full account number is only ever
        // available through the audit-logged Reveal endpoint below, at the
        // moment a refund is actually being processed. The UPI branch has
        // no such restriction — a VPA is shown as-is, since it was never
        // masked or encrypted to begin with.
        var activeBankAccountRaw = await _db.BankAccounts
            .Where(b => b.UserId == id && !b.IsDeleted && b.IsActive)
            .Select(b => new { b.Id, b.AccountHolderName, b.AccountNumberLast4, b.IfscCode, b.BankName, b.BranchName })
            .FirstOrDefaultAsync();

        object? activePayoutMethod = null;

        if (activeBankAccountRaw != null)
        {
            activePayoutMethod = new
            {
                Type = "Bank",
                activeBankAccountRaw.Id,
                activeBankAccountRaw.AccountHolderName,
                MaskedAccountNumber = $"XXXXXX{activeBankAccountRaw.AccountNumberLast4}",
                activeBankAccountRaw.IfscCode,
                activeBankAccountRaw.BankName,
                activeBankAccountRaw.BranchName,
            };
        }
        else
        {
            var activeUpiAccount = await _db.UpiAccounts
                .Where(u => u.UserId == id && !u.IsDeleted && u.IsActive)
                .Select(u => new { u.Id, u.UpiId })
                .FirstOrDefaultAsync();

            if (activeUpiAccount != null)
                activePayoutMethod = new { Type = "Upi", activeUpiAccount.Id, activeUpiAccount.UpiId };
        }

        return Ok(new
        {
            user.Id,
            user.FullName,
            user.Email,
            user.PhoneNumber,
            user.CreatedAt,
            user.LoyaltyPoints,
            user.Addresses,
            user.Orders,
            ActivePayoutMethod = activePayoutMethod,
        });
    }

    public record RevealBankAccountRequest(string? Reason);

    // The only place in the entire system a full account number is ever
    // returned. Every call is logged to BankAccountRevealLogs — who, when,
    // and (if provided) why — since this is meant to be used at the exact
    // moment a refund is being manually processed, not browsed casually.
    // There is no UPI equivalent of this endpoint — a UPI ID is already
    // returned in full by GetOne above, since it was never masked.
    [HttpPost("{userId}/bank-accounts/{bankAccountId:int}/reveal")]
    public async Task<IActionResult> RevealBankAccount(string userId, int bankAccountId, RevealBankAccountRequest req)
    {
        var account = await _db.BankAccounts
            .FirstOrDefaultAsync(b => b.Id == bankAccountId && b.UserId == userId && !b.IsDeleted);
        if (account == null) return NotFound();

        var adminUserId = User.FindFirst("sub")!.Value;

        _db.BankAccountRevealLogs.Add(new BankAccountRevealLog
        {
            BankAccountId = account.Id,
            RevealedByAdminUserId = adminUserId,
            Reason = req.Reason,
        });
        await _db.SaveChangesAsync();

        var fullAccountNumber = _crypto.Decrypt(account.AccountNumberEncrypted);

        return Ok(new
        {
            account.AccountHolderName,
            AccountNumber = fullAccountNumber,
            account.IfscCode,
            account.BankName,
            account.BranchName,
            notice = "This reveal has been logged against your admin account.",
        });
    }
}
