# POS Operator RBAC and Permission Model Plan

> Documentation-only architecture audit of the current local-flag + local-role
> guard model used by the Migration Operations dashboard, and a phased plan
> for evolving to a backend-supported permission model.
>
> This document **does not** change current authorization behaviour. It does
> **not** remove or weaken any existing guard. It defines a target state and
> a phased migration path.
>
> Companion runbooks:
> - [`operator-tenant-db-migration-runbook.md`](operator-tenant-db-migration-runbook.md) — migration / cutover / rollback lifecycle.
> - [`operator-retention-cleanup-runbook.md`](operator-retention-cleanup-runbook.md) — storage retention cleanup.
> - [`operator-controlled-production-pilot-runbook.md`](operator-controlled-production-pilot-runbook.md) — controlled pilot rollout.
> - [`operator-pilot-signoff-rollout-decision-runbook.md`](operator-pilot-signoff-rollout-decision-runbook.md) — post-pilot sign-off and decision.
> - [`tenant-db-rollback.md`](tenant-db-rollback.md) — rollback deep-dive.
> - [`BACKEND_API_REQUIREMENTS.md`](BACKEND_API_REQUIREMENTS.md) — current backend API contract.

---

## 1. Scope

This document is a **plan only**. It documents:

- The current access model — local `global_settings.json` feature flags plus
  the local `OperatorAccessService` role allow-list — exactly as Phase 10.9B
  / 10.12C / 10.13B / 10.14B / 10.15A / 10.16B / 10.17A / 10.17B shipped it.
- The known risks and limitations of that model.
- A target backend-supported permission model.
- A phased migration plan from "local flags + local roles" to "backend
  permission claims + local flags + wrapper guards + confirmation phrase +
  evidence bundle + dangerous-operation lock" — without ever removing the
  existing local guards.

The scope is the WPF desktop POS client and the operator-facing surfaces
introduced across the Phase 10.10 → Phase 10.18 series:

- Migration Operations dashboard read-only sections (diagnostics, dry-run
  previews, inventory, retention preview, gate check, readiness, evidence).
- Migration Operations dashboard read-only exports (diagnostics JSON,
  preflight, inventory, pilot readiness, pilot evidence bundle).
- Guarded destructive operations: real migration, runtime cutover,
  rollback, retention cleanup.

This document does **not** replace the guarded wrapper services
(`GuardedRealMigrationExecutorService`, `GuardedRuntimeCutoverExecutorService`,
`GuardedRollbackExecutorService`, `GuardedRetentionCleanupExecutorService`).
The wrappers remain the only legitimate execution path for their respective
destructive operations, regardless of any future permission model.

This document does **not** expose confirmation phrase values. Phrases live
in the internal secure runbook.

## 2. Current Access Model

The current model is a defence-in-depth stack:

1. **Local feature flags** in `%LocalAppData%\PosSystem\global_settings.json`:
   - `operator_migration_dashboard_enabled` — gates opening the dashboard.
   - `operator_diagnostics_ui_enabled` — gates opening the Operator Diagnostics window.
   - `operator_real_migration_ui_enabled` — gates the Execute Real Migration section.
   - `operator_runtime_cutover_ui_enabled` — gates the Execute Runtime Cutover section.
   - `operator_rollback_ui_enabled` — gates the Execute Rollback section.
   - `operator_retention_cleanup_ui_enabled` — gates the Execute Retention Cleanup section.
   - `operator_access_allow_missing_role` — explicit per-machine override that allows operator UIs when no role is recorded on the session.
   - `shared_to_tenant_migration_enabled` — server-side gate the real migrator additionally enforces.
   - `tenant_db_runtime_enabled` — runtime tenant DB switch flag. **Never** set manually; only the guarded runtime cutover wrapper writes it. Only the guarded rollback wrapper clears it.
2. **Local `OperatorAccessService`** role check (`IsAuthorizedByRole`) against an allow-list `{ ADMIN, GLOBAL_ADMIN, SUPER_ADMIN, SUPPORT, OWNER }`. CASHIER is explicitly excluded.
3. **Guarded wrapper services** that enforce additional guards before invoking the underlying executors: `Force=true`, exact confirmation phrase, external-backup acknowledgement, recent preflight + inventory exports, diagnostics (pending/poison sales = 0), readiness gate verdict, runtime/provider state, migration marker, verifier, retention preview.
4. **Dangerous-operation global lock** (Phase 10.15A) that prevents concurrent execution and double-clicks of the four destructive operations.
5. **Audit evidence**: every wrapper writes a redacted JSON audit log under `%LocalAppData%\PosSystem\logs\…`; the Pilot Evidence Bundle (Phase 10.17B) aggregates a sanitized JSON snapshot for support tickets.

