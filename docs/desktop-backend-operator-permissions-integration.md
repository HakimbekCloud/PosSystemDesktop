# Desktop Backend Operator Permission Integration (Phase 10.19C)

Display-only integration with the Phase 10.19B backend operator permission
API. The Migration Operations dashboard shows the backend's view of the
current operator's identity and permission set. **No enforcement.** **No
gating change.** **No guarded wrapper changes.**

This document complements:

- [`operator-rbac-permission-model-plan.md`](operator-rbac-permission-model-plan.md) — desktop-side architecture audit and the eight-phase RBAC migration plan.
- `Ham-Pos/docs/backend-operator-permission-api.md` — backend endpoint reference produced in Phase 10.19B.

---

## Purpose

The Phase 10.19A RBAC plan defines eight phases (RBAC-1 through RBAC-8)
for moving from the desktop's current local-flag + role gate to a
backend-supported permission model. Phase 10.19B implemented the backend
API skeleton. Phase 10.19C — this phase — implements only the
**display-only** desktop client and the visibility surface in the
Migration Operations dashboard. The desktop's authorization behaviour is
unchanged.

Operators and support engineers can compare:

- What the backend says the operator's role and permissions are.
- What the desktop's local flags + `OperatorAccessService` role check
  currently grant.

Discrepancies surface as warnings in the dashboard's new "Backend
Operator Permissions" card. The discrepancies do **not** change any
button's enabled state and do **not** affect any guarded wrapper.

## Endpoints Consumed

All three endpoints from Phase 10.19B, via the existing `ApiClient`
(authenticated HttpClient with the desktop's standard tenant + bearer
headers + 401-refresh helper):

- `GET /api/v1/operator/identity` → `OperatorIdentityDto`
- `GET /api/v1/operator/permissions` → `OperatorPermissionsDto`
- `POST /api/v1/operator/permissions/validate` → `OperatorPermissionValidateResultDto`

The validate endpoint is wired (DTOs + `ApiClient` method + wrapper
method) but is not yet invoked by the dashboard's Refresh button. A
future phase (RBAC-5) will call it from inside each guarded wrapper as
an additional fail-closed pre-execution check.

## Fields Displayed

The Migration Operations dashboard's "Backend Operator Permissions" card
displays:

- **Status** — human-readable state of the most recent fetch (Loaded,
  Fetching, Backend identity request failed, Backend permissions request
  failed, Unexpected failure).
- **Identity** — user id, username, backend role, tenant id, store id,
  authenticated boolean, permissions source string (currently
  `code-mapping-v1`), generated timestamp, expires timestamp.
- **Counts** — total permission count, dangerous permission count,
  read-only permission count.
- **All permissions** — slate list of every dotted-lowercase permission
  key the backend grants the user.
- **Dangerous permissions** — red list of just the dangerous subset.
- **Read-only permissions** — slate list of just the read-only subset.
- **Warnings** — amber list of comparison warnings (see below).
- **Errors** — red list of fetch failures (`Identity request returned
  null.`, `Permissions request returned null.`, or an unexpected
  exception message).

The card is placed immediately after the "Production Pilot Evidence
Bundle" card, next to the other read-only diagnostic / readiness /
evidence cards near the top of the dashboard. It does NOT replace any
existing card.

## Comparison Warnings

After a successful fetch, the dashboard composes warnings from the
combination of backend response + local flag state:

1. **Backend role is CASHIER but dashboard is open.** The dashboard's
   local role gate ought to have rejected the CASHIER session; Phase
   10.19C surfaces a warning instead of changing the behaviour.
2. **Backend returned zero operator permissions.** The local gate may
   still allow read-only actions via local flags + local role; the
   warning makes the discrepancy visible.
3. **Backend permission claim is expired.** `expiresAt` < now. Phase
   10.19C is display-only so an expired claim does not block buttons; a
   future enforcement phase would fail closed.
4. **`operator.dashboard.open` missing.** The backend says this user
   shouldn't be opening the dashboard; the local flag + role gate is
   currently authoritative.
5. **Backend grants dangerous permissions AND local flag X is on.** For
   each of `operator_real_migration_ui_enabled`,
   `operator_runtime_cutover_ui_enabled`, `operator_rollback_ui_enabled`,
   `operator_retention_cleanup_ui_enabled` — if the local flag is `"1"`
   and the backend's permission set includes any dangerous permission,
   the warning prompts the operator to confirm this is the intended
   operation window.
6. **Backend role differs from local cached role.** The locally-stored
   `user_role` setting and the backend's response disagree. The
   recommended action is logout + re-login to refresh the local cache.

All six warnings are display-only. None affects `CanExecute` for any
command.

## Why Enforcement Is Not Enabled Yet

Phase 10.19C deliberately stops short of enforcement so:

- The desktop and backend can be deployed independently. Old desktops
  continue to ignore the new endpoints. Newer backends can serve the
  endpoints without breaking older desktops.
- Operators and support engineers can validate the role-to-permission
  mapping by comparing backend output against local guards across the
  first pilot before any code path treats the backend as authoritative.
- The Phase 10.18A controlled production pilot can be re-run with the
  Phase 10.19C visibility surface enabled and the existing guarded
  wrappers unchanged. Pilot evidence collected during that window will
  inform Phase 10.19D's enforcement decisions.

If the backend is unreachable, the desktop continues to operate exactly
as it did before Phase 10.19C. No destructive action is blocked or
unblocked by Phase 10.19C.

## Future Phase 10.19D Enforcement Plan

Phase 10.19D will graduate this surface from display-only to authoritative:

1. Each of the four guarded wrappers
   (`GuardedRealMigrationExecutorService`,
   `GuardedRuntimeCutoverExecutorService`,
   `GuardedRollbackExecutorService`,
   `GuardedRetentionCleanupExecutorService`) gains a pre-execution call
   to `OperatorPermissionApiClient.ValidateAsync` with the appropriate
   permission key.
2. Backend rejection (`Allowed=false`) fails the wrapper closed before
   any mutation. The local flag + role + guarded wrapper guards remain
   in force.
3. Backend unreachable fails closed for destructive operations per the
   RBAC plan's offline policy. Read-only operations continue to degrade
   per the existing dashboard behaviour.
4. The dashboard gains an emergency-offline approval form (per RBAC plan
   Section 11) that allows destructive operations when the backend is
   unreachable AND Support + Store Manager approval is recorded in the
   pilot ticket. The emergency path remains audited.

Until Phase 10.19D ships, the dashboard's existing local-flag + role +
guarded-wrapper + confirmation-phrase + dangerous-operation-lock stack
is unchanged.

## Security Notes

- **No tokens displayed or logged.** The dashboard's Backend Operator
  Permissions card never shows the access token, refresh token, or
  Authorization header. The DTO classes (`OperatorIdentityDto`,
  `OperatorPermissionsDto`, `OperatorPermissionValidateResultDto`)
  contain no token-shaped properties — backend responses do not include
  them.
- **No Authorization header logged.** `OperatorPermissionApiClient`
  delegates HTTP through the existing `ApiClient` configured
  `HttpClient`. The desktop's existing `NetworkLogHandler` redacts
  Authorization values on its diagnostic log path; the new code path
  introduces no new header logging.
- **No confirmation phrases sent.** The validate request DTO
  (`OperatorPermissionValidateRequestDto`) has no `ConfirmationPhrase`
  field. Phrases stay client-side and are consumed only by the local
  guarded wrapper services.
- **No local DB data sent.** Validate requests carry only the
  permission key, the resolved tenant/store identifiers, the operation
  name, and an optional approval ticket id. No local DB rows, no
  evidence-bundle contents, no log file contents.
- **No raw secrets in the UI.** The dashboard's Backend Operator
  Permissions card renders only the structural fields documented above.
  Comparison warnings reference flag names by their settings-key
  identifiers, never their stored values.
- **Backend offline handled safely.** Any HTTP failure, deserialization
  error, or unexpected exception in the new path yields a null result
  via `OperatorPermissionApiClient`. The dashboard surfaces a clear
  error message and the user can dismiss / retry without crashing the
  app, logging out, or restarting.

## Troubleshooting

- **"Backend identity request failed"** — the desktop hit a non-2xx
  response or a network failure. Check that the backend is running, that
  the operator has a valid session (try logging out and back in), and
  that `api_base_url` in `global_settings.json` points at the correct
  backend host. The dashboard's Errors card surfaces the wrapper-level
  failure prefixed `BackendPermissions: …`.
- **"Backend returned zero operator permissions"** — the operator's
  backend role is not yet mapped in the Phase 10.19B
  `OperatorPermissionService`. The most common cause is a custom role
  that does not appear in the backend's `Role` enum. Until that role is
  added to the enum and mapped, the local flag + role gate continues to
  govern the dashboard.
- **"Backend role differs from local cached role"** — the locally
  stored `user_role` setting is stale. Log out and back in. If the
  warning persists, contact Support — the backend may have re-issued the
  role between sessions.
- **`operator.dashboard.open` missing** — the backend's mapping for this
  user does not include `operator.dashboard.open`. In Phase 10.19C the
  local flag + role gate still allows the dashboard to open. In Phase
  10.19D this would fail closed.
- **Permission claim expired** — Phase 10.19B does not yet populate
  `expiresAt`. If a future backend phase populates it and the desktop
  shows the warning, refresh the permissions card by clicking Refresh
  Backend Operator Permissions.
- **Unexpected failure: <message>** — an exception escaped the
  fail-closed wrapper. Capture the message in a defect ticket and attach
  the dashboard's Export Diagnostics JSON file. Do not paste raw audit
  logs into public channels.

## Out of Scope for Phase 10.19C

The following are deliberately left for later phases:

- Calling validate from within the guarded wrapper services. (Phase
  RBAC-5 / Phase 10.19D.)
- Caching the permission claim with explicit expiry handling. (Phase
  RBAC-3 hardening.)
- Including the permission snapshot in the per-wrapper audit logs and
  the Pilot Evidence Bundle. (Phase RBAC-6.)
- Uploading audit/evidence to the backend. (Phase RBAC-7, optional.)
- An emergency offline approval form. (Phase 10.19D once enforcement
  ships.)
