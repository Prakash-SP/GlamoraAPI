using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Services;

// Pure calculation helper (no DB access) — reused by both the customer-facing
// OrdersController and AdminOrdersController so the refund/deduction math
// exists in exactly one place, not duplicated across two controllers.
public static class OrderCancellationCalculator
{
    public record CancellationPreview(
        decimal OriginalAmount, decimal ShippingDeduction, decimal RefundAmount,
        bool PaymentReceived, string DeductionReason);

    public static CancellationPreview Calculate(Order order)
    {
        var paymentReceived = order.Payment?.Status == PaymentStatus.Paid;

        // Once shipped, the shipping cost has genuinely been spent — the
        // customer/admin flow doesn't offer an instant refund at that point;
        // the item has to come back before anything is refunded, and the
        // shipping fee is what's deducted from the eventual refund.
        var alreadyShipped = order.Status is OrderStatus.Shipped or OrderStatus.OutForDelivery or OrderStatus.Delivered;
        var shippingDeduction = alreadyShipped ? order.ShippingAmount : 0m;

        // Tax is deliberately NOT deducted separately: CartService computes
        // TaxAmount only on the product subtotal (never on shipping), so tax
        // is tied to the merchandise being returned — it's refunded in full
        // along with everything else except the shipping deduction above.
        var refundAmount = paymentReceived ? Math.Max(order.TotalAmount - shippingDeduction, 0m) : 0m;

        string reason;
        if (!paymentReceived)
            reason = "Payment was not yet received for this order, so no refund is due.";
        else if (alreadyShipped)
            reason = $"A shipping charge of ₹{shippingDeduction:0.00} is deducted since the order had already shipped. The refund is processed once the returned product is received back.";
        else
            reason = "The full amount is refunded since the order had not yet shipped.";

        return new CancellationPreview(order.TotalAmount, shippingDeduction, refundAmount, paymentReceived, reason);
    }

    // Gate for admin order-status changes. Only UPI (and similar "pay first")
    // methods require confirmed payment before the status can progress — Cash
    // on Delivery is exempt by design, since its payment is only collected at
    // delivery. Written as a per-method check (not a hardcoded UPI-only check)
    // so adding more "pay later" methods in future doesn't require touching
    // every call site — just this one function.
    public static bool RequiresPaymentBeforeStatusChange(Payment? payment)
        => payment != null && payment.Method != PaymentMethod.CashOnDelivery;

    public static bool CanChangeStatus(Order order)
        => !RequiresPaymentBeforeStatusChange(order.Payment) || order.Payment!.Status == PaymentStatus.Paid;
}
