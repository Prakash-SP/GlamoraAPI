using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeachyGlamora.Api.Services;

// Fixes a well-known EF Core + SQL Server gotcha: every DateTime in this
// codebase is written as DateTime.UtcNow (Kind = Utc), but SQL Server's
// datetime2 column type has no concept of "kind" — when EF Core reads a
// value back out, it comes back with Kind = Unspecified, even though the
// underlying value is still correct UTC. System.Text.Json then serializes
// an Unspecified-kind DateTime WITHOUT a trailing "Z", and per the ISO 8601
// spec, a timestamp with no zone marker is interpreted as already being
// LOCAL time — so the browser silently double-shifts every timestamp by
// the difference between UTC and the visitor's timezone (5:30 for IST).
//
// This converter forces every DateTime — on the way in AND out — to be
// explicitly tagged Kind = Utc, so the "Z" suffix is always present and
// every frontend page (which relies on Angular's `date` pipe correctly
// auto-converting UTC -> local time) displays the right time with zero
// changes needed on the Angular side.
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTime();
        return value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utcValue = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        // "O" (round-trip) format always includes the trailing "Z" for a
        // Utc-kind DateTime — e.g. "2026-07-31T05:00:00.0000000Z" — which
        // is exactly what tells the browser "this is UTC, convert me."
        writer.WriteStringValue(utcValue.ToString("O"));
    }
}