- Validate-preview UI per-dangerous-operation. Skipped in Phase 10.19C
  to keep the visibility surface small; reserved for Phase 10.19D when
  validate becomes authoritative.

---

## Phase 10.19D — Feature-flagged Preflight Enforcement

Phase 10.19D adds a **click-time** backend permission preflight to the
four dangerous Execute commands (Real Migration, Runtime Cutover,
Rollback, Retention Cleanup). The preflight runs *between* the captured
confirmation phrase being cleared from the UI and the guarded wrapper
service being invoked. It is gated by a new local feature flag that
defaults OFF, so existing installations continue to behave exactly as
Phase 10.19C until an operator opts in.

### Enforcement Flag

`operator_backend_permission_enforcement_enabled` in
`%LocalAppData%\PosSystem\global_settings.json`.

- Missing / empty / `"0"` → enforcement **OFF**. Default for every new
  install and every existing install upgrading to Phase 10.19D. Dangerous
  commands behave exactly like Phase 10.19C — the local flag + role +
  guarded wrapper + confirmation phrase + dangerous-operation-lock stack
  is unchanged; the preflight short-circuits to `Allowed=true,
  Status="Skipped"` without calling the backend.
- `"1"` → enforcement **ON**. Dangerous commands additionally require
  the backend's `POST /api/v1/operator/permissions/validate` to return
  `allowed=true` for the operation's permission key (see mapping below)
  before the guarded wrapper is invoked.

The dashboard does **not** expose a control for changing this flag. The
flag must be edited manually in `global_settings.json` by an authorized
operator on the machine in question, with the change recorded in the
pilot ticket.

### Permission Mapping

| Execute command | Backend permission key |
|---|---|
| Execute Real Migration | `operator.migration.execute` |
| Execute Runtime Cutover | `operator.cutover.execute` |
| Execute Rollback | `operator.rollback.execute` |
| Execute Retention Cleanup | `operator.retention.cleanup.execute` |

Read-only commands (Refresh, exports, previews, gate check, inventory,
retention preview, readiness, evidence bundle, the Refresh Backend
Operator Permissions card itself) are **not** preflight-enforced.

### Click-time Preflight Behaviour

For each of the four `Execute*CoreAsync` methods, the preflight call
sits inside the existing `try` block, after the local input prerequisites
have already been satisfied (`CanExecute` returned true), after the
confirmation phrase has been captured to a local AND cleared from the
bound input property, and **before** the call to the corresponding
guarded wrapper service.

When enforcement is **OFF**, the helper returns `Allowed=true,
Status="Skipped"` without making any network call. The wrapper is
invoked exactly as before — Phase 10.19C behaviour preserved byte-for-
byte.

When enforcement is **ON** and the backend returns `Allowed=true` with
the expected metadata (`requiresLocalFlag=true,
requiresConfirmationPhrase=true, requiresGuardedWrapper=true`), the
helper returns `Allowed=true, Status="Allowed"` and the wrapper is
invoked exactly as before.

