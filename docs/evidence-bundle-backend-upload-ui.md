# Desktop Evidence Bundle — Backend Upload + Finalize UI (Phase 10.22F)

Phase 10.22F wires the WPF desktop to the **already-live** Phase 10.22C/D
backend evidence bundle API. The operator takes the local `manifest.json`
+ files produced by the Phase 10.22E export pipeline and uploads them
to `/api/v1/operator/evidence/bundles` + `/{uuid}/files` + `/{uuid}/finalize`,
with no `.zip` upload, no review / download / retention button, and no
backend code change.

Companion documents:

- [`evidence-bundle-local-export-ui.md`](./evidence-bundle-local-export-ui.md) — Phase 10.22E local export
- [`../../Ham-Pos/docs/operator-evidence-bundle-upload-api.md`](../../Ham-Pos/docs/operator-evidence-bundle-upload-api.md) — backend Phase 10.22C/D API
- [`../../Ham-Pos/docs/operator-evidence-bundle-security-redaction.md`](../../Ham-Pos/docs/operator-evidence-bundle-security-redaction.md) — backend Phase 10.22D hardening
- [`../../Ham-Pos/docs/operator-evidence-bundle-schema.md`](../../Ham-Pos/docs/operator-evidence-bundle-schema.md) — backend Phase 10.22B schema
- [`desktop-backend-operator-permissions-integration.md`](./desktop-backend-operator-permissions-integration.md)

---

## 1. Purpose

The Phase 10.22E export card produces a sanitized `manifest.json` + a
local `.zip` archive. Phase 10.22F is the **second half** of the
operator workflow: it ships the manifest + each manifest-listed file
to the backend behind a default-OFF desktop flag, then finalizes the
bundle so the backend marks it `FINALIZED` with a server-computed
SHA-256.

Phase 10.22F adds **zero new backend code, zero DB migration, zero
new endpoint**. The pipeline consumes the Phase 10.22C upload API +
Phase 10.22D hardening exactly as the backend ships today.

---

## 2. Local upload flag

| Key                                              | Default | Effect when missing / "" / "0" / "false" |
|--------------------------------------------------|---------|------------------------------------------|
| `operator_evidence_bundle_upload_ui_enabled`     | OFF     | Card shows disabled banner; every command is `CanExecute=false`; no folder scan, no backend HTTP call. |

The Phase 10.22E local-export flag
(`operator_evidence_bundle_export_ui_enabled`) is **independent**:

- Export flag ON + upload flag OFF: the desktop can generate the local
  ZIP but the upload card stays disabled.
- Export flag OFF + upload flag ON: the desktop can still upload if the
  operator manually selects a folder containing a valid
  Phase 10.22E-shaped `manifest.json` (e.g. a hand-carried bundle from
  another workstation).

**Backend evidence bundle API flag** (`operator.evidence.bundle.api.enabled`,
default OFF) is the master gate: when the backend has it OFF, every
Phase 10.22F call returns HTTP 503 `FEATURE_FLAG_OFF` and the desktop
surfaces the typed error in the card.

---

## 3. Relationship to Phase 10.22E

The contract between Phase 10.22E and Phase 10.22F is the
`operator-evidence-bundle-v1` manifest. Phase 10.22F:

- Reads `manifest.json` from the operator-selected folder root.
- Re-validates the manifest using `EvidenceBundleUploadService.ValidateManifestLocally`,
  which mirrors the backend Phase 10.22D strict-validator gates
  (schema version, file matching, sha/size parity, redaction
  checklist all-true, no `manifest.json` in `files[]`, path safety).
- Uploads `manifest.json` first (relativePath = `manifest.json`,
  redacted=true, declaredSha256 = local SHA-256).
- Uploads every `manifest.files[]` entry in **stable ordinal order**
  (`StringComparer.Ordinal` over the path).
- Finalizes the bundle.

Anything Phase 10.22E rejected (high-severity redaction finding,
MIME mismatch, blocked extension, double-extension trick) never lands
in `manifest.files[]`, so it never reaches Phase 10.22F.

