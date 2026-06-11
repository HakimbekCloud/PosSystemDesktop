# Operator Tenant DB Migration Runbook

> Canonical procedure for the tenant DB lifecycle in PosSystem (the WPF
> desktop POS).
> This document complements [`tenant-db-rollback.md`](tenant-db-rollback.md);
> that file is the rollback-specific deep-dive, this one is the full
> Migration → Cutover → (optional) Rollback flow.
> For storage / retention cleanup of old logs and archived artifacts see
> [`operator-retention-cleanup-runbook.md`](operator-retention-cleanup-runbook.md).
> For a single controlled production pilot rollout (the recommended way to
> run this procedure on a real store for the first time) see
> [`operator-controlled-production-pilot-runbook.md`](operator-controlled-production-pilot-runbook.md).

---

## Scope

This runbook covers the production-safe sequence for migrating a shipping
PosSystem install from the legacy shared `pos.db` to per-tenant DBs.

It applies to:

- Real migration of legacy data into per-tenant DBs.
- Enabling tenant DB runtime mode after a successful migration.
- Rolling back to legacy mode if the post-cutover state is unsafe.
- Producing and preserving the audit trail every regulator/support engineer
  will want to see afterwards.

It assumes the operator has direct, physical or remote access to the machine
running PosSystem.exe and read/write permission on
`%LocalAppData%\PosSystem\`.

## Non-Goals

This runbook does **not** cover:

- Cleanup/retention deletion of archived tenant directories, broken legacy DB
  files, or stale logs. No cleanup executor exists yet; the dashboard's
  Retention Preview is observational only.
- Tenant onboarding, multi-tenant deployment from scratch, or sales business
  logic.
- Backend (Ham-Pos) changes — this is a desktop-client operation.
- Automated migration / cutover / rollback. Every dangerous action requires an
  explicit operator click through the dashboard, plus a confirmation phrase
  from your internal runbook (not this document).

## Required Access / Flags

All flags live in
`%LocalAppData%\PosSystem\global_settings.json`. They default to **missing
or `"0"` (disabled)** unless an authorized operator intentionally turns them
on. Do not enable them casually. CASHIER role accounts **must not** be used to
execute any operator action covered here — the dashboard's role gate rejects
them.

| Flag | Purpose | Safe default |
|---|---|---|
| `operator_migration_dashboard_enabled` | Opens the Migration Operations dashboard at all. | Disabled |
| `operator_diagnostics_ui_enabled` | Opens the Operator Diagnostics window. | Disabled |
| `operator_real_migration_ui_enabled` | Enables the "Execute Real Migration" section inside the dashboard. | Disabled |
| `operator_runtime_cutover_ui_enabled` | Enables the "Execute Runtime Cutover" section. | Disabled |
| `operator_rollback_ui_enabled` | Enables the "Execute Rollback" section. | Disabled |
| `shared_to_tenant_migration_enabled` | Server-side gate for real migration. The real migrator refuses to run unless this is `"1"`. | Disabled |
| `tenant_db_runtime_enabled` | Tells the app to open the tenant DB on startup. Flipped to `"1"` by Runtime Cutover; flipped back to `"0"` by Rollback. **Never set this manually.** | `"0"` |
| `shared_to_tenant_migrated_at` | Marker stamp written by the real migrator on success. Its presence is what unlocks Cutover and what makes Rollback meaningful. **Never set this manually.** | absent until real migration completes |

Allowed roles for operator actions: `ADMIN`, `GLOBAL_ADMIN`, `SUPER_ADMIN`,
`SUPPORT`, `OWNER`. `CASHIER` is rejected. The role check skip-grandfathering
is itself gated by `operator_access_allow_missing_role` — also default
disabled.

## Required Exports

Every dangerous operation requires the operator to have recently produced —
and reviewed — these JSON exports. The wrapper services reject paths that
don't live under the expected directory or that are older than **7 days**.

| Directory | Producer | Reviewed by |
|---|---|---|
| `%LocalAppData%\PosSystem\logs\diagnostics\` | Dashboard → Export Diagnostics JSON | Optional but recommended before every dangerous action. |
| `%LocalAppData%\PosSystem\logs\preflight\` | Dashboard → Export Preflight Report | **Required input** for Execute Real Migration, Execute Runtime Cutover, Execute Rollback. |
| `%LocalAppData%\PosSystem\logs\inventory\` | Dashboard → Export Inventory Report | **Required input** for Execute Real Migration, Execute Runtime Cutover, Execute Rollback. |
| `%LocalAppData%\PosSystem\logs\migrations\` | Real migrator (own audit) | Preserve for post-mortem. |
| `%LocalAppData%\PosSystem\logs\migration-executor\` | Guarded migration wrapper | Preserve for post-mortem. |
| `%LocalAppData%\PosSystem\logs\runtime-cutover\` | Guarded runtime cutover wrapper | Preserve for post-mortem. |
| `%LocalAppData%\PosSystem\logs\rollback-executor\` | Guarded rollback wrapper | Preserve for post-mortem. |
| `%LocalAppData%\PosSystem\logs\rollbacks\` | Inner rollback executor (own audit) | Preserve for post-mortem. |

Refresh each export shortly before the dangerous step. A 5-day-old preflight
bundle is still accepted by the guards, but a real-world emergency rarely
benefits from stale context.

## Required Backups

Before **any** real migration, runtime cutover, or rollback, the operator
**must** capture an off-machine backup of the entire directory:

```
%LocalAppData%\PosSystem\
```

Acceptable forms:

- Copy the folder to an external drive (USB / portable SSD).
- Copy the folder to a secure server folder over the LAN.
- Zip the folder and store the archive in a different location than the
  PosSystem machine (e.g. corporate file share, S3, OneDrive).

What to capture: the full `PosSystem` folder, including `pos.db`, `tenants\`,
`backups\`, `logs\`, and `global_settings.json`. Don't selectively skip files —
the audit trail and per-tenant DBs are part of the safety net.

What **not** to do: do not run any cleanup/delete/wipe script on production
data. There is no script in this runbook that mutates production data; if you
find one, it isn't part of this document.

The Execute Real Migration, Runtime Cutover, and Rollback dialogs in the
dashboard each require ticking an "I have captured an off-machine backup"
checkbox. The free-form note field next to it is for your reference (`"backup
to fileshare X, dated YYYY-MM-DD HH:MM"` is a good shape).

