using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Services;

public interface IOrderNotificationService
{
    Task SendOrderConfirmationAsync(int orderId);
    Task SendOrderStatusUpdateAsync(int orderId, string newStatus);
    Task SendNewOrderAdminAlertAsync(int orderId);

    // Article-wise cancellation — fires every time a single item is
    // cancelled (regardless of whether the whole order also flips to
    // Cancelled). Unlike the other notifications above, this one shows the
    // customer the COMPLETE order (status + every item, with the cancelled
    // one clearly marked) alongside the reason and refund amount, so they
    // have full context rather than just a one-line "an item was cancelled".
    Task SendItemCancellationEmailAsync(int orderId, int cancelledItemId, string reason, decimal refundAmount, bool paymentReceived);

    // Fires when an admin corrects an AWB/tracking ID after it was already
    // set (see AdminOrdersController.UpdateTrackingId) — separate from the
    // initial "Shipped" notification since this is specifically about a
    // number changing, not a status change, and needs to show old → new so
    // the customer isn't confused if they already noted the original AWB.
    Task SendTrackingIdUpdatedEmailAsync(int orderId, string? oldTrackingId, string newTrackingId);
}

/// <summary>Composes and sends the order-confirmation email + SMS. Run via a Hangfire background
/// job (see OrderService.CheckoutAsync) rather than inline during checkout, so a slow SMTP/SMS
/// provider never makes the customer wait on the "Order Placed" screen.</summary>
public class OrderNotificationService : IOrderNotificationService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly ISmsService _sms;
    private readonly IConfiguration _config;
    private readonly ILogger<OrderNotificationService> _logger;

    // Support contact details shown on the item-cancellation email/SMS below.
    // Email reuses the same inbox as admin alerts (see
    // SendNewOrderAdminAlertAsync). WhatsApp number reuses the store's
    // published contact number from InvoicePdfService's CompanyPhone —
    // update here if a dedicated WhatsApp Business number is set up later.
    private const string SupportEmail = "peachyglamora@gmail.com";
    private const string SupportWhatsApp = "9021559122";

    // Official India Post tracking page — deliberately NOT a deep-link with
    // the consignment number pre-filled: the site requires a CAPTCHA on
    // every search, so there's no reliable way to auto-submit the number.
    // The email/SMS instead show the ID as plain, easy-to-copy text right
    // next to this link, and the customer pastes it in themselves.
    private const string IndiaPostTrackingUrl = "https://www.indiapost.gov.in/_layouts/15/dop.portal.tracking/trackconsignment.aspx";

    // NEW — " · Teal · Free" style suffix built from the snapshot fields, or
    // "" if the variant had neither a color nor a size. Centralized here so
    // every email template below shows this consistently rather than each
    // building its own ad-hoc string.
    private static string VariantLabel(OrderItem item)
    {
        var parts = new[] { item.ColorSnapshot, item.SizeSnapshot }.Where(p => !string.IsNullOrWhiteSpace(p));
        var joined = string.Join(" · ", parts);
        return string.IsNullOrEmpty(joined) ? "" : $" · {joined}";
    }

    public OrderNotificationService(
        AppDbContext db, IEmailService email, ISmsService sms, IConfiguration config, ILogger<OrderNotificationService> logger)
    {
        _db = db; _email = email; _sms = sms; _config = config; _logger = logger;
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
              <td style='padding:8px 0; border-bottom:1px solid #F0E4D8;'>{System.Net.WebUtility.HtmlEncode(i.ProductNameSnapshot)}{VariantLabel(i)} × {i.Quantity}</td>
              <td style='padding:8px 0; border-bottom:1px solid #F0E4D8; text-align:right;'>₹{i.UnitPriceSnapshot * i.Quantity:0.00}</td>
            </tr>"));

        // EstimatedDeliveryDate is stored/computed in UTC (DateTime.UtcNow.AddDays(5)
        // in OrderService) — convert to IST before showing it to the customer,
        // since this is rendered as plain text, not JSON (see IstTimeHelper).
        var deliveryDateIst = order.EstimatedDeliveryDate.HasValue
            ? IstTimeHelper.ToIst(order.EstimatedDeliveryDate.Value)
            : (DateTime?)null;

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
              <p>Estimated delivery: <b>{deliveryDateIst:dd MMM yyyy}</b></p>
              <p>Shipping to: {System.Net.WebUtility.HtmlEncode(order.ShippingAddress.Line1)}, {System.Net.WebUtility.HtmlEncode(order.ShippingAddress.City)} - {order.ShippingAddress.Pincode}</p>
              <p style='margin-top:24px; font-size:13px; color:#6E5147;'>Track this order any time from My Account → Orders.</p>
            </div>";

        if (!string.IsNullOrWhiteSpace(order.User.Email) && !order.User.Email!.EndsWith("@otp.peachyglamora.local"))
            await _email.SendAsync(order.User.Email!, $"Order Confirmed — {order.OrderNumber}", html);

        if (!string.IsNullOrWhiteSpace(order.User.PhoneNumber))
        {
            var sms = $"Hi {order.User.FullName.Split(' ')[0]}! Your Peachy Glamora order {order.OrderNumber} " +
                      $"(Rs.{order.TotalAmount:0}) is confirmed. Delivery by {deliveryDateIst:dd MMM}. Thank you for shopping with us!";
            await _sms.SendSmsAsync(order.User.PhoneNumber!, sms);
        }
    }

    // Fires once per order, right after checkout — separate from
    // SendOrderConfirmationAsync above (that one goes to the customer;
    // this one goes to the store, so it's a different recipient, subject,
    // and content — no reason to conflate the two into one method).
    // Sent to Smtp:FromEmail — the same address the store sends everything
    // FROM also doubles as the internal notification inbox, per the
    // existing appsettings.json convention (no separate config key added).
    public async Task SendNewOrderAdminAlertAsync(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.User)
            .Include(o => o.ShippingAddress)
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found — skipping admin new-order alert.", orderId);
            return;
        }

        var adminEmail = _config["Smtp:FromEmail"];
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            // Shouldn't happen in practice — Smtp:FromEmail is required for
            // every other email this service sends too — but guard anyway
            // rather than letting SendAsync fail with a confusing error.
            _logger.LogWarning("Smtp:FromEmail is not configured — skipping admin new-order alert for order {OrderId}.", orderId);
            return;
        }

        var itemRows = string.Join("", order.Items.Select(i => $@"
            <tr>
              <td style='padding:8px 0; border-bottom:1px solid #F0E4D8;'>{System.Net.WebUtility.HtmlEncode(i.ProductNameSnapshot)}{VariantLabel(i)} × {i.Quantity}</td>
              <td style='padding:8px 0; border-bottom:1px solid #F0E4D8; text-align:right;'>₹{i.UnitPriceSnapshot * i.Quantity:0.00}</td>
            </tr>"));

        var adminOrderLink = $"{_config["Frontend:BaseUrl"] ?? "http://localhost:4200"}/admin/orders/{order.Id}";

        var html = $@"
            <div style='font-family:Arial,sans-serif; max-width:560px; margin:auto; color:#3E2A24;'>
              <h2 style='color:#9C6650; font-weight:500;'>New Order Placed 🛍️</h2>
              <p style='font-size:15px;'>Order Number: <b>{order.OrderNumber}</b></p>

              <table style='width:100%; border-collapse:collapse; margin:14px 0; font-size:13.5px;'>
                <tr><td style='padding:4px 0; color:#6E5147;'>Customer</td><td style='padding:4px 0; text-align:right;'>{System.Net.WebUtility.HtmlEncode(order.User.FullName)}</td></tr>
                <tr><td style='padding:4px 0; color:#6E5147;'>Email</td><td style='padding:4px 0; text-align:right;'>{System.Net.WebUtility.HtmlEncode(order.User.Email ?? "—")}</td></tr>
                <tr><td style='padding:4px 0; color:#6E5147;'>Phone</td><td style='padding:4px 0; text-align:right;'>{System.Net.WebUtility.HtmlEncode(order.User.PhoneNumber ?? "—")}</td></tr>
                <tr><td style='padding:4px 0; color:#6E5147;'>Payment Method</td><td style='padding:4px 0; text-align:right;'>{order.Payment?.Method.ToString() ?? "—"}</td></tr>
              </table>

              <table style='width:100%; border-collapse:collapse; margin:18px 0;'>{itemRows}</table>
              <p style='font-size:16px;'><b>Total: ₹{order.TotalAmount:0.00}</b></p>

              <p style='font-size:13.5px; color:#6E5147;'>Shipping to: {System.Net.WebUtility.HtmlEncode(order.ShippingAddress.Line1)}, {System.Net.WebUtility.HtmlEncode(order.ShippingAddress.City)} - {order.ShippingAddress.Pincode}</p>

              <p style='margin-top:20px;'><a href='{adminOrderLink}' style='color:#9C6650;'>View this order in the admin panel →</a></p>
            </div>";

        await _email.SendAsync(adminEmail, $"New Order — {order.OrderNumber} (₹{order.TotalAmount:0.00})", html);
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

        // "Shipped" gets an extra tracking block appended — the AWB/consignment
        // ID plus a link to India Post's official tracking page. This was
        // present in an earlier version of this file and had gone missing —
        // restored here. See IndiaPostTrackingUrl above for why this can't
        // be a true deep-link (the site requires a CAPTCHA on every search).
        var trackingHtmlBlock = "";
        if (newStatus == "Shipped" && !string.IsNullOrWhiteSpace(order.TrackingId))
        {
            trackingHtmlBlock = $"""
                <div style="background:#FFF7F4;border:1px solid #F2DDD7;border-radius:12px;padding:16px;margin:16px 0;">
                    <strong>AWB / Tracking (Consignment) Number</strong><br>
                    <span style="font-size:17px; letter-spacing:0.5px;">{System.Net.WebUtility.HtmlEncode(order.TrackingId)}</span>
                    <p style="margin:10px 0 0; font-size:12.5px; color:#6E5147;">
                        Track it on the official India Post website:
                        <a href="{IndiaPostTrackingUrl}" style="color:#9C6650;">indiapost.gov.in → Track &amp; Trace</a>
                        — paste the number above into the Consignment Number field (a captcha is required on their site,
                        so this link can't fill it in automatically).
                    </p>
                </div>
                """;
            smsMessage += $" AWB: {order.TrackingId}. Track at indiapost.gov.in (Track & Trace).";
        }

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

        {trackingHtmlBlock}

        <p>✨ We'll keep you updated as your order progresses.</p>

        <p>Thank you for choosing <strong>Peachy Glamora</strong>.</p>

        <p style="color:#777;font-style:italic;">
            Crafted with elegance. Packed with care. Delivered with love.
        </p>
        """;

            await _email.SendAsync(order.User.Email!, subject, html);
        }
    }

    // Article-wise cancellation notification. Deliberately separate from
    // SendOrderStatusUpdateAsync("Cancelled") above — that one is for the
    // WHOLE order moving to Cancelled and has its own generic copy; this one
    // is specific to a single item being pulled from an order that otherwise
    // still ships, so it shows the customer the full picture: current order
    // status, every item on the order (with the cancelled one marked), the
    // admin's actual reason, the refund amount, an apology, and how to reach
    // support.
    public async Task SendItemCancellationEmailAsync(int orderId, int cancelledItemId, string reason, decimal refundAmount, bool paymentReceived)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found — skipping item cancellation notification.", orderId);
            return;
        }

        var cancelledItem = order.Items.FirstOrDefault(i => i.Id == cancelledItemId);
        if (cancelledItem == null)
        {
            _logger.LogWarning("OrderItem {ItemId} not found on order {OrderId} — skipping item cancellation notification.", cancelledItemId, orderId);
            return;
        }

        // Full order item list, with the cancelled item visually marked —
        // gives the customer the complete picture (what's still coming,
        // what isn't), not just the one line that got pulled.
        var itemRows = string.Join("", order.Items.Select(i =>
        {
            var isCancelledRow = i.Id == cancelledItemId || i.IsCancelled;
            var rowStyle = isCancelledRow ? "color:#B8524A; text-decoration:line-through;" : "";
            var tag = isCancelledRow ? " <span style='text-decoration:none; font-size:10.5px; font-weight:700; background:#FBE3DF; color:#B8524A; padding:2px 8px; border-radius:999px;'>Cancelled</span>" : "";
            return $@"
            <tr>
              <td style='padding:8px 0; border-bottom:1px solid #F0E4D8; {rowStyle}'>{System.Net.WebUtility.HtmlEncode(i.ProductNameSnapshot)}{VariantLabel(i)} × {i.Quantity}{tag}</td>
              <td style='padding:8px 0; border-bottom:1px solid #F0E4D8; text-align:right; {rowStyle}'>₹{i.UnitPriceSnapshot * i.Quantity:0.00}</td>
            </tr>";
        }));

        var refundLine = paymentReceived
            ? $"<p>A refund of <b>₹{refundAmount:0.00}</b> for this item will be processed to your registered payment method shortly.</p>"
            : "<p>No payment had been received for this item, so no refund is due.</p>";

        var html = $"""
        <div style="font-family:Arial,sans-serif; max-width:540px; margin:auto; color:#3E2A24;">
          <h2 style="color:#9C6650; font-weight:500;">An Item From Your Order Was Cancelled</h2>

          <p>Hi {System.Net.WebUtility.HtmlEncode(order.User.FullName)},</p>

          <p>We're writing to let you know that one item from your order has been cancelled by our team. Here are the full details:</p>

          <div style="background:#FFF7F4;border:1px solid #F2DDD7;border-radius:12px;padding:14px 16px;margin:18px 0; font-size:13.5px;">
            <strong>Order Number</strong><br>
            <span style="font-size:17px;">{order.OrderNumber}</span>
            <div style="margin-top:8px;">Current Order Status: <b>{order.Status}</b></div>
          </div>

          <h3 style="font-size:14px; margin-bottom:6px;">Cancelled Item</h3>
          <div style="background:#FBE3DF; border:1px solid #F0C4BC; border-radius:12px; padding:16px; margin-bottom:18px;">
            <strong>{System.Net.WebUtility.HtmlEncode(cancelledItem.ProductNameSnapshot)}</strong>{VariantLabel(cancelledItem)} × {cancelledItem.Quantity}
            <div style="font-size:13px; color:#6E5147; margin-top:6px;">
              Reason: {System.Net.WebUtility.HtmlEncode(reason)}
            </div>
            <div style="font-size:14px; margin-top:10px; font-weight:700;">
              Refund Amount: ₹{refundAmount:0.00}
            </div>
          </div>

          {refundLine}

          <h3 style="font-size:14px; margin-bottom:6px;">Your Full Order</h3>
          <table style="width:100%; border-collapse:collapse; margin-bottom:18px;">{itemRows}</table>
          <div style="display:flex; justify-content:space-between; padding-top:10px; border-top:1.5px solid #3E2A24; font-weight:800; font-size:14px;">
            <span>Order Total</span>
            <span>₹{order.TotalAmount:0.00}</span>
          </div>

          <p style="margin-top:22px;">The rest of your order is unaffected and will continue as usual.</p>

          <p style="margin-top:18px;">We sincerely apologise for any inconvenience this may have caused — this isn't the experience we want you to have with us.</p>

          <p style="margin-top:20px;">If you have any questions, concerns, or need help with anything related to this cancellation, please don't hesitate to reach out:</p>

          <div style="background:#FFF7F4;border:1px solid #F2DDD7;border-radius:12px;padding:14px 16px;margin:16px 0; font-size:13.5px;">
            📧 Email: <a href="mailto:{SupportEmail}" style="color:#9C6650;">{SupportEmail}</a><br>
            💬 WhatsApp: <a href="https://wa.me/91{SupportWhatsApp}" style="color:#9C6650;">+91 {SupportWhatsApp}</a>
          </div>

          <p style="margin-top:24px; font-size:13px; color:#6E5147;">Thank you for your patience — we're here to help.</p>
        </div>
        """;

        if (!string.IsNullOrWhiteSpace(order.User.Email) && !order.User.Email!.EndsWith("@otp.peachyglamora.local"))
            await _email.SendAsync(order.User.Email!, $"Item Cancelled — {order.OrderNumber}", html);

        if (!string.IsNullOrWhiteSpace(order.User.PhoneNumber))
        {
            var sms = $"Hi {order.User.FullName.Split(' ')[0]}, '{cancelledItem.ProductNameSnapshot}{VariantLabel(cancelledItem)}' from order {order.OrderNumber} " +
                      $"(status: {order.Status}) was cancelled. Reason: {reason}. " +
                      (paymentReceived ? $"Refund of Rs.{refundAmount:0} will be processed. " : "") +
                      $"Questions? WhatsApp us on {SupportWhatsApp} or email {SupportEmail}.";
            await _sms.SendSmsAsync(order.User.PhoneNumber!, sms);
        }
    }

    // Fires when an admin corrects an AWB/tracking ID that was already set
    // (see AdminOrdersController.UpdateTrackingId). Shows old → new so the
    // customer isn't left wondering whether the number they already noted
    // down is still correct.
    public async Task SendTrackingIdUpdatedEmailAsync(int orderId, string? oldTrackingId, string newTrackingId)
    {
        var order = await _db.Orders.Include(o => o.User).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found — skipping tracking ID update notification.", orderId);
            return;
        }

        var oldIdBlock = string.IsNullOrWhiteSpace(oldTrackingId)
            ? ""
            : $"""<div style="text-decoration:line-through; color:#B8524A; font-size:13px; margin-bottom:4px;">Previous: {System.Net.WebUtility.HtmlEncode(oldTrackingId)}</div>""";

        var html = $"""
        <div style="font-family:Arial,sans-serif; max-width:520px; margin:auto; color:#3E2A24;">
          <h2 style="color:#9C6650; font-weight:500;">Your Tracking Number Has Been Updated</h2>

          <p>Hi {System.Net.WebUtility.HtmlEncode(order.User.FullName)},</p>

          <p>The AWB / tracking (consignment) number for your order <b>{order.OrderNumber}</b> has been corrected. Please use the new number below going forward:</p>

          <div style="background:#FFF7F4;border:1px solid #F2DDD7;border-radius:12px;padding:16px;margin:18px 0;">
            {oldIdBlock}
            <strong>New AWB / Tracking Number</strong><br>
            <span style="font-size:19px; letter-spacing:0.5px;">{System.Net.WebUtility.HtmlEncode(newTrackingId)}</span>
            <p style="margin:10px 0 0; font-size:12.5px; color:#6E5147;">
                Track it on the official India Post website:
                <a href="{IndiaPostTrackingUrl}" style="color:#9C6650;">indiapost.gov.in → Track &amp; Trace</a>
                — paste the number above into the Consignment Number field (a captcha is required on their site,
                so this link can't fill it in automatically).
            </p>
          </div>

          <p>We apologise for any confusion this may have caused.</p>

          <p style="margin-top:20px;">Questions? Reach us at
            <a href="mailto:{SupportEmail}" style="color:#9C6650;">{SupportEmail}</a> or WhatsApp
            <a href="https://wa.me/91{SupportWhatsApp}" style="color:#9C6650;">+91 {SupportWhatsApp}</a>.
          </p>

          <p style="margin-top:24px; font-size:13px; color:#6E5147;">Thank you for your patience.</p>
        </div>
        """;

        if (!string.IsNullOrWhiteSpace(order.User.Email) && !order.User.Email!.EndsWith("@otp.peachyglamora.local"))
            await _email.SendAsync(order.User.Email!, $"Tracking Number Updated — {order.OrderNumber}", html);

        if (!string.IsNullOrWhiteSpace(order.User.PhoneNumber))
        {
            var sms = $"Hi {order.User.FullName.Split(' ')[0]}, the tracking number for order {order.OrderNumber} " +
                      $"has been updated. New AWB: {newTrackingId}. Track at indiapost.gov.in (Track & Trace).";
            await _sms.SendSmsAsync(order.User.PhoneNumber!, sms);
        }
    }
}