---

## 4. Why the ZIP is not uploaded

The Phase 10.22D backend explicitly rejects `.zip` uploads
(`EvidenceBundlePathSafety.BlockedExtensions` includes `zip / 7z /
rar / tar / gz / tgz / bz2 / xz`) to defeat zip-bomb / nested-archive
attacks. The backend wants the manifest + raw files; it does its own
storage, its own per-file SHA-256, its own server-computed
`bundleSha256` over the manifest hash.

The Phase 10.22E `.zip` therefore remains a **local archive** for
operator portability (USB stick to a sister site, attachment to a
change-management ticket). The desktop card surfaces the local ZIP
path read-only with the label `Local archive only — not uploaded.`
so the operator can never confuse it with the upload target.

---

## 5. Upload + finalize pipeline

```
1. flag gate (operator_evidence_bundle_upload_ui_enabled)
2. parse manifest.json from selected folder
3. local strict validation
     (ValidateManifestLocally mirrors backend Phase 10.22D)
4. POST /api/v1/operator/evidence/bundles    (create DRAFT)
5. POST /{uuid}/files (multipart) for manifest.json
6. for each manifest.files[] entry (ordinal sort):
     POST /{uuid}/files (multipart, declaredSha256 from manifest)
7. POST /{uuid}/finalize
8. surface final {status, bundleSha256, fileCount} in UI
```

Each backend call returns an `EvidenceBundleApiCallOutcome<T>`:

- `Succeeded=true` → continue.
- `Succeeded=false` → stop the workflow, surface
  `{ErrorCode, SafeMessage}` (e.g. `REDACTION_FAILED`, `MIME_MISMATCH`,
  `MANIFEST_INVALID`, `DUPLICATE_FILE`, `FEATURE_FLAG_OFF`, `FORBIDDEN`,
  `NOT_FOUND`) in the card. The bundle UUID is preserved on the result
  if `Create` succeeded, so the operator can re-run the workflow or
  call `Refresh Backend Status` to inspect the partial state.

Backend behaviours preserved:

- 503 `FEATURE_FLAG_OFF` when the backend API flag is OFF.
- 403 `FORBIDDEN` for CASHIER, foreign-tenant ADMIN.
- 409 `DUPLICATE_FILE` when re-uploading the same `(uuid, path)`.
- 400 `REDACTION_FAILED` / `MIME_MISMATCH` / `MANIFEST_INVALID` /
  `UNSAFE_FILE_TYPE` / `CHECKSUM_MISMATCH`.
- 409 `INVALID_STATUS` when uploading to a FINALIZED bundle or
  finalizing a non-DRAFT bundle.

---

## 6. Retry / resume policy

Phase 10.22F is intentionally **conservative**:

- **Create succeeds, upload fails** → the bundle UUID stays on the
  result so the operator can inspect it. **No automatic retry**. The
  operator can call `Refresh Backend Status` to verify state and may
  re-run `Upload + Finalize` only if the local folder is unchanged.
- **Duplicate file** (`DUPLICATE_FILE`) — the desktop does NOT
  silently treat this as a recoverable success. The workflow halts so
  the operator can compare the local + backend file metadata via the
  Refresh button and decide whether to create a new bundle.
- **Finalize fails** (`MANIFEST_INVALID`, `REDACTION_FAILED` on the
  manifest itself, etc.) — the bundle stays DRAFT on the backend; the
  desktop does NOT auto-retry. The operator is told to regenerate the
  manifest via Phase 10.22E and try again.
- **Network interruption** (DNS / TLS / socket reset) — the wrapper
  converts the exception to `NETWORK_FAILURE` and the workflow halts;
  the bundle UUID (if create succeeded) stays visible.
- **No background uploads** — every step runs on the WPF dispatcher
  with a real progress reporter; closing the window cancels via the
  `CancellationToken` flowed through `RelayCommand`.
