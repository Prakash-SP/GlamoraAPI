using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/pincode-master")]
[Authorize(Roles = "Admin")]
public class AdminPincodeMasterController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminPincodeMasterController(AppDbContext db) => _db = db;

    [HttpGet("status")]
    public async Task<IActionResult> Status() =>
        Ok(new { totalRows = await _db.PincodePosts.CountAsync() });

    // One-time (or occasional re-run) import of India Post's public "All India
    // Pincode Directory" CSV from data.gov.in. Reads columns by NAME (not
    // position) so it works with the dataset's actual header row as-is —
    // no manual reordering needed before upload. Wipes and reloads the table
    // wholesale, which is the simplest correct behavior for a reference
    // dataset that's refreshed as a whole rather than incrementally.
    [HttpPost("import")]
    [RequestSizeLimit(50_000_000)] // dataset is roughly 20-30MB
    public async Task<IActionResult> Import(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "No file uploaded." });

        using var reader = new StreamReader(file.OpenReadStream());
        var headerLine = await reader.ReadLineAsync();
        if (headerLine == null) return BadRequest(new { error = "File is empty." });

        var headers = ParseCsvLine(headerLine).Select(h => h.Trim().ToLowerInvariant()).ToArray();

        int IndexOf(params string[] names) => Array.FindIndex(headers, h => names.Contains(h));

        var pinIdx = IndexOf("pincode", "pin_code", "pin code");
        var officeIdx = IndexOf("officename", "office_name", "office name");
        var districtIdx = IndexOf("districtname", "district_name", "district");
        var stateIdx = IndexOf("statename", "state_name", "state");

        if (pinIdx < 0 || districtIdx < 0 || stateIdx < 0)
            return BadRequest(new
            {
                error = "CSV must contain Pincode, District, and State columns (Office Name is optional). " +
                         "Found headers: " + string.Join(", ", headers)
            });

        _db.PincodePosts.RemoveRange(_db.PincodePosts);
        await _db.SaveChangesAsync();

        var batch = new List<PincodePost>(5000);
        int total = 0;
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = ParseCsvLine(line);
            if (cols.Length <= Math.Max(pinIdx, Math.Max(districtIdx, stateIdx))) continue;

            var pincode = cols[pinIdx].Trim();
            if (pincode.Length != 6 || !pincode.All(char.IsDigit)) continue; // skip malformed rows

            batch.Add(new PincodePost
            {
                Pincode = pincode,
                OfficeName = officeIdx >= 0 ? cols[officeIdx].Trim() : "",
                District = cols[districtIdx].Trim(),
                StateName = cols[stateIdx].Trim(),
            });
            total++;

            if (batch.Count >= 5000)
            {
                _db.PincodePosts.AddRange(batch);
                await _db.SaveChangesAsync();
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            _db.PincodePosts.AddRange(batch);
            await _db.SaveChangesAsync();
        }

        return Ok(new { message = "Import complete.", totalRows = total });
    }

    // Minimal quoted-comma-aware CSV split — not a full RFC 4180 parser, but
    // sufficient for this well-formed government dataset (office/district
    // names occasionally contain commas inside quotes).
    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (var c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); continue; }
            current.Append(c);
        }
        result.Add(current.ToString());
        return result.ToArray();
    }
}
