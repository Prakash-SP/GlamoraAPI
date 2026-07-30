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
            .Where(o => o.Id == id)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.Status,
                o.TotalAmount,
                o.CreatedAt,
                CustomerName = o.User.FullName,
                CustomerEmail = o.User.Email,
                CustomerPhone = o.User.PhoneNumber,
                Items = o.Items.Select(i => new
                {
                    i.Id,
                    i.ProductNameSnapshot,
                    i.UnitPriceSnapshot,
                    i.Quantity
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
                    o.Payment.Amount
                }
            })
            .FirstOrDefaultAsync();

        return order == null ? NotFound() : Ok(order);
    }

    public record UpdateStatusDto(OrderStatus Status, string? Note);

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateStatusDto dto)
    {
        if (dto.Status == OrderStatus.Cancelled)
            return BadRequest(new { error = "Use the dedicated Cancel Order action instead — it calculates any refund/deduction first." });

        var order = await _db.Orders.Include(o => o.Payment).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        if (!OrderCancellationCalculator.CanChangeStatus(order))
            return BadRequest(new { error = "Payment must be confirmed before the order status can be updated." });

        order.Status = dto.Status;
        _db.Add(new OrderStatusHistory { OrderId = id, Status = dto.Status, Note = dto.Note });
        await _db.SaveChangesAsync();

        await _notifications.SendOrderStatusUpdateAsync(id, dto.Status.ToString());
        return Ok(new { message = "Order status updated and customer notified." });
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

        order.Status = OrderStatus.Cancelled;
        foreach (var item in order.Items) item.ProductVariant.StockQuantity += item.Quantity;

        _db.Add(new OrderStatusHistory
        {
            OrderId = id,
            Status = OrderStatus.Cancelled,
            Note = preview.PaymentReceived
                ? $"Cancelled by admin. Refund of ₹{preview.RefundAmount:0.00} initiated (₹{preview.ShippingDeduction:0.00} shipping deduction applied)."
                : "Cancelled by admin. No payment had been received, so no refund is due.",
        });

        if (order.Payment != null && preview.PaymentReceived)
            order.Payment.Status = PaymentStatus.Refunded;

        await _db.SaveChangesAsync();
        await _notifications.SendOrderStatusUpdateAsync(id, OrderStatus.Cancelled.ToString());

        return Ok(new { message = "Order cancelled.", refundAmount = preview.RefundAmount });
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
            OrderItem = new
            {
                r.OrderItem.Id,
                r.OrderItem.ProductNameSnapshot,
                r.OrderItem.Quantity,
                Order = new
                {
                    r.OrderItem.Order.OrderNumber,
                    r.OrderItem.Order.Status
                }
            }
        })
        .ToListAsync());

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
                u.Id, u.FullName, u.Email, u.PhoneNumber, u.CreatedAt, u.LoyaltyPoints,
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
            user.Id, user.FullName, user.Email, user.PhoneNumber, user.CreatedAt, user.LoyaltyPoints,
            user.Addresses, user.Orders,
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