- **No delete endpoint** — Phase 10.22D intentionally doesn't ship a
  delete endpoint (the FUTURE_ONLY `operator.evidence.bundle.delete.admin`
  key is reserved for a co-signed admin flow); the desktop therefore
  never tries to clean up a half-uploaded bundle. The card tells the
  operator to contact an admin if backend cleanup is needed.

---

## 7. Security boundaries

- ✅ **No backend code changed** — Phase 10.22F adds zero new Java
  files, zero DB migration, zero new endpoint. The pipeline consumes
  the existing Phase 10.22C/D API verbatim.
- ✅ **No object storage** — backend storage adapter remains the
  `FS_LOCAL` shipped in Phase 10.22C; the desktop never sees it.
- ✅ **No dangerous execution change** — Real Migration / Runtime
  Cutover / Rollback / Retention Cleanup `CanExecute`, their guarded
  wrappers, the dangerous-operation lock, and the confirmation-phrase
  capture are unchanged.
- ✅ **No guarded wrapper change.**
- ✅ **No confirmation phrase movement** — the desktop source tree
  never names any of the six known guarded-flow literals. The
  Phase 10.22E redaction scanner ran client-side before the manifest
  was generated; the backend re-scans on upload. No phrase ever
  enters the upload pipeline.
- ✅ **No raw secret in UI / logs** — every backend-supplied
  message passes through `OperatorPermissionAdminRedaction.ScrubAndTruncate`
  before display; multipart bodies are never logged; file content
  is streamed (`HttpCompletionOption.ResponseHeadersRead`,
  `FileShare.Read`).
- ✅ **No local absolute path sent to backend** — the upload service
  sends only the bare relative path (taken from `manifest.files[]`)
  + the file bytes. The multipart `filename` is the bare last
  segment of the relative path.
- ✅ **No `.zip` upload** — the local export's `.zip` is shown read-only
  in the card with a "Local archive only — not uploaded" label. The
  upload service refuses any `manifest.files[]` entry whose path ends
  in `.zip` at the local-validation gate (before the first HTTP
  socket).

---

## 8. Backend compatibility expectations

The desktop is built against backend Phase 10.22C/D behaviour:

| Backend state                                                                 | Desktop result                                              |
|-------------------------------------------------------------------------------|-------------------------------------------------------------|
| API flag OFF (`operator.evidence.bundle.api.enabled=false`)                   | `BackendBlocked` with `FEATURE_FLAG_OFF` + status 503.       |
| CASHIER role                                                                  | `BackendBlocked` with `FORBIDDEN` + status 403.              |
| ADMIN with foreign tenant                                                     | `BackendBlocked` with `FORBIDDEN`.                           |
| GLOBAL_ADMIN cross-tenant                                                     | OK across tenants.                                           |
| Backend redaction scanner finds a hit                                          | `BackendBlocked` with `REDACTION_FAILED`.                    |
| Backend MIME validator mismatch                                                | `BackendBlocked` with `MIME_MISMATCH`.                       |
| Backend duplicate `(uuid, path)`                                              | `BackendBlocked` with `DUPLICATE_FILE`.                      |
| Backend manifest invalid                                                       | `BackendBlocked` with `MANIFEST_INVALID`.                    |
| Backend finalize on a non-DRAFT bundle                                         | `BackendBlocked` with `INVALID_STATUS`.                      |
| Backend FINALIZED + bundleSha256 returned                                      | `Finalized` outcome; UI shows status / SHA / file count.     |

---

## 9. Manual verification checklist

1. Start desktop with `operator_evidence_bundle_upload_ui_enabled` missing
   or set to `0`. **Expected**: upload card shows `Disabled (…)`; all six
   buttons disabled; no folder scan, no backend HTTP request.
2. Set `operator_evidence_bundle_export_ui_enabled=1` but leave upload flag
   OFF. **Expected**: the Phase 10.22E card generates a local ZIP; the
   Phase 10.22F card stays disabled.
3. Set `operator_evidence_bundle_upload_ui_enabled=1` and reopen the dashboard.
   Click `Select Folder` and pick a valid Phase 10.22E export folder
   (must contain `manifest.json`). **Expected**: folder path displays
   truncated; manifest path shows `manifest.json`; local ZIP path
   shows the most-recent `.zip` in the folder (display only).
