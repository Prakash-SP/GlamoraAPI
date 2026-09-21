namespace PeachyGlamora.Api.Models;

public class CartItem
{
    public int Id { get; set; }
    // UserId is null for guest carts, which are tracked by a GuestCartId cookie/token instead.
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public string? GuestCartId { get; set; }

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = default!;
    public int Quantity { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

public enum CouponType { PercentOff, FlatOff, FreeShipping, BuyXGetY }
public enum CouponScopeType { WholeCart, SpecificProducts, SpecificCategories }

public class Coupon
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;      // e.g. GLAM20
    public CouponType Type { get; set; }
    public decimal Value { get; set; }                 // 20 (%) or flat rupee amount
    public CouponScopeType ScopeType { get; set; } = CouponScopeType.WholeCart;
    public decimal? MinOrderValue { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public int? UsageLimitPerUser { get; set; }
    public int? TotalUsageLimit { get; set; }
    public int TimesUsed { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<CouponProduct> CouponProducts { get; set; } = new List<CouponProduct>();
    public ICollection<CouponCategory> CouponCategories { get; set; } = new List<CouponCategory>();
}

public class CouponProduct
{
    public int Id { get; set; }
    public int CouponId { get; set; }
    public Coupon Coupon { get; set; } = default!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;
}

public class CouponCategory
{
    public int Id { get; set; }
    public int CouponId { get; set; }
    public Coupon Coupon { get; set; } = default!;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = default!;
}

public class CouponUsage
{
    public int Id { get; set; }
    public int CouponId { get; set; }
    public Coupon Coupon { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;
    public int OrderId { get; set; }
    public DateTime UsedAt { get; set; } = DateTime.UtcNow;

    // Set when the order this usage belongs to gets CANCELLED — the coupon
    // redemption never actually completed, so it shouldn't count against
    // either UsageLimitPerUser or TotalUsageLimit anymore. Never deleted
    // (kept for audit trail, same "never delete, mark state" pattern as
    // RefundStatus elsewhere) — CartService.ValidateCouponAsync excludes
    // reversed rows from its per-user count, and OrderService/
    // OrdersController/AdminOrdersController decrement Coupon.TimesUsed to
    // match.
    //
    // Deliberately NOT set by:
    // - A completed Return (ReturnsController/AdminReturnsController) — the
    //   sale genuinely happened, so that coupon use stays counted.
    // - Per-item admin cancellation (AdminOrdersController.CancelItem) — the
    //   coupon was validly applied to the order as it stood at checkout and
    //   stays applied regardless of a later partial cancellation, even if
    //   the remaining items would no longer clear the coupon's minimum
    //   order value on their own. Only whole-order cancellation reverses
    //   usage (OrdersController.CancelOrder, AdminOrdersController.CancelOrder).
    public bool IsReversed { get; set; }
    public DateTime? ReversedAt { get; set; }
}

public class GiftCard
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;
    public decimal InitialBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum OrderStatus { Pending, Confirmed, Processing, Shipped, OutForDelivery, Delivered, Cancelled, Returned, RefundInitiated, Refunded }
public enum PaymentMethod { UPI, Card, NetBanking, Wallet, CashOnDelivery, GiftCard }
public enum PaymentStatus { Pending, Paid, Failed, Refunded, PartiallyRefunded }

// Tracks refund state for a single cancelled OrderItem (Article-wise
// cancellation), independent of Order-level Payment.Status — a partial
// (single-item) cancellation never touches Payment.Status, so this is the
// only place a pending per-item refund is recorded.
public enum RefundStatus { NotApplicable, Pending, Refunded }

public class Order
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = default!;  // human-readable, e.g. PG-000482
    public string UserId { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;

    public int ShippingAddressId { get; set; }
    public Address ShippingAddress { get; set; } = default!;
    public int BillingAddressId { get; set; }
    public Address BillingAddress { get; set; } = default!;

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public string? CouponCode { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EstimatedDeliveryDate { get; set; }

    // AWB / consignment (tracking) number — required when an admin marks the
    // order Shipped (see AdminOrdersController.UpdateStatus). Stored on the
    // order itself (not just in the OrderStatusHistory note) so it can be
    // reliably reused later — e.g. resending the shipped email, or showing
    // it on the order detail page — without parsing free text.
    public string? TrackingId { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
    public Payment? Payment { get; set; }
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = default!;
    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = default!;

    // Snapshot fields so historical orders stay accurate even if the product changes later
    public string ProductNameSnapshot { get; set; } = default!;
    // NEW — same historical-accuracy reasoning as ProductNameSnapshot above:
    // without these, every email/order-view/admin-note only ever shows the
    // product name, with no way to tell which color/size was actually
    // ordered once you're several products or restocks removed from
    // checkout. Nullable since not every variant has a color or size.
    public string? ColorSnapshot { get; set; }
    public string? SizeSnapshot { get; set; }
    public decimal UnitPriceSnapshot { get; set; }
    public int Quantity { get; set; }

    // HSN/tax snapshot the invoice must always reflect the rate actually
    // charged at purchase time, even if that HSN code's rate changes later.
    public string HsnCodeSnapshot { get; set; } = default!;
    public decimal TaxRatePercentSnapshot { get; set; }
    public decimal TaxAmountSnapshot { get; set; }

    // --- Per-item cancellation (admin only, "Article-wise" cancellation) ---
    // Only ever set while the parent Order is Confirmed or Processing (enforced
    // in AdminOrdersController.CancelItem, not here) — a cancelled item stays on
    // the order for history/invoice purposes rather than being deleted.
    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    // --- Refund tracking for the cancellation above ---
    // Set alongside IsCancelled: Pending if the order had been paid (a real
    // refund is owed), NotApplicable if no payment had been received yet.
    // Flipped to Refunded manually by an admin — ONLY after supplying a
    // transaction reference (see AdminOrderRefundsController.MarkRefunded) —
    // once the UPI transfer to the customer's active payout method has
    // actually been sent. There's no gateway webhook for this since refunds
    // go out over a plain UPI VPA, not a PG, so a reference is mandatory:
    // nothing may be marked Refunded without one.
    public RefundStatus RefundStatus { get; set; } = RefundStatus.NotApplicable;
    public DateTime? RefundedAt { get; set; }
}

public class OrderStatusHistory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public OrderStatus Status { get; set; }
    public string? Note { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = default!;
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string Gateway { get; set; } = default!;      // "Razorpay" | "Stripe" | "COD"
    public string? GatewayTransactionId { get; set; }
    public string? GatewayOrderId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }

    // --- Refund confirmation (already-shipped whole-order cancellation) ---
    // Only ever set by AdminOrdersController.ConfirmRefund, and only ever
    // together — a non-null RefundTransactionRef is required before
    // Status can become Refunded via that endpoint, so these two fields are
    // always both-null or both-set. Mirrors the same "no reference, no
    // Refunded status" rule enforced on OrderItem via
    // AdminOrderRefundsController.MarkRefunded, and on ReturnRequest below.
    public string? RefundTransactionRef { get; set; }
    public DateTime? RefundConfirmedAt { get; set; }
}

public enum ReturnStatus { Requested, Approved, Rejected, PickedUp, Refunded }

public class ReturnRequest
{
    public int Id { get; set; }
    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public string Reason { get; set; } = default!;
    public bool IsExchange { get; set; }
    public ReturnStatus Status { get; set; } = ReturnStatus.Requested;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    // --- Refund confirmation ---
    // Set only when Status transitions to Refunded, and only together with a
    // non-empty RefundTransactionRef supplied by the admin — enforced in
    // AdminReturnsController.UpdateStatus. Same "no reference, no Refunded
    // status" rule as Payment and OrderItem above, applied here so customer
    // returns can't be marked refunded without proof either.
    public string? RefundTransactionRef { get; set; }
    public DateTime? RefundedAt { get; set; }
}
