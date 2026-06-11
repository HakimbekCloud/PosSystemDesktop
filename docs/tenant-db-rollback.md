# Tenant DB Cutover — Manual Rollback Runbook

This is the canonical runbook for reverting the WPF POS client from
**runtime tenant DB mode** (`%LocalAppData%\PosSystem\tenants\<sub>\pos.db`)
back to **legacy shared mode** (`%LocalAppData%\PosSystem\pos.db`).

Rollback is **fully manual**. The app ships a read-only
`TenantDbRollbackReadinessChecker` service that classifies the current state
into one of four statuses and produces a parameterized, deterministic step
list, but it never modifies any file, never flips a flag, and never deletes
any directory.

> **Do not perform any of these steps while `PosSystem.exe` is running.**
> SQLite files held by the running app cannot be safely renamed or replaced.

## When rollback is needed

Typical triggers:

- A tenant DB is corrupted or inaccessible and the operator wants the app
  to fall back to legacy `pos.db` until a clean re-migration can be
  scheduled.
- A regression in Phase 10.5 / 10.5C runtime behavior needs to be unwound
  while a fix is prepared.
- The cutover was performed on the wrong machine or per-tenant data
  isolation is no longer required.

Rollback preserves legacy `pos.db` exactly as it was at the moment of
migration (Phase 10.4B keeps the file in place and also writes a
SHA-256-verified copy under `backups/`). It does **not** automatically
recover sales / sync watermarks / retry-poison state that the app wrote
into a tenant DB after the cutover; those rows live in
`tenants/<sub>/pos.db` and must be recovered manually if needed (see the
"Recovering unsynced sales" section below).

## Pre-rollback checklist

1. **Confirm the operator has access to the machine that hosts the install.**
   The DB files live under each Windows user's `LocalAppData`; DPAPI tokens
   are also bound to that user (Phase 9). Rollback must run as the same
   Windows user that originally installed the app.

2. **Run the read-only readiness check.** Resolve
   `TenantDbRollbackReadinessChecker` from DI (debugger / future operator
   UI) and call `Check()`. Inspect:
   - `Status` (one of `NotInTenantRuntimeMode`, `Ready`,
     `ReadyWithWarnings`, `Blocked`)
   - `CanRollback`
   - `LegacyDbExists`, `LegacyDbReadable`, `LegacyDbSizeBytes`
   - `TenantDbCount` + `TenantDbs`
   - `LegacyBackupCount` + `MostRecentBackupPath`
   - `RuntimeFlagEnabled`, `GlobalMigrationMarkerPresent`,
     `ProviderInTenantMode`
   - `Warnings` + `RecommendedSteps`