4. Click `Validate Folder`. **Expected**: outcome `LocalValidationOnly`;
   "(N+1) file(s) ready to upload" in the status message; errors list
   empty.
5. Click `Upload + Finalize` while the **backend** API flag is OFF.
   **Expected**: outcome `BackendBlocked`; status shows
   `Backend rejected: FEATURE_FLAG_OFF.`; no bundle UUID populated.
6. Enable the backend API flag and re-run `Upload + Finalize`.
   **Expected**: bundle UUID is populated; status moves to `Finalized`;
   backend status shows `FINALIZED`; backend bundle SHA-256 is 64 hex
   chars; files-uploaded / total counts match.
7. Inspect the Upload Steps list. **Expected**: the first uploaded file
   is `manifest.json`. Subsequent steps are the manifest's files in
   `OrdinalComparer` order.
8. Inspect the Uploaded Files list. **Expected**: every entry has
   `relativePath (size, sha256:short)`. **No `.zip` entry is present.**
9. Add a file with `relativePath` ending in `.zip` to `manifest.files[]`
   manually (or hand-craft a malformed manifest) and run `Validate Folder`.
   **Expected**: outcome `LocalBlocked`; errors list shows
   `manifest.files[] contains '.zip' which is not uploadable: …`. **No
   backend HTTP request is sent.**
10. Click `Refresh Backend Status` while a finalized bundle UUID is
    visible. **Expected**: the backend GET returns the same UUID with
    `FINALIZED` + matching SHA; status message reads
    `Refreshed: status=FINALIZED, files=N.`
11. Try a folder with a manifest whose `redactionChecklist.authorizationHeadersRemoved`
    = false. **Expected**: `Validate Folder` → `LocalBlocked` with
    `manifest.json redactionChecklist must have all six required keys set to true.`
    No backend HTTP request.
12. Edit one file's content after manifest generation. Re-run `Validate Folder`.
    **Expected**: `LocalBlocked` with `Local SHA-256 for <file> does not
    match manifest sha256.` No backend HTTP request.
13. Truncate one file after manifest generation. Re-run `Validate Folder`.
    **Expected**: `LocalBlocked` with `Local size for <file> (…) does
    not match manifest sizeBytes (…).`
14. Run `Upload + Finalize` with a folder containing only `manifest.json`
    (no files referenced from manifest). **Expected**: local validation
    passes for the manifest's empty `files[]`; backend create succeeds;
    manifest upload succeeds; finalize succeeds (backend permits an
    empty `files[]` per Phase 10.22D contract); outcome `Finalized`.
15. Simulate a network interruption mid-upload (disconnect the test
    network). **Expected**: outcome `BackendBlocked` with
    `NETWORK_FAILURE`; bundle UUID preserved if create succeeded. No
    desktop crash.
16. Search the entire UI + the application log for the literal strings
    `Authorization`, `Bearer`, `password`, `enc:v1:`, `EXECUTE_`,
    `ENABLE_`, `ROLLBACK_`. **Expected**: zero matches in card content,
    log lines, or screenshots.
17. Issue `POST /api/v1/operator/permissions/validate` for a dangerous
    key via existing dashboards. **Expected**: response is byte-identical
    to pre-10.22F baseline.
18. Open the dangerous-execute commands (Real Migration, Runtime
    Cutover, Rollback, Retention Cleanup). **Expected**: `CanExecute`,
    guarded wrappers, confirmation-phrase capture, dangerous-operation
    lock — all unchanged.
19. Open the POS view, ring up a sale. **Expected**: business flow
    works exactly as before this phase.
20. Inspect the upload card carefully. **Expected**: no `Review`,
    `Download`, `Delete`, `Retention`, `Execute`, `Approve` button;
    no confirmation-phrase input; no raw SQL input.

---

## 10. What does NOT change

