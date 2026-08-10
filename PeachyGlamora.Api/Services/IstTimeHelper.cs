namespace PeachyGlamora.Api.Services;

// Only needed for the handful of places that render a date directly into
// plain text — emails and the PDF invoice — rather than returning it as
// JSON to the frontend. JSON responses are already handled correctly by
// UtcDateTimeConverter + Angular's `date` pipe, which auto-converts to
// whatever timezone the browser is in; these two cases bypass all of that
// and need an explicit conversion since there's no browser involved.
public static class IstTimeHelper
{
    private static readonly TimeZoneInfo IstZone = ResolveIst();

    // "Asia/Kolkata" is the IANA id (Linux/macOS — your VPS). "India Standard
    // Time" is the Windows id (your local dev machine). Try IANA first since
    // that's where this actually needs to run correctly in production.
    private static TimeZoneInfo ResolveIst()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
    }

    public static DateTime ToIst(DateTime utcOrUnspecified)
    {
        // Same Kind=Unspecified issue as the JSON converter handles — a
        // value read back from SQL Server via EF Core needs to be
        // explicitly re-tagged as Utc before TimeZoneInfo will convert it
        // correctly; otherwise ConvertTimeFromUtc throws.
        var utc = utcOrUnspecified.Kind == DateTimeKind.Utc
            ? utcOrUnspecified
            : DateTime.SpecifyKind(utcOrUnspecified, DateTimeKind.Utc);

        return TimeZoneInfo.ConvertTimeFromUtc(utc, IstZone);
    }
}
