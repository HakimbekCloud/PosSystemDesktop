using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using PosSystem.Core.DTOs;

namespace PosSystem.Services;

// ── Phase 10.19J — Fail-closed wrapper around the operator-audit-review
// HTTP API.
//
// Mirrors OperatorPermissionApiClient (Phase 10.19C) and
// OperatorAuditEvidenceApiClient (Phase 10.19G). The wrapper:
//   • swallows HTTP / network / JSON deserialization errors and returns
//     null so the dashboard never crashes when the backend is offline,
//   • re-throws OperationCanceledException for cooperative cancellation,
//   • never logs tokens, Authorization headers, or response bodies.
//
// The review API is read-only on the backend (Phase 10.19I). The desktop
// treats every backend response as informational; no UI action depends
// on the backend's verdict.
public sealed class OperatorAuditReviewApiClient
{
    private readonly ApiClient _api;

    public OperatorAuditReviewApiClient(ApiClient api)
    {
        _api = api;
    }

    public async Task<OperatorAuditEventPageDto?> GetEventsAsync(
        OperatorAuditReviewQuery query,
        CancellationToken ct = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));
        try
        {
            var page = await _api.GetOperatorAuditEventsAsync(query, ct);
            return page;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    public async Task<OperatorAuditEventDetailDto?> GetEventAsync(
        string eventId, CancellationToken ct = default)
    {
        try { return await _api.GetOperatorAuditEventAsync(eventId, ct); }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    public async Task<OperatorAuditEventDetailDto?> GetIntentAsync(
        string intentId, CancellationToken ct = default)
    {
        try { return await _api.GetOperatorAuditIntentAsync(intentId, ct); }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    public async Task<OperatorAuditEventDetailDto?> GetEvidenceAsync(
        string registrationId, CancellationToken ct = default)
    {
        try { return await _api.GetOperatorAuditEvidenceAsync(registrationId, ct); }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }
}

// ── Desktop-side re-redaction.
//
// The backend (Phase 10.19I) already sanitizes before responding. The
// desktop runs a parallel scrub over string values before rendering so
// any upstream gap is caught before the operator sees it. Returns the
// rewritten map and a boolean that flags whether any change was made.
internal static class OperatorAuditReviewRedaction
{
    private static readonly Regex SecretPattern = new(
        @"(?i)(bearer\s+[A-Za-z0-9._\-]+" +
        @"|eyJ[A-Za-z0-9._\-]{8,}" +
        @"|authorization\s*[:=]\s*\S+" +
        @"|access[_-]?token\s*[:=]\s*\S+" +
        @"|refresh[_-]?token\s*[:=]\s*\S+" +
        @"|password\s*[:=]\s*\S+" +
        @"|enc:v1:[A-Za-z0-9+/=._\-]+)",
        RegexOptions.Compiled);

    // Known confirmation-phrase literals the desktop must never display
    // even if the backend accidentally returns them. The list mirrors
    // the six phrases used by the local guarded wrapper services.
    private static readonly string[] ConfirmationPhrases = new[]
    {
        "EXECUTE_REAL_TENANT_DB_MIGRATION",
        "ENABLE_TENANT_DB_RUNTIME_MODE",
        "EXECUTE_TENANT_DB_RUNTIME_ROLLBACK",
        "ROLLBACK_TO_LEGACY_POS_DB",
        "I UNDERSTAND TENANT DB ROLLBACK",
        "EXECUTE_RETENTION_CLEANUP",
    };

    private const string Redacted = "[REDACTED]";

    public static (Dictionary<string, object>? Sanitized, bool Redacted) Sanitize(
        Dictionary<string, object>? input)
    {
        if (input is null || input.Count == 0)
        {
            return (input, false);
        }
        bool changed = false;
        var output = new Dictionary<string, object>(input.Count);
        foreach (var kv in input)
        {
            object? sanitized = SanitizeValue(kv.Value, ref changed);
            if (sanitized is not null)
            {
                output[kv.Key] = sanitized;
            }
        }
        return (output, changed);
    }

    private static object? SanitizeValue(object? value, ref bool changed)
    {
        if (value is null) return null;

        if (value is JsonElement je)
        {
            return SanitizeJsonElement(je, ref changed);
        }
        if (value is string s)
        {
            return SanitizeString(s, ref changed);
        }
        return value;
    }

    private static object? SanitizeJsonElement(JsonElement je, ref bool changed)
    {
        switch (je.ValueKind)
        {
            case JsonValueKind.String:
                return SanitizeString(je.GetString() ?? "", ref changed);
            case JsonValueKind.Number:
                if (je.TryGetInt64(out long l)) return l;
                if (je.TryGetDouble(out double d)) return d;
                return je.ToString();
            case JsonValueKind.True:  return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Null:  return null;
            case JsonValueKind.Object:
            {
                var nested = new Dictionary<string, object>();
                foreach (var p in je.EnumerateObject())
                {
                    object? sanitized = SanitizeJsonElement(p.Value, ref changed);
                    if (sanitized is not null)
                    {
                        nested[p.Name] = sanitized;
                    }
                }
                return nested;
            }
            case JsonValueKind.Array:
            {
                var list = new List<object?>();
                foreach (var e in je.EnumerateArray())
                {
                    list.Add(SanitizeJsonElement(e, ref changed));
                }
                return list;
            }
            default:
                return je.ToString();
        }
    }

    private static string SanitizeString(string value, ref bool changed)
    {
        if (string.IsNullOrEmpty(value)) return value;

        string after = SecretPattern.Replace(value, Redacted);
        if (!ReferenceEquals(after, value) && !after.Equals(value, StringComparison.Ordinal))
        {
            changed = true;
            value = after;
        }
        foreach (var phrase in ConfirmationPhrases)
        {
            if (value.Contains(phrase, StringComparison.Ordinal))
            {
                value = value.Replace(phrase, Redacted);
                changed = true;
            }
        }
        return value;
    }

    /// <summary>
    /// Flattens a sanitized metadata tree into key/value lines suitable
    /// for display in an ObservableCollection&lt;string&gt;. Nested maps and
    /// lists are joined with shallow paths (e.g. "includedFiles[0]",
    /// "warnings[2]"). The output is the same after each call.
    /// </summary>
    public static List<string> FlattenForDisplay(Dictionary<string, object>? sanitized)
    {
        var lines = new List<string>();
        if (sanitized is null) return lines;
        foreach (var kv in sanitized)
        {
            FlattenNode(kv.Key, kv.Value, lines);
        }
        return lines;
    }

    private static void FlattenNode(string path, object? value, List<string> lines)
    {
        if (value is null)
        {
            lines.Add($"{path} = null");
            return;
        }
        if (value is Dictionary<string, object> map)
        {
            foreach (var kv in map)
            {
                FlattenNode(path + "." + kv.Key, kv.Value, lines);
            }
            return;
        }
        if (value is List<object?> list)
        {
            int i = 0;
            foreach (var item in list)
            {
                FlattenNode($"{path}[{i++}]", item, lines);
            }
            return;
        }
        // Primitive: bool, long, double, string.
        var s = value switch
        {
            string str => str,
            bool b     => b ? "true" : "false",
            _          => value.ToString() ?? "",
        };
        if (s.Length > 500) s = s.Substring(0, 500) + "…";
        lines.Add($"{path} = {s}");
    }
}
