namespace PeachyGlamora.Api.Models;

// Single source of truth for GST rates by HSN code. Products reference this
// by HsnTaxRateId — tax is always looked up from here, never manually set
// per-product, so there's exactly one place the rate can be wrong/updated.
//
// Deliberately has NO reverse navigation collection back to Product
// (no `ICollection<Product> Products`). Every other circular-reference JSON
// crash this session came from a two-way navigation property; skipping it
// here avoids that whole bug class proactively rather than fixing it later.
public class HsnTaxRate
{
    public int Id { get; set; }
    public string HsnCode { get; set; } = default!;
    public string? Description { get; set; }
    public decimal TaxRatePercent { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
