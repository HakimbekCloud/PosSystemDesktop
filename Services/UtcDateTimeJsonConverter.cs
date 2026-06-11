using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PosSystem.Services;

// Robust ISO-8601 → UTC `DateTime` deserializers for the Ham-Pos backend.
//
// Why custom — System.Text.Json's built-in DateTime reader is strict ISO 8601
// with at most 7 fractional digits (because .NET DateTime resolution is 100 ns
// = 7 decimals). Jackson on the Java side serializes Instant / LocalDateTime
// with full nanosecond precision (9 digits), e.g. "2026-05-27T10:50:19.894196667Z".
// The built-in reader throws JsonException on those, which the UI surfaces as
// a sync error and which would also reject any future field that comes in at
// the same precision.
//
// What this converter accepts (all returned as DateTime, Kind = Utc):
//   • "2026-05-27T10:50:19.894196667Z"   — 9-digit fractional + Z
//   • "2026-05-27T10:50:19.894196"        — naive with sub-second
//   • "2026-05-27T10:50:19Z"              — Z, no fractional
//   • "2026-05-27T10:50:19"               — naive, no fractional
//   • "2026-05-27T10:50:19+05:00"         — explicit offset
//
// Naive timestamps (no Z / offset) are treated as UTC. The backend's LocalDateTime
// fields document this contract (Ham-Pos serializes everything in UTC).
//
// Failures throw JsonException with the offending value so they surface clearly
// instead of being silently mis-parsed.

internal static class IsoUtcDateTime
{
    // Trim fractional second digits beyond 7. Anchors at .digits before
    // either an offset or end-of-string. Non-capturing groups keep the
    // replacement compact.
    private static readonly Regex ExcessFractionalSeconds = new(
        @"(\.\d{7})\d+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static DateTime ParseToUtc(string raw, string? fieldHint = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new JsonException($"Empty DateTime value{FormatHint(fieldHint)}");

        // First pass — accept as-is. Covers everything S.T.J would normally
        // accept plus offset-bearing values.
        if (DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var dto))
            return dto.UtcDateTime;

        // Second pass — strip excess fractional precision (Java nanoseconds).
        var trimmed = ExcessFractionalSeconds.Replace(raw, "$1");
        if (trimmed != raw &&
            DateTimeOffset.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out dto))
            return dto.UtcDateTime;

        // Third pass — naive timestamp without offset; assume UTC and normalize.
        if (DateTime.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dt))
            return dt;

        throw new JsonException($"Cannot parse ISO-8601 timestamp '{raw}'{FormatHint(fieldHint)}");
    }

    private static string FormatHint(string? hint) =>
        string.IsNullOrEmpty(hint) ? "" : $" (field: {hint})";
}

internal sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            throw new JsonException("Unexpected null for non-nullable DateTime");
        var raw = reader.GetString();
        return IsoUtcDateTime.ParseToUtc(raw!, typeToConvert.Name);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        // Round-trip ISO with explicit UTC marker — matches what the backend expects
        // on the request side (existing code uses ToString("O") for outbound watermarks).
        writer.WriteStringValue(value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
}

internal sealed class NullableUtcDateTimeJsonConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var raw = reader.GetString();
        if (string.IsNullOrEmpty(raw)) return null;
        return IsoUtcDateTime.ParseToUtc(raw, typeToConvert.Name);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        writer.WriteStringValue(value.Value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }
}
