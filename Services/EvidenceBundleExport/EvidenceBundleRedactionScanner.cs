using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PosSystem.Services.EvidenceBundleExport;

// Phase 10.22E desktop redaction scanner. Mirrors the backend Phase
// 10.22D EvidenceBundleRedactionScanner pattern-for-pattern so a
// file the desktop accepts will never be rejected by the backend
// upload-time scan (Phase 10.22F).
//
// Safety guarantees (identical to backend):
//   • No raw matched value is ever returned, logged, or stored.
//   • The Preview on every Finding is the literal "[REDACTED]" token.
//   • The confirmation-phrase pattern matches the SHAPE (verb keyword
//     + 2+ ALL-CAPS underscore-separated segments). The actual
//     guarded-flow phrase literals are never named in this file.
//   • Text-like files (.md / .txt / .json / .csv / .log) only;
//     binary files (.png / .jpg / .jpeg / .pdf) are passthrough.
//   • Strict UTF-8 decoding — invalid bytes surface as DecodingFailure
//     and the orchestrator fails closed.
//   • Per-file byte cap; truncation is reported via the result flag.
public sealed class EvidenceBundleRedactionScanner
{
    public const int DefaultMaxBytes = 5 * 1024 * 1024; // 5 MiB

    private static readonly HashSet<string> TextLikeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "md", "txt", "json", "csv", "log",
    };

    // Verb-led shape for confirmation phrases. Avoids false positives
    // on legitimate ALL-CAPS enum/audit constants (OPERATOR_AUDIT_…,
    // TENANT_ARCHIVED, …) which never start with one of these verbs.
    private static readonly Regex AuthorizationHeader = new(
        @"\bauthorization\s*:\s*\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BearerToken = new(
        @"\bbearer\s+[A-Za-z0-9._\-]{4,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex JwtLike = new(
        @"\beyJ[A-Za-z0-9._\-]{16,}",
        RegexOptions.Compiled);

    private static readonly Regex AccessTokenKv = new(
        @"\baccess[_\-]?token\b[""']?\s*[:=]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RefreshTokenKv = new(
        @"\brefresh[_\-]?token\b[""']?\s*[:=]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PasswordKv = new(
        @"\b(?:password|passwd|pwd)\b[""']?\s*[:=]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SecretKv = new(
        @"\b(?:client[_\-]?secret|secret)\b[""']?\s*[:=]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ApiKeyKv = new(
        @"\bapi[_\-]?key\b[""']?\s*[:=]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DpapiSealedValue = new(
        @"\benc:v1:[A-Za-z0-9+/=_\-]{4,}",
        RegexOptions.Compiled);

    private static readonly Regex PrivateKeyHeader = new(
        @"-----BEGIN (?:RSA |EC |DSA |OPENSSH |ENCRYPTED |PRIVATE)?PRIVATE KEY-----",
        RegexOptions.Compiled);

    private static readonly Regex ConfirmationPhraseShape = new(
        @"\b(?:EXECUTE|ENABLE|ROLLBACK|RUN|START|BEGIN|RESET|DELETE|DROP|FORCE)(?:_[A-Z0-9]{2,}){2,}\b",
        RegexOptions.Compiled);

    private static readonly (Regex Pattern, EvidenceBundleRedactionFindingType Type)[] Probes =
    {
        (AuthorizationHeader,  EvidenceBundleRedactionFindingType.AuthorizationHeader),
        (BearerToken,          EvidenceBundleRedactionFindingType.BearerToken),
        (JwtLike,              EvidenceBundleRedactionFindingType.JwtLike),
        (AccessTokenKv,        EvidenceBundleRedactionFindingType.AccessToken),
        (RefreshTokenKv,       EvidenceBundleRedactionFindingType.RefreshToken),
        (PasswordKv,           EvidenceBundleRedactionFindingType.Password),
        (SecretKv,             EvidenceBundleRedactionFindingType.Secret),
        (ApiKeyKv,             EvidenceBundleRedactionFindingType.ApiKey),
        (DpapiSealedValue,     EvidenceBundleRedactionFindingType.DpapiSealedValue),
        (PrivateKeyHeader,     EvidenceBundleRedactionFindingType.PrivateKey),
        (ConfirmationPhraseShape, EvidenceBundleRedactionFindingType.ConfirmationPhrase),
    };

    private readonly int _maxBytes;

    public EvidenceBundleRedactionScanner(int maxBytes = DefaultMaxBytes)
    {
        // Defensive lower bound; matches the backend's policy.
        _maxBytes = Math.Max(1024, maxBytes);
    }

    public int MaxBytes => _maxBytes;

    public sealed record ScanResult(
        IReadOnlyList<EvidenceBundleRedactionFinding> Findings,
        bool Truncated,
        ScannableType Type);

    public enum ScannableType { Text, BinaryPassthrough }

    public ScanResult Scan(string relativePath, Stream content)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(content);

        var lower = relativePath.ToLowerInvariant();
        var dot = lower.LastIndexOf('.');
        var ext = dot < 0 ? "" : lower[(dot + 1)..];
        if (!TextLikeExtensions.Contains(ext))
            return new ScanResult(Array.Empty<EvidenceBundleRedactionFinding>(),
                false, ScannableType.BinaryPassthrough);

        return ScanTextLike(relativePath, content);
    }

    private ScanResult ScanTextLike(string relativePath, Stream content)
    {
        var findings = new List<EvidenceBundleRedactionFinding>();
        var truncated = false;
        var bytesConsumed = 0L;
        var lineNumber = 0;

        // Strict UTF-8 — invalid byte sequence throws DecoderFallbackException,
        // which we capture as DecodingFailure (caller fails closed).
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        try
        {
            using var reader = new StreamReader(content, utf8, detectEncodingFromByteOrderMarks: false,
                bufferSize: 8 * 1024, leaveOpen: true);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                bytesConsumed += line.Length + 1L;
                InspectLine(relativePath, line, lineNumber, findings);
                if (bytesConsumed >= _maxBytes)
                {
                    truncated = true;
                    break;
                }
            }
        }
        catch (DecoderFallbackException)
        {
            findings.Add(new EvidenceBundleRedactionFinding(
                relativePath,
                EvidenceBundleRedactionFindingType.DecodingFailure,
                lineNumber));
        }
        catch (IOException)
        {
            findings.Add(new EvidenceBundleRedactionFinding(
                relativePath,
                EvidenceBundleRedactionFindingType.DecodingFailure,
                lineNumber));
        }

        return new ScanResult(findings, truncated, ScannableType.Text);
    }

    private static void InspectLine(
        string relativePath,
        string line,
        int lineNumber,
        List<EvidenceBundleRedactionFinding> findings)
    {
        if (string.IsNullOrEmpty(line)) return;
        foreach (var (pattern, type) in Probes)
        {
            if (pattern.IsMatch(line))
            {
                findings.Add(new EvidenceBundleRedactionFinding(relativePath, type, lineNumber));
            }
        }
    }
}