Read-only sections (Refresh, exports, previews, gate check, readiness, evidence bundle) require only the flag + role gate. Destructive sections require the full stack.

## 3. Current Protected Operations

| Operation | Current UI section | Current flag | Current wrapper/service | Current risk level |
|---|---|---|---|---|
| Open Migration Operations dashboard | Window itself | `operator_migration_dashboard_enabled` | `OperatorAccessService.CanOpenMigrationOperations` | Low (read-only entry point) |
| Export Diagnostics JSON | "Refresh / Export Diagnostics JSON" action bar | dashboard flag | `OperatorDiagnosticsExportService` | Low (read-only file write under `logs\diagnostics\`) |
| Preview Migration Dry-Run | "Migration Dry-Run Preview" card | dashboard flag | `MigrationDryRunPreviewService` (hardcoded `DryRunOnly=true`) | Low (read-only, side-effect guard) |
| Preview Rollback Dry-Run | "Rollback Dry-Run Preview" card | dashboard flag | `RollbackDryRunPreviewService` (hardcoded `DryRunOnly=true`) | Low (read-only, side-effect guard) |
| Export Preflight Report | "Preflight Export" card | dashboard flag | `MigrationOperationsPreflightExportService` | Low (read-only redacted JSON) |
| Refresh Inventory | "Tenant DB Inventory" card | dashboard flag | `TenantDatabaseInventoryService` | Low (read-only filesystem metadata) |
| Preview Retention Plan | "Retention Preview" card | dashboard flag | `TenantDatabaseRetentionPreviewService` (preview-only) | Low (read-only classification) |
| Export Inventory Report | "Inventory Export" card | dashboard flag | `TenantDatabaseInventoryExportService` | Low (read-only redacted JSON) |
| Check Real Migration Gate | "Real Migration Gate" card | dashboard flag | `RealMigrationExecutionGateService` | Low (read-only verdict) |
| Execute Real Migration | "Guarded Real Migration Execution" card | `operator_real_migration_ui_enabled` + dashboard flag | `GuardedRealMigrationExecutorService` (the ONLY real-run path) | **Destructive — High** |
| Execute Runtime Cutover | "Guarded Runtime Cutover Execution" card | `operator_runtime_cutover_ui_enabled` + dashboard flag | `GuardedRuntimeCutoverExecutorService` (writes `tenant_db_runtime_enabled="1"`) | **Destructive — High** |
| Execute Rollback | "Guarded Rollback Execution" card | `operator_rollback_ui_enabled` + dashboard flag | `GuardedRollbackExecutorService` (wraps `TenantDbRollbackExecutor`) | **Destructive — High** |
| Execute Retention Cleanup | "Guarded Retention Cleanup Execution" card | `operator_retention_cleanup_ui_enabled` + dashboard flag | `GuardedRetentionCleanupExecutorService` (deletes only preview candidates) | **Destructive — Medium** |
| Generate Pilot Readiness Report | "Production Pilot Readiness" card | dashboard flag | `ProductionPilotReadinessReportService` | Low (read-only redacted JSON) |
| Export Pilot Evidence Bundle | "Production Pilot Evidence Bundle" card | dashboard flag | `ProductionPilotEvidenceBundleService` | Low (read-only sanitized JSON folder + optional ZIP) |

## 4. Current Risks and Limitations

The current model is a **safe local guard** for the limited blast radius of one POS machine. As the tenant DB rollout expands to more stores, the following limitations should be addressed:

- **Local flags can be edited on the machine.** A user with filesystem
  access to `%LocalAppData%\PosSystem\global_settings.json` can flip a
  flag. The wrapper guards still apply, but the flag is no longer a
  meaningful authorization layer in that scenario.
- **Local role can be stale.** `user_role` is captured at login from the
  backend's response and persisted to local Settings. If the role is
  revoked server-side between sessions, the desktop continues with the
  stale role until the next login.
- **Backend has no automatic visibility** into the fact that a particular
  operator performed a destructive action on a particular machine for a
  particular tenant. Audit logs are local only unless they are exported
  and attached to a support ticket.
- **Offline mode complicates permission refresh.** The POS is offline-
  first by design; the desktop cannot consult the backend for every
  destructive action.
- **Support/operator distinction may need finer-grained permission keys.**
  Today every member of the role allow-list can in principle attempt any
  destructive operation given the right flags + phrase. Real production
  use will want narrower scoping (e.g. SUPPORT may cutover but not
  cleanup; OWNER may approve rollback but not execute migration).
- **Missing-role fallback must be treated carefully.**
  `operator_access_allow_missing_role="1"` is an explicit override; an
  enterprise rollout may want to remove that escape hatch entirely.
- **The local confirmation phrase is not a substitute for authorization.**
  It is a deliberate-action gate, not an authorization layer.
- **Audit logs are local-only.** Until the backend collects audit events,
  multi-operator accountability depends on humans attaching evidence
  bundles to tickets.
- **Multi-operator accountability** is limited. The local audit captures
  the OS user and machine, but not a server-verified operator identity.

The current model is **safe** for guarded operations on a small pilot.
It should **evolve** into a backend-supported model before broader rollout.

## 5. Target Permission Model

The target is a **layered** access model. Backend permission claims are
**added on top of** the existing layers, not in place of them:

1. **Backend-issued role / permission claims.** The desktop fetches these
   at login and on explicit refresh. The backend is authoritative for the
   operator's role and tenant/store scope.
2. **Local feature flag window.** Existing flags remain. They continue to
   provide a per-machine "destructive UI is actionable for this operation
   window" signal. Backend permission alone is **not** enough to make the
   UI actionable.
3. **Local guarded wrapper.** All existing wrapper guards remain. Backend
   permission is checked **in addition to** wrapper guards, not instead of.
4. **Confirmation phrase.** Remains operator-typed at execution time. Never
   substitutable by backend permission. Never logged raw.
5. **Backup / export / readiness gates.** Remain unchanged. The wrappers
   continue to require external-backup acknowledgement, recent preflight +
   inventory exports, diagnostics clean, readiness gate verdict.
6. **Dangerous-operation lock.** Remains. The four destructive operations
   continue to be mutually exclusive within a single desktop session.
7. **Audit / evidence bundle.** Each destructive operation continues to
   write its own redacted audit log; the evidence bundle continues to
   aggregate sanitized JSON for support tickets. Audit gains a permission
   claim snapshot once Phase RBAC-6 ships.

Backend permission **never** replaces wrapper guards. It is an additional
gate that fails closed when the backend rejects or cannot be reached for a
destructive operation.

## 6. Proposed Permission Keys

| Permission key | Description | Suggested roles | Dangerous? | Requires local flag? |
|---|---|---|---|---|
| `operator.dashboard.open` | Open the Migration Operations dashboard window. | ADMIN, GLOBAL_ADMIN, SUPER_ADMIN, SUPPORT, OWNER | No | Yes (`operator_migration_dashboard_enabled`) |
| `operator.diagnostics.export` | Export Diagnostics JSON. | Same as above | No | Yes (dashboard flag) |
| `operator.migration.preview` | Run Preview Migration Dry-Run. | Same as above | No | Yes (dashboard flag) |
| `operator.rollback.preview` | Run Preview Rollback Dry-Run. | Same as above | No | Yes (dashboard flag) |
| `operator.preflight.export` | Run Export Preflight Report. | Same as above | No | Yes (dashboard flag) |
| `operator.inventory.view` | Run Refresh Inventory. | Same as above | No | Yes (dashboard flag) |
| `operator.inventory.export` | Run Export Inventory Report. | Same as above | No | Yes (dashboard flag) |
| `operator.retention.preview` | Run Preview Retention Plan. | Same as above | No | Yes (dashboard flag) |
| `operator.migration.gate.check` | Run Check Real Migration Gate. | Same as above | No | Yes (dashboard flag) |
| `operator.migration.execute` | Execute Real Migration through the guarded wrapper. | ADMIN, GLOBAL_ADMIN, SUPER_ADMIN, SUPPORT, OWNER (per-tenant) | Yes | Yes (`operator_real_migration_ui_enabled`) |
| `operator.cutover.execute` | Execute Runtime Cutover through the guarded wrapper. | ADMIN, GLOBAL_ADMIN, SUPER_ADMIN, SUPPORT, OWNER (per-tenant) | Yes | Yes (`operator_runtime_cutover_ui_enabled`) |
| `operator.rollback.execute` | Execute Rollback through the guarded wrapper. | ADMIN, GLOBAL_ADMIN, SUPER_ADMIN, SUPPORT, OWNER (per-tenant; joint approval required) | Yes | Yes (`operator_rollback_ui_enabled`) |
| `operator.retention.cleanup.execute` | Execute Retention Cleanup through the guarded wrapper. | ADMIN, GLOBAL_ADMIN, SUPER_ADMIN, SUPPORT, OWNER | Yes | Yes (`operator_retention_cleanup_ui_enabled`) |
| `operator.pilot.readiness.generate` | Generate Pilot Readiness Report. | Same as dashboard | No | Yes (dashboard flag) |
| `operator.pilot.evidence.export` | Export Pilot Evidence Bundle. | Same as dashboard | No | Yes (dashboard flag) |
| `operator.flags.view` | Inspect operator-facing flag values (future UI). | ADMIN, GLOBAL_ADMIN, SUPER_ADMIN, SUPPORT | No | Yes (dashboard flag) |
| `operator.flags.change` | **Future only.** Modify operator-facing flag values through a backend-mediated control plane. Dangerous; should be restricted to GLOBAL_ADMIN / SUPER_ADMIN with audit. | GLOBAL_ADMIN, SUPER_ADMIN | Yes | Yes + server-side audit |

Every dangerous permission carries the same wrapper-guard / phrase /
backup-ack / readiness requirements as today. Permission key is an
authorization layer, not a substitute for those guards.

## 7. Role-to-Permission Matrix

Values: **Yes** (permitted), **No** (denied), **Conditional** (permitted only with additional approval / scoping), **Future** (proposed for a future role not yet recorded by the backend).

| Role | dashboard.open | diagnostics.export | preview / inventory / retention | migration.gate.check | migration.execute | cutover.execute | rollback.execute | retention.cleanup.execute | pilot.readiness | pilot.evidence | flags.view | flags.change |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **CASHIER** | No | No | No | No | No | No | No | No | No | No | No | No |
| **MANAGER** (proposed) | Conditional | Conditional | Conditional | No | No | No | No | No | Conditional | Conditional | No | No |
| **ADMIN** | Yes | Yes | Yes | Yes | Conditional (per-tenant + Support approval) | Conditional | Conditional | Conditional | Yes | Yes | Yes | No |
| **SUPPORT** | Yes | Yes | Yes | Yes | Conditional (per-tenant + business approval) | Conditional | Conditional (joint approval) | Conditional | Yes | Yes | Yes | No |
| **OWNER** | Yes | Yes | Yes | Yes | Conditional (per-tenant) | Conditional | Conditional (joint approval) | Conditional | Yes | Yes | Yes | No |
| **GLOBAL_ADMIN** | Yes | Yes | Yes | Yes | Conditional (per-tenant + Support approval) | Conditional | Conditional | Conditional | Yes | Yes | Yes | Future |
| **SUPER_ADMIN** | Yes | Yes | Yes | Yes | Conditional (per-tenant + Support approval) | Conditional | Conditional | Conditional | Yes | Yes | Yes | Future |

Notes on the matrix:

- **CASHIER is denied across the board.** This matches the current
  `OperatorAccessService.IsAuthorizedByRole` behaviour and must remain in
  every future iteration. CASHIER is a sales role, not an operator role.
- **MANAGER** is proposed as a future role that can view diagnostics and
  read-only evidence for the store they manage — but cannot execute any
  destructive action and cannot run the migration gate check. This role
  does not yet exist in the backend's role taxonomy.
- **ADMIN / OWNER / GLOBAL_ADMIN / SUPER_ADMIN** can perform most actions,
  but every destructive action is **Conditional** — it requires the local
  flag, the confirmation phrase, the wrapper guards, the dangerous
  operation lock, and (for some actions) joint approval from Support and
  Store Manager per the pilot runbooks.
- **SUPPORT** is scoped: per-tenant where applicable. A SUPPORT user must
  not execute destructive operations against a tenant that does not
  match the support ticket scope. This is enforced server-side once
  Phase RBAC-2 ships.
- **`flags.change`** is **Future** for GLOBAL_ADMIN / SUPER_ADMIN. It is
  not part of the current model — operator flags are flipped today by
  direct file edit. Any future UI for flag changes must itself be
  guarded.

## 8. Backend API Requirements

The backend (Ham-Pos) currently issues role information at login (see
[`BACKEND_API_REQUIREMENTS.md`](BACKEND_API_REQUIREMENTS.md)). The
following future API needs apply:

- **`GET /api/v1/operator/permissions`** — fetch the current operator's
  permission claims. Returns the permission key list, the operator's
  tenant/store scope, the role, the issued-at timestamp, and the expiry.
- **`GET /api/v1/operator/identity`** — fetch the operator's role and
  tenant/store scope (a strict subset of the permissions endpoint
  intended for low-latency UI display).
- **`POST /api/v1/operator/permissions/validate`** — server-side
  pre-execution permission check for a specific dangerous operation key.
  Used by the desktop just before invoking the guarded wrapper as
  defence in depth.
- **`POST /api/v1/operator/intent`** (optional) — record an operator's
  intent to execute a destructive operation, with a support/approval
  ticket reference. Server-side approval log entry.
- **`POST /api/v1/operator/evidence`** (optional) — upload an evidence
  bundle ZIP. Server stores under a per-tenant retention policy.
- **`POST /api/v1/operator/audit`** (optional) — accept an audit summary
  payload. Server records server-side audit events independent of the
  desktop's local audit logs.
- **Server-side audit events:** dashboard opened, readiness generated,
  evidence exported, migration executed, cutover executed, rollback
  executed, retention cleanup executed. Each event records the operator
  identity, role, permission snapshot, tenant/store, machine, outcome,
  and timestamps.
- **Tenant / store scoping** is mandatory for destructive permissions: a
  SUPPORT user authorized for tenant A must not be able to execute a
  destructive operation against tenant B, even if the desktop is
  pointing at tenant B's data. This is enforced by the server-side
  validation endpoint.

**Phase 10.19A does not implement any of these APIs.** They are the
future deliverables of Phase RBAC-2 (backend permission schema/API).

## 9. Desktop Client Requirements

The future desktop behaviour, once the APIs above ship:

- **On login**, fetch permission claims via `GET /api/v1/operator/permissions`.
  Cache them locally with an expiry timestamp.
- **On explicit refresh** (e.g. operator clicks a Refresh Permissions
  button, or the dashboard's existing Refresh action), re-fetch claims.
- **Show permission status** in the Operator Diagnostics card and the
  Pilot Readiness Report. Operators should be able to see which
  permissions they have without trial-and-error.
- **Disable UI sections** whose required permission is missing, in
  addition to disabling for missing local flag / role. The UI gate is
  the AND of all four (flag + role + permission + per-section state).
- **Re-check permission immediately before** the guarded wrapper call
  for every destructive operation. Use `POST /api/v1/operator/permissions/validate`
  with the specific permission key and tenant subdomain. Fail closed
  if the server rejects or is unreachable for destructive operations.
- **Include the permission snapshot** in the local audit log (the
  current redacted JSON shape gains a sanitized permission section).
- **Include the permission snapshot** in the Pilot Evidence Bundle's
  `diagnostics-summary.json` or in a new sanitized JSON file
  `permission-snapshot.json`.
- **Handle permission fetch failure safely:** for read-only sections
  (diagnostics / previews / exports / readiness / inventory / retention
  preview), fall back to the current local flag + role check (Phase 10.9B
  behaviour). For destructive operations, fail closed and require a
  fresh permission fetch.
- **Never allow backend permission to bypass wrapper guards.** The
  wrapper continues to require Force=true, the confirmation phrase,
  backup-ack, recent exports, readiness verdict, the runtime/provider
  double-check, and the dangerous-operation lock. Backend permission is
  an additional gate, not a substitute.

**Phase 10.19A does not implement any of these client behaviours.** They
are Phase RBAC-3 / RBAC-4 / RBAC-5 deliverables.

## 10. Token / Session Requirements

- The access token (or an introspection endpoint) should carry the
  operator's role and permission keys. Phase RBAC-2 decides whether to
  embed permissions in the JWT claims (small, no extra round-trip) or
  fetch via the dedicated permissions endpoint (cleaner cache control).
- Permission claims must have an explicit **expiry**. Stale claims must
  not be used past expiry for destructive operations.
- The refresh-token flow must not silently grant permissions without a
  server-side re-check. Each refresh either re-issues claims or carries
  a `permissions_revalidated_at` timestamp.
- If the operator's role changes server-side, the desktop should refresh
  the permission state at the next refresh-token cycle (or sooner if a
  push channel exists). The dashboard should expose a "Refresh
  Permissions" action.
- **Session expiry must not lose unsynced sales.** Per Phase 0 / Phase
  6.1, unsynced sales survive logout / session expiry locally. Permission
  refresh does not change this contract.
- **Permission refresh failure must fail closed for destructive actions.**
  Read-only actions may degrade per the offline policy below.

## 11. Offline / Degraded Mode Policy

The POS is offline-first. The permission model must accommodate
intentional and unintentional offline periods.

- **Destructive actions** (`*.execute`) should generally **fail closed**
  when backend permission cannot be verified. The wrapper guards still
  apply, but the wrapper additionally checks for a fresh permission
  claim before proceeding. Stale or absent claims block execution.
- **Read-only diagnostics / inventory / preview / readiness / evidence
  bundle** may remain available locally based on flag + role, with a
  warning in the dashboard's UI when the cached permission is stale.
- **Real migration / runtime cutover / rollback / retention cleanup**
  should require **either**:
  1. A fresh backend permission claim (verified within the last N
     minutes — N is a policy decision for Phase RBAC-2), **OR**
  2. An explicitly documented **emergency offline approval process**.
- **Emergency offline approval process** requirements (rare; audited):
  - Support Engineer approval, recorded in the pilot/incident ticket.
  - Store Manager approval, recorded in the same ticket.
  - Off-machine backup of `%LocalAppData%\PosSystem\` captured.
  - Fresh Pilot Evidence Bundle exported.
  - The relevant local flag enabled.
  - The relevant confirmation phrase typed verbatim by the operator.
  - A free-form audit note explaining the emergency justification.
  - Both approvers' names and UTC timestamps written into the ticket
    before the operator clicks Execute.

The emergency offline mode is intentionally cumbersome. It is a backstop
for the rare case where the backend is unreachable for hours/days but
the store cannot defer the destructive operation. The mode should be
**rare** and **always audited**.

## 12. Audit Logging Requirements

Each destructive operation's audit log (already produced by the guarded
wrappers in Phases 10.12B / 10.13A / 10.14A / 10.16A) must additionally
record the operator's permission snapshot when the backend permission
model is live. Required fields:

- **Operator user id** (server-assigned identifier).
- **Username** (login identifier; not the operator's real name).
- **Role** (server-issued, not the local cached value).
- **Permission keys snapshot** (the list of permission keys the operator
  held at the time of the operation, redacted).
- **Tenant / store scope** the permission applied to.
- **Machine id or device name** if available.
- **Operation name** (e.g. `migration.execute`, `cutover.execute`).
- **Local flag state** for the relevant per-operation flag (boolean).
- **Approval ticket id** (if the operator-intent API was used).
- **Confirmation phrase accepted** boolean only — never the raw phrase.
- **Backup acknowledged** boolean — never the free-form note.
- **Reviewed export paths** (preflight, inventory) — paths only, not
  the file contents.
- **Readiness status** (from the relevant gate / readiness service).
- **Outcome** (Success / Failed / Rejected / NoOp).
- **Timestamps** — started, completed, both UTC.
- **Evidence bundle path** that the operator exported during the
  pilot window.
- **Redaction applied** boolean — always true on a successful audit
  write.

**Must NOT be present** in any audit log:

- Raw confirmation phrases (replaced with `<redacted-confirmation-phrase>`).
- Auth tokens, refresh tokens, `Authorization` / `Bearer` values.
- JWTs (replaced with `<redacted-jwt>`).
- DPAPI `enc:v1:` blobs (replaced with `<redacted-encrypted>`).
- Passwords or password hashes.
- Raw DB rows (sales, customers, products, payments).
- Full customer or payment data.

These exclusions are already enforced by `MigrationAuditLogger.RedactSecrets`
plus the per-wrapper phrase scrub. Adding permission snapshot fields must
not weaken any existing redaction.

## 13. Migration Plan

The migration from the current local model to backend-supported
permissions proceeds in eight phases. **Every phase preserves the existing
local guards** — wrapper guards, local flags, confirmation phrases, and
the dangerous operation lock remain in force throughout.

| Phase | Goal | Hard rules |
|---|---|---|
| **RBAC-1** — Documentation + permission key agreement | This document. Agreement on permission keys, role matrix, scope boundaries with backend team. | No wrapper guard removal. Local flags remain. Confirmation phrases remain. Dangerous operation lock remains. |
| **RBAC-2** — Backend permission schema / API | Backend defines and ships the schema + APIs documented in Section 8. | No wrapper guard removal. Local flags remain. Confirmation phrases remain. Dangerous operation lock remains. |
| **RBAC-3** — Desktop permission fetch + read-only display | Desktop fetches permissions at login; dashboard shows current permission status in the diagnostics card. Read-only display only — UI behaviour unchanged. | No wrapper guard removal. Local flags remain. Confirmation phrases remain. Dangerous operation lock remains. |
| **RBAC-4** — Dashboard open gate uses backend permission + local flag | Opening the Migration Operations dashboard requires BOTH backend `operator.dashboard.open` permission AND `operator_migration_dashboard_enabled="1"`. The local role check remains as a defence-in-depth third gate. | No wrapper guard removal. Local flags remain. Confirmation phrases remain. Dangerous operation lock remains. |
| **RBAC-5** — Dangerous command preflight re-check | Every guarded wrapper's `ExecuteAsync` calls `POST /api/v1/operator/permissions/validate` immediately before invoking the underlying executor. Fails closed if backend rejects or is unreachable. | No wrapper guard removal. Local flags remain. Confirmation phrases remain. Dangerous operation lock remains. Local fail-closed behaviour matches the emergency offline policy. |
| **RBAC-6** — Audit / evidence bundle includes permission snapshot | Wrapper audit logs and the Pilot Evidence Bundle gain a sanitized `permission-snapshot.json` (or equivalent section). Redaction rules unchanged. | No wrapper guard removal. Local flags remain. Confirmation phrases remain. Dangerous operation lock remains. |
| **RBAC-7** — Optional backend audit upload | Desktop optionally uploads audit summaries via `POST /api/v1/operator/audit` and evidence bundles via `POST /api/v1/operator/evidence`. Upload failures must not block destructive operations — the local audit remains the primary record. | No wrapper guard removal. Local flags remain. Confirmation phrases remain. Dangerous operation lock remains. |
| **RBAC-8** — Pilot RBAC rollout | Pilot the full RBAC stack on a single store via the existing controlled production pilot runbook. Validate that fail-closed and emergency offline modes behave correctly. | No wrapper guard removal. Local flags remain. Confirmation phrases remain. Dangerous operation lock remains. |

## 14. Backward Compatibility Plan

- **Current behaviour continues to work** while the backend RBAC is not
  ready. The desktop's existing flag + role gate (Phase 10.9B) is the
  authority until RBAC-4 / RBAC-5 ship.
- **Default behaviour stays deny** for dangerous actions unless the
  local flag is on AND the local role is authorized. This matches today.
- When backend permissions are introduced (RBAC-4 onwards):
  - **Destructive actions require BOTH** backend permission **AND** the
    local flag. Removing either gate causes destructive actions to fail
    closed.
  - **Read-only actions** may be gradually moved to backend permission
    primary, with the local flag as a fallback for offline mode.
- **Older desktop clients** during rollout must continue to behave
  safely. The backend rollout must not break existing flag-based clients.
  This is achieved by:
  - The backend permission API being additive (older clients don't
    call it).
  - Older clients continuing to enforce the local flag + role gate.
  - The pilot runbooks remaining valid for both old and new clients.
- **Existing pilot runbooks** (controlled pilot + sign-off) remain in
  force unchanged. The RBAC rollout is itself piloted via the controlled
  pilot runbook, not as a special case.

## 15. Testing Strategy

Each phase of the RBAC rollout adds test cases. The baseline set:

- **CASHIER** cannot open the dashboard. (Pre-RBAC test; remains
  post-RBAC.)
- **CASHIER** cannot execute any dangerous action even if every local
  flag is on. (Pre-RBAC test; remains.)
- **ADMIN without backend permission** cannot execute any dangerous
  action. (Post-RBAC-5 test.)
- **SUPPORT with backend permission but local flag off** cannot execute.
  (Post-RBAC-4 test — the local flag remains a hard gate.)
- **SUPPORT with backend permission + local flag on + wrong phrase**
  cannot execute. (Existing wrapper guard test; remains.)
- **SUPPORT with backend permission + local flag on + correct phrase but
  pending sales > 0** cannot execute. (Existing wrapper guard test;
  remains.)
- **Permission revoked mid-session** blocks the next dangerous operation.
  The preflight re-check (RBAC-5) catches the revocation.
- **Offline permission fetch failure** blocks dangerous operations.
  Emergency offline approval (Section 11) must be exercised for the
  documented exception path.
- **Read-only diagnostics policy** works as documented in Section 11 —
  read-only operations remain available under flag + role, with a stale-
  permission warning.
- **Audit log contains permission snapshot** but no raw confirmation
  phrases, no tokens, no JWTs, no DPAPI blobs. Grep the audit logs for
  the sentinel values.
- **Evidence bundle contains permission summary** but no raw tokens or
  phrases. Same grep test against the bundle ZIP.
- **Old client (pre-RBAC)** still behaves safely. Flag + role gate
  continues to enforce the existing deny-by-default policy.

## 16. Security Review Checklist

Reviewers must confirm every item below before approving each RBAC phase:

- [ ] **No authorization based only on UI visibility.** Hidden UI is not
      a security control; the wrapper guards are.
- [ ] **Backend permission is checked server-side** where the API exists.
      The desktop never trusts a cached claim past its expiry for
      destructive operations.
- [ ] **Local wrapper guards remain.** Every destructive wrapper still
      requires Force + phrase + backup-ack + exports + readiness +
      diagnostics + provider state + (where relevant) marker +
      verifier.
- [ ] **Dangerous operation lock remains.** The four destructive
      operations stay mutually exclusive within a single desktop
      session.
- [ ] **Confirmation phrase is never logged raw.** Audit logs scrub via
      `MigrationAuditLogger.RedactSecrets` + per-wrapper phrase scrub.
      Permission snapshots do not introduce a new leak vector.
- [ ] **Tokens never exported.** The Pilot Evidence Bundle continues to
      exclude raw `auth_token`, `refresh_token`, `Authorization`, and
      `Bearer` values.
- [ ] **Permission stale state handled** for both read-only and
      destructive operations per the offline policy.
- [ ] **Emergency offline approval process documented** and audited.
- [ ] **Audit and evidence bundle redaction** unchanged; the permission
      snapshot does not bypass redaction.
- [ ] **CASHIER is denied** at every layer (flag, role, permission,
      wrapper).
- [ ] **SUPPORT is scoped** to the tenant/store of the support ticket.

## 17. Rollout Strategy

The RBAC stack is itself rolled out using a controlled pilot, mirroring
the tenant DB pilot pattern documented in
[`operator-controlled-production-pilot-runbook.md`](operator-controlled-production-pilot-runbook.md).

1. **Start with read-only permission display.** RBAC-3 displays the
   operator's permission set in the dashboard's diagnostics card and
   in the readiness report; no behaviour changes.
2. **Gate dashboard opening.** RBAC-4 requires BOTH backend permission
   AND the local flag for dashboard open. Local flag remains primary
   for offline / degraded mode.
3. **Gate dangerous command preflight.** RBAC-5 adds the server-side
   re-check immediately before every guarded wrapper executes.
4. **Pilot with support-only users.** The first RBAC pilot is run by
   SUPPORT-role users only, against the existing pilot tenant from
   Phase 10.18A. Validate that destructive operations still go through
   every existing guard plus the new permission gate.
5. **Validate evidence bundle and audit.** Confirm the permission
   snapshot is recorded and redacted correctly. Run the security
   review checklist (Section 16) against the produced evidence.
6. **Do not remove local flags.** Local flags continue to gate UI
   actionability through the full RBAC rollout. Removing them is
   explicitly out of scope.
7. **Expand gradually.** After SUPPORT-only pilot, expand to ADMIN /
   OWNER per the role matrix. Multi-store rollout follows the existing
   controlled pilot pattern.
8. **Rollback plan for the RBAC rollout itself:**
   - **Feature flag** in the backend (e.g. `rbac_enforcement_enabled`)
     that disables backend permission enforcement and reverts the
     desktop to the current flag + role model.
   - Falling back never weakens guards — the desktop's local flag +
     role + wrapper guards remain in force.
   - **Audit logs preserved.** Even when RBAC enforcement is disabled,
     local audit logs continue to be produced.

## 18. Manual Review Checklist

Reviewers of this document confirm:

- [ ] All 18 sections present and populated.
- [ ] Section 5 (Current Protected Operations) lists each of the 15
      operations the dashboard currently exposes.
- [ ] Section 6 (Proposed Permission Keys) lists all 17 keys including
      the `flags.change` future key.
- [ ] Section 7 (Role-to-Permission Matrix) covers CASHIER / MANAGER /
      ADMIN / SUPPORT / OWNER / GLOBAL_ADMIN / SUPER_ADMIN.
- [ ] Section 8 (Backend API Requirements) is clear and explicitly
      states "Phase 10.19A does not implement any of these APIs".
- [ ] Section 9 (Desktop Client Requirements) is clear and explicitly
      states "Phase 10.19A does not implement any of these behaviours".
- [ ] Section 11 (Offline / Degraded Mode Policy) defines an emergency
      offline approval process with all six guardrails (support +
      manager approval, off-machine backup, evidence bundle, local
      flag, confirmation phrase, audit note).
- [ ] Section 12 (Audit Logging Requirements) explicitly excludes raw
      confirmation phrases, tokens, refresh tokens, DPAPI blobs,
      passwords, raw DB rows, and full customer/payment data.
- [ ] Section 13 (Migration Plan) has eight phases (RBAC-1 through
      RBAC-8) and every phase carries the "no wrapper guard removal,
      local flags remain, confirmation phrases remain, dangerous
      operation lock remains" rule.
- [ ] No raw confirmation phrase value appears anywhere in this
      document. Every phrase reference points to "the internal secure
      runbook".
- [ ] No destructive shell command appears (`Remove-Item`, `rd /s`,
      `del /q`, `rm`, `del`, `Set-Content -Force`, `New-Item -Force`,
      `Move-Item` against DB files, etc.).
- [ ] No source code file (`.cs` / `.xaml` / `.csproj`) was modified
      by the commit that introduced this document.
- [ ] Cross-reference lines were added to the two named runbooks
      (controlled pilot + sign-off) pointing readers at this document.
