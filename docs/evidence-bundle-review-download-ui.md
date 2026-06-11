# Desktop Evidence Bundle — Reviewer + Download UI (Phase 10.22G)

Phase 10.22G adds a **read-only review + download** card to the WPF
Migration Operations dashboard. The card consumes the Phase 10.22G
backend endpoints behind a default-OFF desktop flag.

Companion documents:

- [`evidence-bundle-backend-upload-ui.md`](./evidence-bundle-backend-upload-ui.md) — Phase 10.22F upload UI
- [`evidence-bundle-local-export-ui.md`](./evidence-bundle-local-export-ui.md) — Phase 10.22E local export
- [`../../Ham-Pos/docs/operator-evidence-bundle-review-download-api.md`](../../Ham-Pos/docs/operator-evidence-bundle-review-download-api.md) — backend Phase 10.22G API
- [`desktop-backend-operator-permissions-integration.md`](./desktop-backend-operator-permissions-integration.md)

---

## 1. Local flag

| Key                                              | Default | Effect when missing / "" / "0" / "false" |
|--------------------------------------------------|---------|------------------------------------------|
| `operator_evidence_bundle_review_ui_enabled`     | OFF     | Card shows disabled banner; every command is `CanExecute=false`; no backend HTTP call. |

Independent of the Phase 10.22E export flag and the Phase 10.22F
upload flag. The backend's master flag
`operator.evidence.bundle.api.enabled` is the upstream gate — when
OFF the desktop receives HTTP 503 `FEATURE_FLAG_OFF` and surfaces it
verbatim.

---

## 2. Card placement and shape

A new "Operator Evidence Bundle Reviewer + Download (Phase 10.22G)"
card sits immediately after the Phase 10.22F upload card in
`MigrationOperationsWindow.xaml`. It carries a yellow banner:

> *Read-only review + download of FINALIZED / REVIEWED / REJECTED /
> NEEDS_CHANGES bundles. No upload, no delete, no retention, no
> dangerous execution.*

UI sections (top to bottom):

1. **Local flag status** — `Enabled (...)` / `Disabled (...)`.
2. **Filters** — `evidenceType`, `phase`, `tenantId`, `status` (combo
   box of `''` / `FINALIZED` / `REVIEWED` / `REJECTED` /
   `NEEDS_CHANGES` / `ARCHIVED` / `QUARANTINED`), `page`, `size`.
3. **Action buttons** — `Refresh Flag`, `List Bundles`, `Clear`.
4. **Bundles `DataGrid`** — read-only columns: UUID / Type / Phase /
   Status / TenantId / FileCount / TotalBytes / CreatedAt.
5. **Selected bundle UUID** input + `Load Bundle Metadata` +
   `Refresh Backend Status` buttons.
6. **Selected bundle metadata panel** — 10 rows: EvidenceType, Phase,
   Environment, TenantId/StoreId, Status, FileCount/TotalBytes,
   bundleSha256, createdBy, finalizedAt, reviewedAt.
7. **Files list** — `RelativePath (size, sha256:short)`.
8. **Review decision combo** + **review notes** textbox + **Submit
   Review** button.
9. **Download folder display** + overwrite checkbox + downloaded
   filename / path / size / SHA-256 + **Choose Download Folder** +
   **Download Bundle (ZIP)** buttons.
10. **Status summary** + Warnings + Errors lists.

### Explicitly absent from the card

- No `Upload`, `Finalize`, `Delete`, `Retention` button.
- No `Execute`, `Approve` (dangerous-execute) button.
- No confirmation-phrase input.
- No raw SQL input.
- No raw file-browsing endpoint exposure — the only download is the
  whole ZIP.

---

## 3. Review workflow

1. **List filtered bundles** via `GET /api/v1/operator/evidence/bundles`
   (default filter: `status=FINALIZED`).
2. **Select a bundle UUID** in the input box (operator can copy from
   the data grid or paste from elsewhere).
3. **Load bundle metadata** via `GET /{uuid}`. Display all 10 rows of
   metadata + the file manifest with bare relative paths + short SHAs.
4. **Choose decision** from the allow-list combo
   (`APPROVED` / `REJECTED` / `NEEDS_CHANGES`).
5. **Type review notes** (optional; backend scrubs + truncates).
6. **Submit Review** → `POST /{uuid}/review`. Backend transitions
   status and returns the updated metadata; the card refreshes.

If the backend returns `SELF_REVIEW_FORBIDDEN` / `INVALID_STATUS` /
`FORBIDDEN` / `VALIDATION_FAILED` / `FEATURE_FLAG_OFF`, the card
surfaces the typed code verbatim and the bundle is unchanged.

---

## 4. Download workflow

