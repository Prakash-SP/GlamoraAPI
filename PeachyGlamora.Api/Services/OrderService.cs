using Hangfire;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.DTOs;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Services;

public interface IOrderService
{
    Task<OrderConfirmationDto> CheckoutAsync(string userId, CheckoutRequest req);
}

public class OrderService : IOrderService
{
    private readonly AppDbContext _db;
    private readonly ICartService _cart;
    private readonly IPaymentGatewayService _paymentGateway;
    private readonly IBackgroundJobClient _backgroundJobs;

    public OrderService(AppDbContext db, ICartService cart, IPaymentGatewayService paymentGateway, IBackgroundJobClient backgroundJobs)
    {
        _db = db;
        _cart = cart;
        _paymentGateway = paymentGateway;
        _backgroundJobs = backgroundJobs;
    }

    public async Task<OrderConfirmationDto> CheckoutAsync(string userId, CheckoutRequest req)
    {
        // Use a DB transaction: order creation + stock deduction must succeed or fail together,
        // otherwise concurrent checkouts could oversell the last unit of a variant.
        await using var tx = await _db.Database.BeginTransactionAsync();

        var cartItems = await _db.CartItems
            .Include(c => c.ProductVariant).ThenInclude(v => v.Product)
            .Where(c => c.UserId == userId)
            .ToListAsync();

        if (cartItems.Count == 0)
            throw new InvalidOperationException("Your bag is empty.");

        foreach (var item in cartItems)
        {
            if (item.ProductVariant.StockQuantity < item.Quantity)
                throw new InvalidOperationException($"{item.ProductVariant.Product.Name} no longer has enough stock.");
        }

        var summary = await _cart.GetCartAsync(userId, null, req.CouponCode);

        var order = new Order
        {
            OrderNumber = $"PG-{DateTime.UtcNow:yyMMdd}{Random.Shared.Next(1000, 9999)}",
            UserId = userId,
            ShippingAddressId = req.ShippingAddressId,
            BillingAddressId = req.BillingAddressId,
            Subtotal = summary.Subtotal,
            DiscountAmount = summary.DiscountAmount,
            TaxAmount = summary.EstimatedTax,
            ShippingAmount = summary.ShippingEstimate,
            TotalAmount = summary.Total,
            CouponCode = req.CouponCode,
            Status = OrderStatus.Pending,
            EstimatedDeliveryDate = DateTime.UtcNow.AddDays(5)
        };

        foreach (var item in cartItems)
        {
            order.Items.Add(new OrderItem
            {
                ProductVariantId = item.ProductVariantId,
                ProductNameSnapshot = item.ProductVariant.Product.Name,
                UnitPriceSnapshot = item.ProductVariant.PriceOverride,
                Quantity = item.Quantity
            });
            item.ProductVariant.StockQuantity -= item.Quantity; // reserve stock immediately
        }

        order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Pending, Note = "Order placed" });

        _db.Orders.Add(order);
        _db.CartItems.RemoveRange(cartItems);
        await _db.SaveChangesAsync();

        var method = Enum.Parse<PaymentMethod>(req.PaymentMethod);
        string? upiIntentUri = null;
        string? upiQrBase64 = null;
        var payment = new Payment
        {
            OrderId = order.Id,
            Method = method,
            Amount = order.TotalAmount,
            Gateway = method == PaymentMethod.CashOnDelivery ? "COD" : "UPI-QR"
        };

        if (method == PaymentMethod.CashOnDelivery)
        {
            payment.Status = PaymentStatus.Pending; // collected on delivery
            order.Status = OrderStatus.Confirmed;
        }
        else
        {
            // UPI payments start "Pending" — there's no automatic gateway callback for a plain
            // VPA, so the order stays Pending until PaymentsController.ConfirmPayment is called
            // (staff reconciliation) or the payment is verified some other way.
            var upiResult = await _paymentGateway.CreatePaymentOrderAsync(order.TotalAmount, order.OrderNumber);
            payment.GatewayOrderId = upiResult.GatewayReferenceId;
            payment.Status = PaymentStatus.Pending;
            upiIntentUri = upiResult.UpiIntentUri;
            upiQrBase64 = upiResult.QrCodeBase64Png;
        }

        _db.Payments.Add(payment);

        if (!string.IsNullOrWhiteSpace(req.CouponCode))
        {
            var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == req.CouponCode!.ToUpper());
            if (coupon != null) coupon.TimesUsed++;
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        // Fire-and-forget via Hangfire so a slow SMTP/SMS provider never delays the checkout
        // response. For COD/UPI both, we confirm receipt of the *order* immediately; a separate
        // "Payment Received" notification goes out later once UPI payment is reconciled.
        _backgroundJobs.Enqueue<IOrderNotificationService>(s => s.SendOrderConfirmationAsync(order.Id));

        return new OrderConfirmationDto(order.OrderNumber, order.TotalAmount, order.Status.ToString(),
            order.EstimatedDeliveryDate!.Value, upiIntentUri, upiQrBase64);
    }
}