When enforcement is **ON** and any of the failure conditions below
applies, the wrapper is **not** called. The dashboard records an
operation-specific rejection (`Outcome=Rejected`, `Executed/FlagChanged
=false`), surfaces a clear status message ("Rejected by backend
permission preflight (<status>): <reason>"), appends a blocker line to
the operation's blocking-reasons collection, and emits a
`BackendPermissions: preflight rejected (<permission-key>): <reason>`
entry in the dashboard's red Errors card:

| Failure condition | Resulting status |
|---|---|
| Backend offline / 401 / 5xx / network failure / invalid JSON | `Unavailable` — `"Backend permission validation unavailable. Dangerous operation blocked because enforcement is enabled."` |
| Backend returns `allowed=false` | `Denied` — reason text comes from the backend's response (e.g. CASHIER denied, tenant scope mismatch, unknown key, future-only key, role not mapped) |
| Backend returns `allowed=true` but with missing metadata booleans | `MetadataMismatch` — `"Backend validation metadata did not confirm local flag + confirmation phrase + guarded wrapper requirements."` |

In every failure case the captured local phrase variable is overwritten
to `""` immediately after the preflight returns, matching the existing
phrase-clearing contract.

### Why CanExecute Is Not Changed

`CanExecuteRealMigration` / `CanExecuteRuntimeCutover` /
`CanExecuteRollback` / `CanExecuteRetentionCleanup` are **not**
modified by Phase 10.19D. The reasons:

- Backend permission checks require a network round-trip; running them
  inside `CanExecute` would block the UI thread or introduce async
  hazards in WPF's command pipeline.
- `CanExecute` should remain fast and deterministic. The local flag +
  role + per-section input gates are correct and immediate; backend
  permission is correctly modelled as a click-time preflight that the
  user explicitly initiated.
- A failed click-time preflight produces a clear rejection message in
  the operation's own outcome panel, which matches the existing pattern
  for guarded-wrapper rejections (per Phases 10.12C / 10.13B / 10.14B /
  10.16B).

### Backend Metadata Expectations

The desktop expects the backend's validate response for any allowed
dangerous permission to set:

- `requiresLocalFlag=true`
- `requiresConfirmationPhrase=true`
- `requiresGuardedWrapper=true`

These flags express the backend's understanding of the desktop's defence
stack. If the backend ever responds with any of these as `false`, the
desktop treats it as a protocol violation and **fails closed** with
`Status="MetadataMismatch"`. This guards against accidental backend
downgrade of the desktop guard contract.

### Emergency Rollback of Enforcement

If the backend has a defect that blocks legitimate operators on a pilot
machine, set:

```
operator_backend_permission_enforcement_enabled = "0"
```

in `%LocalAppData%\PosSystem\global_settings.json` and restart the
dashboard (or just click Refresh). Enforcement immediately reverts to
Phase 10.19C visibility-only behaviour. Local flag + role + guarded
wrapper + confirmation phrase + dangerous-operation-lock continue to
protect every dangerous operation regardless of the enforcement flag
state.

This emergency rollback path is **per-machine** and **manual** by design
— there is no kill-switch in the dashboard, and there is no remote
override. Operator + Support Engineer + Store Manager approval is
recorded in the pilot ticket whenever the flag is toggled.

### Security Notes

- **Confirmation phrase is never sent to the backend.** The validate
  request DTO has no `ConfirmationPhrase` field. The phrase is captured
  locally for the guarded wrapper only.
- **Local DB paths are never sent.** Validate requests carry only the
  permission key, the resolved tenant subdomain, the operation name,
  and an optional approval ticket id.
- **Evidence bundle content is never sent.** Phase 10.19D does not
  upload anything; if Phase RBAC-7 ever adds an upload endpoint, it
  will be a separate explicit operator action.
- **Tokens are never logged.** The new code path adds no log statements.
- **No DB switch, logout, or restart on preflight failure.** The
  dashboard surfaces the failure and the dangerous-operation lock
  releases when the command returns; nothing else changes.

---

## Phase 10.19E — Readiness Report and Evidence Bundle Integration

Phase 10.19E extends the Phase 10.17A **Production Pilot Readiness
Report** with a new **Area I — Backend operator permissions** and the
Phase 10.17B **Production Pilot Evidence Bundle** with a new sanitized
JSON file `backend-permission-summary.json`. The two read-only
artifacts now reflect the backend permission stack so support and
operators can decide whether a controlled pilot is safe to attempt with
enforcement enabled.

**Phase 10.19E does not change enforcement behaviour.** It does not
change `CanExecute` predicates. It does not modify guarded wrapper
services. It is purely a read-only evidence + readiness improvement.

### Readiness Report Integration

A new `BackendOperatorPermissionSnapshotService` reads the enforcement
flag and queries the backend identity / permissions / four
dangerous-permission validations. The snapshot is produced fail-closed:
backend offline / 401 / 5xx / network failures yield a snapshot with
populated `Warnings` / `Errors` rather than an exception.

The readiness report's `GenerateAsync` now calls the snapshot service
after the existing Area H runbook checks and adds Area I checks plus a
new `BackendPermissionSummary` line on the report object. Severity:

| Snapshot outcome | When enforcement is OFF | When enforcement is ON |
|---|---|---|
| Identity unavailable | Warning | Blocked |
| Permissions unavailable | Warning | Blocked |
| Permission claim expired | Warning | Blocked |
| Backend role is CASHIER | Warning | Blocked |
| `operator.dashboard.open` missing | Warning | Blocked |
| Dangerous validation `Denied` | Warning | Blocked |
| Dangerous validation `Unavailable` | Warning | Blocked |
| Dangerous validation `MetadataMismatch` | Warning | Blocked |
| Dangerous validation `Allowed` | Pass | Pass |
| Enforcement flag (informational) | Info | Pass |

The existing overall-status calculation walks the `Checks` list — any
`Blocked` → `Blocked`; any `Warning` → `ReadyWithWarnings`; otherwise
`Ready`. Adding Area I therefore naturally promotes the report to
`Blocked` when enforcement is on and the backend is unavailable / the
operator is unauthorised, and naturally produces only warnings when
enforcement is off so the existing controlled-pilot path continues to
work unchanged.

### Evidence Bundle Integration

The evidence bundle now writes one additional sanitized JSON file:

```
%LocalAppData%\PosSystem\logs\pilot-evidence\pilot-evidence-<utc>\backend-permission-summary.json
```

Contents:

- The snapshot record (`BackendOperatorPermissionSnapshot`): generated
  timestamp, enforcement flag state, identity/permissions availability,
  backend-reachable boolean, user/role/tenant/store, permission counts
  + full lists, four dangerous-permission validation snapshots, warnings,
  errors.
- No tokens. No JWT. No DPAPI blob. No raw confirmation phrase.
- The same `MigrationAuditLogger.RedactSecrets` + literal-phrase scrub
  pipeline used by every other sanitized file in the bundle.
- The manifest's `IncludedFiles` array gains `backend-permission-summary.json`
  automatically (the manifest is written last and lists every file the
  bundle actually contains).

### Enforcement Enabled vs Disabled Interpretation

- **Enforcement OFF** (default) — the report's Area I rows are
  informational/Warning. A backend unavailable does NOT block the
  pilot. The bundle still includes `backend-permission-summary.json` so
  Support can see the current backend state.
- **Enforcement ON** — Area I failures become Blockers. The
  controlled-pilot decision matrix routes Blocked outcomes to Hold
  Rollout or Rollback / Recovery Required per the existing
  `operator-pilot-signoff-rollout-decision-runbook.md`.

### Backend Unavailable Handling

If the snapshot service cannot reach the backend at all:

- Identity-available / Permissions-available checks fail with the
  severity rule above.
- All four dangerous-permission validations record `Status="Unavailable"`.
- The report is still produced. The bundle is still written. The
  manifest still lists `backend-permission-summary.json`.
- No exception escapes; the dashboard's Refresh / Export flow remains
  usable.
- If enforcement is OFF, the pilot can still proceed exactly as
  Phase 10.19C / 10.17B documented. If enforcement is ON, the
  controlled-pilot decision matrix surfaces Hold Rollout per
  `operator-pilot-signoff-rollout-decision-runbook.md`.

### Sanitization / Security Notes (Phase 10.19E)

- **No confirmation phrase sent.** The snapshot service constructs
  validate requests with no `ConfirmationPhrase` field; the desktop
  never sends a phrase to the backend.
- **No tokens in the evidence bundle.** The `OperatorIdentityDto`,
  `OperatorPermissionsDto`, and `OperatorPermissionValidateResultDto`
  classes have no token-shaped properties; the snapshot copies only
  structural fields.
- **No raw Authorization header.** The existing `ApiClient` pipeline
  handles the bearer header; the snapshot service has no access to it.
- **No local DB rows.** Nothing in the snapshot service queries SQLite
  or copies DB files.
- **No DB paths.** The snapshot service does not include any
  `%LocalAppData%\PosSystem\...\*.db` path in its output.
- **Redaction pipeline.** The new file passes through the same
  `WriteSanitizedJson` helper as every other file in the bundle —
  `JsonSerializer.Serialize` → `MigrationAuditLogger.RedactSecrets`
  (5-pass) → six-phrase literal scrub (`EXECUTE_REAL_TENANT_DB_MIGRATION`,
  `ENABLE_TENANT_DB_RUNTIME_MODE`, `EXECUTE_TENANT_DB_RUNTIME_ROLLBACK`,
  `ROLLBACK_TO_LEGACY_POS_DB`, `I UNDERSTAND TENANT DB ROLLBACK`,
  `EXECUTE_RETENTION_CLEANUP`) — before atomic temp-to-final write.
- **No bundle upload.** Phase 10.19E does not upload anything to the
  backend. The bundle remains the operator's local artifact for support
  tickets.
- **No DB switch / logout / restart on snapshot failure.** The new
  service is read-only; if it fails it surfaces a warning/error and the
  rest of the report/bundle proceeds.

---

## Phase 10.19G — Audit intent & evidence registration (desktop client)

Adds a thin, **default-OFF**, **non-blocking** desktop client for the two
Phase 10.19F backend endpoints:

- `POST /api/v1/operator/audit-intent` — records that the operator declared
  intent to perform a specific dangerous operation.
- `POST /api/v1/operator/evidence/register` — registers sanitized metadata
  about a Pilot Evidence Bundle the desktop has already exported locally.

### Feature flags (both default OFF)

| Flag | Effect when `"1"` |
|---|---|
| `operator_backend_audit_intent_enabled` | Issues a non-blocking audit-intent call **after** the Phase 10.19D backend permission preflight is `Allowed` (or skipped because enforcement is OFF) and **before** the local guarded executor runs. |
| `operator_backend_evidence_registration_enabled` | After a successful Pilot Evidence Bundle export, sends sanitized bundle metadata to the backend. |

Missing / empty / `"0"` ⇒ **no HTTP call is made**. There is no UI to write
either flag. The current value of each flag is displayed read-only in the
"Backend audit / evidence integration" sub-card next to the Phase 10.19D
enforcement state.

### Non-blocking audit-intent semantics

The audit-intent call is **never** a precondition for executing a
dangerous operation. The flow is:

1. Capture confirmation phrase → clear bound textbox → clear local copy
   immediately after wrapper returns (unchanged).
2. Phase 10.19D backend permission preflight (unchanged). If denied,
   early return. If skipped (enforcement OFF), proceed.
3. **Phase 10.19G audit-intent call (this step).** Flag OFF ⇒ skip.
   Flag ON ⇒ send the audit intent. Outcome:
   - `accepted: true` ⇒ record `intentId` + reason in the display state.
   - `accepted: false` ⇒ `Warnings.Add("BackendAudit: intent rejected ...")`.
   - HTTP/network/JSON failure ⇒ `Warnings.Add("BackendAudit: intent registration unavailable ...")`.
   - In **all three** cases above, **execution proceeds to step 4**.
   The backend is response-only in Phase 10.19F and not yet the system
   of record; using it as a hard gate would risk false negatives.
4. Guarded local wrapper executes (unchanged) — local flag + role +
   `OperatorAccessService` + confirmation phrase + readiness gates +
   dangerous-operation lock all enforce as before.

Permission-key + operation-name pairs sent in the audit-intent request:

| Core method | `permissionKey` | `operationName` |
|---|---|---|
| Real Migration   | `operator.migration.execute`         | `execute-real-migration` |
| Runtime Cutover  | `operator.cutover.execute`           | `execute-runtime-cutover` |
| Rollback         | `operator.rollback.execute`          | `execute-rollback` |
| Retention Cleanup| `operator.retention.cleanup.execute` | `execute-retention-cleanup` |

### Metadata-only evidence registration

The evidence-registration call runs only after `ProductionPilotEvidenceBundleService`
reports `Outcome="Success"`. It sends a sanitized metadata payload — no
ZIP bytes, no JSON file contents, no raw log content, no DB content, no
backups. Specifically:

- `EvidenceBundleId` is the bundle's folder name (`pilot-evidence-...`)
  — the leaf, not the full path.
- `IncludedFiles` is a list of **bare file names** (e.g. `manifest.json`).
  Desktop runs `Path.GetFileName` over every entry as defence-in-depth
  even though the bundle service already records bare names.
- `ManifestSha256` is the SHA-256 hex digest of `manifest.json` if the
  file exists on disk. Computed off the UI thread via `Task.Run`.
- `BundleSha256` is the SHA-256 hex digest of the ZIP if the bundle
  service produced a ZIP; null otherwise.
- `ClientMachineNameHash` is the SHA-256 hex digest of
  `Environment.MachineName`. **Raw machine name is never sent.**
- `BackendPermissionEnforcementEnabled` / `BackendPermissionSummaryStatus`
  reflect the current Phase 10.19D flag state — they are descriptive
  fields, not gating fields.

### What is NEVER sent to the backend

- The operator's confirmation phrase (no field exists on the DTOs).
- Access tokens, refresh tokens, Authorization headers (the `ApiClient`
  transport layer handles bearer; the wrapper neither inspects nor logs).
- Raw machine name (only its SHA-256 digest).
- Local DB paths or any `%LocalAppData%\...\*.db` reference.
- DB files, backup files, raw audit logs, raw evidence JSON content,
  bundle ZIP bytes.
- Any string ending in `.db`, `.db-wal`, or `.db-shm` (also rejected by
  the backend sanitiser).

### Failure modes (non-fatal)

| Condition | Behaviour |
|---|---|
| Both flags OFF (default) | Zero HTTP calls. Zero behaviour change vs. Phase 10.19F. |
| Audit-intent flag ON, backend returns `accepted=false` | `BackendAudit:` warning. Wrapper still executes. |
| Audit-intent flag ON, network/JSON failure | `BackendAudit:` warning. Wrapper still executes. |
| Evidence flag ON, backend returns `accepted=false` | `BackendEvidence:` warning. **Local bundle remains valid** and visible in the Pilot Evidence Bundle card. |
| Evidence flag ON, network/JSON failure | `BackendEvidence:` warning. Local bundle preserved. |
| Manifest / ZIP missing on disk at hash time | The corresponding hash field is sent as null. Warning is surfaced. The request still goes out. |

### Future persistence

Phase 10.19F backend records have `auditSource="response-only"` — the
backend does not yet persist either record. A future phase extends the
canonical audit taxonomy so these records can flow through
`AuditEventPublisher` → `AuditOutboxPoller` → `AuditOutboxRetentionService`
without disturbing the Tier-1 business-mutation channel. Until then the
local Pilot Evidence Bundle on disk remains the source of truth.

---

## Phase 10.19J — Read-only Audit / Evidence Review UI (desktop client)

Adds a new **default-OFF, read-only** card in the Migration Operations
dashboard that consumes the Phase 10.19I backend review API.

### Feature flag

| Flag | Default | When `"1"` |
|---|---|---|
| `operator_backend_audit_review_ui_enabled` | OFF (missing / empty / `"0"`) | Operator may use Refresh / Previous / Next / Lookup buttons to read sanitized audit-event projections from the backend. |

Missing / `"0"` ⇒ **no backend call is made**. The card still renders
with a "Disabled by local flag" message; every command short-circuits.
There is no UI to write the flag. The flag's current state is
displayed read-only inside the card.

### Endpoints consumed

| Endpoint | Wrapper method | Notes |
|---|---|---|
| `GET /api/v1/operator/audit/events` | `OperatorAuditReviewApiClient.GetEventsAsync(query, ct)` | Page list. Size is desktop-clamped to `[1, 200]` before the URL is built; backend re-clamps. |
| `GET /api/v1/operator/audit/events/{eventId}` | `GetEventAsync(eventId, ct)` | Single row by audit_logs.id. 404 → null. |
| `GET /api/v1/operator/audit/intents/{intentId}` | `GetIntentAsync(intentId, ct)` | Single operator-maintenance event by entity_id. |
| `GET /api/v1/operator/audit/evidence/{registrationId}` | `GetEvidenceAsync(registrationId, ct)` | Single operator-evidence event by entity_id. |

All four methods catch HTTP / network / JSON exceptions and return
`null`. The ViewModel adds a `BackendAuditReview:` warning + status
message on null and never crashes the app. The existing bearer +
tenant-header + 401-refresh HTTP pipeline is re-used; no separate
unauthenticated client is created.

### Filters

The list endpoint accepts: `tenantId`, `entityType`, `action`,
`operationName`, `permissionKey`, `accepted`, `from`, `to`, `page`,
`size`. The desktop card binds all filter fields to ViewModel
properties so the operator can refine the view without writing the
URL. `Clear Filters` resets every filter and resets `page` to 0.

### Pagination

- Default page 0, default size 50, max size 200 (clamped on the desktop
  before the URL is built).
- **Next Page** button issues a new fetch only if `HasNext=true`.
- **Previous Page** button issues a new fetch only if `Page > 0`.
- Total event count is rendered as `Total events: N` under the filters.

### Lookup

Three separate textboxes + buttons:

- `eventId` → `Lookup Event` → `GET /events/{eventId}` (eventId is the
  numeric `audit_logs.id`; the controller responds 404 if invalid).
- `intentId` → `Lookup Intent` → `GET /intents/{intentId}`.
- `registrationId` → `Lookup Evidence` → `GET /evidence/{registrationId}`.

Each lookup populates the "Selected event" sub-card with the sanitized
detail projection. A miss (404 / network failure) clears the selected
panel and shows "No event/intent/evidence found, or backend unreachable."

### Desktop-side re-redaction

Even though the backend (Phase 10.19I) re-sanitizes metadata before
returning, the desktop applies its own scrub layer before rendering.
The regex in `OperatorAuditReviewRedaction.SecretPattern` matches:

- `Bearer …`
- JWT-shaped `eyJ…`
- `Authorization: …`
- `access_token=…`
- `refresh_token=…`
- `password=…`
- DPAPI prefix `enc:v1:…`

…and replaces every match with `[REDACTED]`. In addition, the six
known confirmation-phrase literals
(`EXECUTE_REAL_TENANT_DB_MIGRATION`, `ENABLE_TENANT_DB_RUNTIME_MODE`,
`EXECUTE_TENANT_DB_RUNTIME_ROLLBACK`, `ROLLBACK_TO_LEGACY_POS_DB`,
`I UNDERSTAND TENANT DB ROLLBACK`, `EXECUTE_RETENTION_CLEANUP`) are
replaced verbatim wherever they appear inside any string value. The
scrub recurses into nested maps and lists (matching the backend's
metadata shape).

If any value was rewritten by this layer, the card adds the warning

> `BackendAuditReview: desktop redaction applied to returned metadata.`

to both the card-local warnings list and the dashboard-wide warnings
list. The "redacted" field of the selected detail is reported as
`true` whenever EITHER the backend's `redacted` flag was true OR the
desktop layer rewrote a value.

### Read-only boundary

- **No mutation, no execute, no delete.** No `POST` / `PUT` / `PATCH` /
  `DELETE` requests are issued by this card. No backend mutation
  endpoint exists.
- **`CanExecute` of every dangerous command is unchanged.** The card's
  commands do NOT take the dangerous-operation lock. The Migration /
  Cutover / Rollback / Retention Cleanup buttons keep their existing
  flag + role + guarded wrapper + confirmation phrase + dangerous-
  operation lock guards.
- **No raw file download / upload.** The card has no file-picker, no
  export button, no download button. It only displays text projections.
- **No tokens / passwords / confirmation phrases rendered.** Every
  string passes through the desktop scrub before display; DTOs by
  design have no token / password / phrase field.

### Troubleshooting

| Symptom | Cause | Behaviour |
|---|---|---|
| Card shows "Disabled by local flag — no backend call made." | `operator_backend_audit_review_ui_enabled` is missing / `""` / `"0"`. | Set the flag to `"1"` in the local global settings to enable; the card will show "Enabled (operator_backend_audit_review_ui_enabled=\"1\")." on the next refresh. |
| `Refresh Events` returns "Backend unreachable or rejected the request." | Backend offline, 401, 403, or 5xx. | Warning is logged; the existing event list is cleared but the app keeps running. No logout / restart / DB switch happens. |
| Lookup returns "No event/intent/evidence found, or backend unreachable." | 404 from the backend (row not visible to the caller's tenant) or HTTP failure. | The selected panel is cleared. The lookup buttons may be retried freely. |
| Warning `BackendAuditReview: desktop redaction applied to returned metadata.` | A returned string still contained a secret-looking substring after backend sanitization. | Acceptable defence-in-depth — the value was overwritten with `[REDACTED]` before being added to the metadata pane. File a follow-up on the source field if this recurs. |

---

## Phase 10.20G — Operator Permission Admin Read-Only UI (desktop client)

Adds a new **default-OFF, read-only** card on the Migration Operations
dashboard that consumes the Phase 10.20F backend admin API.

### Feature flag

| Flag | Default | When `"1"` |
|---|---|---|
| `operator_permission_admin_readonly_ui_enabled` | OFF (missing / empty / `"0"`) | Operator may use the Refresh / Previous / Next / Clear / Resolve buttons in the new card to read persisted definitions, role grants, user overrides, and DB-shadow effective permissions. |

Missing / `"0"` ⇒ **no backend call is made**. The card still renders
with a "Disabled by local flag" message; every command short-circuits.
There is no UI control to write the flag — operators must edit the
local global settings table.

### Backend endpoints consumed

| Endpoint | Wrapper method | Notes |
|---|---|---|
| `GET /api/v1/admin/operator-permissions/definitions` | `OperatorPermissionAdminApiClient.GetDefinitionsAsync(query, ct)` | Filters: permissionKey / category / active / dangerous. Page size desktop-clamped to `[1, 200]`. |
| `GET /api/v1/admin/operator-permissions/role-grants` | `GetRoleGrantsAsync(query, ct)` | Filters: role / permissionKey / tenantScopePolicy / active. |
| `GET /api/v1/admin/operator-permissions/user-overrides` | `GetUserOverridesAsync(query, ct)` | Filters: userId / tenantId / storeId / permissionKey / grantType / active / expired. ADMIN's tenantId is forced server-side. |
| `GET /api/v1/admin/operator-permissions/effective` | `GetEffectiveAsync(query, ct)` | Query: userId (optional, default = caller) / tenantId / storeId. Backend's Phase 10.20D resolver runs in shadow mode; the desktop projects the result. |

All four wrapper methods catch HTTP / network / JSON exceptions and
return `null`. The ViewModel adds a `PermissionAdmin:` warning + status
message on null. Existing bearer + tenant-header + 401-refresh HTTP
pipeline is re-used.

### Sections / tabs

The card is a single scrollable Border with four sub-cards rendered
top-to-bottom:

1. **Definitions** — filter inputs + Refresh / Previous / Next / Clear
   buttons + total counter + `[ACTIVE] permission.key | CATEGORY |
   dangerous=… | localFlag=… | phrase=… | wrapper=…` rows.
2. **Role grants** — filter inputs + same buttons + `[ACTIVE] ROLE ->
   permission.key | TENANT_SCOPE_POLICY` rows.
3. **User overrides** — filter inputs + same buttons + `[ACTIVE]
   user=N tenant=T store=S permission=K grant=ALLOW|DENY expires=…
   reason="…" ticket=…` rows.
4. **Effective view (DB shadow)** — `userId` / `tenantId` / `storeId`
   inputs + Resolve / Clear buttons + result summary (auditSource,
   enabled, healthy, permissionsSource, comparison.matchesCode) +
   effective permissions list + decisions list (`permission.key |
   allowed=… | source=… | reason=…`).

### Pagination

- Default page 0, default size 50, max size 200 (desktop-clamped
  before the URL is built).
- **Next** button issues a fetch only when `HasNext=true`.
- **Previous** button only when `Page > 0`.
- Total count is rendered under each sub-card's filters as
  `Total <kind>: N`.

### Desktop redaction

Even though the backend (Phase 10.20F) already scrubs `reason` and
`approvalTicketId`, every string value rendered by the card passes
through `OperatorPermissionAdminRedaction.ScrubAndTruncate`:

- regex over `Bearer`, JWT-shaped `eyJ…`, `Authorization`,
  `access_token`, `refresh_token`, `password`, DPAPI `enc:v1:` →
  `[REDACTED]`.
- Six known confirmation-phrase literals
  (`EXECUTE_REAL_TENANT_DB_MIGRATION` etc.) → `[REDACTED]`.
- Length truncated to 500 chars + `…` if exceeded.

If any redaction fires while rendering a row, the warning
`PermissionAdmin: desktop redaction applied.` is added to both the
local PermissionAdminWarnings and the dashboard-wide `Warnings` list.

### Read-only boundary

- **No mutation control on the card.** No "Grant", "Revoke", "Save",
  "Delete", "Edit", "Upload", or "Execute" button. No `POST` / `PUT` /
  `PATCH` / `DELETE` HTTP request is issued by this UI.
- **No DB authoritative toggle.** The card does not switch any flag.
- **`CanExecute` of every dangerous command unchanged.** The card's
  commands do NOT take the dangerous-operation lock and do NOT call
  any `Guarded*ExecutorService`.
- **No raw files / logs / backups downloaded or uploaded.**
- **No confirmation phrases / tokens / passwords displayed.**
- **No logout / restart / DB switch on failure.** Failures surface a
  warning + status message; the rest of the dashboard remains usable.

### Troubleshooting

| Symptom | Cause | Behaviour |
|---|---|---|
| Card shows `Disabled (operator_permission_admin_readonly_ui_enabled missing or "0").` | Flag missing / `"0"`. | Set the flag to `"1"` in the local global settings and refresh; the buttons enable on the next status refresh. |
| Refresh returns `Backend unreachable or rejected the request.` | Backend offline, 401, 403, or 5xx. | `PermissionAdmin:` warning is logged; the list for that sub-section is cleared but the app keeps running. No logout / restart / DB switch happens. |
| Effective view shows `enabled=false` even though the Refresh seemed to work | Backend's `operator.permission.db.effective.shadow.enabled` flag is off. | Set the backend flag to `true` and restart the backend; the next Resolve will return the full effective set. |
| Warning `PermissionAdmin: desktop redaction applied.` | A returned string still contained a secret-looking substring after backend sanitization. | Acceptable defence-in-depth — the value was overwritten with `[REDACTED]` before display. File a follow-up on the source field if this recurs. |
| User overrides list is empty when you expect data | ADMIN's tenant filter is forced server-side to the caller's tenant. Cross-tenant rows are invisible to ADMIN. Sign in as GLOBAL_ADMIN to see cross-tenant overrides. | — |

---

## Phase 10.20I — Operator Permission Admin Mutation UI (desktop client)

Adds a **default-OFF, dual-gated** desktop card that creates / revokes
DB permission rows by calling the Phase 10.20H backend mutation API.

### Dual feature flag

| Layer | Flag | Default | Effect when both ON |
|---|---|---|---|
| Desktop | `operator_permission_admin_mutation_ui_enabled` | OFF (missing / empty / `"0"`) | Mutation buttons enable; clicking a button validates locally, opens a Yes/No confirmation, and calls the backend wrapper. |
| Backend | `operator.permission.admin.mutations.enabled` | OFF | Endpoint accepts the request and writes the row + audit row atomically. |

When either flag is OFF the UI surfaces a clear status message. The
backend's flag-OFF response is HTTP 403 with `code="FEATURE_FLAG_OFF"`;
the desktop displays the typed message in the result pane.

There is no UI to write either flag. Both flags are read-only displays.

### Endpoints consumed

| Endpoint | Wrapper method |
|---|---|
| `POST /api/v1/admin/operator-permissions/user-overrides` | `OperatorPermissionAdminMutationApiClient.CreateUserOverrideAsync` |
| `POST /api/v1/admin/operator-permissions/user-overrides/{id}/revoke` | `RevokeUserOverrideAsync` |
| `POST /api/v1/admin/operator-permissions/role-grants` | `CreateRoleGrantAsync` |
| `POST /api/v1/admin/operator-permissions/role-grants/{id}/revoke` | `RevokeRoleGrantAsync` |

The wrapper translates non-2xx responses into a synthetic
`OperatorPermissionAdminMutationResponseDto<T>` with `Success=false`
and the typed backend error (`{status, code, message}`) so the UI can
render the rejection without crashing.

### Forms

The card contains four sub-cards each with its own inputs + Clear
button + Submit button:

1. **Create user override** — `userId`, `tenantId`, `storeId`,
   `permissionKey`, `grantType` (ALLOW / DENY), `expiresAt`
   (ISO-8601 text), `reason`, `approvalTicketId`.
2. **Revoke user override** — `overrideId` (numeric), `reason`,
   `approvalTicketId`.
3. **Create role grant** — `role`, `permissionKey`,
   `tenantScopePolicy` (TENANT_ONLY / CROSS_TENANT / STORE_SCOPED),
   `reason`, `approvalTicketId`.
4. **Revoke role grant** — `roleGrantId` (numeric), `reason`,
   `approvalTicketId`.

A fifth "Last mutation result" sub-card displays the outcome of the
most recent attempt (`status`, `auditSource`, `message`, item
summary, timestamp).

### Local validation

Before the wrapper is called the ViewModel checks:

- `userId` / `overrideId` / `roleGrantId` must be numeric.
- `permissionKey` / `role` / `tenantScopePolicy` non-blank.
- `grantType` must be `ALLOW` or `DENY` (case-insensitive).
- `reason` and `approvalTicketId` non-blank.
- `expiresAt` (when provided) must be parseable as ISO-8601.
- Dangerous-permission ALLOW (`operator.migration.execute`,
  `operator.cutover.execute`, `operator.rollback.execute`,
  `operator.retention.cleanup.execute`, `operator.flags.change`)
  requires non-null `expiresAt`. (Backend additionally enforces a
  24h cap.)

Validation failures add a `PermissionAdminMutation:` warning + error
and never reach the backend.

### Confirmation dialogs

Every successful local validation triggers a Yes/No `MessageBox`
warning before the HTTP call. The dialog body lists the form values
plus the standard "this changes DB permission admin tables; DB
permissions are still not authoritative; runtime decisions are
unchanged; this does not approve / execute maintenance operations;
mutation is audited by backend; backend may reject if its server-side
flag is OFF" copy.

Dangerous ALLOW overrides additionally show:

> *Dangerous ALLOW grants must be temporary and audited. This does
> not bypass desktop local flags, confirmation phrase, or guarded
> wrappers.*

No dangerous-operation confirmation phrase is reused. The desktop's
six maintenance-execution phrases (`EXECUTE_REAL_TENANT_DB_MIGRATION`
etc.) are not displayed or required — they're irrelevant here.

### Read-only / runtime behaviour unchanged

- `OperatorPermissionService` runtime decisions (backend-side) are
  unchanged.
- `/api/v1/operator/permissions` / `/permissions/validate` responses
  are byte-identical.
- The Phase 10.20G read-only card is unaffected. The new card
  optionally triggers `RefreshUserOverridesAsync` / `RefreshRoleGrantsAsync`
  after a successful mutation so the read-only lists pick up the
  fresh state.
- `CanExecute` of every dangerous-operation command is unchanged. The
  mutation card does NOT take the dangerous-operation lock, call any
  `Guarded*ExecutorService`, or modify any local desktop flag.

### `auditSource` display

On every successful response the result pane shows the backend's
`auditSource` value. Under the Phase 10.20H strict-audit policy this
is always `"audit-outbox"` — the alternative outcomes
(`"audit-outbox-failed"`) trigger a rollback, so the desktop never
sees a success envelope with that value.

### Desktop-side redaction

Every string value rendered in the result pane (item summary,
backend message) is passed through
`OperatorPermissionAdminRedaction.ScrubAndTruncate` (Phase 10.20G):
regex over `Bearer`, JWT-shaped `eyJ…`, `Authorization`,
`access_token`, `refresh_token`, `password`, DPAPI `enc:v1:`, plus
the six known confirmation-phrase literals, plus 500-char
truncation. Transport-failure exception messages run through the
same scrub before being surfaced.

### Troubleshooting

| Symptom | Cause | Behaviour |
|---|---|---|
| Buttons greyed; status says `Disabled (operator_permission_admin_mutation_ui_enabled missing or "0").` | Local flag missing / `"0"`. | Set the flag in local global settings; reopen the dashboard. |
| Backend rejects with `FEATURE_FLAG_OFF` | Backend `operator.permission.admin.mutations.enabled` is `false`. | Coordinate with the backend operator to flip the flag. Desktop cannot change it. |
| Backend rejects with `FORBIDDEN` (self-`ALLOW`) | Self-`ALLOW` overrides are blanket-denied. | Use self-`DENY` for safety lockouts, or have a peer GLOBAL_ADMIN create the grant. |
| Backend rejects with `BAD_REQUEST` (dangerous `ALLOW` `expiresAt`) | Dangerous `ALLOW` requires non-null `expiresAt` ≤ 24h. | Enter a future timestamp within 24h. |
| Backend rejects with `CONFLICT` | Duplicate active row already exists. | Inspect the Phase 10.20G read-only list; revoke the existing row first if intentional. |
| Backend rejects with `AUDIT_FAILED` | Backend's strict-audit policy rolled the mutation back because the audit-outbox INSERT failed. | Coordinate with backend ops; retry once the audit pipeline is healthy. No row was inserted. |
| Mutation succeeded but read-only list doesn't show the row | List refresh runs after success. If the refresh failed (e.g., transient network blip), click `Refresh User Overrides` or `Refresh Role Grants` manually in the Phase 10.20G card. | — |

---

## Phase 10.20J — Limited Pilot Runbook (documentation)

A limited-pilot runbook for the Phase 10.20A-I Operator Permission
Administration stack is now published:

→ [`operator-permission-admin-limited-pilot-runbook.md`](./operator-permission-admin-limited-pilot-runbook.md)
(desktop-side pointer; canonical document lives in the backend repo
at [`Ham-Pos/docs/operator-permission-admin-limited-pilot-runbook.md`](../../Ham-Pos/docs/operator-permission-admin-limited-pilot-runbook.md)).

Key desktop-side facts the runbook captures:

- Both desktop flags
  (`operator_permission_admin_readonly_ui_enabled` and
  `operator_permission_admin_mutation_ui_enabled`) **remain default
  OFF**. They are flipped briefly during the pilot and restored to
  OFF at the end.
- The pilot must verify (scenario T22) that no dangerous-operation
  `CanExecute` state changes because of the pilot's flag flips.
- The pilot's scenario T23 runs a normal POS sale as a smoke check
  to confirm the sales flow is unaffected.
- Phase 10.20J ships the runbook only — it does not execute the
  pilot. Execution requires an operator to follow the runbook on a
  controlled environment and produce the documented evidence bundle.

---

## Phase 10.20K — Controlled Rollout Runbook (documentation)

A controlled-rollout runbook is now published as the successor to
the 10.20J limited pilot:

→ [`operator-permission-admin-controlled-rollout-runbook.md`](./operator-permission-admin-controlled-rollout-runbook.md)
(desktop-side pointer; canonical lives at
[`Ham-Pos/docs/operator-permission-admin-controlled-rollout-runbook.md`](../../Ham-Pos/docs/operator-permission-admin-controlled-rollout-runbook.md)).

Desktop-side facts the runbook captures:

- Both desktop flags continue to be default OFF outside of
  operational use. The mutation flag is **only** enabled per the
  rollout wave's documented mutation window and restored to OFF per
  the wave's exit criteria.
- Six waves: Wave 0 staging → Wave 1 single GLOBAL_ADMIN +
  controlled tenant → Wave 2 limited ADMIN read-only → Wave 3
  limited ADMIN own-tenant DENY mutation → Wave 4 broader read-only
  → Wave 5 broader mutation (only if approved by the operations
  council).
- Every wave includes the dangerous-operation `CanExecute` check
  (T22-equivalent) and the POS sales smoke (T23-equivalent) at
  wave end. Any change there triggers rollback.
- Phase 10.20K ships the runbook only — it does not execute the
  rollout. Execution requires starting at Wave 0 and following each
  wave's per-window discipline.

## Phase 10.21D — Read-Only DB-Authoritative Pilot Runbook (documentation)

A pilot runbook for the Phase 10.21C read-only DB-authoritative
resolver is now published. The canonical doc lives in the backend
repository; a desktop-side pointer doc covers the desktop-specific
notes:

→ desktop pointer:
[`operator-permission-readonly-authoritative-pilot-runbook.md`](./operator-permission-readonly-authoritative-pilot-runbook.md)
(canonical at
[`Ham-Pos/docs/operator-permission-readonly-authoritative-pilot-runbook.md`](../../Ham-Pos/docs/operator-permission-readonly-authoritative-pilot-runbook.md)).

Desktop-side facts the pilot captures:

- **Desktop runtime behaviour is unchanged** throughout the pilot.
  The pilot exercises backend `/validate` and `/permissions`
  decisions for read-only keys when the backend
  `operator.permission.db.authoritative.readonly.enabled` flag is
  briefly enabled. The desktop's own runtime behaviour (POS sales,
  login, dashboard) is not modified.
- **Dangerous buttons remain unchanged.** Real Migration / Cutover
  / Rollback / Retention Cleanup keep the same `CanExecute` state
  before, during, and after the pilot. T19 of the test matrix is
  the explicit dangerous-button check; T16-T18 verify the
  dangerous-key `/validate` decisions are byte-identical to
  baseline.
- **No confirmation phrase leaves the desktop.** The pilot's
  override mutations (`reason`, `approvalTicketId`) deliberately do
  not accept or display any of the six maintenance-execution
  phrases; the desktop scrub layer additionally redacts them on
  display if a backend response ever included one.
- **POS sales smoke (T21).** Cashier login + one sale runs at the
  end of the pilot to prove no regression slipped in.
- **No backend execution path.** The pilot does not trigger any
  migration / cutover / rollback / retention-cleanup; the desktop's
  guarded wrappers remain the only legitimate execution path for
  those operations.

Phase 10.21D ships the runbook only — it does not execute the
pilot. Execution requires an operator to follow the canonical
runbook on a controlled environment and produce the evidence bundle
described in §10.

## Phase 10.21A — DB-Authoritative Permission Design Plan (documentation)

The next workstream — making DB-backed operator permissions
authoritative for runtime decisions — has a published design plan:

→ canonical at
[`Ham-Pos/docs/backend-operator-permission-db-authoritative-plan.md`](../../Ham-Pos/docs/backend-operator-permission-db-authoritative-plan.md)

Desktop-side facts the plan captures:

- The current desktop UI can **show** persisted permission data
  (Phase 10.20G read-only card) and **administer** persisted grants
  /overrides (Phase 10.20I mutation card, dual-flag gated and
  default OFF). Both stay unchanged in Phase 10.21A.
- **Runtime permission decisions remain code-only.** Throughout
  Phase 10.21A, `OperatorPermissionService.validate(...)` continues
  to resolve from the in-code map; the desktop's display of `role`
  and `permissions` from `/api/v1/operator/permissions` keeps showing
  `permissionsSource = "code-mapping-v1"`. No desktop view model
  property, observable, or XAML control changes.
- **Future authoritative status display belongs to Phase 10.21+.**
  When that workstream lands, the Migration Operations dashboard
  gains a read-only sub-section showing authoritative mode,
  DB-authoritative read-only / dangerous flags, fallback-used /
  mismatch counters, and the per-request `decisionTraceId`. That
  sub-section is gated by a new desktop flag
  `operator_permission_db_authoritative_summary_ui_enabled` (default
  OFF) and exposes **no** buttons or inputs.
- **Dangerous buttons must not become easier to execute.** Phase 10.21
  is explicit: the desktop's five-layer dangerous check (local flag +
  role + confirmation phrase + dangerous-operation lock + backend
  preflight) is untouched. A DB-authoritative ALLOW satisfies the
  preflight layer only when the other four layers also pass.