3. **Capture in-flight evidence** before changing anything:
   - The Phase 10.4C audit log directory:
     `%LocalAppData%\PosSystem\logs\migrations\`
   - `global_settings.json` (note current values of
     `tenant_db_runtime_enabled`, `shared_to_tenant_migrated_at`,
     `last_tenant_subdomain`)
   - SQLite Browser screenshots of any tenant DB with `Synced = 0` sales;
     these will not be auto-migrated back. Recovery is manual.

## Status decision tree

| Status | `CanRollback` | What it means |
|---|---|---|
| `NotInTenantRuntimeMode` | `false` | Runtime mode is already off. Nothing to roll back. |
| `Ready` | `true` | Legacy `pos.db` is readable; no warnings. Follow the recommended steps. |
| `ReadyWithWarnings` | `true` | Legacy `pos.db` is readable, but the checker has observations the operator should read first (provider tenant-scoped, no backups, etc.). Follow the recommended steps after acknowledging the warnings. |
| `Blocked` | `false` | Legacy `pos.db` is missing/unreadable **and** no backup file exists. Stop. Recover `pos.db` from external storage before continuing. |

## Step-by-step manual rollback (Ready / ReadyWithWarnings)

Use the parameterized `RecommendedSteps` from the checker to get the exact
paths on the current machine. The general shape:

1. **Close `PosSystem.exe`.** Verify in Task Manager that no `PosSystem.exe`
   process remains. If the app runs as a Windows service / scheduled task,
   stop that as well.

2. **Backup the entire `%LocalAppData%\PosSystem` directory.** Copy it to a
   sibling like `PosSystem.before-rollback-<utc>`. This single archive
   captures pos.db + tenants\\ + backups\\ + global_settings.json + logs\\ —
   the operator can revert the entire rollback by restoring it.

3. **Ensure legacy `pos.db` is present and readable.**
   - If the checker reports `LegacyDbExists = true` and
     `LegacyDbReadable = true` → no restore needed.
   - Otherwise, copy
     `%LocalAppData%\PosSystem\backups\pos.db.backup-<stamp>.legacy` over
     `%LocalAppData%\PosSystem\pos.db`. Verify the file opens in SQLite
     Browser. If integrity is in doubt, compare its SHA-256 against the
     `BackupSha256` value recorded in
     `…\logs\migrations\migration-*-Success.json`.

4. **Disable the runtime flag.** Edit
   `%LocalAppData%\PosSystem\global_settings.json` and set
   `tenant_db_runtime_enabled` to `"0"` (or remove the key). **Do NOT
   remove `shared_to_tenant_migrated_at` unless you fully understand the
   consequences** — leaving it set is harmless and keeps the migration
   marker available for diagnostic / future re-cutover use. Leave these
   keys untouched:

   ```
   api_base_url
   last_tenant_subdomain
   tablet_mode / ui_scale / receipt_printer / label_printer
   shared_to_tenant_migration_enabled
   ```

5. **Do NOT permanently delete `tenants\`.** The directory holds the
   sales / sync watermarks / retry-poison state written after cutover.
   Permanent loss is unrecoverable. Permanent deletion is **never** a
   recommended step in this runbook.

6. **(Optional) Rename `tenants\` to `tenants.before-rollback-<utc>\`.**
   This prevents accidental tenant-runtime reuse if `tenant_db_runtime_enabled`
   ever gets flipped back on by mistake. The directory remains available
   for manual recovery.

7. **Restart `PosSystem.exe`.** Watch Debug output (DebugView or VS Output
   window). You should **not** see a
   `[Phase 10.5B] Switched to tenant DB ...` line — confirming the runtime
   switch is disabled.

8. **Validate POS opens against legacy `pos.db`.**
   - Log in with the cashier's existing credentials. (Existing legacy
     tokens still decrypt under Phase 9 because the Windows user identity
     is unchanged.)
   - Confirm Products / Customers list matches the pre-migration snapshot.
     Use the audit-log row counts if you have them.
   - Confirm recent sales history is the pre-migration history. Sales
     made after cutover live in the renamed
     `tenants.before-rollback-<utc>\` directory and are not auto-recovered.

9. **Retain the archived `tenants\` directory and `PosSystem.before-rollback-<utc>\`
   backup for at least 30 days** before considering deletion. Recovery
   windows for missing sales are typically much shorter than this, but
   retention costs little.

## Recovering unsynced sales from the archived tenants directory

Sales made after cutover but before rollback live in
`tenants.before-rollback-<utc>\<sub>\pos.db.Sales`. They are not visible to
the rolled-back app. If recovery is required:

1. Open `tenants.before-rollback-<utc>\<sub>\pos.db` in SQLite Browser.
2. Identify rows with `Synced = 0` (still pending) — these never reached
   the backend and represent lost revenue if not recovered.
3. Either:
   - Re-key them into the legacy `pos.db` manually via SQL `INSERT`
     (column lists are documented in
     `Services/SharedToTenantDatabaseMigrator.cs` as the `SalesColumns` and
     `SaleItemsColumns` constants), then start the app and let sync push
     them. Phase 0.5's `TenantSubdomain` tag will route them under the
     correct tenant when login completes.
   - Or temporarily re-enable runtime tenant DB mode (reverse this
     rollback), restore the archived `tenants\` directory in place, let
     the app sync the pending rows, then proceed with rollback again.

> Manual SQL `INSERT` into `Sales` is destructive if column order or
> defaults differ from the current EF migration. Always copy `pos.db` to
> a working file before editing and verify the row count after.

## Safety rules

- **Never manually edit `auth_token` or `refresh_token` Settings rows.**
  They are DPAPI-encrypted (Phase 9). Hand-editing produces an unusable
  blob; the app will treat it as session expired on next read.
- **Never permanently delete `tenants\` as a routine step.** Rename and
  archive instead. Operators routinely discover missing sales weeks after
  cutover that need recovery.
- **Never delete or rename `pos.db` while `PosSystem.exe` is running.**
  SQLite file locks prevent atomic rename and the next start may produce
  a half-written file.
- **Never auto-execute these steps.** All operator actions must be
  human-confirmed. A future `TenantDbRollbackExecutor` will automate
  steps 4–7 with explicit confirmation, but it is not implemented today.

## Rollback executor — future work

A future `TenantDbRollbackExecutor` service would automate steps 4–7 with
operator confirmation and produce its own audit log entry. It is **not**
implemented in this phase. Until it ships, the checker's `RecommendedSteps`
is the canonical list for manual execution.

## Related services

| Service | Phase | Role in rollback |
|---|---|---|
| `TenantDbRollbackReadinessChecker` | 10.6A / 10.6A.1 | Read-only inspection + parameterized step list + status enum |
| `TenantCutoverReadinessGate` | 10.5A.1 | Inverse decision — when allowing forward cutover |
| `SharedToTenantMigrationVerifier` | 10.4C | Used by the gate; not needed during rollback |
| `SharedToTenantDatabaseMigrator` | 10.4B | Created the backups; not run during rollback |
| `MigrationAuditLogger` | 10.4C | Source of `BackupSha256` for backup integrity check |
| `TenantScopeService` | 10.3B | Switches provider during normal cutover; rollback uses the disabled flag instead |
