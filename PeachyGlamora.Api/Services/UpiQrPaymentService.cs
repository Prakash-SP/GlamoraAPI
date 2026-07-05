using QRCoder;

namespace PeachyGlamora.Api.Services;

/// <summary>What the customer's checkout screen needs to render a scannable UPI payment.</summary>
public record PaymentInitiationResult(string GatewayReferenceId, string UpiIntentUri, string QrCodeBase64Png);

public interface IPaymentGatewayService
{
    Task<PaymentInitiationResult> CreatePaymentOrderAsync(decimal amount, string orderNumber);
}

/// <summary>
/// Generates a standard NPCI UPI deep-link ("upi://pay?...") and renders it as a QR code that any
/// UPI app (GPay, PhonePe, Paytm, BHIM) can scan and pre-fill.
///
/// IMPORTANT LIMITATION: a plain merchant VPA has no API that tells your server a payment
/// succeeded — banks don't expose that to individuals/small merchants. This service only
/// *generates* the payment request. Confirming that money actually landed has to happen one of
/// two ways, both implemented below:
///   1. Manual reconciliation — staff match incoming bank SMS/UPI app alerts against pending
///      orders and confirm via PaymentsController.ConfirmPayment (Admin-only).
///   2. Customer self-report — the customer taps "I've Paid" after completing the UPI transfer,
///      which flips the order to "Payment Under Verification" so ops can prioritise it.
/// If you later want automatic, real-time confirmation, swap this service for a UPI-enabled
/// aggregator (Razorpay, Cashfree, PhonePe PG, PayU) that exposes a webhook — the
/// IPaymentGatewayService interface is intentionally the seam to do that swap later.
/// </summary>
public class UpiQrPaymentService : IPaymentGatewayService
{
    private readonly IConfiguration _config;
    public UpiQrPaymentService(IConfiguration config) => _config = config;

    public Task<PaymentInitiationResult> CreatePaymentOrderAsync(decimal amount, string orderNumber)
    {
        var vpa = _config["Upi:VpaId"]
            ?? throw new InvalidOperationException("Upi:VpaId is not configured in appsettings (e.g. peachyglamora@okhdfcbank).");
        var payeeName = _config["Upi:PayeeName"] ?? "Peachy Glamora";

        var uri = "upi://pay" +
                  $"?pa={Uri.EscapeDataString(vpa)}" +
                  $"&pn={Uri.EscapeDataString(payeeName)}" +
                  $"&tr={Uri.EscapeDataString(orderNumber)}" +   // transaction reference, used for reconciliation
                  $"&am={amount.ToString("0.00")}" +
                  "&cu=INR" +
                  $"&tn={Uri.EscapeDataString("Peachy Glamora Order " + orderNumber)}";

        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        // PngByteQRCode is pure-managed (no System.Drawing dependency), so it works fine on
        // Linux containers — important since this deploys to Railway/Render, not Windows.
        var png = new PngByteQRCode(qrData).GetGraphic(20);
        var base64 = "data:image/png;base64," + Convert.ToBase64String(png);

        return Task.FromResult(new PaymentInitiationResult(orderNumber, uri, base64));
    }
}
