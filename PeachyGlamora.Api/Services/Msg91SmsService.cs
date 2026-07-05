using System.Net.Http.Json;

namespace PeachyGlamora.Api.Services;

/// <summary>Sends real SMS via MSG91 (widely used for Indian DLT-registered SMS/OTP traffic —
/// swap the request body/endpoint for Twilio or Gupshup's WhatsApp Business API if you need a
/// different provider; everything else in the app only depends on ISmsService).</summary>
public class Msg91SmsService : ISmsService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<Msg91SmsService> _logger;

    // Registered via builder.Services.AddHttpClient<ISmsService, Msg91SmsService>() in Program.cs,
    // which injects a pooled, pre-configured HttpClient here (avoids the classic "new HttpClient()
    // per call" socket-exhaustion mistake).
    public Msg91SmsService(HttpClient http, IConfiguration config, ILogger<Msg91SmsService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task SendSmsAsync(string phoneNumber, string message)
    {
        var apiKey = _config["Sms:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "CHANGE_ME")
        {
            // Config not filled in yet (e.g. local dev) — don't throw and don't silently pretend
            // it worked either; log clearly so it's obvious SMS is not actually going out.
            _logger.LogWarning("SMS NOT sent (Sms:ApiKey not configured). Would have sent to {Phone}: {Message}", phoneNumber, message);
            return;
        }

        var mobile = NormalizeToE164WithoutPlus(phoneNumber);
        if (mobile == null)
        {
            _logger.LogWarning("Skipping SMS — could not normalize phone number: {Phone}", phoneNumber);
            return;
        }

        var payload = new
        {
            sender = _config["Sms:SenderId"] ?? "PEACHY",
            route = _config["Sms:Route"] ?? "4",
            country = "91",
            sms = new[] { new { message, to = new[] { mobile } } }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.msg91.com/api/v2/sendsms")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("authkey", apiKey);

        try
        {
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("MSG91 SMS send failed for {Phone}: {Status} {Body}", phoneNumber, response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            // SMS failures must never break the calling flow (OTP request, order confirmation) —
            // the order/account already exists; log and let ops investigate delivery separately.
            _logger.LogError(ex, "Exception sending SMS to {Phone}", phoneNumber);
        }
    }

    private static string? NormalizeToE164WithoutPlus(string phoneNumber)
    {
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            10 => "91" + digits,                 // bare Indian mobile number
            12 when digits.StartsWith("91") => digits, // already has country code
            _ => digits.Length >= 10 ? digits : null
        };
    }
}
