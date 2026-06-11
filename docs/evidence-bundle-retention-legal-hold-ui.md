# Evidence Bundle — Retention & Legal Hold UI (Phase 10.22H)

> **Status:** shipped 2026-05-30. Mirrors the backend
> `Ham-Pos/docs/operator-evidence-bundle-retention-legal-hold.md` doc
> from the desktop's perspective.

---

## 1. Scope

Phase 10.22H adds a single read-only-when-disabled / mutation-when-enabled
card to `MigrationOperationsWindow.xaml`:

```
Operator Evidence Bundle Retention + Legal Hold (Phase 10.22H)
```

It consumes the Phase 10.22H backend endpoints (retention update, legal-hold
toggle, archive, expire, retention-candidates) plus the read-only GET-bundle
endpoint that has been available since Phase 10.22C.

The card has **no** upload / finalize / delete / hard-delete /
dangerous-execute / confirmation-phrase / raw-SQL / storage-path
control.

---

## 2. Local feature flag

| Flag                                                | Default | Effect                                                                                                  |
|-----------------------------------------------------|---------|---------------------------------------------------------------------------------------------------------|
| `operator_evidence_bundle_retention_ui_enabled`     | OFF     | When OFF, every input on the card is disabled, every command short-circuits to `FEATURE_FLAG_OFF`, and no HTTP call is made.    |

The flag value is read from `GlobalSettingsRepository` at command
invocation time (no caching across `await` boundaries). The card has
no toggle; operators flip it via the local settings store and click
`Refresh Flag` on the card.

---

## 3. Card placement

The card is appended immediately after the Phase 10.22G reviewer card
inside `MigrationOperationsWindow.xaml`. Visual cue: yellow header
(`#92400E`) on a `#FFFBEB` background with a `#F59E0B` border — distinct
from the green 10.22E export, teal 10.22F upload, and blue 10.22G review
cards.

The status-bar at the bottom of the window is unchanged.

---

## 4. Supported actions

| Action                          | Backend call                                                              | Mutation? |
|---------------------------------|---------------------------------------------------------------------------|-----------|
| `Refresh Flag`                   | none — re-reads local flag                                                | no        |
| `List Retention Candidates`      | `GET /retention-candidates`                                               | no        |
| `Load Bundle Metadata`           | `GET /{uuid}`                                                             | no        |
| `Submit Retention Update`        | `POST /{uuid}/retention`                                                  | yes       |
| `Submit Legal Hold Toggle`       | `POST /{uuid}/legal-hold`                                                 | yes       |
| `Archive Bundle`                 | `POST /{uuid}/archive`                                                    | yes       |
| `Expire Bundle`                  | `POST /{uuid}/expire`                                                     | yes       |
| `Clear`                          | none — clears local form state                                            | no        |

There is intentionally **no** download button on this card — that lives
on the Phase 10.22G reviewer card. There is **no** delete button.

---

## 5. Disabled behaviour

While `operator_evidence_bundle_retention_ui_enabled` is OFF:

- The card displays its flag status string verbatim
  (e.g. `Disabled (operator_evidence_bundle_retention_ui_enabled missing or "0").`).
- Every input control has `IsEnabled="false"`.
- Every `[RelayCommand]` is `CanExecute = false` via
  `CanRunEvidenceRetention()` / `CanRunEvidenceRetentionWithSelected()`.
- The orchestrator (`EvidenceBundleRetentionService`) short-circuits
  every method to a `FEATURE_FLAG_OFF` outcome. No HTTP call is made.
- `NetworkLogService` records zero entries for retention endpoints.

---

## 6. Backend error code mapping

The card displays the verbatim backend code + scrubbed message from
each outcome via the existing `OperatorPermissionAdminRedaction.ScrubAndTruncate`
helper. Common backend codes:

| Code                          | When                                                                                                                |
|-------------------------------|---------------------------------------------------------------------------------------------------------------------|
| `FEATURE_FLAG_OFF`            | Backend master `operator.evidence.bundle.api.enabled=false` (HTTP 503) or local flag OFF (synthetic, httpStatus 0). |
| `FORBIDDEN`                   | CASHIER actor, or ADMIN against foreign tenant.                                                                     |
| `NOT_FOUND`                   | Unknown `bundleUuid`.                                                                                               |
| `LEGAL_HOLD_ACTIVE`           | Attempted archive / expire while `legalHold=true`.                                                                  |
| `RETENTION_INVALID`           | `retentionUntil` not in the future, or shortening on a held bundle.                                                 |
| `RETENTION_TICKET_REQUIRED`   | Blank `reason` / `ticketId` on any retention mutation. (Also synthesised client-side as a preflight.)               |
| `ARCHIVE_NOT_ALLOWED`         | Current status not archivable (e.g. DRAFT).                                                                         |
| `EXPIRE_NOT_ALLOWED`          | Current status not expirable, or ADMIN trying to early-expire.                                                      |
| `NETWORK_FAILURE`             | Transport-layer exception before the response arrives.                                                              |
| `DESERIALIZATION_FAILURE`     | Backend returned a non-parsable body for a 2xx.                                                                     |

