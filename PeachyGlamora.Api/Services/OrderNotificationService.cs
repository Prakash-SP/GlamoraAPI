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
              <p>Your Peachy Glamora order has been confirmed.</p>
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

        var friendly = newStatus switch
        {
            "Shipped" => "has been shipped and is on its way",
            "OutForDelivery" => "is out for delivery today",
            "Delivered" => "has been delivered — we hope you love it!",
            "Cancelled" => "has been cancelled",
            _ => $"is now {newStatus}"
        };

        if (!string.IsNullOrWhiteSpace(order.User.PhoneNumber))
            await _sms.SendSmsAsync(order.User.PhoneNumber!, $"Your Peachy Glamora order {order.OrderNumber} {friendly}.");

        if (!string.IsNullOrWhiteSpace(order.User.Email) && !order.User.Email!.EndsWith("@otp.peachyglamora.local"))
            await _email.SendAsync(order.User.Email!, $"Order Update — {order.OrderNumber}",
                $"<p>Your order <b>{order.OrderNumber}</b> {friendly}.</p>");
    }
}
