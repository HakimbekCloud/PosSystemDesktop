using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using PosSystem.Core.DTOs;

namespace PosSystem.Services;

// ── Phase 10.19G — Fail-closed wrapper around the operator audit-intent and
//                    evidence-registration HTTP endpoints.
//
// Mirrors OperatorPermissionApiClient (Phase 10.19C). The wrapper:
//   • swallows HTTP / network / JSON deserialization errors and returns null
//     so callers can surface a user-facing warning without crashing,
//   • re-throws OperationCanceledException (cooperative cancellation),
//   • never logs tokens, refresh tokens, Authorization headers, confirmation
//     phrases, raw payload bodies, or local DB / log / bundle file content.
//
// The audit-intent and evidence-registration endpoints are response-only in
// the backend (Phase 10.19F). Desktop callers therefore treat any backend
// failure as non-fatal — the desktop's local guarded wrappers, confirmation
// phrase, dangerous-operation lock, and the local Pilot Evidence Bundle on
// disk remain the operational source of truth in this phase.
public sealed class OperatorAuditEvidenceApiClient
{
    private readonly ApiClient _api;

    public OperatorAuditEvidenceApiClient(ApiClient api)
    {
        _api = api;
    }

    public async Task<OperatorAuditIntentResultDto?> RegisterIntentAsync(
        OperatorAuditIntentRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        try
        {
            return await _api.RegisterOperatorAuditIntentAsync(request, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Fail-closed: network failure, 4xx/5xx, JSON deserialization
            // failure all collapse to null. The caller (a dangerous-op Core
            // method) records a warning and continues into the guarded
            // wrapper unchanged.
            return null;
        }
    }

    public async Task<OperatorEvidenceRegisterResultDto?> RegisterEvidenceAsync(
        OperatorEvidenceRegisterRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        try
        {
            return await _api.RegisterOperatorEvidenceAsync(request, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }
}

// ── SHA-256 hex utilities used by Phase 10.19G evidence registration.
//
// Co-located with the wrapper so the evidence-registration call site has a
// single using directive. Hashing is intentionally minimal: streaming reads
// (no whole-file load) so a multi-megabyte bundle ZIP does not balloon
// memory.
internal static class OperatorAuditEvidenceHashing
{
    public static string Sha256HexOfString(string value)
    {
        if (value is null) return "";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return ToHex(hash);
    }

    public static string Sha256HexOfFile(string path)
    {
        using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: false);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[4096];
        int read;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            sha.AppendData(buffer, 0, read);
        }
        return ToHex(sha.GetHashAndReset());
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        for (int i = 0; i < bytes.Length; i++)
        {
            sb.Append(bytes[i].ToString("x2"));
        }
        return sb.ToString();
    }
}