---

## 7. Security boundaries

- **No upload / finalize / delete / hard-delete buttons.**
- **No confirmation-phrase input.** The card has no field that captures
  one of the six known guarded-flow literals, and the orchestrator
  refuses to send a body containing them by virtue of having no field
  to put one in.
- **No storage-path / bucket / provider display.** The selected-bundle
  panel surfaces only `evidenceType / phase / status / tenantId /
  retentionClass / retentionUntil / legalHold / reviewedBy /
  reviewedAt`. The download flow (when needed) lives on the 10.22G
  card and surfaces only the safe destination filename + truncated
  path.
- **No raw SQL / shell input.**
- **Scrub on display.** Every backend message that reaches the UI is
  routed through `ScrubForDisplay` (the existing scrubber +
  truncator).
- **No token / Authorization header logging.** The orchestrator
  inherits the existing `OperatorEvidenceBundleApiClient` `SafeAsync`
  wrapper that never logs the multipart body, the request body, the
  Authorization header, or any storage path.
- **Dangerous-execute commands** (real migration / runtime cutover /
  rollback / retention cleanup) are not visible from this card and
  their `CanExecute` semantics are unchanged.

---

## 8. Backward compatibility

- Phase 10.22E local export pipeline — unchanged.
- Phase 10.22F upload + finalize pipeline — unchanged.
- Phase 10.22G reviewer + download card — unchanged.
- Phase 10.19F audit-intent / evidence-register flows — unchanged.
- Phase 10.22C/D `EvidenceBundleResponseDto` / `EvidenceBundlePageItemDto`
  receive additive fields (`retentionUntil`, `legalHold`, `reviewedBy`,
  `reviewedAt`). Old desktop builds simply ignore the new properties.
- POS sales / cashbox / inventory flows — unaffected.
- Dangerous permission decisions for the 25 prior keys — unchanged.

---

## 9. 20-step manual verification

1. **Backend API flag OFF** (`operator.evidence.bundle.api.enabled=false`)
   → every retention endpoint returns 503 `FEATURE_FLAG_OFF`. The
   desktop card surfaces the code verbatim.
2. **Desktop flag OFF** — card is disabled; `NetworkLogService` shows
   zero entries for `/retention`, `/legal-hold`, `/archive`, `/expire`,
   `/retention-candidates`.
3. **Create + finalize a bundle via the Phase 10.22F flow** — note the
   UUID.
4. **Review + download still works via the Phase 10.22G card.**
5. **ADMIN updates `retentionUntil` for the bundle in their own tenant**
   → succeeds; the metadata panel refreshes; backend audit emits
   `OPERATOR_EVIDENCE_BUNDLE_RETENTION_UPDATED`.
6. **ADMIN attempts the same call against a foreign tenant** → backend
   refuses with `FORBIDDEN`; UI surfaces the code.
7. **GLOBAL_ADMIN updates retention cross-tenant** → succeeds.
8. **Toggle `legalHold = true`** (with reason + ticket) → succeeds; the
   metadata panel shows the new value.
9. **Attempt `Archive Bundle` while hold is ON** → backend refuses with
   `LEGAL_HOLD_ACTIVE`.
10. **Attempt `Expire Bundle` while hold is ON** → backend refuses with
    `LEGAL_HOLD_ACTIVE`.
11. **Toggle `legalHold = false`** with reason + ticket → succeeds.
12. **Click `Archive Bundle`** (FINALIZED, no hold) → status transitions
    to `ARCHIVED`; storage objects still exist on the FS; downloading
    via the 10.22G card succeeds.
13. **Try to expire an `ARCHIVED` bundle whose retention is in the
    future, as ADMIN** → backend refuses with `EXPIRE_NOT_ALLOWED`.
14. **Repeat as GLOBAL_ADMIN** → succeeds; status transitions to
    `EXPIRED`; audit captures `earlyExpire=true`.
15. **Try to download the `EXPIRED` bundle as ADMIN via the 10.22G card**
    → backend refuses with `DOWNLOAD_NOT_AVAILABLE`.