## Pre-Migration Checklist

Before touching the Execute Real Migration button:

- [ ] All pending offline sales synced (`PendingSalesCount = 0`).
- [ ] No poison sales (`PoisonSalesCount = 0`).
- [ ] `shared_to_tenant_migration_enabled = "1"`.
- [ ] `tenant_db_runtime_enabled` is **not** `"1"`. (Migration must precede
      cutover.)
- [ ] `shared_to_tenant_migrated_at` is empty/absent. (Migration must not
      have already run.)
- [ ] Path provider currently in legacy mode (`IsTenantScoped` false).
      Restart the app in legacy mode first if not.
- [ ] CASHIER not logged in. Use ADMIN/SUPPORT/OWNER/SUPER_ADMIN/GLOBAL_ADMIN.
- [ ] Off-machine backup of `%LocalAppData%\PosSystem\` captured **and
      verified**.
- [ ] Fresh preflight bundle (`logs\preflight\`).
- [ ] Fresh inventory bundle (`logs\inventory\`).
- [ ] Migration Dry-Run side-effect check **Passed**.
- [ ] Rollback Dry-Run side-effect check **Passed**.

## Migration Dry-Run Preview

The dashboard's "Preview Migration Dry-Run" button safely simulates what the
real migration would do. It writes nothing to tenant DBs, backups, or
settings. It also runs a filesystem side-effect guard that verifies, before
and after, that the watched paths are byte-identical.

When the operator clicks Preview Migration Dry-Run:

1. The migration auditor walks the legacy DB and proposes a per-tenant plan.
2. The migrator runs through its plan in dry-run mode (no writes).
3. The side-effect guard snapshots `pos.db`, `global_settings.json`,
   `tenants\`, `backups\`, `logs\migrations\`, `logs\rollbacks\` before/after.
4. Any unexpected change is surfaced as a red "side-effect difference" line.

A clean dry-run with `side-effects=Passed` and no errors is a prerequisite for
attempting real migration.

## Preflight Export

Click "Export Preflight Report". The dashboard writes a single redacted JSON
file under `logs\preflight\` containing:

- Operator diagnostics report (active DB paths, runtime flags, sales counts,
  cache counts, warnings).
- Migration audit (per-tenant plan, untagged source rows, observations).
- Migration verification (per-tenant counts, AllVerified flag).
- Cutover readiness (status, blocking checks, warnings).
- Rollback readiness (status, legacy DB checks, backup discovery).
- Migration dry-run preview (outcome + side-effect verdict).
- Rollback dry-run preview (outcome + planned steps + side-effect verdict).
- Top-level `OverallStatus`: `Passed` / `PassedWithWarnings` / `Failed`.

Review the file before the dangerous step. Attach a copy to your change
ticket. Confirmation phrases (`EXECUTE_REAL_TENANT_DB_MIGRATION`,
`ENABLE_TENANT_DB_RUNTIME_MODE`, `EXECUTE_TENANT_DB_RUNTIME_ROLLBACK`,
`ROLLBACK_TO_LEGACY_POS_DB`) never appear raw in this file — they are
scrubbed to `<redacted-confirmation-phrase>`. JWTs, refresh tokens, and DPAPI
blobs are scrubbed by the redactor.

## Inventory / Retention Export

Click "Refresh Inventory" first to load the current filesystem snapshot, then
"Export Inventory Report". The dashboard writes a single redacted JSON file
under `logs\inventory\` containing:

- Full per-tenant DB list with sizes and mtimes.
- All backup files with sizes and mtimes.
- All archived tenant directories (`tenants.before-rollback-*`).
- All broken legacy DB files (`pos.db.broken-*`).
- Log directory summaries (file counts, total sizes).
- Embedded Retention Preview classifying each artifact as candidate-for-future-cleanup
  or protected.
- Total known storage size.

Review and attach to the same ticket. No cleanup executor exists yet — the
retention classification is informational only.

## Real Migration Gate

Before attempting real migration, click "Check Real Migration Gate". The
dashboard re-evaluates the same signals the wrapper will check, plus
"no recent preflight/inventory export" warnings.

The verdict is one of:

- **Allowed** — no blockers, no warnings. Migration is safe to attempt.
- **AllowedWithWarnings** — no blockers, but at least one warning. Read each
  warning carefully before deciding to proceed.
- **Blocked** — at least one hard blocker. Migration must not be attempted
  until every blocker is resolved.

The gate is informational only; nothing in the codebase decides to call the
migrator based on this verdict. It exists for the operator.

## Execute Real Migration

Only after every Pre-Migration Checklist item is satisfied and the Gate
verdict is Allowed or AllowedWithWarnings (with conscious approval):

1. In the dashboard, scroll to "Guarded Real Migration Execution".
2. Paste the path to your reviewed preflight JSON.
3. Paste the path to your reviewed inventory JSON.
4. Tick "I have captured an off-machine backup of %LocalAppData%\PosSystem.".
5. Optionally type a note describing where the backup is.
6. If the gate said AllowedWithWarnings AND you accept the warnings, tick
   "Allow gate warnings".
7. Type the **migration confirmation phrase** from your internal runbook
   (it is **not** displayed in this document or in the dashboard).
8. Click "Execute Real Migration".
9. Wait for the result panel to populate.

The wrapper invokes `SharedToTenantDatabaseMigrator.MigrateAsync` exactly
once. The migrator writes per-tenant DBs under `tenants\<sub>\pos.db`,
creates a backup under `backups\`, and stamps
`shared_to_tenant_migrated_at`.

**Real migration does NOT enable runtime tenant DB mode.** It does NOT switch
the current DB session. The next time you click anywhere in PosSystem (sale,
sync, login), you are still talking to legacy `pos.db`.

## Post-Migration Verification

Immediately after Real Migration returns Success:

1. Click Refresh on the dashboard.
2. Confirm the "Verification" card shows `AllVerified=true`, the expected
   tenant count, and no unverified tenants.
3. Confirm the "Migration Audit" card now shows the migrated state.
4. Confirm the migrator's audit log appeared under `logs\migrations\`.
5. Confirm the wrapper's audit log appeared under `logs\migration-executor\`.
6. Click "Check Real Migration Gate" again — it should now indicate the
   migration marker is present and runtime cutover can be considered.
7. Copy / archive both new log files alongside the preflight + inventory
   bundles for your ticket.

If verification fails (AllVerified=false), **do not proceed to cutover**.
Investigate the per-tenant Issues list, restore from your external backup if
necessary, and escalate.

## Execute Runtime Cutover

Only after a successful Real Migration with `AllVerified=true`:

1. In the dashboard, scroll to "Guarded Runtime Cutover Execution".
2. Paste the path to a fresh preflight JSON (Export Preflight Report again if
   the previous one is more than a few minutes old — the cutover wrapper
   independently verifies the bundle is recent).
3. Paste the path to a fresh inventory JSON.
4. Tick the external-backup acknowledgement and fill the note.
5. If the cutover readiness check is `AllowedWithWarnings` and you accept
   them, tick "Proceed if cutover readiness is AllowedWithWarnings".
6. Type the **runtime cutover confirmation phrase** from your internal
   runbook (different from the migration phrase, not displayed here).
7. Click "Execute Runtime Cutover".
8. Wait for the result panel.

A successful cutover writes `tenant_db_runtime_enabled = "1"` plus
`tenant_db_runtime_enabled_at` and `tenant_db_runtime_enabled_by` metadata.
**No DB switch is performed.** **The current session is unchanged.**

## Restart / Re-login Procedure

This step is the operator's responsibility. The dashboard will not restart
or log you out.

1. After a successful Runtime Cutover, close any in-flight sale dialog.
2. From File menu, choose Logout. Wait for the login screen.
3. Close PosSystem.exe completely.
4. Reopen PosSystem.exe.
5. Wait for the splash / login screen.
6. Log in again.

At startup, the existing `MaybeSwitchToTenantDbAtStartup` path reads
`tenant_db_runtime_enabled` and `last_tenant_subdomain`, consults
`TenantCutoverReadinessGate`, and only then flips the path provider to point
at the tenant DB.

## Post-Cutover Validation

After re-login, validate the post-cutover runtime:

1. Open Operator Diagnostics. Confirm `IsTenantScoped = true` and the active
   DB path is `%LocalAppData%\PosSystem\tenants\<sub>\pos.db`.
2. Open the POS view and ring up a low-value test sale (e.g. a single low-
   priced item) on a real terminal. Confirm sync to the backend succeeds.
3. Open the products list. Confirm at least one product loads (data was
   copied during migration).
4. Open the customers list. Confirm at least one customer loads.
5. Confirm the offline retry/poison panel is empty.
6. Confirm sync watermark settings (`last_product_sync_at`,
   `last_customer_sync_at`) update on the tenant DB.
7. Export Diagnostics JSON again and archive it as the post-cutover baseline.

If anything in this validation fails, weigh Rollback against an in-place fix.
A failed validation that is recoverable (e.g. one tenant's stale cache) does
not necessarily warrant rollback.

## Rollback Decision Criteria

Rollback is **destructive** and should be considered only when:

- Post-cutover validation fails irrecoverably.
- The tenant DB cannot be opened or fails read-only probes consistently.
- Startup tenant DB switch fails repeatedly and the operator decides to
  return the install to legacy mode while the underlying defect is
  investigated off-line.
- A critical production blocker (data integrity, hardware, fiscal) exists
  after cutover and there is no fast in-place fix.

Rollback is **not** appropriate for:

- Cosmetic UI issues.
- One-off failed sync attempts.
- A single transient error.
- A defect that can be fixed by re-running sync, restarting the app, or
  redeploying a hotfix.

If in doubt, do not roll back. Capture an additional external backup and
escalate.

## Execute Rollback

When the decision to roll back is unambiguous and operator-acknowledged:

1. Stop accepting new sales. Coordinate with the cashier(s).
2. If possible, capture a final pre-rollback Export Diagnostics / Preflight
   / Inventory bundle.
3. Capture a fresh off-machine backup of `%LocalAppData%\PosSystem\`. This
   is required by the wrapper but also gives you a clean post-cutover
   snapshot independent of the impending rollback action.
4. Restart PosSystem.exe so it boots in legacy mode if possible (the
   underlying executor refuses if the provider is currently tenant-scoped).
   Use the login screen's tenant prompt to leave runtime mode if needed —
   the rollback wrapper's Guard 5 will reject otherwise.
5. Open the Migration Operations dashboard.
6. Scroll to "Guarded Rollback Execution".
7. Paste the path to your reviewed preflight JSON.
8. Paste the path to your reviewed inventory JSON.
9. Tick the external-backup acknowledgement and fill the note.
10. Confirm "Archive tenants directory" is checked. (Required for safe
    rollback — the inner executor renames `tenants\` to
    `tenants.before-rollback-<utc>\` instead of deleting it.)
11. Confirm "Disable runtime flag" is checked. (Required for safe rollback —
    sets `tenant_db_runtime_enabled` to `"0"`.)
12. Only tick "Restore legacy pos.db from most recent backup" if the legacy
    `pos.db` is missing or unreadable AND a backup file exists under
    `backups\`.
13. If readiness reports `ReadyWithWarnings` and you accept them, tick
    "Allow readiness warnings".
14. Type the **rollback confirmation phrase** from your internal runbook
    (different from migration and cutover phrases, not displayed here).
15. Click "Execute Rollback".
16. Wait for the result panel.

The wrapper invokes `TenantDbRollbackExecutor.ExecuteAsync` exactly once. The
inner executor:

- Optionally restores `pos.db` from the most recent
  `pos.db.backup-*.legacy` file if the legacy DB is missing/unreadable AND
  the option is set. The broken legacy file is renamed `pos.db.broken-<utc>`
  rather than deleted.
- Renames `tenants\` to `tenants.before-rollback-<utc>\` — preserves all
  per-tenant DBs.
- Sets `tenant_db_runtime_enabled = "0"`.
- Writes its own audit log under `logs\rollbacks\`.

**Rollback does NOT switch the current DB session.** **The current session
is not logged out.** **The app is not restarted.**

## Post-Rollback Restart / Re-login Procedure

This step is the operator's responsibility:

1. Close any in-flight sale dialog.
2. Log out from the POS.
3. Close PosSystem.exe completely.
4. Reopen PosSystem.exe.
5. Log in.

At startup `MaybeSwitchToTenantDbAtStartup` reads
`tenant_db_runtime_enabled` — now `"0"` — and stays in legacy mode. The path
provider opens the legacy `pos.db`.

## Post-Rollback Validation

After re-login on the legacy DB:

1. Open Operator Diagnostics. Confirm `IsTenantScoped = false` and the
   active DB path is `%LocalAppData%\PosSystem\pos.db`.
2. Confirm `tenant_db_runtime_enabled = "0"` in `global_settings.json`.
3. Confirm the rename happened: `tenants\` is gone (or has fewer entries)
   and a sibling `tenants.before-rollback-<utc>\` now holds the previous
   per-tenant DBs.
4. Ring up a single low-priced test sale on the legacy DB. Confirm it syncs.
5. Confirm products / customers / cache load from the legacy DB.
6. Export a post-rollback Diagnostics JSON for the ticket.
7. **Do not delete** the archived `tenants.before-rollback-<utc>\` directory.
   Keep it for at least 30 days. There is no cleanup executor yet anyway —
   manual deletion is the only option and it is not part of this runbook.
8. Preserve every audit log file from this rollback episode.

## Dangerous Operation Lock

The dashboard enforces that **Execute Real Migration**, **Execute Runtime
Cutover**, and **Execute Rollback** cannot run concurrently:

- A single global lock disables all three buttons whenever any one is in
  flight.
- The lock is visible at the top of the dashboard as a "Dangerous Operation
  Lock" card with three rows: a boolean `running` indicator, the operation
  name, and a status message.
- Double-clicks on any of the three buttons are ignored — the wrapper helper
  short-circuits the second invocation.
- Read-only actions (Refresh, exports, previews, gate check, inventory) are
  **not** locked by this mechanism. They remain available throughout. They
  use the dashboard's existing `IsLoading` flag if they need their own
  per-action gate.

## Audit Logs

Preserve every JSON file under these directories for at least 90 days after a
production migration / cutover / rollback:

```
%LocalAppData%\PosSystem\logs\preflight\*.json
%LocalAppData%\PosSystem\logs\inventory\*.json
%LocalAppData%\PosSystem\logs\diagnostics\*.json
%LocalAppData%\PosSystem\logs\migrations\*.json
%LocalAppData%\PosSystem\logs\migration-executor\*.json
%LocalAppData%\PosSystem\logs\runtime-cutover\*.json
%LocalAppData%\PosSystem\logs\rollback-executor\*.json
%LocalAppData%\PosSystem\logs\rollbacks\*.json
```

What you can expect inside:

- **Confirmation phrases are redacted.** Any occurrence of
  `EXECUTE_REAL_TENANT_DB_MIGRATION`, `ENABLE_TENANT_DB_RUNTIME_MODE`,
  `EXECUTE_TENANT_DB_RUNTIME_ROLLBACK`, `ROLLBACK_TO_LEGACY_POS_DB`, or the
  deprecated `I UNDERSTAND TENANT DB ROLLBACK` is replaced with the literal
  sentinel `<redacted-confirmation-phrase>`. If you find a raw phrase, file a
  defect — that's a bug, not normal behaviour.
- **Tokens are redacted.** Any JWT-shaped value is replaced with
  `<redacted-jwt>`. Any DPAPI `enc:v1:…` blob with `<redacted-encrypted>`.
  Any sensitive-named JSON property is replaced with `<redacted-by-key>`.
- **Phrase booleans are present.** `ConfirmationPhraseProvided` and
  `ConfirmationPhraseAccepted` are the only confirmation-phrase artifacts
  that escape into logs.

Investigating a production problem? Start from the preflight bundle, the
matching executor wrapper log, and the inner executor log. The wrapper log
captures the operator-facing decisions; the inner log captures the actual
mutation result.

## Common Failure Scenarios

| Scenario | Symptom | Safe response |
|---|---|---|
| Pending sales exist (`PendingSalesCount > 0`) | Gate / wrapper rejects with `"N pending sales exist."`. | Run sync until pending count = 0. Do not proceed until then. |
| Poison sales exist (`PoisonSalesCount > 0`) | Gate / wrapper rejects with `"N poison sales exist."`. | Open the POS Failed Sales panel. Either resolve / requeue them or accept that they will be lost on rollback and document that decision. Do not proceed silently. |
| Migration feature flag disabled (`shared_to_tenant_migration_enabled != "1"`) | Wrapper rejects with `"Migration feature flag is off …"`. | Decide whether to enable the flag. If yes, set it in `global_settings.json` and document the change. If no, postpone migration. |
| Preflight export missing or older than 7 days | Wrapper rejects with `"Preflight export: file is older than 7 days"` or similar. | Click Export Preflight Report. Pass the new path. |
| Inventory export missing or older than 7 days | Same shape, inventory channel. | Click Export Inventory Report. Pass the new path. |
| Runtime flag already enabled (`tenant_db_runtime_enabled = "1"`) before migration | Gate / wrapper rejects migration with `"…runtime tenant DB mode is already enabled…"`. | This means migration was attempted out of order. **Do not** clear the flag manually. Investigate the history (settings, audit logs) and escalate. |
| Provider already tenant-scoped (current app session running on tenant DB) | Wrappers reject with `"…path provider is already tenant-scoped…"`. | Restart the app so it boots in legacy mode (clear `tenant_db_runtime_enabled` only if you are also rolling back). |
| Migration marker missing during cutover (`shared_to_tenant_migrated_at` empty) | Cutover wrapper rejects with `"…migration marker is missing…"`. | Real migration has not completed. Run migration first. |
| Verifier fails (`AllVerified=false`) | Cutover wrapper rejects. | Do not proceed to cutover. Investigate per-tenant Issues. Consider restoring from external backup. Escalate. |
| Cutover readiness Blocked | Cutover wrapper rejects with the underlying reason. | Read the readiness card's warnings. Fix the underlying condition (missing tenant DB, schema drift, etc.) before retrying. |
| Rollback readiness Blocked | Rollback wrapper rejects with the underlying reason. | Often this means legacy `pos.db` is missing AND no backup exists. Restore manually from external backup before retrying. Do not proceed. |
| CASHIER role tries to execute operator action | Section status reads `"Disabled by … or role check."`. | Sign in as ADMIN / SUPPORT / OWNER / SUPER_ADMIN / GLOBAL_ADMIN. Do not enable `operator_access_allow_missing_role` casually. |
| App restarted too early after cutover | The current session was on legacy DB; restart-before-cutover-finishes is impossible because cutover is synchronous and short. The risk is restarting **before** sync finishes prior to cutover. | Always wait for the cutover dashboard result. Wait for any final sync. Only then close the app. |
| App not restarted after cutover/rollback | The runtime flag is on disk but the live session is still on the old DB. | Close PosSystem.exe and reopen. `MaybeSwitchToTenantDbAtStartup` will pick up the new flag state. |
| Operator forgot external backup | The dashboard's checkbox is unticked → Execute button stays disabled. | Capture the off-machine backup. Verify it is complete and readable. Then tick the checkbox. Do not check the box without an actual backup. |

## Manual Test Checklist

When validating a complete production rehearsal:

- [ ] Dashboard opens for ADMIN/SUPPORT/OWNER; rejected for CASHIER.
- [ ] All flags default to disabled on a fresh install.
- [ ] Each dangerous section is disabled until its specific flag is set.
- [ ] Migration Dry-Run side-effect check passes against an untouched
      install.
- [ ] Rollback Dry-Run side-effect check passes against the same install.
- [ ] Preflight + inventory exports produce valid, redacted JSON files.
- [ ] Real Migration with valid guards produces `Success` + a per-tenant DB
      under `tenants\<sub>\pos.db` + a migration audit log.
- [ ] Verifier shows `AllVerified=true` post-migration.
- [ ] Runtime Cutover writes `tenant_db_runtime_enabled = "1"` without
      switching the current session.
- [ ] After app restart, the path provider is tenant-scoped and the active
      DB path points at the tenant DB.
- [ ] Rollback renames `tenants\` to `tenants.before-rollback-<utc>\` and
      writes `tenant_db_runtime_enabled = "0"`.
- [ ] After post-rollback restart, the path provider is legacy and the
      active DB path is `%LocalAppData%\PosSystem\pos.db`.
- [ ] All audit logs are redacted (no raw confirmation phrases, no raw
      tokens / JWTs / DPAPI blobs).
- [ ] No automatic DB switch / logout / restart occurs at any stage.
- [ ] Dangerous Operation Lock prevents concurrent execution and double-
      clicks.
- [ ] Normal POS sales flow continues to work between operator actions.

## Emergency Notes

If something has gone wrong mid-operation and the dashboard is unresponsive
or the app is in an unknown state:

1. **Stop sales.** Do not enter new transactions on a machine in an
   unrecoverable state.
2. **Capture state.** Copy `%LocalAppData%\PosSystem\` to an off-machine
   location before doing anything else. This preserves all audit logs and
   the current DB state.
3. **Do not run any cleanup / delete script.** There is no cleanup executor
   yet. Anything that deletes files is a manual decision and must be
   logged.
4. **Do not manually flip `tenant_db_runtime_enabled`** or
   `shared_to_tenant_migrated_at`. These are written only by the wrapper
   services and reading them out of sequence will mislead diagnostics.
5. **Restart the app.** A clean restart often re-enters legacy mode if the
   flag is `"0"` or a tenant-mode boot if the flag is `"1"`. The startup
   path consults the cutover readiness gate before flipping the provider.
6. **Open the Migration Operations dashboard** and run Refresh → Export
   Diagnostics JSON → Export Preflight Report → Export Inventory Report.
7. **Preserve every export and every existing audit log.** They are the
   primary input for any post-mortem.
8. **Escalate.** Contact the support / engineering team with the captured
   bundle.
