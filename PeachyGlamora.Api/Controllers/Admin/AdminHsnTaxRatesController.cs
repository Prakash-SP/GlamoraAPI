using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/hsn-tax-rates")]
[Authorize(Roles = "Admin")]
public class AdminHsnTaxRatesController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminHsnTaxRatesController(AppDbContext db) => _db = db;

    public record HsnTaxRateUpsertDto(string HsnCode, string? Description, decimal TaxRatePercent, bool IsActive);

    // Full list, not paginated — this is a lookup table, not a customer-facing
    // grid; expected to stay small (dozens of HSN codes, not thousands).
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _db.HsnTaxRates
            .OrderBy(h => h.HsnCode)
            .Select(h => new { h.Id, h.HsnCode, h.Description, h.TaxRatePercent, h.IsActive })
            .ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(HsnTaxRateUpsertDto dto)
    {
        if (await _db.HsnTaxRates.AnyAsync(h => h.HsnCode == dto.HsnCode))
            return BadRequest(new { error = $"HSN code \"{dto.HsnCode}\" already exists." });

        var rate = new HsnTaxRate
        {
            HsnCode = dto.HsnCode,
            Description = dto.Description,
            TaxRatePercent = dto.TaxRatePercent,
            IsActive = dto.IsActive,
        };
        _db.HsnTaxRates.Add(rate);
        await _db.SaveChangesAsync();

        return Ok(new { rate.Id, rate.HsnCode, rate.Description, rate.TaxRatePercent, rate.IsActive });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, HsnTaxRateUpsertDto dto)
    {
        var rate = await _db.HsnTaxRates.FindAsync(id);
        if (rate == null) return NotFound();

        if (await _db.HsnTaxRates.AnyAsync(h => h.HsnCode == dto.HsnCode && h.Id != id))
            return BadRequest(new { error = $"HSN code \"{dto.HsnCode}\" is already used by another entry." });

        rate.HsnCode = dto.HsnCode;
        rate.Description = dto.Description;
        rate.TaxRatePercent = dto.TaxRatePercent;
        rate.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();

        return Ok(new { rate.Id, rate.HsnCode, rate.Description, rate.TaxRatePercent, rate.IsActive });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var rate = await _db.HsnTaxRates.FindAsync(id);
        if (rate == null) return NotFound();

        // Products.HsnTaxRateId will be a required FK once Product.cs is
        // updated — block delete if any product still references this rate,
        // same pattern as AdminCategoriesController's category-in-use guard.
        if (await _db.Products.AnyAsync(p => p.HsnTaxRateId == id))
            return BadRequest(new { error = "Cannot delete an HSN code that's still assigned to products. Reassign those products first." });

        _db.HsnTaxRates.Remove(rate);
        await _db.SaveChangesAsync();
        return Ok();
    }
}