16. **Repeat as GLOBAL_ADMIN (retention.admin)** → succeeds.
17. **`List Retention Candidates`** with both flags ON — bundles on
    legal hold are excluded; DRAFT / EXPIRED / QUARANTINED are excluded.
    ADMIN sees only their tenant; GLOBAL_ADMIN sees cross-tenant.
18. **Search the entire UI / `NetworkLogService` / application log** for
    any storage path / bucket / provider / confirmation phrase / raw
    token → zero matches.
19. **Confirm no `Delete` / `Hard Delete` button exists** on this card or
    in any other card on the window.
20. **Open POS view + ring up a sale** → business flow works exactly as
    before.

---

## 10. Future limitations

- Object-storage providers (S3 / GCS / Azure / MinIO) — **shipped in
  Phase 10.22K** (2026-05-30); `FS_LOCAL` remains the default.
- Hard delete remains documented but ungranted (Phase 10.22L).
- Malware scanning / OCR / PDF deep scan still not implemented.
- Emergency offline approval process is documented but not automated.

---

## Phase 10.22I — Docs-Only Pilot (shipped 2026-05-30)

**Phase 10.22I is documentation-only.** It pilots the retention/legal-hold workflow
from Phase 10.22H without adding any code, flags, endpoints, or physical deletion.

The pilot runbook covers enabling the retention flag, updating retention, toggling legal
hold, verifying hold blocks archive/expire, archiving, expiring (GLOBAL_ADMIN only for
early expire), listing retention candidates, and optional sweeper manual invocation
guidance.

Full pilot runbook (backend docs):
[`operator-evidence-bundle-pilot-runbook.md`](../../Ham-Pos/docs/operator-evidence-bundle-pilot-runbook.md)

---

## Phase 10.22J — Docs-Only Controlled Rollout Runbook (shipped 2026-05-30)

**Phase 10.22J is documentation-only.** It provides the wave-gated production rollout
runbook for the Phase 10.22B–H stack, including the retention/legal-hold UI card from
Phase 10.22H. No code, flags, XAML, C# files, migrations, endpoints, or storage changes
are added.

The controlled rollout runbook covers Wave 4 (retention + legal hold in production):
retention update, legal hold ON/OFF, archive, expire, ADMIN early-expire denial, and
GLOBAL_ADMIN early-expire with audit verification.

Full controlled rollout runbook (backend docs):
[`operator-evidence-bundle-controlled-rollout-runbook.md`](../../Ham-Pos/docs/operator-evidence-bundle-controlled-rollout-runbook.md)

---

## Phase 10.22K — Object Storage Adapter / Provider Integration (shipped 2026-05-30)

**Phase 10.22K is backend code + docs only.** No WPF C# file, XAML screen, button,
or desktop UI card is changed. The retention/legal-hold UI card in this document behaves
identically regardless of which storage provider is active on the backend.

Retention, legal hold, archive, and expire operations mutate only DB metadata —
no storage adapter method is called from these flows on either the FS_LOCAL or S3/MinIO
adapter. `operator.evidence.bundle.delete.admin` remains `FUTURE_ONLY` and ungranted.

Full object storage reference (backend docs):
[`operator-evidence-bundle-object-storage.md`](../../Ham-Pos/docs/operator-evidence-bundle-object-storage.md)

---

## Phase 10.22L — Hard Delete (Desktop Deferred) (2026-05-31)

Legal hold **blocks hard delete** at the backend. A bundle with `legalHold = true` always returns `LEGAL_HOLD_ACTIVE` from `POST /{uuid}/hard-delete` regardless of status. Users must release the hold before hard delete can proceed. This interaction is documented here because the legal-hold toggle (implemented in this doc's phase) is a prerequisite management step for any future hard-delete workflow.

Desktop hard-delete UI is deferred. The retention and legal-hold screens in this doc are unchanged.

Backend hard-delete reference: [`operator-evidence-bundle-hard-delete.md`](../../Ham-Pos/docs/operator-evidence-bundle-hard-delete.md)

---

## Phase 10.22P — Lifecycle Scheduler Status (2026-06-02)

A read-only monitoring card was added to `MigrationOperationsWindow` showing
Phase 10.22N retention archive sweeper and Phase 10.22O expiration sweeper
run history. The Phase 10.22H retention and legal-hold card (this doc) is
**unchanged** — no buttons removed, no fields altered, no new flags on the
Phase 10.22H card.

The Phase 10.22P card consumes separate endpoints (`/retention-sweeper/runs`,
`/expiration-sweeper/runs`) and is gated by its own independent flag
`operator_evidence_bundle_lifecycle_scheduler_status_ui_enabled`.

Full specification: [`evidence-bundle-lifecycle-scheduler-status-ui.md`](evidence-bundle-lifecycle-scheduler-status-ui.md)
