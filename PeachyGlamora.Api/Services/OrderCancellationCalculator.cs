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

    // Refund result for a single order item (Article-wise cancellation).
    public record ItemCancellationResult(decimal ItemAmount, decimal RefundAmount, bool PaymentReceived);

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

    // Per-item cancellation is only ever offered while the order is Confirmed
    // or Processing (enforced in AdminOrdersController.CancelItem) — i.e.
    // always pre-shipment — so there's no shipping-deduction branch to worry
    // about here, unlike the whole-order Calculate() above. TaxAmountSnapshot
    // is already the per-item, coupon-prorated tax computed at checkout time
    // (see OrderService.CheckoutAsync), so refunding
    // UnitPriceSnapshot * Quantity + TaxAmountSnapshot is correct and exact.
    public static ItemCancellationResult CalculateForItem(OrderItem item, Payment? payment)
    {
        var paymentReceived = payment?.Status == PaymentStatus.Paid;
        var itemAmount = (item.UnitPriceSnapshot * item.Quantity) + item.TaxAmountSnapshot;
        var refundAmount = paymentReceived ? itemAmount : 0m;
        return new ItemCancellationResult(itemAmount, refundAmount, paymentReceived);
    }

    // Recomputes Payment.Status purely from the actual RefundStatus of every
    // cancelled item on the order — Paid -> PartiallyRefunded (some items
    // marked Refunded, some still Pending) -> Refunded (every cancelled,
    // payment-owing item marked Refunded). This is the single source of
    // truth for Payment.Status going forward: nothing should set
    // Payment.Status = Refunded/PartiallyRefunded directly anymore — call
    // this instead, any time an item's IsCancelled/RefundStatus changes on a
    // paid order, so Payment.Status can never claim money moved before an
    // admin actually confirmed the UPI transfer via Mark Refunded.
    public static void RecalculatePaymentStatus(Order order)
    {
        if (order.Payment == null) return;
        if (order.Payment.Status is not (PaymentStatus.Paid or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
            return; // nothing to recalculate — payment was never actually Paid to begin with

        var refundableItems = order.Items.Where(i => i.IsCancelled && i.RefundStatus != RefundStatus.NotApplicable).ToList();
        if (refundableItems.Count == 0) return; // no cancelled+paid items — leave Payment.Status untouched

        var allRefunded = refundableItems.All(i => i.RefundStatus == RefundStatus.Refunded);
        var anyRefunded = refundableItems.Any(i => i.RefundStatus == RefundStatus.Refunded);

        if (allRefunded)
            order.Payment.Status = PaymentStatus.Refunded;
        else if (anyRefunded)
            order.Payment.Status = PaymentStatus.PartiallyRefunded;
        // else: every cancelled item is still Pending — leave Payment.Status
        // exactly as it was (normally Paid). No money has moved yet, so
        // nothing here should claim otherwise.
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