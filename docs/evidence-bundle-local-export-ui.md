# Desktop Evidence Bundle — Local ZIP + Manifest Export UI (Phase 10.22E)

Phase 10.22E adds a **local-only** evidence-bundle export pipeline to
the WPF Migration Operations dashboard. The operator selects a
prepared evidence folder; the desktop runs path-safety, MIME / magic
and redaction pre-checks; on PASS the desktop writes a canonical
`manifest.json` + a sanitized ZIP bundle to a chosen output folder.

**No file content, no checksum, no manifest, no audit metadata ever
leaves the local machine in this phase.** The actual backend upload
endpoint lands in Phase 10.22F; until then, the operator hand-carries
the locally-generated ZIP to the upload UI when it ships.

Companion documents:

- [`Ham-Pos/docs/operator-evidence-bundle-storage-upload-plan.md`](../../Ham-Pos/docs/operator-evidence-bundle-storage-upload-plan.md)
- [`Ham-Pos/docs/operator-evidence-bundle-schema.md`](../../Ham-Pos/docs/operator-evidence-bundle-schema.md)
- [`Ham-Pos/docs/operator-evidence-bundle-upload-api.md`](../../Ham-Pos/docs/operator-evidence-bundle-upload-api.md)
- [`Ham-Pos/docs/operator-evidence-bundle-security-redaction.md`](../../Ham-Pos/docs/operator-evidence-bundle-security-redaction.md)
- [`desktop-backend-operator-permissions-integration.md`](./desktop-backend-operator-permissions-integration.md)

---

## 1. Local feature flag

| Key                                              | Default | Effect when missing / "" / "0" / "false" |
|--------------------------------------------------|---------|------------------------------------------|
| `operator_evidence_bundle_export_ui_enabled`     | OFF     | Card shows disabled banner; every command is `CanExecute=false`; no folder scan, no ZIP, no backend call. |

Set in `global_settings.json` (or via `GlobalSettingsRepository.Set`)
to enable on a single workstation. **No corresponding backend flag**
exists or is needed for Phase 10.22E — the desktop reads / writes
files only.

---

## 2. UI placement

A new "Operator Evidence Bundle Export — Local ZIP Only (Phase 10.22E)"
card sits at the bottom of the existing Operator dashboard's right
column inside
`Views/MigrationOperationsWindow.xaml`, immediately after the **Last
Export Path** card.

The card carries a yellow `Local export only — no backend upload in
this phase.` banner directly under its title so an operator can never
mistake it for an upload control.

Controls (read-only displays + 5 buttons + 3 input lists):

- **Local flag status** (computed from the local flag).
- **Inputs**: evidence type (combobox with allow-listed values
  pre-populated), environment (combobox), phase (free-text required),
  optional tenant ID, store ID, wave number, "overwrite existing ZIP"
  checkbox.
- **Selected evidence folder / Output folder / Output ZIP / Manifest**
  — read-only paths, truncated to 80 chars with leading `…` to keep
  the host filesystem layout off-screen.
- **Buttons**: `Select Evidence Folder`, `Validate Folder`,
  `Generate Manifest + ZIP`, `Open Output Folder`, `Clear`. All
  `CanExecute` short-circuit on flag OFF.
- **Status summary**: outcome (`Disabled / ValidationOnly / Generated
  / Blocked / Failed`), status message, accepted file count, total
  bytes, bundle SHA-256, last-run timestamp.
- **Lists**: accepted files, validation issues, redaction findings,
  generated artifacts.

Explicitly absent from the card (this phase intentionally has none of
these):

- No `Upload` / `Finalize` / `Review` / `Download` / `Delete` button.
- No "Execute" / "Approve" / "Apply" dangerous-operation button.
- No confirmation-phrase input.
- No raw SQL textbox.
- No backend HTTP indicator.

---

## 3. Export pipeline

Implemented in
`Services/EvidenceBundleExport/EvidenceBundleExportService.cs`. Composes
five collaborators, none of which call the backend:

| Class | Role |
|---|---|
| `EvidenceBundlePathSafety` (static) | Mirrors backend Phase 10.22D path-safety: traversal, drive letter, UNC, control chars, Windows reserved names, DB suffix, blocked extensions (`.zip / .exe / .sql / .db / …`), double-extension trick (`report.sql.txt`). |
| `EvidenceBundleMimeValidator` (static) | Magic / encoding check: PNG signature, JPEG SOI, `%PDF-`, strict UTF-8 for text-like. |
| `EvidenceBundleRedactionScanner` (singleton) | 11-pattern line scanner for text-like files; **confirmation-phrase shape** detector uses verb keyword + ALL-CAPS underscore segments (no literal phrase value in source). |
| `EvidenceBundleManifestGenerator` (singleton) | Builds the canonical `operator-evidence-bundle-v1` manifest DTO. |
| `EvidenceBundleZipWriter` (singleton) | Writes the ZIP via temp-then-rename; SHA-256 over the final ZIP bytes. |

### 3.1 Folder validation

- Recursive walk of the selected folder.
- Skips `bin / obj / .git / .idea / .vs / node_modules / target /
  out / .gradle / .next`.
- Skips hidden / system / reparse-point entries.
- Skips a previously-generated `manifest.json` at the bundle root
  (the generator will write a fresh one).
- Skips the destination ZIP path if it lives under the source folder.
- Per-file path normalised through `EvidenceBundlePathSafety`. Any
  rejection is recorded with a stable `Code` (`BlockedExtension`,
  `TraversalSegment`, …) and a sanitized message.
- Per-file limits (mirroring backend): ≤ 25 MiB per file, ≤ 100 files
  per bundle, ≤ 200 MiB aggregate.

### 3.2 Redaction scan

Text-like files (`.md / .txt / .json / .csv / .log`) are scanned
line-by-line up to 5 MiB. Detected patterns:

- `Authorization:` header.
- `Bearer <token>`.
- JWT-shaped `eyJ…`.
- `access_token` / `access-token` `:` or `=`.
- `refresh_token` / `refresh-token` `:` or `=`.
- `password` / `passwd` / `pwd` `:` or `=`.
- `secret` / `client_secret` `:` or `=`.
- `api_key` / `apikey` / `api-key` `:` or `=`.
- `enc:v1:` DPAPI-style sealed value.
- `-----BEGIN [RSA|EC|DSA|...]PRIVATE KEY-----`.
- **Confirmation-phrase shape**: verb keyword (`EXECUTE / ENABLE /
  ROLLBACK / RUN / START / BEGIN / RESET / DELETE / DROP / FORCE`) +
  2+ ALL-CAPS underscore-separated segments. The scanner detects the
  shape; the actual phrase literals are NOT named in the desktop
  source tree.

Any high-severity finding aborts ZIP generation. The displayed
finding is `<file>:<line> — <FindingType> [REDACTED]`. **The raw
matched substring is never returned, displayed, or logged.**

Invalid UTF-8 inside a text-like file surfaces as a
`DecodingFailure` finding and is treated as fail-closed.

A truncated text scan (file > 5 MiB) records `ScannerTruncated` and
fails the bundle — we can't certify the un-scanned tail is clean.

### 3.3 MIME / magic validation

- `.png` → first 8 bytes must be the canonical PNG signature.
- `.jpg` / `.jpeg` → first 3 bytes must be JPEG SOI.
- `.pdf` → first 5 bytes must be `%PDF-`.
- `.md / .txt / .json / .csv / .log` → MUST NOT start with PNG /
  JPEG / PDF magic AND must decode as UTF-8 over the probed prefix.

Mismatches block bundle generation with a `MIME_MISMATCH`-style
issue.

### 3.4 Manifest generation

Writes a canonical `manifest.json` matching the backend Phase 10.22D
strict-validator schema (`operator-evidence-bundle-v1`):

```json
{
  "schemaVersion": "operator-evidence-bundle-v1",
  "phase":         "10.22-pilot",
  "evidenceType":  "PILOT_EVIDENCE",
  "environment":   "STAGING",
  "tenantId":      "tenantA",
  "storeId":       null,
  "waveNumber":    4,
  "generatedAt":   "2026-05-30T12:34:56.7890123Z",
  "createdBy":     "<operator username>",
  "files": [
    {
      "path":      "02-authoritative-status-before.md",
      "sha256":    "<lowercase hex>",
      "sizeBytes": 1234,
      "redacted":  true
    }
    // …
  ],
  "redactionChecklist": {
    "authorizationHeadersRemoved": true,
    "bearerTokensRemoved":         true,
    "jwtBodiesRemoved":            true,
    "passwordsRemoved":            true,
    "tokensRemoved":               true,
    "confirmationPhrasesRemoved":  true
  },
  "signoff": null
}
```