- **Confirmation phrases stay desktop-local.** Phase 10.21 adds
  no field, no DTO property, and no audit metadata that accepts a
  phrase. The Phase 10.19H scrub continues to redact accidental
  leakage.
- **No backend execution.** Phase 10.21 does not introduce any
  backend endpoint that executes migration, cutover, rollback, or
  retention cleanup. The desktop's guarded wrappers remain the
  only legitimate execution path.
- **Old desktop compatibility.** New `/permissions` / `/validate`
  response fields planned by Phase 10.21 are additive and ignored by
  the current desktop deserializer (System.Text.Json
  ignore-unknown). Old desktops continue to operate exactly as
  today through every Phase 10.21 sub-phase.

## Phase 10.21G — Operator Permission Authoritative Status (desktop client)

Read-only desktop card that surfaces the backend authoritative-mode
status produced by the Phase 10.21G endpoint
(`GET /api/v1/admin/operator-permissions/authoritative-status`).
The canonical contract lives in the backend repository:

→ [`Ham-Pos/docs/backend-operator-permission-authoritative-status.md`](../../Ham-Pos/docs/backend-operator-permission-authoritative-status.md)

Desktop-side facts:

- **Local feature flag:**
  `operator_permission_authoritative_status_ui_enabled`. Default OFF
  (missing / empty / `"0"`). When OFF the card is read-only-disabled
  and no backend call is issued.
