using QRCoder;

namespace PeachyGlamora.Api.Services;

// Generates a PNG QR code (base64) encoding a standard UPI payment-intent
// URI, so admin staff can scan it with any UPI app and pay a refund
// directly to the customer's active UPI ID. Deliberately self-contained
// (QRCoder package) rather than reusing IPaymentGatewayService — that
// service exists to COLLECT payment from the customer at checkout; this is
// the reverse direction (staff paying the customer a refund) and has no
// dependency on the payment gateway at all.
//
// Requires the QRCoder NuGet package:
//   dotnet add package QRCoder
public static class RefundQrGenerator
{
    public static string GenerateBase64Png(string content)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        var bytes = qrCode.GetGraphic(20);
        return Convert.ToBase64String(bytes);
    }

    // Standard UPI deep-link format (same one used by every UPI app for
    // "pay to this VPA"): upi://pay?pa=<vpa>&pn=<name>&am=<amount>&cu=INR&tn=<note>
    public static string BuildUpiIntentUri(string vpa, string payeeName, decimal amount, string note)
    {
        var qs = string.Join("&", new[]
        {
            $"pa={Uri.EscapeDataString(vpa)}",
            $"pn={Uri.EscapeDataString(payeeName)}",
            $"am={amount:0.00}",
            "cu=INR",
            $"tn={Uri.EscapeDataString(note)}",
        });
        return $"upi://pay?{qs}";
    }
}
