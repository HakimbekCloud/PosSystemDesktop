using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PosSystem.Services;

// Writes JSON audit entries for shared-to-tenant DB migration attempts. Each
// invocation creates a single file at:
//   %LocalAppData%\PosSystem\logs\migrations\migration-<utc>-<outcome>.json
//
// Files accumulate; retention is an operator concern. The JSON shape is the
// migration options + result + machine/user identity at the moment of the call.
//
// Secret redaction runs on the serialized JSON before write. Any JWT-shaped
// string (three base64url segments separated by dots, prefixed with the
// canonical "eyJ" JWT header) or DPAPI-encrypted blob (Phase 9 "enc:v1:"
// prefix) is replaced with a sentinel. This is defense-in-depth — current
// result DTOs don't carry tokens, but future FailureReason strings, error
// messages from external libraries, or settings dumps could.
public sealed class MigrationAuditLogger
{
    private readonly string _logDir;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // Matches RFC 7519-shaped JWTs (header.payload.signature, each base64url).
    // Minimum-length guards reduce false positives on unrelated base64 fragments.
    private static readonly Regex JwtPattern = new(
        @"eyJ[A-Za-z0-9_\-]{10,}\.eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}",
        RegexOptions.Compiled);

    // Matches the Phase 9 DPAPI ciphertext prefix used in the Settings table:
    //   enc:v1:<base64 of ProtectedData blob>
    private static readonly Regex DpapiPattern = new(
        @"enc:v1:[A-Za-z0-9+/=]{20,}",
        RegexOptions.Compiled);

    // Key-based redaction: any JSON property whose name matches one of these
    // sensitive-looking identifiers has its string value replaced regardless of
    // shape. Catches opaque tokens / API keys / passwords that wouldn't trip
    // the JWT or DPAPI regexes above. Snake-case and camelCase variants are
    // both listed; IgnoreCase handles PascalCase / SCREAMING_SNAKE_CASE.
    //
    // The value half uses `(?:\\.|[^"\\])*` to correctly consume escaped
    // quotes / backslashes inside the string literal — important so we don't
    // stop at a `\"` in the middle of a value.
    private const string SensitiveKeysAlternation =
        "auth_token|authToken|refresh_token|refreshToken|access_token|accessToken|" +
        "id_token|idToken|password|api_key|apiKey|secret|client_secret|clientSecret|" +
        "authorization|bearer";

    private const string JsonStringValue = "\"(?:\\\\.|[^\"\\\\])*\"";

    private static readonly Regex SensitiveKeyPattern = new(
        "\"(" + SensitiveKeysAlternation + ")\"\\s*:\\s*" + JsonStringValue,
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Setting-row redaction (Key/Value pair style). When AppSetting rows or any
    // dictionary-as-array-of-{Key,Value} payload is serialized, the property
    // names are just "Key" and "Value" — the sensitive name lives in the
    // *value* of the Key field. The direct SensitiveKeyPattern won't trip on
    // that, so two additional patterns match the pair as a unit (either
    // Key-then-Value or Value-then-Key order) and rewrite only the Value side.
    //
    // Capturing groups preserve the original property casing and the
    // surrounding whitespace produced by WriteIndented=true so the rewritten
    // file remains valid pretty-printed JSON.
    private static readonly Regex SettingRowKeyFirstPattern = new(
        "(?<KeyProp>\"(?:Key|key)\")(?<sep1>\\s*:\\s*)" +
        "\"(?<KeyName>" + SensitiveKeysAlternation + ")\"" +
        "(?<sep2>\\s*,\\s*)" +
        "(?<ValueProp>\"(?:Value|value)\")" +
        "(?<sep3>\\s*:\\s*)" + JsonStringValue,
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SettingRowValueFirstPattern = new(
        "(?<ValueProp>\"(?:Value|value)\")(?<sep1>\\s*:\\s*)" +
        JsonStringValue +
        "(?<sep2>\\s*,\\s*)" +
        "(?<KeyProp>\"(?:Key|key)\")" +
        "(?<sep3>\\s*:\\s*)" +
        "\"(?<KeyName>" + SensitiveKeysAlternation + ")\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string LogDirectory => _logDir;

    public MigrationAuditLogger()
    {
        _logDir = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "PosSystem", "logs", "migrations");
        Directory.CreateDirectory(_logDir);
    }

    public string Write(
        SharedToTenantMigrationOptions options,
        SharedToTenantMigrationResult result,
        MigrationVerificationReport? verification = null)
    {
        var stamp = System.DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var path = Path.Combine(_logDir, $"migration-{stamp}-{result.Outcome}.json");

        var entry = new
        {
            TimestampUtc = System.DateTime.UtcNow,
            MachineName  = System.Environment.MachineName,
            OsUser       = System.Environment.UserName,
            Options      = options,
            Result       = result,
            Verification = verification,
        };

        var rawJson = JsonSerializer.Serialize(entry, JsonOptions);
        var safeJson = RedactSecrets(rawJson);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, safeJson);
        if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
        else File.Move(tmp, path);

        return path;
    }

    // Internal for unit testing / debug verification; treat as private API.
    //
    // Four-pass redaction, applied in order:
    //   1. Setting-row Key-first  → rewrite the Value side of {"Key":"<sensitive>","Value":"..."}.
    //   2. Setting-row Value-first → same, for the reverse property order.
    //   3. Direct sensitive key  → any sensitive-named JSON property → "<redacted-by-key>".
    //   4. JWT-shape             → any remaining RFC 7519-shaped value → "<redacted-jwt>".
    //   5. DPAPI-shape           → any remaining "enc:v1:..." blob   → "<redacted-encrypted>".
    //
    // Setting-row passes run BEFORE the direct sensitive-key pass so a
    // dictionary-as-array-of-{Key,Value} payload doesn't slip through just
    // because the JSON property name is the generic "Key"/"Value" pair.
    //
    // Idempotent: re-running on already-redacted output yields the same string
    // because the sentinel values don't match any pattern.
    public static string RedactSecrets(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;

        var step1 = SettingRowKeyFirstPattern.Replace(json,
            "${KeyProp}${sep1}\"${KeyName}\"${sep2}${ValueProp}${sep3}\"<redacted-by-key>\"");

        var step2 = SettingRowValueFirstPattern.Replace(step1,
            "${ValueProp}${sep1}\"<redacted-by-key>\"${sep2}${KeyProp}${sep3}\"${KeyName}\"");

        var step3 = SensitiveKeyPattern.Replace(step2,
            match => $"\"{match.Groups[1].Value}\": \"<redacted-by-key>\"");

        var step4 = JwtPattern.Replace(step3, "<redacted-jwt>");
        var step5 = DpapiPattern.Replace(step4, "<redacted-encrypted>");
        return step5;
    }
}