- **DTOs:** `OperatorPermissionAuthoritativeStatusDto` (top-level)
  plus four sub-DTOs (`FlagStatus`, `ReadinessSummary`,
  `RiskSummary`, `StatusIssue`) under
  `PosSystem.Core.DTOs`. All `[JsonPropertyName]` mapped to the
  backend camelCase shape.
- **API client:**
  `OperatorPermissionAdminApiClient.GetAuthoritativeStatusAsync(...)`
  is the fail-closed wrapper. HTTP / network / deserialization
  failures return `null` so the dashboard never crashes;
  `OperationCanceledException` is re-thrown. Bearer token + tenant
  header come from the existing `ApiClient.GetWithRefreshAsync`
  pattern.
- **ViewModel:** `MigrationOperationsViewModel` gains ~18 observable
  properties (status, source, flag enabled labels, readiness booleans
  as strings, counter strings) and six observable collections
  (`Flags`, `Readiness`, `Risks`, `Issues`, `Errors`, `Warnings`).
  Two `[RelayCommand]` methods —
  `RefreshPermissionAuthoritativeStatusAsync` and
  `ClearPermissionAuthoritativeStatus`. Refresh short-circuits when
  the local flag is OFF and the card displays the disabled banner.
  Apply method passes every string through the existing
  `OperatorPermissionAdminRedaction.ScrubAndTruncate(...)` (Phase
  10.20G).
