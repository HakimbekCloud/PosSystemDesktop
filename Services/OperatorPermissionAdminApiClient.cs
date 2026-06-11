using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using PosSystem.Core.DTOs;

namespace PosSystem.Services;

// ── Phase 10.20G — Fail-closed wrapper around the operator permission
// admin read-only HTTP API (backend Phase 10.20F).
//
// Mirrors the existing OperatorPermissionApiClient (Phase 10.19C),
// OperatorAuditEvidenceApiClient (Phase 10.19G), and
// OperatorAuditReviewApiClient (Phase 10.19J). On HTTP / network /
// JSON deserialization failure it returns null so the dashboard never
// crashes; OperationCanceledException is re-thrown.
public sealed class OperatorPermissionAdminApiClient
{
    private readonly ApiClient _api;

    public OperatorPermissionAdminApiClient(ApiClient api)
    {
        _api = api;
    }

    public async Task<OperatorPermissionAdminPageDto<OperatorPermissionDefinitionAdminDto>?> GetDefinitionsAsync(
        OperatorPermissionDefinitionAdminQuery query, CancellationToken ct = default)
    {
        try { return await _api.GetOperatorPermissionDefinitionsAsync(query, ct); }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    public async Task<OperatorPermissionAdminPageDto<OperatorRolePermissionGrantAdminDto>?> GetRoleGrantsAsync(
        OperatorRoleGrantAdminQuery query, CancellationToken ct = default)
    {
        try { return await _api.GetOperatorPermissionRoleGrantsAsync(query, ct); }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    public async Task<OperatorPermissionAdminPageDto<OperatorUserPermissionOverrideAdminDto>?> GetUserOverridesAsync(
        OperatorUserOverrideAdminQuery query, CancellationToken ct = default)
    {
        try { return await _api.GetOperatorPermissionUserOverridesAsync(query, ct); }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    public async Task<OperatorPermissionEffectiveAdminDto?> GetEffectiveAsync(
        OperatorPermissionEffectiveAdminQuery query, CancellationToken ct = default)
    {
        try { return await _api.GetOperatorPermissionEffectiveAdminAsync(query, ct); }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    // ── Phase 10.21G — authoritative-mode status summary ─────────────────────
    //
    // Read-only. Fail-closed on any HTTP / network / deserialization error
    // → returns null so the dashboard never crashes. The desktop card
    // re-renders with a sanitized warning.
    public async Task<OperatorPermissionAuthoritativeStatusDto?> GetAuthoritativeStatusAsync(
        CancellationToken ct = default)
    {
        try { return await _api.GetOperatorPermissionAuthoritativeStatusAsync(ct); }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }
}

// ── Desktop-side re-redaction for permission admin responses.
//
// The backend (Phase 10.20F) already scrubs `reason` and
// `approvalTicketId`. The desktop re-scrubs every string value before
// it's added to a display row so any upstream gap is caught.
internal static class OperatorPermissionAdminRedaction
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
    private const int    MaxDisplayLength = 500;

    public static (string Sanitized, bool Redacted) ScrubAndTruncate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return (value ?? "", false);
        bool changed = false;
        string after = SecretPattern.Replace(value, m =>
        {
            changed = true;
            return Redacted;
        });
        foreach (var phrase in ConfirmationPhrases)
        {
            if (after.Contains(phrase, StringComparison.Ordinal))
            {
                after = after.Replace(phrase, Redacted);
                changed = true;
            }
        }
        if (after.Length > MaxDisplayLength)
        {
            after = after.Substring(0, MaxDisplayLength) + "…";
        }
        return (after, changed);
    }
}
