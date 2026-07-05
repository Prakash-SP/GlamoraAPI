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

public class Coupon
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;      // e.g. GLAM20
    public CouponType Type { get; set; }
    public decimal Value { get; set; }                 // 20 (%) or flat rupee amount
    public decimal? MinOrderValue { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public int? UsageLimitPerUser { get; set; }
    public int? TotalUsageLimit { get; set; }
    public int TimesUsed { get; set; }
    public bool IsActive { get; set; } = true;
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
    public decimal UnitPriceSnapshot { get; set; }
    public int Quantity { get; set; }
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
}