- **UI card:** new amber-bordered (`#0EA5E9`) read-only card in
  `MigrationOperationsWindow.xaml`, inserted between the
  Phase 10.20I mutation card and the Phase 10.13B guarded runtime
  cutover card. Title: *"Operator Permission Authoritative Status —
  Read Only (Phase 10.21G)"*. Read-only banner:
  *"This section is read-only. It shows DB-authoritative permission
  mode, parity/preflight readiness, and risk status. It does not
  grant, revoke, approve, or execute maintenance operations."*
  Refresh + Clear buttons. Summary grid with the 14 single-value
  fields. Five `ItemsControl` lists (Flags, Readiness, Risks,
  Issues, Errors) with BLOCKER red / WARNING amber / INFO normal
  colour cues. No mutation buttons, no execute buttons, no
  confirmation-phrase field, no upload control.
- **Dangerous behaviour:** unchanged. The card has no impact on
  Real Migration / Cutover / Rollback / Retention Cleanup
  `CanExecute`. The Phase 10.21F dangerous-authoritative resolver
  is observable via the new card's flag / readiness rows, but the
  card never triggers a dangerous-key validation; it only reads
  the aggregated server-side status snapshot.
- **Old desktop compatibility:** old desktops without this card
  continue to work unchanged. The backend endpoint is additive;
  existing endpoints + DTOs are byte-identical.

## Phase 10.21H — Dangerous DB-Authoritative Pilot Runbook (documentation)

A pilot runbook for the Phase 10.21F dangerous DB-authoritative
resolver is now published. The canonical doc lives in the backend
repository; a desktop-side pointer doc covers the desktop-specific
notes:

→ desktop pointer:
[`operator-permission-dangerous-authoritative-pilot-runbook.md`](./operator-permission-dangerous-authoritative-pilot-runbook.md)
(canonical at
[`Ham-Pos/docs/operator-permission-dangerous-authoritative-pilot-runbook.md`](../../Ham-Pos/docs/operator-permission-dangerous-authoritative-pilot-runbook.md)).

Desktop-side facts the pilot captures:

- **Desktop runtime behaviour is unchanged** throughout the pilot.
  The pilot exercises backend dangerous-key `/validate` decisions
  when the
  `operator.permission.db.authoritative.dangerous.enabled` flag is
  briefly enabled. The desktop's own runtime behaviour (POS sales,
  login, dashboard) is not modified.
- **Dangerous buttons remain unchanged.** Real Migration / Cutover
  / Rollback / Retention Cleanup keep the same `CanExecute` state
  before, during, and after the pilot. T20 of the test matrix is
  the explicit dangerous-button check; T2-T6 verify the dangerous-
  key `/validate` response shape and metadata flags stay correct
  (`requiresLocalFlag=true`, `requiresConfirmationPhrase=true`,
  `requiresGuardedWrapper=true` on every dangerous response).
- **No dangerous button is clicked.** The pilot is a permission-
  decision pilot, not an execution pilot. The desktop's guarded
  wrappers remain the only legitimate execution path; this pilot
  does not exercise them.
- **No confirmation phrase leaves the desktop.** The pilot's
  override mutations (`reason`, `approvalTicketId`) deliberately
  do not accept or display any of the six maintenance-execution
  phrases; the desktop scrub layer additionally redacts them on
  display if a backend response ever included one. T20 also
  inspects the desktop network log for any phrase substring — any
  such substring is an immediate STOP.
- **Phase 10.21G authoritative-status card** is flipped ON for the
  pilot window via the desktop flag
  `operator_permission_authoritative_status_ui_enabled` (§9 step
  5) and restored to OFF at pilot close (§9 step 15). The card is
  the day-to-day operational view of the pilot's flag state and
  readiness signals.
