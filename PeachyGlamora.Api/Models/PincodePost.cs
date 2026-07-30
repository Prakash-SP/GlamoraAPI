namespace PeachyGlamora.Api.Models;

// One row per post office from India Post's public "All India Pincode Directory"
// dataset (data.gov.in). Imported once (and re-importable) via
// AdminPincodeMasterController — queried locally from then on instead of
// calling an external API on every checkout/address-form pincode entry.
public class PincodePost
{
    public int Id { get; set; }
    public string Pincode { get; set; } = default!;
    public string OfficeName { get; set; } = default!;
    public string District { get; set; } = default!;
    public string StateName { get; set; } = default!;
}