- Backend Java / resources / migrations: byte-identical to Phase 10.22E.
- Existing dashboards (Migration Operations, Permission Admin,
  Authoritative Status, Backend Audit / Evidence Integration,
  Phase 10.22E Local Export card): unchanged.
- Existing metadata-only `POST /api/v1/operator/evidence/register`
  flow: unchanged.
- Dangerous-execute commands and their guarded wrappers: unchanged.
- POS sales / inventory / cashbox flows: unchanged.

---

## 11. Future limitations

- Reviewer / download UI is **still deferred to Phase 10.22G**. The
  desktop Phase 10.22F card has no `Review` or `Download` button.
- Retention / legal-hold automation is **still deferred to Phase 10.22H**.
- Object-storage provider (S3 / GCS / Azure / MinIO) — **shipped in
  Phase 10.22K** (2026-05-30); `FS_LOCAL` remains the default; see
  [`operator-evidence-bundle-object-storage.md`](../../Ham-Pos/docs/operator-evidence-bundle-object-storage.md).
- OCR / PDF deep content scanning is **still not implemented**.
- Emergency offline approval process is documented but not automated.
- Hard delete of finalized bundles is gated on the FUTURE_ONLY
  `operator.evidence.bundle.delete.admin` key and remains a documented
  future flow (Phase 10.22L).

---

## Phase 10.22I — Docs-Only Pilot (shipped 2026-05-30)

**Phase 10.22I is documentation-only.** It pilots the upload/finalize workflow from
Phase 10.22F without adding any code, flags, endpoints, or physical deletion.

The pilot runbook covers enabling the upload flag, creating a bundle, uploading files,
finalizing, negative test scenarios (MIME mismatch, SHA mismatch, nested ZIP rejection),
and audit verification for the upload endpoints.

Full pilot runbook (backend docs):
[`operator-evidence-bundle-pilot-runbook.md`](../../Ham-Pos/docs/operator-evidence-bundle-pilot-runbook.md)

---

## Phase 10.22J — Docs-Only Controlled Rollout Runbook (shipped 2026-05-30)

**Phase 10.22J is documentation-only.** It provides the wave-gated production rollout
runbook for the Phase 10.22B–H stack, including the upload/finalize UI card from
Phase 10.22F. No code, flags, XAML, C# files, migrations, endpoints, or storage changes
are added.

The controlled rollout runbook covers Wave 2 (upload + finalize in production): manifest
validation, file uploads, finalize, CASHIER denial, no-token-in-logs assertion, and
bundle UUID verification.

Full controlled rollout runbook (backend docs):
[`operator-evidence-bundle-controlled-rollout-runbook.md`](../../Ham-Pos/docs/operator-evidence-bundle-controlled-rollout-runbook.md)

---

## Phase 10.22K — Object Storage Adapter / Provider Integration (shipped 2026-05-30)

**Phase 10.22K is backend code + docs only.** No WPF C# file, XAML screen, button,
or desktop UI card is changed. The upload/finalize UI card in this document behaves
identically regardless of which storage provider is active on the backend.

The backend now supports `provider=S3` (AWS S3), `provider=MINIO` (MinIO via endpoint
override), fail-closed stubs for `AZURE_BLOB` and `GCS`, and the unchanged default
`FS_LOCAL`. Desktop upload calls are provider-opaque: the same `POST` endpoints, same
DTO shapes, same response codes.

Full object storage reference (backend docs):
[`operator-evidence-bundle-object-storage.md`](../../Ham-Pos/docs/operator-evidence-bundle-object-storage.md)

---

## Phase 10.22L — Hard Delete (Desktop Deferred) (2026-05-31)

The backend hard-delete endpoint (`POST /{uuid}/hard-delete`) is implemented in Phase 10.22L. Desktop UI is deferred. The upload screens in this doc are unchanged — the hard-delete flow targets only EXPIRED / ARCHIVED / REJECTED bundles.

Backend hard-delete reference: [`operator-evidence-bundle-hard-delete.md`](../../Ham-Pos/docs/operator-evidence-bundle-hard-delete.md)