- **POS sales smoke (T21).** Cashier login + one sale runs at the
  end of the pilot to prove no regression slipped in.
- **No backend execution path.** The pilot does not trigger any
  migration / cutover / rollback / retention-cleanup; the desktop's
  guarded wrappers remain the only legitimate execution path for
  those operations.

Phase 10.21H ships the runbook only — it does not execute the
pilot. Execution requires an operator to follow the canonical
runbook on a controlled environment and produce the evidence
bundle described in §10. A signed-off PROCEED decision is the
mandatory prerequisite for any future Phase 10.21I controlled-
rollout phase.

## Phase 10.21I — DB-Authoritative Controlled Rollout Runbook (documentation)

A controlled-rollout runbook for the Phase 10.21C/E/F
DB-authoritative permission stack is now published. The canonical
doc lives in the backend repository; a desktop-side pointer doc
covers the desktop-specific notes:

→ desktop pointer:
[`operator-permission-db-authoritative-controlled-rollout-runbook.md`](./operator-permission-db-authoritative-controlled-rollout-runbook.md)
(canonical at
[`Ham-Pos/docs/operator-permission-db-authoritative-controlled-rollout-runbook.md`](../../Ham-Pos/docs/operator-permission-db-authoritative-controlled-rollout-runbook.md)).

Desktop-side facts the rollout captures:

- **Desktop status card can support rollout evidence.** The
  Phase 10.21G `/authoritative-status` desktop card is flipped ON
  for the duration of waves that require observation (Waves 0+
  per §6 of the canonical runbook). The card's screenshot +
  rendered values feed into `07-desktop-verification.md` for each
  wave. The card has NO mutation, NO execute, NO confirmation-
  phrase, NO upload control — strictly observation.
- **Dangerous buttons remain unchanged** throughout every wave.
  Real Migration / Cutover / Rollback / Retention Cleanup keep
  the same `CanExecute` state before, during, and after each
  wave. Step 11 of the per-wave procedure (§8) is the explicit
  dangerous-button check; stop condition #11 watches for any
  drift.
- **No dangerous button is clicked during any wave.** The
  rollout is a permission-decision rollout, not an execution
  rollout. The desktop's guarded wrappers remain the only
  legitimate execution path.
- **No confirmation phrase leaves the desktop.** No backend field,
  no audit metadata, no rollout artifact accepts or echoes a
  phrase value. Stop condition #10 watches for any phrase
  substring in UI / log / evidence / network capture.
- **POS sales smoke (step 12 of the per-wave procedure).** Each
  wave ends with a cashier login + one sale to prove no
  regression slipped in. Stop condition #12 watches for any POS
  breakage.
- **The dangerous-authoritative flag flip discipline** is strictly
  windowed (Waves 4-5 only, ≤ 1 hour windows, ≥ 24 hours between
  windows per operator). The desktop status card lets operators
  observe the current backend flag state without exposing any
  control to flip it.

Phase 10.21I ships the runbook only — it does not execute the
rollout. Execution requires an operator to follow the canonical
runbook wave-by-wave and produce the consolidated evidence
folder. A signed-off §19 final-decision is the canonical output;
the steady-state flag policy it records becomes the new
production baseline.

## Phase 10.22A — Evidence Bundle Storage / Upload Design Plan (documentation)

The next workstream — a backend-managed evidence-bundle storage +
upload system that supports every Phase 10.19 → 10.21 pilot /
rollout evidence flow — has a published design plan:

→ canonical at
[`Ham-Pos/docs/operator-evidence-bundle-storage-upload-plan.md`](../../Ham-Pos/docs/operator-evidence-bundle-storage-upload-plan.md)

Desktop-side facts the plan captures:

- **Current desktop evidence flow is unchanged.** Phase 10.22A
  does not modify the desktop. The Phase 10.18A
  `ProductionPilotEvidenceBundleService` still produces the
  local bundle on disk; operators still copy folders off-machine
  manually; the Phase 10.21G status card stays read-only.
- **Future desktop evidence bundle upload UI belongs to
  Phase 10.22+.** When Phase 10.22E ships, a new desktop action
  ("Upload pilot evidence") on the Migration Operations
  dashboard lets the operator select an approved evidence
  folder, generate the canonical manifest + per-file SHA-256s,
  zip the bundle, run a client-side redaction scan, and (in
  Phase 10.22F) upload it to the backend with one operator
  action.
- **The new desktop flow is read-only relative to dangerous
  execution.** The Upload UI does not trigger any dangerous
  button, does not relax any `CanExecute` gate, and does not
  short-circuit local feature flags / confirmation phrases /
  guarded wrappers / dangerous-operation lock. Confirmation
  phrases never enter the bundle; the desktop redaction scan
  blocks them client-side before zipping, and the backend
  re-scans on receive.
- **Current status card remains read-only.** The Phase 10.21G
  card behaviour stays exactly as today. Future Phase 10.22G
  may add a reviewer-side companion card for browsing /
  downloading finalised bundles, gated by its own default-OFF
  desktop flag.
- **Old desktop compatibility.** Old desktops without the
  upload UI continue to work unchanged. The Phase 10.22A
  backend endpoints are additive; the existing Phase 10.19F
  `POST /api/v1/operator/evidence/register` metadata-only flow
  continues to operate exactly as today.

---

## Phase 10.22E — Local ZIP + Manifest Export UI (shipped)

The first piece of the Phase 10.22+ desktop evidence flow has shipped.
It is **local-only** — no backend HTTP call, no upload, no finalize.
A new card in `MigrationOperationsWindow.xaml` titled "Operator
Evidence Bundle Export — Local ZIP Only (Phase 10.22E)" lets the
operator:

1. select a prepared evidence folder,
2. validate it against the desktop mirror of the Phase 10.22D
   backend validators (path safety, MIME / magic, redaction scan),
3. generate a canonical `manifest.json` matching the backend
   `operator-evidence-bundle-v1` schema, and
4. write a sanitized ZIP via temp-then-rename plus the file's
   SHA-256.

**Local flag** (default OFF; missing / `""` / `"0"` / `"false"`
→ disabled card, all buttons `CanExecute=false`, no scan, no ZIP,
no HTTP):

```text
operator_evidence_bundle_export_ui_enabled
```

**What the card does NOT do** (deferred to later sub-phases):

- No `Upload`, `Finalize`, `Review`, `Download`, or `Delete`
  button — Phase 10.22F-G.
- No backend call. The desktop touches the
  `EvidenceBundleExportService` and its four collaborators only;
  it does NOT inject `OperatorEvidenceBundleApiClient` (which
  does not exist yet). The `NetworkLogService` shows zero
  `/api/v1/operator/evidence/bundles*` traffic.
- No execution of any dangerous operation. The card has no
  confirmation-phrase input and no `Execute` / `Approve` button.
- No raw match value, no token, no password, no confirmation
  phrase, no absolute filesystem path, no machine name in any
  UI string, log line, manifest, or ZIP entry. The
  redaction-finding preview is always the literal `[REDACTED]`.
  The structural confirmation-phrase scanner does **not** name
  the six known guarded-flow literals in the desktop source.

The pipeline mirrors backend Phase 10.22D byte-for-byte on the
algorithm shapes (same forbidden-pattern list, same path-safety
rules, same magic / extension checks, same manifest schema) so a
desktop-accepted bundle is guaranteed to pass the backend's strict
validators when Phase 10.22F wires the upload UI.

Full reference + 18-step manual verification checklist:
[`evidence-bundle-local-export-ui.md`](./evidence-bundle-local-export-ui.md).

---

## Phase 10.22F — Backend Upload + Finalize UI (shipped)

The second piece of the Phase 10.22+ desktop evidence flow has
shipped. A new "Operator Evidence Bundle Upload — Backend Upload +
Finalize (Phase 10.22F)" card sits immediately after the Phase 10.22E
local-export card and lets the operator:

1. select a folder produced by Phase 10.22E (or hand-carried from
   another workstation as long as it contains a valid
   `operator-evidence-bundle-v1` `manifest.json`),
2. re-run the strict local validator (mirrors backend Phase 10.22D),
3. POST the manifest + each `manifest.files[]` entry (multipart,
   manifest.json first, then files in stable ordinal order with
   `declaredSha256` from the manifest),
4. finalize the bundle on the backend, and
5. refresh backend metadata to confirm `FINALIZED` + bundle SHA-256.

**Local flag** (default OFF; missing / `""` / `"0"` / `"false"`
→ disabled card, all buttons `CanExecute=false`, no backend HTTP):

```text
operator_evidence_bundle_upload_ui_enabled
```

This flag is **independent** of the Phase 10.22E export flag. The
backend's own `operator.evidence.bundle.api.enabled` flag is the
ultimate gate — when OFF the desktop receives HTTP 503
`FEATURE_FLAG_OFF` and surfaces it verbatim.

**The Phase 10.22E local `.zip` is NEVER uploaded.** Phase 10.22D
blocks `.zip` at the backend; the desktop refuses any
`manifest.files[]` entry whose path ends in `.zip` at the
local-validation gate, before any HTTP socket opens. The card shows
the local ZIP path read-only with a `Local archive only — not
uploaded` label.

**What the card does NOT do** (deferred to later sub-phases):

- No `Review`, `Download`, `Delete`, `Retention`, `Execute`,
  `Approve` button — Phase 10.22G-H.
- No confirmation-phrase input. The redaction scanner in Phase 10.22E
  (desktop) and Phase 10.22D (backend) protect against
  confirmation-phrase leakage; the upload pipeline carries none.
- No dangerous-operation execution. The upload card only consumes
  the bundle-metadata endpoints; the four guarded executors and the
  dangerous-operation lock are unchanged.
- No `.zip` upload (see above).
- No background uploads — the workflow runs on the WPF dispatcher
  with a real `IProgress<string>` reporter and respects
  `CancellationToken`.

Typed backend errors surface verbatim (`FEATURE_FLAG_OFF`,
`FORBIDDEN`, `REDACTION_FAILED`, `MIME_MISMATCH`, `MANIFEST_INVALID`,
`DUPLICATE_FILE`, `INVALID_STATUS`, `STORAGE_OBJECT_MISSING`,
`NETWORK_FAILURE`). The card scrubs every backend-supplied message
through `OperatorPermissionAdminRedaction.ScrubAndTruncate` before
display.

