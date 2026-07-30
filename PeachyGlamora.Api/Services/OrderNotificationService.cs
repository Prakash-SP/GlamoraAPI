using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;

namespace PeachyGlamora.Api.Services;

public interface IOrderNotificationService
{
    Task SendOrderConfirmationAsync(int orderId);
    Task SendOrderStatusUpdateAsync(int orderId, string newStatus);
}

/// <summary>Composes and sends the order-confirmation email + SMS. Run via a Hangfire background
/// job (see OrderService.CheckoutAsync) rather than inline during checkout, so a slow SMTP/SMS
/// provider never makes the customer wait on the "Order Placed" screen.</summary>
public class OrderNotificationService : IOrderNotificationService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly ISmsService _sms;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(AppDbContext db, IEmailService email, ISmsService sms, ILogger<OrderNotificationService> logger)
    {
        _db = db; _email = email; _sms = sms; _logger = logger;
    }

    public async Task SendOrderConfirmationAsync(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.User)
            .Include(o => o.ShippingAddress)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found — skipping confirmation notification.", orderId);
            return;
        }

        var itemRows = string.Join("", order.Items.Select(i => $@"
            <tr>
              <td style='padding:8px 0; border-bottom:1px solid #F0E4D8;'>{System.Net.WebUtility.HtmlEncode(i.ProductNameSnapshot)} × {i.Quantity}</td>
              <td style='padding:8px 0; border-bottom:1px solid #F0E4D8; text-align:right;'>₹{i.UnitPriceSnapshot * i.Quantity:0.00}</td>
            </tr>"));

        var html = $@"
            <div style='font-family:Arial,sans-serif; max-width:520px; margin:auto; color:#3E2A24;'>
              <h2 style='color:#9C6650; font-weight:500;'>Thank you, {System.Net.WebUtility.HtmlEncode(order.User.FullName)}!</h2>
              <p>Your Peachy Glamora order has been Saved.</p>
              <p>Our team will verify your payment.</p>
              <p>Your order will be confirmed once the payment is verified.</p>
                <p style='margin-top:20px;'>
                  <strong>💖 Thank you for choosing Peachy Glamora. We'll notify you as soon as your order is confirmed.</strong>
                </p>
              <p style='font-size:15px;'>Order Number: <b>{order.OrderNumber}</b></p>
              <table style='width:100%; border-collapse:collapse; margin:18px 0;'>{itemRows}</table>
              <p style='font-size:16px;'><b>Total Paid: ₹{order.TotalAmount:0.00}</b></p>
              <p>Estimated delivery: <b>{order.EstimatedDeliveryDate:dd MMM yyyy}</b></p>
              <p>Shipping to: {System.Net.WebUtility.HtmlEncode(order.ShippingAddress.Line1)}, {System.Net.WebUtility.HtmlEncode(order.ShippingAddress.City)} - {order.ShippingAddress.Pincode}</p>
              <p style='margin-top:24px; font-size:13px; color:#6E5147;'>Track this order any time from My Account → Orders.</p>
            </div>";

        if (!string.IsNullOrWhiteSpace(order.User.Email) && !order.User.Email!.EndsWith("@otp.peachyglamora.local"))
            await _email.SendAsync(order.User.Email!, $"Order Confirmed — {order.OrderNumber}", html);

        if (!string.IsNullOrWhiteSpace(order.User.PhoneNumber))
        {
            var sms = $"Hi {order.User.FullName.Split(' ')[0]}! Your Peachy Glamora order {order.OrderNumber} " +
                      $"(Rs.{order.TotalAmount:0}) is confirmed. Delivery by {order.EstimatedDeliveryDate:dd MMM}. Thank you for shopping with us!";
            await _sms.SendSmsAsync(order.User.PhoneNumber!, sms);
        }
    }

    public async Task SendOrderStatusUpdateAsync(int orderId, string newStatus)
    {
        var order = await _db.Orders.Include(o => o.User).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) return;

        var (subject, title, emailMessage, smsMessage) = newStatus switch
        {
            "Pending" => (
                $"Order Received — {order.OrderNumber}",
                "Order Received 🛍️",
                "We've received your order and are waiting for payment verification. We'll notify you once it's confirmed.",
                $"Your Peachy Glamora order {order.OrderNumber} has been received and is awaiting payment verification."
            ),

            "Confirmed" => (
                $"Order Confirmed — {order.OrderNumber}",
                "Your Order Has Been Confirmed 🎉",
                "Thank you for your purchase! Your payment has been verified and your order is now confirmed.",
                $"🎉 Your Peachy Glamora order {order.OrderNumber} has been confirmed."
            ),

            "Processing" => (
                $"We're Preparing Your Order — {order.OrderNumber}",
                "We're Preparing Your Order ✨",
                "Our team is carefully preparing your order. We'll update you as soon as it's ready to ship.",
                $"✨ Your Peachy Glamora order {order.OrderNumber} is now being prepared."
            ),

            "Shipped" => (
                $"Order Shipped — {order.OrderNumber}",
                "Your Order Has Been Shipped 📦",
                "Great news! Your order has been shipped and is on its way.",
                $"📦 Your Peachy Glamora order {order.OrderNumber} has been shipped."
            ),

            "OutForDelivery" => (
                $"Out for Delivery — {order.OrderNumber}",
                "Out for Delivery 🚚",
                "Exciting news! Your order is out for delivery and should reach you today.",
                $"🚚 Your Peachy Glamora order {order.OrderNumber} is out for delivery today."
            ),

            "Delivered" => (
                $"Order Delivered — {order.OrderNumber}",
                "Your Order Has Been Delivered 💖",
                "Your order has been delivered successfully. We hope you love your Peachy Glamora purchase. Thank you for shopping with us!",
                $"💖 Your Peachy Glamora order {order.OrderNumber} has been delivered. Enjoy!"
            ),

            "Cancelled" => (
                $"Order Cancelled — {order.OrderNumber}",
                "Order Cancelled",
                "Your order has been cancelled. If this wasn't expected or you need assistance, please contact our support team.",
                $"Your Peachy Glamora order {order.OrderNumber} has been cancelled."
            ),

            "Returned" => (
                $"Return Received — {order.OrderNumber}",
                "Return Received ↩️",
                "We've received your returned order. Our team is inspecting it and will update you shortly.",
                $"↩️ We've received the return for your Peachy Glamora order {order.OrderNumber}."
            ),

            "RefundInitiated" => (
                $"Refund Initiated — {order.OrderNumber}",
                "Your Refund Has Been Initiated 💳",
                "Your refund has been initiated successfully. Depending on your payment method, it may take a few business days to appear in your account.",
                $"💳 Refund initiated for your Peachy Glamora order {order.OrderNumber}."
            ),

            "Refunded" => (
                $"Refund Completed — {order.OrderNumber}",
                "Your Refund Has Been Processed ✅",
                "Your refund has been processed successfully. Thank you for your patience, and we hope to serve you again in the future.",
                $"✅ Refund completed for your Peachy Glamora order {order.OrderNumber}."
            ),

            _ => (
                $"Order Update — {order.OrderNumber}",
                "Order Updated",
                $"Your order status has been updated to <strong>{newStatus}</strong>.",
                $"Your Peachy Glamora order {order.OrderNumber} status is now {newStatus}."
            )
        };

        if (!string.IsNullOrWhiteSpace(order.User.PhoneNumber))
        {
            await _sms.SendSmsAsync(order.User.PhoneNumber!, smsMessage);
        }

        if (!string.IsNullOrWhiteSpace(order.User.Email) &&
            !order.User.Email.EndsWith("@otp.peachyglamora.local"))
        {
            var html = $"""
        <h2 style="color:#D99084;margin-bottom:8px;">{title}</h2>

        <p>Hi {order.User.FullName ?? "there"},</p>

        <p>{emailMessage}</p>

        <div style="background:#FFF7F4;border:1px solid #F2DDD7;border-radius:12px;padding:16px;margin:24px 0;">
            <strong>Order Number</strong><br>
            <span style="font-size:18px;">{order.OrderNumber}</span>
        </div>

        <p>✨ We'll keep you updated as your order progresses.</p>

        <p>Thank you for choosing <strong>Peachy Glamora</strong>.</p>

        <p style="color:#777;font-style:italic;">
            Crafted with elegance. Packed with care. Delivered with love.
        </p>
        """;

            await _email.SendAsync(order.User.Email!, subject, html);
        }
    }
}
