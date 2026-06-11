# Controlled Production Pilot Runbook

> Canonical checklist for moving **one** controlled pilot tenant/store from
> legacy shared local DB mode to per-tenant DB runtime mode in the PosSystem
> WPF desktop client.
>
> This document is the master pilot checklist. It references — and does **not**
> duplicate — the detailed procedures in:
> - [`operator-tenant-db-migration-runbook.md`](operator-tenant-db-migration-runbook.md) — migration / cutover / rollback lifecycle
> - [`operator-retention-cleanup-runbook.md`](operator-retention-cleanup-runbook.md) — storage retention cleanup
> - [`tenant-db-rollback.md`](tenant-db-rollback.md) — rollback deep-dive
>
> When the pilot window closes (success, with warnings, or with
> rollback), continue with
> [`operator-pilot-signoff-rollout-decision-runbook.md`](operator-pilot-signoff-rollout-decision-runbook.md)
> to decide whether to expand, hold, or recover the rollout.
> For the longer-term architecture audit and migration plan from local
> flag/role gates to backend-supported operator permissions, see
> [`operator-rbac-permission-model-plan.md`](operator-rbac-permission-model-plan.md).
> As of Phase 10.19E, the Pilot Readiness Report and the Pilot Evidence
> Bundle now include backend-permission state (Area I + a new
> `backend-permission-summary.json` file inside the bundle). See
> [`desktop-backend-operator-permissions-integration.md`](desktop-backend-operator-permissions-integration.md).

---

## Scope

This runbook covers:

- A **single** controlled pilot tenant/store transitioning from legacy shared
  local DB mode to per-tenant DB runtime mode.