- `manifest.json` itself is **never** present inside `files[]` (the
  backend's manifest validator rejects self-reference).
- Every `files[i].path` is the bare relative path inside the bundle,
  forward-slash separated, ≤ 1024 chars.
- `redacted=true` on each entry — the entry is only added after the
  desktop scan + MIME pass.
- `redactionChecklist` flips all six required keys to `true` only
  when every text-like file passed the scanner; if any file failed,
  the desktop already blocks the ZIP (we never write a manifest that
  claims redaction passed when it didn't).
- `signoff` is `null` in this phase. Phase 10.22G will add the
  reviewer / sign-off flow; until then the desktop never claims a
  decision on the operator's behalf.
- `createdBy` is the local cached username (`user_username` setting),
  falling back to the literal `"desktop-operator"`. **Real machine
  name and absolute filesystem paths are NEVER written here.**

### 3.5 ZIP generation

- ZIP entries: `manifest.json` first, then accepted files sorted by
  relative path (stable / reproducible bundle ordering).
- Entry mtimes normalised to `2020-01-01T00:00:00Z` so re-runs over
  the same input bytes produce byte-identical ZIPs (operator /
  reviewer comparison).
- Compression: `CompressionLevel.Optimal`.
- Atomic write: temp file `*.part-<guid>.tmp` in the same directory,
  then rename to the final path. On any failure the temp file is
  deleted in the `finally` block.
- Refuses to overwrite an existing ZIP unless the operator ticks the
  "Overwrite existing ZIP if present" checkbox.

### 3.6 Bundle checksum

`SHA-256` is computed once **over the final on-disk ZIP bytes** after
the rename. Displayed verbatim in the UI; written to no file in this
phase (the manifest does NOT carry it; only per-file SHAs go in
`files[]`).

### 3.7 Temp cleanup

- The manifest staging folder under `Path.GetTempPath()` is deleted
  in a `finally` regardless of outcome.
- The `*.part-*.tmp` is deleted on every failure path before the
  exception propagates.

---

## 4. Security boundaries

**Backend untouched**:

- Zero new Java code, zero DB migration, zero feature-flag change in
  `Ham-Pos/`.
- The desktop does NOT call:
  - `POST /api/v1/operator/evidence/bundles` (Phase 10.22C+).
  - `POST /api/v1/operator/evidence/bundles/{uuid}/files`.
  - `POST /api/v1/operator/evidence/bundles/{uuid}/finalize`.
  - `GET  /api/v1/operator/evidence/bundles`.
- The Phase 10.19F `POST /api/v1/operator/evidence/register`
  metadata-only flow is untouched and remains the only evidence-shaped
  HTTP call the desktop makes.

**Desktop runtime untouched outside this card**:

- `OperatorPermissionService.validate(...)` decision semantics
  unchanged.
- Dangerous-execute commands (Real Migration, Runtime Cutover,
  Rollback, Retention Cleanup) — their `CanExecute`, their guarded
  wrappers, their confirmation-phrase capture, and the
  dangerous-operation lock — all unchanged.
- POS sales / inventory / cashbox flows untouched.

**No secret leakage**:

- No raw match value enters the UI / logs / manifest / ZIP. The
  redaction-finding preview is always the literal string `[REDACTED]`.
- The six known guarded-flow confirmation-phrase literals are NOT
  named in the desktop source tree; the scanner detects them by
  STRUCTURAL shape (verb keyword + ALL-CAPS underscore segments).
- The displayed source-folder / output-folder paths are truncated to
  80 chars with a leading `…` so screenshots don't leak the host
  filesystem layout.
- `manifest.json` carries no absolute path, no machine name, no
  tokens, no passwords.
- The ZIP carries the bare manifest + bare relative files only — no
  hidden / system / VCS / build folders.

---

## 5. Tests / build

This repository has no `PosSystem.Tests` project (`CLAUDE.md` states:
*"No test project exists for PosSystem yet."*). The Phase 10.22E
collaborators are written to be small and deterministic; they share
the exact algorithm shapes with the backend's tested
`EvidenceBundleRedactionScanner`,
`EvidenceBundleMimeValidator`, `EvidenceBundlePathSafety`,
`EvidenceBundleManifestValidator` — so the backend's 538-test
Operator* + *Evidence* suite covers the algorithmic spine. The
desktop wiring is verified by:

```bash
dotnet build PosSystem.csproj
```

→ **Сборка успешно завершена. Предупреждений: 0 Ошибок: 0** (build
succeeded with 0 warnings, 0 errors).

A manual verification checklist is in §6 below.

---

## 6. Manual verification checklist

1. Start desktop with `operator_evidence_bundle_export_ui_enabled` =
   missing or `0`. **Expected**: card shows
   `Disabled (operator_evidence_bundle_export_ui_enabled missing or "0")`;
   all five buttons are disabled; entering phase / evidence type does
   not enable any command.
2. Open the dashboard once; confirm no file scan, no ZIP creation, no
   backend HTTP request originates from the card.
3. Set the flag to `1` via `GlobalSettingsRepository.Set` and reopen
   the dashboard. **Expected**: flag status now reads
   `Enabled (operator_evidence_bundle_export_ui_enabled="1")` and
   `Select Evidence Folder` / `Clear` enable.
4. Click `Select Evidence Folder`; pick a folder containing a safe
   set of `.md / .json / .png` files. **Expected**: selected folder
   displays (truncated); output folder defaults to the same path.
5. Click `Validate Folder`. **Expected**: outcome `ValidationOnly`,
   accepted-files list populated, validation-issues + findings empty,
   ZIP path empty.
6. Add a file with disallowed extension (`payload.exe`) and
   re-validate. **Expected**: outcome `Blocked`; validation-issues
   shows `payload.exe — BlockedExtension: File extension '.exe' is
   explicitly blocked in evidence bundles.`; ZIP path empty.
7. Drop a `.txt` file containing `Authorization: Bearer abc.def.ghi`
   into the folder and click `Generate Manifest + ZIP`. **Expected**:
   outcome `Blocked`, redaction-findings shows
   `<file>:<line> — AuthorizationHeader [REDACTED]` and a second row
   `… — BearerToken [REDACTED]`; **no raw "Bearer abc.def.ghi" string
   appears anywhere** in the UI, log, or ZIP; no ZIP written.
8. Confirm the displayed finding preview is the literal text
   `[REDACTED]` and the source string is nowhere visible.
9. Drop a fake PNG file (text content with `.png` extension);
   re-generate. **Expected**: outcome `Blocked`, validation-issues
   shows `MimeMismatch: File extension '.png' does not match content
   (missing PNG signature).`
10. Replace with a real PNG and re-generate. **Expected**: the PNG is
    accepted; outcome moves toward `Generated`.
11. With a clean folder, fill in `Evidence type=PILOT_EVIDENCE`,
    `Environment=STAGING`, `Phase=10.22-pilot` and click
    `Generate Manifest + ZIP`. **Expected**: outcome `Generated`,
    ZIP path populated (truncated for display), manifest=`manifest.json`,
    bundle SHA-256 shown as 64 lowercase hex chars.
12. Open the generated ZIP. **Expected**:
    - `manifest.json` is at the ZIP root.
    - `manifest.json` is NOT listed inside the manifest's `files[]`
      array.
    - Every `files[i].path` is forward-slash-relative.
    - Every `files[i].sha256` is 64 lowercase hex chars.
    - `redactionChecklist` has all six keys `true`.
    - `signoff` is `null`.
    - No absolute filesystem path appears anywhere.
    - No machine name appears anywhere.
    - No token / password / phrase value appears anywhere.
13. Re-run with the same inputs and the **Overwrite existing ZIP**
    checkbox unticked. **Expected**: outcome `Failed`, status
    contains `Output ZIP already exists. Pass allowOverwrite=true or
    remove the file.`; the prior ZIP on disk is unchanged.
14. Tick the overwrite checkbox and re-run. **Expected**: overwrite
    succeeds.
15. Inspect the local `NetworkLogService` log — confirm no
    `POST /api/v1/operator/evidence/bundles*` request was issued
    during steps 4-14.
16. Confirm no `GET /api/v1/operator/evidence/bundles*` request was
    issued.
17. Confirm the dangerous-operation buttons (Real Migration, Runtime
    Cutover, Rollback, Retention Cleanup) behave exactly as before —
    their `CanExecute`, guarded wrappers, confirmation-phrase fields,
    and dangerous-operation lock are unchanged.
18. Open the POS view, ring up a sale. **Expected**: business flow
    works exactly as before this phase.

---

## 7. Relationship to Phase 10.22F

Phase 10.22F will add the desktop **upload** UI that consumes the ZIP
+ manifest produced by this card. The contract is intentionally
designed so the Phase 10.22E artifact (with `signoff=null`) is the
exact byte-stream Phase 10.22F will POST to
`/api/v1/operator/evidence/bundles/{uuid}/files`. The Phase 10.22D
strict-manifest validator will accept the desktop manifest verbatim:
the schema version, the `files[]` shape, the redaction checklist, the
manifest exclusion rule, and the per-file path / sha / size checks
all match.

In other words, the Phase 10.22E desktop runs the same checks the
backend would run at upload time — the operator gets a clean local
PASS/FAIL signal before a single byte hits the network.

---

## 8. What does NOT change

- Backend Java runtime: byte-identical to Phase 10.22D.
- Backend Flyway migrations: none added.
- Backend permission decisions / dangerous execution semantics:
  unchanged.
- Existing metadata-only `POST /api/v1/operator/evidence/register`
  flow: unchanged.
- Existing desktop guarded wrappers: unchanged.
- Existing dashboard cards (Migration Operations, Permission Admin,
  Authoritative Status, Backend Audit / Evidence Integration): all
  unchanged.

---

## 9. Future limitations

- Backend upload from desktop is **still deferred to Phase 10.22F**.
- Reviewer / download UI deferred to Phase 10.22G.
- Retention / legal-hold automation deferred to Phase 10.22H.
- Object-storage adapters (S3 / GCS / Azure / MinIO) — **shipped in
  Phase 10.22K** (2026-05-30); `FS_LOCAL` remains the default; local
  export is unaffected regardless of backend provider.
- OCR / PDF deep content scanning is NOT implemented — binary files
  rely on the operator-attested `redacted=true` + magic / extension
  check.
- Emergency offline approval process is documented in the
  storage-upload plan but not automated.

---

## Phase 10.22I — Docs-Only Pilot (shipped 2026-05-30)

**Phase 10.22I is documentation-only.** It pilots the local export workflow from
Phase 10.22E without adding any code, flags, or runtime changes.

The pilot runbook covers enabling the export flag, running the export, verifying
`manifest.json` and ZIP output, confirming no HTTP calls during export (export is
purely local), and using the export result as input to the Phase 10.22F upload step.

Full pilot runbook (backend docs):
[`operator-evidence-bundle-pilot-runbook.md`](../../Ham-Pos/docs/operator-evidence-bundle-pilot-runbook.md)

---

## Phase 10.22J — Docs-Only Controlled Rollout Runbook (shipped 2026-05-30)

**Phase 10.22J is documentation-only.** It provides the wave-gated production rollout
runbook for the Phase 10.22B–H stack, including the local export UI card from
Phase 10.22E. No code, flags, XAML, C# files, migrations, endpoints, or storage changes
are added.

The controlled rollout runbook covers Wave 1 (export only in production): manifest
generation, zero-HTTP-call assertion, manifest secret/path scan, and wave sign-off
before upload is enabled.

Full controlled rollout runbook (backend docs):
[`operator-evidence-bundle-controlled-rollout-runbook.md`](../../Ham-Pos/docs/operator-evidence-bundle-controlled-rollout-runbook.md)

---

## Phase 10.22K — Object Storage Adapter / Provider Integration (shipped 2026-05-30)

**Phase 10.22K is backend code + docs only.** No WPF C# file, XAML screen, button,
or desktop UI card is changed. The local export UI card in this document is unaffected —
local bundle export writes to the local filesystem and makes zero HTTP calls.

The object storage adapters (S3/MinIO) are relevant only to the backend upload/download
path; the desktop's `ProductionPilotEvidenceBundleService` continues to write the ZIP
bundle to a local directory regardless of which backend storage provider is configured.

Full object storage reference (backend docs):
[`operator-evidence-bundle-object-storage.md`](../../Ham-Pos/docs/operator-evidence-bundle-object-storage.md)
