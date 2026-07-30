using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;

namespace PeachyGlamora.Api.Controllers;

[ApiController]
[Route("api/pincode")]
public class PincodeController : ControllerBase
{
    private readonly AppDbContext _db;
    public PincodeController(AppDbContext db) => _db = db;

    public record PincodeLookupDto(string State, List<string> Cities);

    // Backs the Checkout/Addresses pincode autofill — one indexed query
    // against our own DB instead of an external API round-trip.
    [HttpGet("{pincode}")]
    public async Task<IActionResult> Lookup(string pincode)
    {
        var rows = await _db.PincodePosts.Where(p => p.Pincode == pincode).ToListAsync();
        if (rows.Count == 0) return NotFound();

        var state = rows[0].StateName;
        var cities = rows.Select(r => r.District).Distinct().OrderBy(c => c).ToList();

        return Ok(new PincodeLookupDto(state, cities));
    }
}