- The offline-first WPF POS desktop client running on the operator's
  Windows host with data under `%LocalAppData%\PosSystem\`.
- The full operator workflow from pre-pilot readiness → real migration →
  runtime cutover → restart/re-login → post-cutover validation → optional
  rollback → pilot sign-off.
- Evidence collection for support and audit before, during, and after the
  pilot window.

## Non-Goals

This runbook does **not** cover:

- Backend (Ham-Pos) schema migration.
- Multi-store mass rollout. The pilot is one store; multi-store rollout is
  a separate exercise after pilot sign-off.
- Automatic / unattended rollout. Every destructive action in this runbook is
  an explicit operator click through a guarded UI.
- Arbitrary file cleanup. See `operator-retention-cleanup-runbook.md` —
  cleanup is its own runbook and its own audit trail.
- Sale or payment data repair. The pilot fails fast on pending/poison sales
  rather than attempting repair.
- Replacing the full RBAC model. The current allow-list (ADMIN /
  GLOBAL_ADMIN / SUPER_ADMIN / SUPPORT / OWNER) remains in force.
- Exposing or distributing confirmation phrase values. Phrases stay in the
  internal secure runbook.
- Bypassing any guarded executor. Every destructive action goes through the
  guarded UI in the Migration Operations dashboard.

## Pilot Roles

| Role | Allowed to | Not allowed to |
|---|---|---|
| **Pilot Operator** | Open Migration Operations dashboard, refresh, run read-only previews and exports, generate Pilot Readiness Report, export Pilot Evidence Bundle, type confirmation phrase from the internal secure runbook, click the four Execute buttons, observe outcome, archive evidence. | Approve rollback unilaterally; sign off pilot success unilaterally; cleanup files manually outside the cleanup UI. |
| **Support Engineer** | Review evidence bundles, approve `AllowWarnings` paths, approve rollback, decide on `RestoreLegacyFromBackupIfMissing`, advise on `tenant_db_runtime_enabled` flag flips, sign off pilot success jointly with Store Manager. | Type the confirmation phrase on the operator's behalf; enable destructive UI flags remotely; edit `global_settings.json` from outside the operator's host. |
| **Backend / Platform Engineer** | Confirm backend `shared_to_tenant_migration_enabled` is acceptable for the pilot tenant; advise on Idempotency-Key semantics; review post-pilot logs for backend-side concerns. | Authorize a desktop-side rollback; modify desktop settings. |
| **Store Manager / Business Owner** | Authorize the pilot window, authorize rollback, authorize pilot sign-off. | Type the confirmation phrase; execute destructive UI actions; bypass any safety check. |

Operator-facing flag flips (`operator_*_ui_enabled`) and `shared_to_tenant_migration_enabled` may be enabled **only** when explicitly approved by the Support Engineer for the duration of the operation window. Approval must be recorded in the pilot ticket.

## Required Pre-Pilot Artifacts

Every artifact below must exist before the pilot window opens. Attach each to the pilot support ticket.

| Artifact | How produced | Where to attach |
|---|---|---|
| Off-machine backup of `%LocalAppData%\PosSystem\` | Manual file copy to external drive / secure server folder / off-site zip | Ticket attachment OR linked path that is verifiable by Support |
| Export Diagnostics JSON | Dashboard → **Export Diagnostics JSON** button | Ticket attachment |
| Export Preflight Report | Dashboard → **Export Preflight Report** button | Ticket attachment |
| Export Inventory Report | Dashboard → **Refresh Inventory** → **Export Inventory Report** | Ticket attachment |
| Preview Migration Dry-Run result | Dashboard → **Preview Migration Dry-Run** (screenshot of side-effect=Passed) | Ticket attachment |
| Preview Rollback Dry-Run result | Dashboard → **Preview Rollback Dry-Run** (screenshot of side-effect=Passed) | Ticket attachment |
| Preview Retention Plan result | Dashboard → **Preview Retention Plan** (informational; cleanup is NOT executed during pilot) | Ticket attachment |
| Production Pilot Readiness Report | Dashboard → **Generate Pilot Readiness Report** → JSON under `logs\pilot-readiness\` | Ticket attachment |
| Production Pilot Evidence Bundle | Dashboard → **Export Pilot Evidence Bundle** → folder + ZIP under `logs\pilot-evidence\` | Ticket attachment of the ZIP |

The Support Engineer reviews every artifact before authorizing the pilot to proceed.

## Flag Window Policy

Default state: **all** dangerous-UI and `shared_to_tenant_migration_enabled` flags are off. Enable only one at a time, only for the exact operation window, and turn off immediately after.

| Flag | Purpose | Normal safe value | May be temporarily enabled when… | Approval required from |
|---|---|---|---|---|
| `operator_migration_dashboard_enabled` | Opens Migration Operations dashboard. | `"0"` or missing outside the pilot window; `"1"` for the pilot window. | Whenever an authorized operator needs to use the dashboard's read-only or guarded actions during the pilot. | Support Engineer (one-time for the pilot day). |
| `operator_real_migration_ui_enabled` | Makes the Execute Real Migration section actionable. | `"0"` or missing. | Only during the real-migration sub-window after the readiness gate is satisfied and evidence is captured. | Support Engineer for the specific sub-window. |
| `operator_runtime_cutover_ui_enabled` | Makes the Execute Runtime Cutover section actionable. | `"0"` or missing. | Only during the runtime-cutover sub-window after verification AllVerified=true and post-migration evidence is captured. | Support Engineer for the specific sub-window. |
| `operator_rollback_ui_enabled` | Makes the Execute Rollback section actionable. | `"0"` or missing. | Only when rollback has been formally decided by Support and Store Manager. | Support Engineer AND Store Manager. |
| `operator_retention_cleanup_ui_enabled` | Makes the Execute Retention Cleanup section actionable. | `"0"` or missing. | Only AFTER pilot sign-off; not used as part of the pilot recovery workflow. | Support Engineer in a separate maintenance window. |
| `shared_to_tenant_migration_enabled` | Server-side gate for real migration. | `"0"` or missing outside the migration sub-window. | Only during the real-migration sub-window of the pilot day. | Support Engineer + Backend Engineer for the specific tenant. |
| `tenant_db_runtime_enabled` | Tells the app to open the tenant DB on startup. **Never set this manually.** | `"0"` or missing — flipped to `"1"` only by the Guarded Runtime Cutover wrapper, flipped back to `"0"` only by the Guarded Rollback wrapper. | Never by manual edit. | n/a — written only by the wrappers. |

Bookend rule: every dangerous UI flag is flipped on as the **first** step of its sub-window and flipped off as the **last** step.

## Pre-Pilot Readiness Gate

Complete every item below before executing any destructive action. Any blocker → **STOP**.

- [ ] PosSystem app version confirmed (note the build hash in the ticket).
- [ ] Pilot tenant subdomain confirmed in the ticket and in the dashboard's Tenant input.
- [ ] Operator's user role is in the allow-list (ADMIN / GLOBAL_ADMIN / SUPER_ADMIN / SUPPORT / OWNER); not CASHIER.
- [ ] Diagnostics shows `PendingSalesCount = 0`.
- [ ] Diagnostics shows `PoisonSalesCount = 0`.
- [ ] Diagnostics warnings/errors reviewed by Support; either accepted with reason in ticket, or resolved.
- [ ] Pilot Readiness Report `OverallStatus` is `Ready` OR `ReadyWithWarnings` with every warning documented and accepted in the ticket.
- [ ] Pilot Evidence Bundle ZIP exported and attached.
- [ ] Off-machine backup of `%LocalAppData%\PosSystem\` exists AND has been independently verified by a second person (Support or Store Manager).
- [ ] Store is in a low-traffic or closed window. Cashier coordination confirmed.
- [ ] Rollback Owner (Support + Store Manager) is available for the duration of the pilot.

If **any** item is unchecked:

> **STOP. Do not execute migration. Do not proceed to any destructive step.**

Document why the pilot was postponed and replan.

## Controlled Real Migration Procedure

This procedure uses the **Guarded Real Migration Execution** section only. The wrapper service is the sole legitimate execution path; the underlying `SharedToTenantDatabaseMigrator` must never be invoked from a debugger, a CLI, or anywhere outside the dashboard's Execute button.

1. Set `operator_migration_dashboard_enabled = "1"` in `%LocalAppData%\PosSystem\global_settings.json`.
2. Set `shared_to_tenant_migration_enabled = "1"` in the same file (server-side gate). Record the change in the ticket.
3. Set `operator_real_migration_ui_enabled = "1"`.
4. Open the Migration Operations dashboard (`Ctrl + Shift + M`).
5. Click **Refresh**. Confirm runtime / verifier / readiness cards repopulate.
6. Click **Generate Pilot Readiness Report**. Confirm `OverallStatus` is Ready or ReadyWithWarnings (with approved warnings).
7. Click **Export Pilot Evidence Bundle**. Note the bundle path in the ticket.
8. Scroll to **Guarded Real Migration Execution**. Confirm the section status reads Enabled.
9. Paste the path to your reviewed preflight JSON.
10. Paste the path to your reviewed inventory JSON.
11. Tick the external-backup-acknowledgement checkbox. Add a note describing where the backup lives.
12. If the gate is `AllowedWithWarnings` and Support approved, tick **Allow gate warnings**.
13. Type the migration confirmation phrase from the **internal secure runbook**. The phrase is not displayed in the UI or in this document. Type it carefully — wrong phrase rejects with no leak to logs.
14. Click **Execute Real Migration**.
15. Wait for the outcome panel to populate. The dashboard's Dangerous Operation Lock card will flip to running during the call.
16. Capture an outcome screenshot of the dashboard.
17. Click **Export Pilot Evidence Bundle** again to capture post-migration state. Attach to the ticket.
18. Set `operator_real_migration_ui_enabled = "0"` (or remove it). This bookends the sub-window.

Explicit reminders:

- Real migration **does NOT enable tenant runtime mode.** `tenant_db_runtime_enabled` stays at its previous value.
- Real migration **does NOT switch the current DB session.** The running POS app stays on legacy `pos.db`.
- Real migration **does NOT log out or restart the app.** The operator continues working through the dashboard.

## Post-Migration Verification

Immediately after Real Migration returns Success:

- [ ] Click Refresh. Confirm the Verification card shows `AllVerified=true` and the expected tenant count.
- [ ] Confirm the Migration Audit card matches the new migrated state.
- [ ] Confirm a migrator audit log exists under `logs\migrations\`.
- [ ] Confirm a wrapper audit log exists under `logs\migration-executor\`.
- [ ] Click **Generate Pilot Readiness Report** again. Confirm overall status is Ready or ReadyWithWarnings.
- [ ] Click **Export Pilot Evidence Bundle** to capture the post-migration state. Attach to ticket.
- [ ] Confirm the per-tenant DB now exists: `%LocalAppData%\PosSystem\tenants\<tenant>\pos.db`.
- [ ] Confirm POS still works in the current session — ring up a small test sale on the legacy DB; sale should still go through (current session has not switched yet).
- [ ] Confirm `PendingSalesCount` and `PoisonSalesCount` are still 0.

If verification fails (`AllVerified=false`, missing tenant DB, unexpected errors):

> **STOP. Do not execute runtime cutover. Open a defect ticket. Decide on rollback per the Rollback Decision Path section below.**

## Controlled Runtime Cutover Procedure

Only after Real Migration succeeded AND post-migration verification passed AND Support + Store Manager approved the cutover sub-window:

1. Set `operator_runtime_cutover_ui_enabled = "1"` in `global_settings.json`.
2. Open the dashboard (it should already be open from the migration sub-window; if not, reopen).
3. Click **Refresh**. Click **Generate Pilot Readiness Report**. Confirm status is Ready or ReadyWithWarnings.
4. Click **Export Pilot Evidence Bundle**. Attach to ticket.
5. Scroll to **Guarded Runtime Cutover Execution**. Confirm section status reads Enabled.
6. Paste a fresh preflight JSON path (re-export with **Export Preflight Report** if the previous one is more than a few minutes old).
7. Paste a fresh inventory JSON path.
8. Tick the external-backup-acknowledgement checkbox and add the note.
9. If readiness is `AllowedWithWarnings` and Support approved, tick **Proceed if cutover readiness is AllowedWithWarnings**.
10. Type the **runtime cutover confirmation phrase** from the internal secure runbook (a different phrase from the migration phrase — not shown here).
11. Click **Execute Runtime Cutover**.
12. Wait for the outcome panel.
13. Confirm the result panel shows `RuntimeFlagAfter = True`. Read `tenant_db_runtime_enabled` from `global_settings.json` independently to confirm it is now `"1"`.
14. Click **Export Pilot Evidence Bundle** post-cutover. Attach to ticket.
15. Set `operator_runtime_cutover_ui_enabled = "0"` (or remove it). Bookend the sub-window.

Explicit reminders:

- Runtime cutover only enables runtime mode **for the next startup/login**.
- It **does NOT switch the current DB session.** The current POS continues on legacy DB.
- It **does NOT log out or restart the app.** That is the next manual step (Restart / Re-login Procedure).

## Restart / Re-Login Procedure

After a successful runtime cutover:

1. If a sale is in progress, finish it cleanly on the legacy DB (still active).
2. Close any open dialogs.
3. From the POS menu choose **Logout**. Wait for the login screen.
4. Close PosSystem.exe completely (do not force-kill unless instructed by Support).
5. Reopen PosSystem.exe.
6. Wait for the splash / login screen.
7. Log in as the authorized pilot user.
8. Confirm tenant DB runtime mode is now active — Operator Diagnostics should show `IsTenantScoped = True` and `ActiveDbPath` ending in `…\tenants\<tenant>\pos.db`.

At startup the existing `MaybeSwitchToTenantDbAtStartup` path reads `tenant_db_runtime_enabled` and consults `TenantCutoverReadinessGate` before flipping the path provider; failure of either short-circuits the switch and the app stays on legacy mode (with a Debug log message documented in the migration runbook).

## Post-Cutover Validation

Concrete checks after re-login on the tenant DB:

- [ ] Products list loads.
- [ ] Customers list loads.
- [ ] Cashbox opens and displays prior shift state.
- [ ] A low-value test sale (single item, ≤ minimum currency unit) completes end-to-end.
- [ ] Mixed payment (Naqt / Karta / Qarz combination) flow still works if part of the store's normal usage.
- [ ] Debt sale still works if the store uses that flow.
- [ ] Stock decreases correctly for the test sale (Phase 5.1 reconcile marker should not regress).
- [ ] Sync succeeds when the internet is online again (no permanent failures, no poison sale appearance).
- [ ] Logout works.
- [ ] Login back in works; `IsTenantScoped` remains `True`.
- [ ] Diagnostics shows `PendingSalesCount = 0` and `PoisonSalesCount = 0` after sync.
- [ ] Click **Export Pilot Evidence Bundle** one more time after validation. Attach to ticket.

Pass/fail criteria:

| Outcome | Action |
|---|---|
| **All checks pass.** | Continue with the pilot. Schedule Pilot Sign-Off communication. |
| **One non-critical warning** (e.g. a single cosmetic UI artefact). | Document in ticket; proceed cautiously. Decide whether to continue the pilot or schedule a fix in the next maintenance window. |
| **Any critical failure** (cannot sell, data missing, sync permanently broken, app cannot start). | **STOP.** Hold sales. Decide on rollback per the Rollback Decision Path below. |

## Rollback Decision Path

Rollback is **destructive** and should be considered only when:

- The app cannot open after the post-cutover restart.
- Tenant DB runtime mode actively blocks sales (data missing, schema mismatch, write errors).
- Critical data is missing from the tenant DB compared to the legacy backup.
- The Verifier/Readiness Report transitions to `Blocked` after cutover.
- Support AND Store Manager jointly approve the rollback.

Rollback is **not** appropriate for:

- A single product or customer cosmetic correction.
- A temporary network/sync outage that resolves on retry.
- A normal validation warning that Support already accepted.
- A retention cleanup issue (cleanup is its own runbook).
- An operator typo that the wrapper guards already rejected without mutation.

Before attempting rollback, require:

- A fresh off-machine backup of `%LocalAppData%\PosSystem\` captured after the failed cutover (do not rely solely on the pre-pilot backup).
- A fresh diagnostics / preflight / inventory bundle.
- A fresh Pilot Readiness Report and Pilot Evidence Bundle, attached to the ticket.
- Explicit Support + Store Manager approval, recorded in the ticket.

## Controlled Rollback Procedure Reference

This runbook does not duplicate every rollback detail. Refer to:

- [`tenant-db-rollback.md`](tenant-db-rollback.md) — full rollback deep-dive.
- [`operator-tenant-db-migration-runbook.md`](operator-tenant-db-migration-runbook.md) — Rollback Decision Criteria + Execute Rollback sections.

Short summary in the pilot context:

- Rollback must use the **Guarded Rollback Execution** section of the dashboard. No direct call to the inner executor is permitted.
- The current DB session is not switched by the wrapper. After a successful rollback the operator must restart/re-login per the Post-Rollback Restart / Re-login Procedure in the migration runbook.
- Post-rollback validation is required before sales resume.
- The rollback confirmation phrases are kept in the internal secure runbook. They are not shown here.
- Set `operator_rollback_ui_enabled = "0"` immediately after the rollback sub-window ends.

## Cleanup Policy During Pilot

- Retention cleanup should **not** be executed during the initial pilot migration/cutover window. Cleanup is storage hygiene, not recovery.
- Cleanup may be executed only AFTER the pilot is signed off as successful, or by a separate Support decision in a dedicated maintenance window.
- Cleanup has its own runbook: [`operator-retention-cleanup-runbook.md`](operator-retention-cleanup-runbook.md). Follow it; do not improvise.
- During the pilot, archived `tenants.before-rollback-*` directories and old `pos.db.broken-*` files (if any) must be **preserved** for at least 30 days regardless of retention thresholds.

## Evidence and Audit Preservation

Preserve every JSON file under these directories for at least 90 days after the pilot completes:

```
%LocalAppData%\PosSystem\logs\pilot-evidence\
%LocalAppData%\PosSystem\logs\pilot-readiness\
%LocalAppData%\PosSystem\logs\diagnostics\
%LocalAppData%\PosSystem\logs\preflight\
%LocalAppData%\PosSystem\logs\inventory\
%LocalAppData%\PosSystem\logs\migrations\
%LocalAppData%\PosSystem\logs\migration-executor\
%LocalAppData%\PosSystem\logs\runtime-cutover\
%LocalAppData%\PosSystem\logs\rollback-executor\
%LocalAppData%\PosSystem\logs\rollbacks\
%LocalAppData%\PosSystem\logs\retention-cleanup\
```

Rules:

- **Do not delete** any of these files during the pilot.
- Attach the final Pilot Evidence Bundle ZIP to the pilot ticket.
- Preserve the off-machine backup until the pilot has been signed off as successful (or, on rollback, until rollback is signed off and the store has run normally for at least one full day).
- A raw confirmation phrase must **not** appear in any attachment. If it does, treat that as a redactor defect and file a separate security ticket.

## Communication Template

Use these short templates in the pilot ticket. They keep stakeholders aligned and the audit trail searchable.

### Before Pilot

```
PILOT: <pilot name / store name>
Tenant subdomain: <subdomain>
Planned window:   <YYYY-MM-DD HH:MM local → HH:MM local>
Pilot Operator:   <name>
Support Owner:    <name>
Rollback Owner:   <name>
Store Manager:    <name>
Backup location:  <verified off-machine path or storage system>
Pilot Evidence Bundle (pre): <ticket attachment id>
Pilot Readiness Report status (pre): <Ready / ReadyWithWarnings — list of accepted warnings>
```

### Migration Completed

```
MIGRATION RESULT
Outcome:                <Success / Failed / Rejected>
Migration marker stamp: <UTC>
Verifier AllVerified:   <true / false>
Per-tenant count:       <N>
Runtime mode enabled:   No (cutover not executed yet)
DB session switched:    No
Pilot Evidence Bundle (post-migration): <ticket attachment id>
Next planned cutover time: <YYYY-MM-DD HH:MM local>
```

### Cutover Completed

```
CUTOVER RESULT
Outcome:                  <Success / Failed / Rejected>
tenant_db_runtime_enabled: <"1" / "0">
Restart/re-login done:    <Yes / No / pending>
Post-restart IsTenantScoped: <true / false>
Post-cutover validation:  <Pass / Pass with warnings / Fail>
Pilot Evidence Bundle (post-cutover): <ticket attachment id>
```

### Rollback Triggered

```
ROLLBACK
Reason:               <concrete observed symptom>
Approval (Support):   <name + timestamp>
Approval (Store Mgr): <name + timestamp>
Evidence Bundle (pre-rollback): <ticket attachment id>
Rollback Outcome:     <Success / Failed / Rejected>
Runtime flag after:   <"0">
Tenants archive path: <…\tenants.before-rollback-<utc>\>
Restart/re-login done:<Yes / No / pending>
Post-rollback validation: <Pass / Pass with warnings / Fail>
Evidence Bundle (post-rollback): <ticket attachment id>
```

### Pilot Signed Off

```
PILOT SIGN-OFF
Pilot:                  <store name>
Window:                 <start → end>
Final state:            tenant_db_runtime_enabled = "1", IsTenantScoped = true
Validation summary:     <Pass / Pass with warnings>
Known warnings carried forward: <list, with severity>
Rollback used:          <Yes / No>
Evidence bundle (final): <ticket attachment id>
Recommendation for broader rollout: <Yes / Yes with caveats / No — fix first>
Signed off by Support:  <name + timestamp>
Signed off by Manager:  <name + timestamp>
```

## Common Failure Scenarios

| Symptom | Safe Response | Stop? | Evidence to Collect |
|---|---|---|---|
| `PendingSalesCount > 0` before migration window | Run sync until count = 0. Verify no permanent failures. | Yes, until resolved. | Diagnostics JSON, sync log. |
| `PoisonSalesCount > 0` before migration window | Open POS Failed Sales panel. Either resolve / requeue / document each. Do not silently ignore. | Yes, until count = 0 OR Support documents acceptance. | Diagnostics JSON, screenshot of Failed Sales panel. |
| Off-machine backup missing or not verified | Stop. Capture and verify backup. Do not proceed until a second person confirms backup integrity. | Yes. | Backup-location attestation. |
| Pilot Readiness Report status = Blocked | Read every blocker. Fix the underlying condition (pending sales, marker state, runtime flag drift, etc.). | Yes. | Readiness Report JSON, screenshot of dashboard. |
| Pilot Evidence Bundle export failed | Retry once. If still failing, capture the dashboard's Errors card. File a defect ticket. Decide if pilot can proceed without bundle — usually NO. | Probably yes — bundle is a hard requirement. | Dashboard Errors text, manual diagnostics JSON. |
| Execute Real Migration returns Outcome=Rejected | Read every blocker. Fix and retry. The wrapper did not call the migrator; no mutation occurred. | Yes until resolved. | Wrapper audit log under `logs\migration-executor\`. |
| Execute Real Migration returns Outcome=Failed | Capture the failure reason. Do not retry blindly. Decide whether to roll back the partial state via the migration runbook's rollback path. | Yes. | Migrator audit log under `logs\migrations\` + wrapper audit log. |
| Verifier reports `AllVerified=false` after migration | Investigate per-tenant Issues. Do not proceed to cutover. Consider restoring from backup, opening a defect, or rolling back. | Yes. Cutover must not run. | Verifier output, dashboard screenshot. |
| Execute Runtime Cutover returns Outcome=Rejected | Read the blocker (often a gate refusal). Fix and retry the cutover sub-window. | Yes until resolved. | Cutover wrapper audit log under `logs\runtime-cutover\`. |
| App fails to open after the post-cutover restart | Do not force-modify settings. Capture diagnostics if possible. Contact Support. Decide on rollback. | Yes. Halt sales. | Last-known-good Pilot Evidence Bundle, post-cutover settings snapshot, Windows event log. |
| Test sale fails after cutover (e.g. write error, missing product) | Stop sales. Investigate the specific sale path. Compare tenant DB content with legacy backup. Decide on rollback. | Yes for sales; investigate first. | Diagnostics, sale screenshot, sync log. |
| Rollback readiness reports Blocked (legacy DB missing) | Investigate. Restore legacy `pos.db` from external backup before attempting rollback. Never run rollback with a missing legacy. | Yes. | Rollback readiness JSON, inventory JSON. |
| Execute Rollback returns Outcome=Failed | Capture failure reason. Do not auto-retry. Engage Support. Preserve all evidence — this is a forensic case. | Yes. | Rollback wrapper audit log, inner rollback audit log, evidence bundle, off-machine backup snapshot. |
| A raw confirmation phrase appears in any log file | Treat as a redactor defect. File a security ticket. Preserve the leaking file off-line; do not share externally. | Continue pilot if otherwise healthy, but escalate. | The leaking JSON file (offline only). |
| Active `pos.db` or live tenant DB appears as a cleanup candidate in retention preview | Stop. Do not execute cleanup. The preview classification has a defect. File a defect ticket. | Yes for any cleanup; pilot itself unaffected if cleanup was not started. | Retention preview JSON, dashboard screenshot. |
| Pilot Operator double-clicks Execute (any flavour) | The wrapper helper short-circuits. Confirm the dashboard's Dangerous Operation Lock card flipped to running only once. Confirm the relevant audit log has exactly one entry. | No, this is the safe path. | Audit log under the matching `logs\…executor\` directory. |
| Operator forgot to flip a flag back to `"0"` after a sub-window | Set the flag to `"0"`. Note the duration of unintended actionability in the ticket. Confirm no destructive operation was attempted during that window. | No, if no operation was attempted. Yes for audit logging. | Audit logs, ticket annotation. |

## Manual Review Checklist

The Support Engineer reviews this document for every pilot. Confirm:

- [ ] No raw confirmation phrase value appears anywhere in this document. Every phrase reference points to "the internal secure runbook".
- [ ] No destructive shell command (`Remove-Item`, `rd /s`, `del /q`, `rm`, `del`) appears.
- [ ] No direct DB file manipulation command appears. The document never says "copy `pos.db`" or "delete `tenants\…\pos.db`" from the operator's command line.
- [ ] Every dangerous action references the **Guarded Real Migration / Runtime Cutover / Rollback / Retention Cleanup** UI sections by name. No bypass path is described.
- [ ] Flag-on / flag-off bookends are documented for every dangerous sub-window.
- [ ] A Pilot Evidence Bundle export is required both before and after every destructive sub-window.
- [ ] An off-machine backup is required before every destructive sub-window AND verified by a second person.
- [ ] The Rollback Decision Path is explicit — both "when to roll back" and "when NOT to roll back" lists are present.
- [ ] Cleanup is explicitly stated to be storage hygiene, not pilot recovery. Cleanup is not part of the pilot recovery workflow.
- [ ] Communication templates exist for each major event (Before Pilot, Migration Completed, Cutover Completed, Rollback Triggered, Pilot Signed Off).
- [ ] No `.cs` / `.xaml` / `.csproj` files were modified by the change that introduced this document.

---

## Phase 10.19G — Backend evidence registration (optional, default OFF)

When the operator sets `operator_backend_evidence_registration_enabled="1"`
in the local global settings, the desktop will additionally send sanitized
metadata about a successfully exported Pilot Evidence Bundle to the
backend (`POST /api/v1/operator/evidence/register`) and may receive a
backend `registrationId`. This is **metadata only** — no ZIP bytes, no
JSON contents, no DB files, no backups, no raw logs.

If the backend rejects the metadata or is unreachable, the desktop adds a
`BackendEvidence:` warning to the dashboard. **The local Pilot Evidence
Bundle on disk remains the source of truth** for sign-off and audit. A
backend `registrationId`, when present, is informational — the local
bundle artifact is still the artifact reviewed by sign-off stakeholders.

Similarly, when `operator_backend_audit_intent_enabled="1"`, the desktop
records a non-blocking audit-intent declaration with the backend
immediately before each guarded dangerous operation runs. Backend
rejection / unavailability never blocks the local guarded executor in
this phase. See
[`desktop-backend-operator-permissions-integration.md`](desktop-backend-operator-permissions-integration.md)
section "Phase 10.19G" for the full contract.

---

## Phase 10.19J — Backend audit/evidence review (optional, default OFF)

When the optional `operator_backend_audit_review_ui_enabled="1"` flag is
set, the Migration Operations dashboard exposes a read-only review card
that lets the operator browse persisted operator-audit / operator-evidence
events through the backend's Phase 10.19I review API. Useful for
verifying that a particular `auditSource` / `intentId` / `registrationId`
was indeed persisted before continuing the pilot.

This is **read-only**: no event can be modified, deleted, or exported
through the card. Backend rejection, 404, or network failure is treated
as informational — the local Pilot Evidence Bundle on disk remains the
**source of truth for sign-off** unless support policy explicitly says
otherwise. Full contract:
[`desktop-backend-operator-permissions-integration.md`](desktop-backend-operator-permissions-integration.md)
section "Phase 10.19J — Read-only Audit / Evidence Review UI".

---

## Phase 10.20G — Operator Permission Admin Read-Only UI (optional, default OFF)

When `operator_permission_admin_readonly_ui_enabled="1"` is set, the
Migration Operations dashboard exposes a new read-only card that lets
the operator inspect persisted permission definitions, role grants,
user overrides, and DB-shadow effective permissions through the
Phase 10.20F backend admin API.

This is **read-only**: the card has no grant / revoke / approve /
execute / upload / save / delete / edit control. It does not toggle
any DB-authoritative flag. It does not bypass the local guarded
wrapper services. Backend rejection or unreachability produces a
`PermissionAdmin:` warning but does not crash, log out, restart, or
switch databases. The local Pilot Evidence Bundle on disk and the
local guarded wrappers remain authoritative for sign-off and pilot
execution. Full contract:
[`desktop-backend-operator-permissions-integration.md`](desktop-backend-operator-permissions-integration.md)
section "Phase 10.20G — Operator Permission Admin Read-Only UI".

---

## Phase 10.20I — Operator Permission Admin Mutation UI (optional, default OFF)

When both `operator_permission_admin_mutation_ui_enabled="1"` is set
locally AND `operator.permission.admin.mutations.enabled=true` is set
on the backend, the Migration Operations dashboard exposes a separate
permission-mutation card that can create or revoke DB permission
overrides and role grants by calling the Phase 10.20H backend API.

**This card should remain disabled during normal pilot windows.**
Permission changes during a controlled pilot are unusual; if a
change is required, it must be tracked against a separate approval
ticket and reviewed via the audit-review API afterwards. DB
permissions are still not authoritative — flipping a DB row does not
change runtime permission decisions or override the local guarded
wrappers / confirmation phrases / dangerous-operation lock.

Every mutation is audited by the backend under strict-audit policy:
a permission row never commits without its corresponding audit row.
Full contract:
[`desktop-backend-operator-permissions-integration.md`](desktop-backend-operator-permissions-integration.md)
section "Phase 10.20I — Operator Permission Admin Mutation UI".

---

## Phase 10.20J — Operator Permission Admin Limited Pilot (separate runbook)

There is now a **separate** limited-pilot runbook specifically for
the Operator Permission Administration stack (Phases 10.20A-I):

→ [`operator-permission-admin-limited-pilot-runbook.md`](./operator-permission-admin-limited-pilot-runbook.md)

This permission-admin pilot is **independent** of the tenant-DB
migration pilot documented in this file. They share the Migration
Operations dashboard, they share the desktop's flag-OFF defaults,
and they share the audit pipeline, but they do not share any
scenario. Run the permission-admin pilot on its own controlled
window; do not interleave it with the tenant-DB cutover pilot. The
permission-admin pilot does **not** execute migration, cutover,
rollback, or retention cleanup; the local guarded wrappers and
confirmation phrases remain authoritative for those operations.

The controlled rollout that follows a successful 10.20J pilot is
documented in a separate Phase 10.20K runbook:

→ [`operator-permission-admin-controlled-rollout-runbook.md`](./operator-permission-admin-controlled-rollout-runbook.md)

Like the pilot, the controlled rollout is **independent** of the
tenant-DB migration rollout. Do not interleave the two. Each has
its own waves, its own evidence folder, and its own sign-off.

A further, **independent** pilot exists for the Phase 10.21C
read-only DB-authoritative permission resolver (Phase 10.21D):

→ [`operator-permission-readonly-authoritative-pilot-runbook.md`](./operator-permission-readonly-authoritative-pilot-runbook.md)

That pilot is also independent of the tenant-DB migration pilot.
Read-only DB-authoritative pilots exercise the backend runtime
resolver path for read-only operator permission decisions when
`operator.permission.db.authoritative.readonly.enabled=true`;
they do not change dangerous-permission behaviour, they do not
execute any maintenance operation, and they preserve the desktop's
guarded wrapper stack as the only legitimate execution path for
dangerous operations.

A further, **independent** pilot exists for the Phase 10.21F
dangerous DB-authoritative permission resolver (Phase 10.21H):

→ [`operator-permission-dangerous-authoritative-pilot-runbook.md`](./operator-permission-dangerous-authoritative-pilot-runbook.md)

That pilot is also independent of the tenant-DB migration pilot,
the permission-admin pilot, and the read-only DB-authoritative
pilot. The dangerous DB-authoritative pilot exercises the backend
runtime resolver path for dangerous operator permission decisions
when `operator.permission.db.authoritative.dangerous.enabled=true`
(with `operator.permission.db.authoritative.dangerous_preflight.enabled=true`
as a hard dependency); it does not change desktop dangerous-button
behaviour, it does not execute any maintenance operation, and it
preserves the desktop's guarded wrapper stack as the only
legitimate execution path. Confirmation phrases stay desktop-local.

A further, **independent** controlled rollout runbook exists for
the DB-authoritative operator permission stack (Phase 10.21I):

→ [`operator-permission-db-authoritative-controlled-rollout-runbook.md`](./operator-permission-db-authoritative-controlled-rollout-runbook.md)

That rollout is also independent of the tenant-DB rollout, the
permission-admin rollout (Phase 10.20K), and any other workstream.
It depends on signed-off Phase 10.21D + Phase 10.21H pilot
evidence as mandatory prerequisites. The DB-authoritative rollout
exercises only backend authorisation decisions; it does NOT click
any dangerous button, does NOT change guarded wrappers, and does
NOT move confirmation phrases to the backend.

Do not interleave any of these five workstreams (tenant-DB
migration, permission-admin, read-only DB-authoritative permission,
dangerous DB-authoritative permission, DB-authoritative permission
controlled rollout); each has its own runbook, evidence folder,
and sign-off.

---

## Phase 10.22P — Lifecycle Scheduler Status (2026-06-02)

A read-only desktop monitoring card showing evidence bundle lifecycle
scheduler run history (Phase 10.22N retention archive sweeper +
Phase 10.22O expiration sweeper) has been added to
`MigrationOperationsWindow`.

When the `operator_evidence_bundle_lifecycle_scheduler_status_ui_enabled`
flag is `"1"`, the desktop calls `/api/v1/operator/evidence/bundles/
retention-sweeper/runs` and `/expiration-sweeper/runs`. The card shows
run UUID, status, trigger type, dry-run flag, candidate/archived/expired
counts, and any safe error code.

This monitoring card does **not** hard-delete bundles, does **not**
delete storage objects, and does **not** trigger any local dangerous
operation. It is safe to enable alongside active pilot evidence collection.

If `operator_evidence_bundle_lifecycle_scheduler_manual_run_ui_enabled`
is also `"1"`, operators may submit `POST /run-once` requests directly
from the desktop. Each manual run defaults to `dryRun=true` unless
explicitly unchecked.

Full specification: [`evidence-bundle-lifecycle-scheduler-status-ui.md`](evidence-bundle-lifecycle-scheduler-status-ui.md)