1. **Select a bundle UUID** + **Load Bundle Metadata** (so the
   operator knows what they're about to download).
2. **Choose Download Folder** — opens `Microsoft.Win32.OpenFolderDialog`
   for a clean folder pick.
3. (Optional) Tick **Overwrite existing file if present**.
4. **Download Bundle (ZIP)** → `GET /{uuid}/download`.

Streaming behavior:

- The desktop uses `HttpCompletionOption.ResponseHeadersRead` and a
  `FileStream` with `useAsync: true` so the body never lands in
  memory as a byte array.
- The downloaded bytes go to a sibling temp file
  `*.part-<guid>.tmp`; on success the temp file is atomically
  renamed to the destination filename. On any failure the temp file
  is deleted in a `finally` block.
- The destination filename is taken from the backend's
  `Content-Disposition` header. Falls back to
  `operator-evidence-bundle-<uuid>.zip` if absent. Always sanitised
  (alphanumeric + `-_.`); always ends in `.zip`.
- The download SHA-256 is computed inline as bytes are written. The
  card displays it next to the filename.
- The downloaded path is displayed truncated (80 char with leading
  `…`) so screenshots don't leak the host filesystem layout.

The card does **not** auto-unzip and does **not** auto-open the
downloaded file. The operator has a manual `Open Output Folder` step
on the Phase 10.22E export card if they want to inspect it.

---

## 5. Retry / resume policy

**Review** — one explicit operator click. No background review, no
auto-retry. Failure surfaces the backend error code/message and
leaves the bundle's backend state unchanged. The operator may fix
the input and resubmit.

**Download** — one explicit operator click. On `NETWORK_FAILURE`
mid-stream the temp file is deleted; the destination file is
unchanged. The operator may retry. No background downloads.

---

## 6. Security boundaries

- ✅ **No backend code changed** beyond the additive Phase 10.22G
  endpoints. The desktop calls only the new endpoints + the
  Phase 10.22C/D list/get endpoints.
- ✅ **No dangerous execution change** — Real Migration / Runtime
  Cutover / Rollback / Retention Cleanup `CanExecute`, their guarded
  wrappers, the dangerous-operation lock, and the
  confirmation-phrase capture are byte-identical.
- ✅ **No guarded wrapper change.**
- ✅ **No confirmation phrase movement** — the desktop source tree
  never names any of the six known guarded-flow literals; this card
  never asks for or sends a phrase.
- ✅ **No raw secret in UI / logs** — every backend message passes
  through `OperatorPermissionAdminRedaction.ScrubAndTruncate` before
  display; multipart bodies are never logged; download body bytes
  are streamed (not logged); Authorization header is never logged.
- ✅ **No storage path leak** — the desktop only ever sees
  `bundleUuid` and the safe Content-Disposition filename.
- ✅ **No raw file-browsing endpoint** — the backend offers no
  `/files/{path}` endpoint and the desktop never tries one.
- ✅ **No delete / retention / hard-delete control on the card.**
- ✅ **No object-storage SDK** — backend still uses the
  `FS_LOCAL` adapter; download is a server-side stream of those
  bytes.

---

## 7. Backward compatibility

- ✅ Phase 10.22E export-only flow unaffected.
- ✅ Phase 10.22F upload + finalize flow unaffected.
- ✅ Phase 10.22B/C/D schema + endpoints unaffected (V75 only widens
  the status CHECK constraint).
- ✅ Existing dashboards unchanged.
- ✅ POS sales unchanged.
- ✅ Dangerous-operation behaviour unchanged.

---

## 8. Manual verification checklist

1. **Start desktop with `operator_evidence_bundle_review_ui_enabled` =
   missing or `0`**. **Expected**: review card shows `Disabled (…)`;
   every button disabled; no `/api/v1/operator/evidence/bundles*`
   request in `NetworkLogService`.
2. **Set `operator_evidence_bundle_review_ui_enabled=1`** and reopen
   the dashboard. **Expected**: flag status shows `Enabled (…)`;
   buttons enable per `CanExecute` rules.
3. **Finalize a bundle via the Phase 10.22F upload card** (with
   backend flags ON). Note the returned UUID.
4. **Open the reviewer card, set status filter to `FINALIZED`,
   click `List Bundles`**. **Expected**: outcome `Listed`; the data
   grid shows the just-finalized bundle's row.
5. **Paste the UUID into the Selected bundle UUID box and click
   `Load Bundle Metadata`**. **Expected**: outcome `Loaded`; metadata
   panel populates; files list shows the manifest entries with bare
   relative paths.
6. **As CASHIER (different login), try `List Bundles` / `Load Bundle
   Metadata`**. **Expected**: outcome `Failed` with backend
   `FORBIDDEN`. No state change.
7. **As ADMIN on tenantA, try to load a bundle whose `tenantId`
   is tenantOther**. **Expected**: outcome `Failed` with backend
   `FORBIDDEN`.
8. **As GLOBAL_ADMIN, load a cross-tenant bundle**. **Expected**:
   succeeds.
9. **As ADMIN who created the bundle, try to review it**.
   **Expected**: outcome `Failed` with backend `SELF_REVIEW_FORBIDDEN`.
10. **Try to review a DRAFT bundle**. **Expected**: outcome `Failed`
    with backend `INVALID_STATUS` (the REVIEWABLE_STATUSES gate fires
    on DRAFT).
11. **As a DIFFERENT ADMIN, submit `APPROVED` review for the
    FINALIZED bundle from step 3**. **Expected**: outcome `Reviewed`;
    selected bundle status shows `REVIEWED`; `reviewedAt` populated.
12. **Try to review the now-REVIEWED bundle again**. **Expected**:
    outcome `Failed` with backend `INVALID_STATUS` (terminal state).
13. **Try to download a DRAFT bundle**. **Expected**: outcome
    `Failed` with backend `DOWNLOAD_NOT_AVAILABLE`. No file written.
14. **Download a FINALIZED or REVIEWED bundle into a chosen folder**.
    **Expected**: outcome `Downloaded`; filename
    `operator-evidence-bundle-<uuid>.zip` (or backend-supplied);
    SHA-256 displays as 64 hex chars; the temp `*.part-*.tmp` is
    gone.
15. **Open the downloaded ZIP**. **Expected**: contains
    `manifest.json` + every file from the manifest's `files[]` under
    its bare relative path. No `.git`, no `__MACOSX`, no absolute
    paths, no storage prefix.
16. **Inspect the displayed downloaded path on the card**. **Expected**:
    truncated to 80 chars with leading `…`. The host filesystem
    layout never appears in full.
17. **Search the entire UI + log + `NetworkLogService`** for raw
    strings like `Authorization`, `Bearer`, `password`, `enc:v1:`,
    `EXECUTE_`, `ENABLE_`, `ROLLBACK_`, raw absolute paths.
    **Expected**: zero matches.
18. **Issue `POST /api/v1/operator/permissions/validate`** for a
    dangerous key via the existing dashboards. **Expected**:
    response byte-identical to pre-10.22G baseline.
19. **Open the dangerous-execute commands** (Real Migration, Runtime
    Cutover, Rollback, Retention Cleanup). **Expected**: `CanExecute`,
    guarded wrappers, dangerous-operation lock, confirmation-phrase
    capture all unchanged.
20. **Open the POS view, ring up a sale**. **Expected**: business
    flow works exactly as before this phase.

---

## 9. Future limitations

- Object-storage adapter (S3 / GCS / Azure / MinIO) — **shipped in
  Phase 10.22K** (2026-05-30); download continues to stream through the
  backend (no presigned URLs); `FS_LOCAL` remains the default.
- Hard-delete flow gated on FUTURE_ONLY
  `operator.evidence.bundle.delete.admin` is **documented but not
  implemented** (Phase 10.22L).
- OCR / PDF deep content scanning is **still not implemented**.
- Emergency offline approval process is documented but not automated.

---

## Phase 10.22I — Docs-Only Pilot (shipped 2026-05-30)

**Phase 10.22I is documentation-only.** It pilots the review/download workflow from
Phase 10.22G without adding any code, flags, endpoints, or storage changes.

The pilot runbook covers enabling the review flag, switching to a separate reviewer
account, submitting a decision, downloading the ZIP, verifying no self-review is possible,
and confirming audit events.

Full pilot runbook (backend docs):
[`operator-evidence-bundle-pilot-runbook.md`](../../Ham-Pos/docs/operator-evidence-bundle-pilot-runbook.md)

---

## Phase 10.22J — Docs-Only Controlled Rollout Runbook (shipped 2026-05-30)

**Phase 10.22J is documentation-only.** It provides the wave-gated production rollout
runbook for the Phase 10.22B–H stack, including the review/download UI card from
Phase 10.22G. No code, flags, XAML, C# files, migrations, endpoints, or storage changes
are added.

The controlled rollout runbook covers Wave 3 (review + download in production): reviewer
decision, self-review denial, ZIP download, downloaded path truncation, and audit
verification.

Full controlled rollout runbook (backend docs):
[`operator-evidence-bundle-controlled-rollout-runbook.md`](../../Ham-Pos/docs/operator-evidence-bundle-controlled-rollout-runbook.md)

---

## Phase 10.22K — Object Storage Adapter / Provider Integration (shipped 2026-05-30)

**Phase 10.22K is backend code + docs only.** No WPF C# file, XAML screen, button,
or desktop UI card is changed. The review and download UI card in this document behaves
identically regardless of which storage provider is active on the backend.

The download endpoint continues to stream ZIP bytes through the backend — no presigned
URL is returned. The `FS_LOCAL` adapter remains the default; S3/MinIO are selectable
via `OPERATOR_EVIDENCE_BUNDLE_STORAGE_PROVIDER` server-side.

Full object storage reference (backend docs):
[`operator-evidence-bundle-object-storage.md`](../../Ham-Pos/docs/operator-evidence-bundle-object-storage.md)

---

## Phase 10.22L — Hard Delete (Desktop Deferred) (2026-05-31)

The backend hard-delete endpoint is implemented in Phase 10.22L. Download (`GET /{uuid}/download`) is permanently blocked for `HARD_DELETED` bundles — no bytes can be streamed after storage objects are deleted. Desktop UI for hard delete is deferred.

Backend hard-delete reference: [`operator-evidence-bundle-hard-delete.md`](../../Ham-Pos/docs/operator-evidence-bundle-hard-delete.md)
