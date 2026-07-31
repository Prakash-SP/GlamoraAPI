using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeachyGlamora.Api.Controllers;

[ApiController]
[Route("api/pincode")]
public class PincodeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PincodeController> _logger;

    private const string ExternalApiBase = "https://api.postalpincode.in/pincode";

    public PincodeController(AppDbContext db, IHttpClientFactory httpClientFactory, ILogger<PincodeController> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public record PincodeLookupDto(string State, List<string> Cities);

    // Backs the Checkout/Addresses pincode autofill. Tries our own DB first
    // (fast, indexed, no external dependency); if the pincode isn't in our
    // imported dataset yet, falls back to India Post's public API. That
    // fallback call happens server-side, so it isn't subject to the browser
    // CORS restriction a direct Angular call would hit. A successful
    // fallback result is cached into PincodePosts so future lookups for the
    // same pincode hit the DB directly.
    [HttpGet("{pincode}")]
    public async Task<IActionResult> Lookup(string pincode)
    {
        var dbResult = await LookupFromDb(pincode);
        if (dbResult != null) return Ok(dbResult);

        var externalResult = await LookupFromExternalApi(pincode);
        if (externalResult == null) return NotFound();

        await CacheResult(pincode, externalResult);
        return Ok(externalResult);
    }

    private async Task<PincodeLookupDto?> LookupFromDb(string pincode)
    {
        var rows = await _db.PincodePosts.Where(p => p.Pincode == pincode).ToListAsync();
        if (rows.Count == 0) return null;

        var state = rows[0].StateName;
        var cities = rows.Select(r => r.District).Distinct().OrderBy(c => c).ToList();
        return new PincodeLookupDto(state, cities);
    }

    private async Task<PincodeLookupDto?> LookupFromExternalApi(string pincode)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetAsync($"{ExternalApiBase}/{pincode}");
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();
            var results = JsonSerializer.Deserialize<List<PostOfficeApiResponse>>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            var result = results?.FirstOrDefault();
            if (result == null || result.Status != "Success" || result.PostOffice == null || result.PostOffice.Count == 0)
                return null;

            var state = result.PostOffice[0].State;
            var cities = result.PostOffice.Select(p => p.District).Distinct().OrderBy(c => c).ToList();
            return new PincodeLookupDto(state, cities);
        }
        catch (Exception ex)
        {
            // Network error, timeout, malformed response, etc. — treat the
            // same as "not found" so the caller falls through to manual entry.
            _logger.LogWarning(ex, "External pincode API lookup failed for {Pincode}", pincode);
            return null;
        }
    }

    // Warms the cache so the next lookup for this pincode is a DB hit. Best
    // effort — if this fails for some reason, we still return the result to
    // the caller; we just log and move on rather than failing the request.
    private async Task CacheResult(string pincode, PincodeLookupDto result)
    {
        try
        {
            var rowsToAdd = result.Cities.Select(city => new PincodePost
            {
                Pincode = pincode,
                OfficeName = "",
                District = city,
                StateName = result.State,
            });

            _db.PincodePosts.AddRange(rowsToAdd);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache external pincode result for {Pincode}", pincode);
        }
    }

    private class PostOfficeApiResponse
    {
        [JsonPropertyName("Message")]
        public string? Message { get; set; }

        [JsonPropertyName("Status")]
        public string? Status { get; set; }

        [JsonPropertyName("PostOffice")]
        public List<PostOffice>? PostOffice { get; set; }
    }

    private class PostOffice
    {
        [JsonPropertyName("Name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("District")]
        public string District { get; set; } = "";

        [JsonPropertyName("State")]
        public string State { get; set; } = "";
    }
}