Full reference + 20-step manual verification checklist:
[`evidence-bundle-backend-upload-ui.md`](./evidence-bundle-backend-upload-ui.md).

---

## Phase 10.22G — Reviewer + Download UI (shipped)

The third piece of the Phase 10.22+ desktop evidence flow has
shipped. A new "Operator Evidence Bundle Reviewer + Download
(Phase 10.22G)" card sits immediately after the Phase 10.22F upload
card and lets the operator (with the `operator.evidence.bundle.review`
and/or `operator.evidence.bundle.download` permissions):

1. **List + filter** bundles by `evidenceType`, `phase`, `tenantId`,
   `status` (combo of `''` / `FINALIZED` / `REVIEWED` / `REJECTED` /
   `NEEDS_CHANGES` / `ARCHIVED` / `QUARANTINED`), `page`, `size`.
2. **Select** a bundle UUID and **load metadata** (file manifest,
   sizes, hashes, createdBy, finalizedAt, reviewedAt).
3. **Submit a review decision** (`APPROVED` / `REJECTED` /
   `NEEDS_CHANGES`) with optional scrubbed notes. Self-review is
   refused by the backend with `SELF_REVIEW_FORBIDDEN`.
4. **Choose a download folder** + click `Download Bundle (ZIP)` to
   stream the backend's sanitized ZIP into a sibling temp file and
   atomically rename it on success. Computes the local SHA-256
   inline; surfaces filename + size + SHA next to the displayed
   (truncated) path.

**Local flag** (default OFF; missing / `""` / `"0"` / `"false"` →
disabled card, all buttons `CanExecute=false`, no backend HTTP):

```text
operator_evidence_bundle_review_ui_enabled
```

This flag is **independent** of the Phase 10.22E / 10.22F flags.
The backend's own `operator.evidence.bundle.api.enabled` flag is the
upstream gate.

**What the card does NOT do** (deferred / out-of-scope):

- No `Upload`, `Finalize`, `Delete`, `Retention`, `Hard-Delete`
  button — Phase 10.22F handles upload; Phase 10.22H+ will handle
  retention; hard-delete is gated on FUTURE_ONLY
  `operator.evidence.bundle.delete.admin`.
- No `Execute`, `Approve` (dangerous-execute) button.
- No confirmation-phrase input. The desktop never sends a phrase.
- No raw SQL input.
- No raw file-browsing endpoint exposure — only the whole ZIP is
  downloadable, and the local file is never auto-unzipped.
- No background uploads / downloads. Every workflow runs on the WPF
  dispatcher with a real `CancellationToken`.

Typed backend errors flow back verbatim (`FEATURE_FLAG_OFF`,
`FORBIDDEN`, `INVALID_STATUS`, `SELF_REVIEW_FORBIDDEN`,
`DOWNLOAD_NOT_AVAILABLE`, `STORAGE_OBJECT_MISSING`, …) and pass
through `OperatorPermissionAdminRedaction.ScrubAndTruncate` before
display.

Full reference + 20-step manual verification checklist:
[`evidence-bundle-review-download-ui.md`](./evidence-bundle-review-download-ui.md).

---

## Phase 10.22H — Retention + Legal Hold UI (shipped)

The fourth piece of the Phase 10.22+ desktop evidence flow has
shipped. A new "Operator Evidence Bundle Retention + Legal Hold
(Phase 10.22H)" card sits immediately after the Phase 10.22G
reviewer card and lets the operator (with the
`operator.evidence.bundle.retention.admin` permission):

1. **Filter + list** retention candidates by `evidenceType`,
   `status`, `tenantId`, `before` (ISO UTC), `page`, `size`. The
   backend pre-excludes legal-hold rows and DRAFT / EXPIRED /
   QUARANTINED statuses.
2. **Select** a bundle UUID and **load metadata** (status,
   retentionUntil, legalHold, reviewedAt, reviewedBy, retention
   class).
3. **Update `retentionUntil`** with reason + ticketId (both
   scrubbed server-side). Refused when the new value would shorten
   retention on a held bundle.
4. **Toggle `legalHold`** with reason + ticketId for both
   directions (ON and OFF).
5. **Archive** the bundle. Refused when held; refused for DRAFT.
   Storage objects are NOT deleted; archived bundles remain
   downloadable via the 10.22G card.
6. **Expire** the bundle. Refused when held; refused for DRAFT.
   ADMIN may only expire after the retention horizon has elapsed;
   GLOBAL_ADMIN may early-expire. Storage objects are NOT deleted.

**Local flag** (default OFF; missing / `""` / `"0"` → disabled
card, every input `IsEnabled=false`, every command
`CanExecute=false`, no backend HTTP):

```text
operator_evidence_bundle_retention_ui_enabled
```

This flag is **independent** of the Phase 10.22E / 10.22F / 10.22G
flags. The backend's own `operator.evidence.bundle.api.enabled`
flag is the upstream gate; the optional
`operator.evidence.bundle.retention.sweeper.enabled` backend flag
is independent of this UI.

**What the card does NOT do** (deferred / out-of-scope):

- No `Upload`, `Finalize`, `Download`, `Delete`, `Hard-Delete`
  button. (Upload lives on the 10.22F card; download lives on the
  10.22G card. Hard delete is gated on FUTURE_ONLY
  `operator.evidence.bundle.delete.admin` and is not implemented.)
- No `Execute`, `Approve` (dangerous-execute) button.
- No confirmation-phrase input. The card has no field that captures
  one of the six known guarded-flow literals.
- No raw SQL input. No storage-path / bucket / provider display.
- No physical blob deletion at any code path — archive / expire
  only mutate DB metadata.

Typed backend errors flow back verbatim (`FEATURE_FLAG_OFF`,
`FORBIDDEN`, `LEGAL_HOLD_ACTIVE`, `RETENTION_INVALID`,
`RETENTION_TICKET_REQUIRED`, `ARCHIVE_NOT_ALLOWED`,
`EXPIRE_NOT_ALLOWED`, …) and pass through
`OperatorPermissionAdminRedaction.ScrubAndTruncate` before display.

Full reference + 20-step manual verification checklist:
[`evidence-bundle-retention-legal-hold-ui.md`](./evidence-bundle-retention-legal-hold-ui.md).

---

## Phase 10.22I — Docs-Only Pilot (shipped 2026-05-30)

**Phase 10.22I is documentation-only.** It pilots the complete Phase 10.22E–H desktop
evidence bundle stack end-to-end. No new cards, code, flags, service registrations, or
XAML changes are added.

The pilot runbook covers all four desktop evidence cards (export 10.22E, upload/finalize
10.22F, review/download 10.22G, retention/legal-hold 10.22H), flag enablement sequence,
separation of duties, negative tests, audit verification, and rollback procedure.

Full pilot runbook:
[`../../../Ham-Pos/docs/operator-evidence-bundle-pilot-runbook.md`](../../../Ham-Pos/docs/operator-evidence-bundle-pilot-runbook.md)

---

## Phase 10.22J — Docs-Only Controlled Rollout Runbook (shipped 2026-05-30)

**Phase 10.22J is documentation-only.** It provides the wave-gated production rollout
runbook for the Phase 10.22B–H evidence bundle stack, covering all four desktop UI cards
(Phase 10.22E–H). No code, flags, XAML, C# files, migrations, endpoints, or storage
changes are added.

The controlled rollout runbook defines the enabling sequence (export → upload → review →
retention), wave sign-off criteria, preflight checks, and rollback procedure for
production flag enablement on each operator workstation.

Full controlled rollout runbook:
[`../../../Ham-Pos/docs/operator-evidence-bundle-controlled-rollout-runbook.md`](../../../Ham-Pos/docs/operator-evidence-bundle-controlled-rollout-runbook.md)

---

## Phase 10.22L — Hard Delete Backend (2026-05-31)

The backend `POST /api/v1/operator/evidence/bundles/{uuid}/hard-delete` endpoint is implemented and tested (Phase 10.22L). Desktop UI for hard delete is **deferred** — no WPF screen, no `RelayCommand`, no feature flag, no observable properties are added to any existing view model or window in this phase.

When the desktop phase ships, it will add a hard-delete workflow accessible only from the evidence-bundle detail view when `status ∈ {EXPIRED, ARCHIVED, REJECTED}` and no legal hold is active. It will require both feature flags to be enabled and the `operator.evidence.bundle.delete.admin` permission to be explicitly granted (currently FUTURE_ONLY and ungranted by default).

Backend hard-delete reference: [`operator-evidence-bundle-hard-delete.md`](../../Ham-Pos/docs/operator-evidence-bundle-hard-delete.md)

---

## Phase 10.22P — Lifecycle Scheduler Status UI (2026-06-02)

Added a read-only WPF monitoring card to `MigrationOperationsWindow` that displays evidence bundle lifecycle scheduler run history from the Phase 10.22N retention archive sweeper and Phase 10.22O expiration sweeper.

**Feature flags (both default OFF):**

| Flag | Effect |
|---|---|
| `operator_evidence_bundle_lifecycle_scheduler_status_ui_enabled` | Enables card; HTTP calls made on Load/Refresh. |
| `operator_evidence_bundle_lifecycle_scheduler_manual_run_ui_enabled` | Shows manual-run inputs and Submit buttons. |

**New desktop files:**
- `Core/DTOs/EvidenceBundleSchedulerRunDtos.cs` — 6 DTOs matching backend record shapes
- `Services/EvidenceBundleLifecycleScheduler/EvidenceBundleLifecycleSchedulerStatusService.cs`
- 6 new `ApiClient.cs` methods + 6 `OperatorEvidenceBundleApiClient` wrapper methods

**Safety:** no hard-delete, no storage object deletion, no dangerous operation, no confirmation phrase, no storage key/bucket/endpoint displayed. All strings scrubbed via `ScrubForDisplay` before binding.

Full specification: [`evidence-bundle-lifecycle-scheduler-status-ui.md`](evidence-bundle-lifecycle-scheduler-status-ui.md)
