# Operator Retention Cleanup Runbook

> Canonical procedure for safely deleting old local maintenance artifacts in
> the PosSystem WPF desktop client via the **Guarded Retention Cleanup
> Execution** UI.
>
> For tenant DB migration, cutover, and rollback see the companion runbook
> [`operator-tenant-db-migration-runbook.md`](operator-tenant-db-migration-runbook.md).
> For rollback-specific deep-dive see [`tenant-db-rollback.md`](tenant-db-rollback.md).

---

## Scope

This runbook covers the production-safe sequence for retention cleanup of
local maintenance artifacts on a machine running PosSystem.exe:

- Old diagnostics logs under `logs\diagnostics\`.
- Old preflight logs under `logs\preflight\`.
- Old migration logs under `logs\migrations\`.
- Old rollback logs under `logs\rollbacks\`.
- Old backup files under `backups\`.
- Old archived tenant directories named `tenants.before-rollback-*`.
- Old broken legacy DB files named `pos.db.broken-*`.

This procedure is **storage / maintenance hygiene only**. It is not a fix for
business data problems, sync issues, or sales corruption.

## Non-Goals

This runbook does **not** cover:

- Tenant DB migration. See `operator-tenant-db-migration-runbook.md`.
- Runtime cutover. See `operator-tenant-db-migration-runbook.md`.
- Rollback. See `tenant-db-rollback.md` and the migration runbook.
- Repair of corrupted sales, pending sales, or poison sales — cleanup is
  rejected by design when those exist.
- Deletion of arbitrary files. The cleanup UI exposes no path input that
  becomes a delete target.
- Deletion of the active legacy `pos.db`, live tenant DB files, or anything
  under the live `tenants\` directory.
- Changes to backend (Ham-Pos) data, server-side records, or audit trails.
- Changes to user permissions, roles, or auth tokens.

## Required Access / Flags

All flags live in `%LocalAppData%\PosSystem\global_settings.json`. They
default to missing/`"0"` (disabled) unless an authorized operator
intentionally turns them on. Do not enable them casually.

| Flag | Purpose | Safe default |
|---|---|---|
| `operator_migration_dashboard_enabled` | Allows opening the Migration Operations dashboard at all. | `0` / missing |
| `operator_retention_cleanup_ui_enabled` | Allows the "Guarded Retention Cleanup Execution" section inside the dashboard to be actionable. | `0` / missing |

Additional notes:

- Path: `%LocalAppData%\PosSystem\global_settings.json`. Direct file edit by
  an authorized operator on the machine in question; no in-app UI exposes
  control over these flags.
- Allowed roles for cleanup: `ADMIN`, `GLOBAL_ADMIN`, `SUPER_ADMIN`,
  `SUPPORT`, `OWNER`. The cleanup section's role check is enforced inside the
  dashboard.
- `CASHIER` accounts **must not** execute retention cleanup. The role gate
  rejects them.
- No UI exists to enable the cleanup flag — it is intentionally manual so
  that turning cleanup on requires a documented, audit-trailed operator
  action on the host.

## What Retention Cleanup Can Delete

The dashboard's seven cleanup category checkboxes each opt in to a single
preview-classified candidate set. Cleanup targets come only from the
retention preview; the UI has no path input that becomes a delete target.

| Category | Typical location | UI checkbox label |
|---|---|---|
| diagnostics logs | `logs\diagnostics\` | "Old diagnostics logs (logs\diagnostics\)." |
| preflight logs | `logs\preflight\` | "Old preflight logs (logs\preflight\)." |
| migration logs | `logs\migrations\` | "Old migration logs (logs\migrations\)." |
| rollback logs | `logs\rollbacks\` | "Old rollback logs (logs\rollbacks\)." |
| old backups | `backups\` | "Old backups (backups\). Latest backup is always protected." |
| archived tenant directories | sibling `tenants.before-rollback-*` of `tenants\` | "Archived tenant directories (tenants.before-rollback-*\)." |
| broken legacy DB files | sibling `pos.db.broken-*` of `pos.db` | "Broken legacy DB files (pos.db.broken-*)." |

Operating notes:

- Candidates come only from `TenantDatabaseRetentionPreviewService`. The
  cleanup UI does not accept arbitrary delete paths.
- Category checkboxes only opt into preview-selected candidates; turning a
  checkbox on does not enlarge the candidate set, it just allows that
  category's candidates to participate in deletion.
- All categories default to checked. Leave them all checked unless you have
  a specific reason to exclude one.

## What Retention Cleanup Must Never Delete

The following items are protected. The guarded wrapper re-checks every one
of these protections **even if** the UI or preview claims otherwise — the
wrapper does not trust the preview blindly.

- **Active legacy DB:** `%LocalAppData%\PosSystem\pos.db`.
- **Live tenants directory:** `%LocalAppData%\PosSystem\tenants\` itself.
- **Live tenant DB files:** every `%LocalAppData%\PosSystem\tenants\<tenant>\pos.db`.
- **Latest backup file:** the newest file under `backups\` by mtime.
- **Newest log files** that the preview marks Protected, in each of the four
  log categories.
- **Any path outside the small allow-list of categorised roots.** Items
  pointing anywhere other than `logs\diagnostics\`, `logs\preflight\`,
  `logs\migrations\`, `logs\rollbacks\`, `backups\`, sibling
  `tenants.before-rollback-*\` directories, or sibling `pos.db.broken-*`
  files are skipped.
- **Anything from prefix-trick folders** such as a path under `preflight_evil\`
  or `inventory_evil\`. The wrapper normalises paths via `Path.GetFullPath`,
  appends a trailing separator to expected roots, and uses
  `StartsWith(OrdinalIgnoreCase)` so a sibling folder with a similar name
  cannot match.
- **Arbitrary files selected by the operator.** There is no field in the
  cleanup UI that accepts a delete-target path. The only path field is
  `ReviewedInventoryExportPath`, which is the inventory bundle you
  reviewed — that file is never a delete target.

If any of these items somehow appears as a candidate (for example a defect
in the preview classification), **stop**. See the Common Failure Scenarios
table below.

## Required Backups

Before executing retention cleanup the operator **must** capture an
off-machine backup of:

```
%LocalAppData%\PosSystem\
```

Acceptable forms:

- Copy the folder to an external drive (USB / portable SSD).
- Copy the folder to a secure server folder over the LAN.
- Zip the folder and store the archive in a different location than the
  PosSystem machine (e.g. corporate file share, OneDrive, S3).

> Do not run cleanup if you cannot prove that an off-machine backup exists.

The cleanup UI requires the operator to tick an "I have captured an
off-machine backup of `%LocalAppData%\PosSystem`." checkbox. The free-form
note field next to it is for your reference (e.g. `"backup to fileshare X,
dated YYYY-MM-DD HH:MM"`).

This runbook does not include any destructive command or script. Backups
should be created using the operator's normal file-management tooling.

## Required Exports

Before executing retention cleanup the operator **must** have a fresh
inventory export to feed into the cleanup wrapper.

| Property | Requirement |
|---|---|
| Path | A file under `%LocalAppData%\PosSystem\logs\inventory\` |
| Extension | `.json` (case-insensitive accepted) |
| Age | `LastWriteTimeUtc` less than 7 days old |
| Source | Produced by the dashboard's "Export Inventory Report" button |
| Reviewed | Operator has actually opened and read the JSON |

Recommended additional context, captured shortly before cleanup:

- A fresh `Export Diagnostics JSON` under `logs\diagnostics\`.
- A fresh `Preview Retention Plan` populating the dashboard's preview card.

## Pre-Cleanup Checklist

Run through this list once, in order, every cleanup window:

1. Confirm normal POS sales are not actively running. Coordinate with cashier(s).
2. Confirm `PendingSalesCount = 0` in the dashboard's diagnostics card.
3. Confirm `PoisonSalesCount = 0` in the same card.
4. Confirm the latest off-machine backup exists and is readable.
5. Open the Migration Operations dashboard (`Ctrl + Shift + M` when allowed).
6. Click Refresh to reload diagnostics and gates.
7. Click Refresh Inventory.
8. Click Export Inventory Report. Note the JSON path under
   `logs\inventory\` — you will paste it in step 5 of the execute procedure.
9. Click Preview Retention Plan.
10. Review the candidate list and the protected list (see the "Preview
    Retention Plan" section below).
11. Decide which category checkboxes to leave ticked.
12. Set `operator_retention_cleanup_ui_enabled=1` in
    `global_settings.json` only for the cleanup window. (Leaving the flag
    enabled outside the window expands the attack surface unnecessarily.)
13. Execute cleanup only if every preceding item is clean.

## Preview Retention Plan

Use the dashboard's existing "Preview Retention Plan" button before
cleanup. The Preview is read-only and lists every candidate and every
protected item with their categories and reasons.

What to check in the preview before executing cleanup:

- **Candidate count** is plausible — matches roughly what you expect from the
  retention rules (default: log retention 30 / 30 / 90 / 90 days, backups
  90 days, archived tenant dirs 90 days, broken legacy DBs 90 days).
- **Candidate total size** is plausible. A sudden multi-GB total when you
  expect MBs deserves investigation.
- **Protected items list** contains the newest log in each category and
  the latest backup file.
- **Active DB protection.** Confirm `<base>\pos.db` appears in the
  Protected list, not the Candidate list.
- **Live tenants directory protection.** Confirm the live `tenants\`
  directory does NOT appear in the Candidate list. (Only sibling
  `tenants.before-rollback-*` directories may be candidates.)
- **Warnings and errors** are reviewed and accepted, or the cleanup is
  postponed until they are resolved.

> If preview looks wrong, do not execute cleanup. Export diagnostics and
> create a defect ticket.

## Execute Guarded Retention Cleanup

Only after every Pre-Cleanup Checklist item is satisfied AND the Preview
Retention Plan output is unambiguous and acceptable:

1. Ensure `operator_retention_cleanup_ui_enabled=1` in
   `global_settings.json` (set just for this window).
2. Open the Migration Operations dashboard as an authorized role (ADMIN /
   GLOBAL_ADMIN / SUPER_ADMIN / SUPPORT / OWNER).
3. Confirm the "Guarded Retention Cleanup Execution" section is enabled —
   the section status text reads `"Enabled (…)"`.
4. Paste the path to the reviewed inventory JSON into "Reviewed inventory
   export path".
5. Tick "I have captured an off-machine backup of `%LocalAppData%\PosSystem`."
6. Add an external backup note describing where the backup is (free-form
   text, e.g. `"backup to fileshare X, dated YYYY-MM-DD HH:MM"`).
7. Select the cleanup categories you want to delete. All seven default to
   checked. Untick a category to exclude its candidates from this run.
8. Type the **retention cleanup confirmation phrase** from your internal
   secure runbook (not this document).
9. Click "Execute Retention Cleanup".
10. Wait for the operation to finish — the Dangerous Operation Lock card
    at the top of the dashboard flips to `True / Retention Cleanup`
    during execution.
11. Review the **Outcome** field. Expected values: `Success`, `NoOp`,
    `Rejected`, or `Failed` (see "Common Failure Scenarios" below).
12. Review the **Deleted / Skipped / Failed counts** in the outcome panel.
13. Review the **Items list** for per-file actions and reasons.
14. Click Export Diagnostics JSON to capture the post-cleanup state.
15. Set `operator_retention_cleanup_ui_enabled` back to `0` (or remove it
    from `global_settings.json`).

> Use the confirmation phrase from your internal secure runbook. This
> document never displays the phrase.

## Post-Cleanup Validation

After the cleanup outcome is recorded, validate that PosSystem is still
healthy:

- POS app still opens normally on the machine in question.
- Products list loads.
- Customers list loads.
- A normal low-priced test sale can be created end-to-end (rang up + synced
  or queued).
- `PendingSalesCount` remains 0.
- `PoisonSalesCount` remains 0.
- `%LocalAppData%\PosSystem\pos.db` still exists. (If the wrapper somehow
  deleted it, the wrapper has a defect — escalate immediately.)
- If tenant DB runtime mode is in use on this install: the live
  `%LocalAppData%\PosSystem\tenants\` directory still exists. Live tenant
  DBs still load.
- The latest backup file under `backups\` still exists.
- The newest log file in each of `logs\diagnostics\`, `logs\preflight\`,
  `logs\migrations\`, `logs\rollbacks\` still exists.
- A retention cleanup audit log appeared under `logs\retention-cleanup\`.
- Grep the new audit log for raw confirmation phrases — none should be
  present, only the `<redacted-confirmation-phrase>` sentinel.

## Audit Logs

Preserve every JSON file under these directories for at least 90 days
after a production cleanup. The cleanup-specific directory is:

```
%LocalAppData%\PosSystem\logs\retention-cleanup\
```

Related directories that may carry relevant cleanup-adjacent evidence:

```
%LocalAppData%\PosSystem\logs\diagnostics\
%LocalAppData%\PosSystem\logs\inventory\
%LocalAppData%\PosSystem\logs\preflight\
%LocalAppData%\PosSystem\logs\migrations\
%LocalAppData%\PosSystem\logs\rollbacks\
%LocalAppData%\PosSystem\logs\migration-executor\
%LocalAppData%\PosSystem\logs\runtime-cutover\
%LocalAppData%\PosSystem\logs\rollback-executor\
```

Expected redaction sentinels inside audit JSON:

| Sensitive value | Expected audit representation |
|---|---|
| any confirmation phrase (cleanup, migration, runtime cutover, rollback) | `<redacted-confirmation-phrase>` |
| any JWT-shaped value | `<redacted-jwt>` |
| any DPAPI `enc:v1:…` blob | `<redacted-encrypted>` |
| any sensitive-named JSON property | `<redacted-by-key>` |

If you find a raw confirmation phrase inside any audit JSON, **file a
defect** — that is a bug in the redactor, not normal behaviour.

## Dangerous Operation Lock

Cleanup is part of the dashboard's global dangerous-operation lock added
in Phase 10.15A. The lock visibly appears at the top of the dashboard as
the "Dangerous Operation Lock" card with a `running` boolean, an
`operation name`, and a status message.

Behaviour during a cleanup run:

- The lock flips to `running=True`, `name="Retention Cleanup"`.
- **Execute Real Migration** is disabled.
- **Execute Runtime Cutover** is disabled.
- **Execute Rollback** is disabled.
- **Execute Retention Cleanup** itself is disabled (this is the
  double-click prevention — a second click on the same button is a no-op).
- Read-only buttons (Refresh, exports, previews, Check Real Migration
  Gate, Refresh Inventory, Preview Retention Plan) follow the existing
  dashboard loading rules and are not gated by this lock.

When cleanup finishes (Success / NoOp / Failed / Rejected), the lock
clears and every Execute button re-evaluates based on its own input
conditions.

## Common Failure Scenarios

| Symptom | Likely Cause | Safe Response |
|---|---|---|
| Cleanup UI section is disabled / greyed out | `operator_retention_cleanup_ui_enabled` is missing/`"0"` OR current user role is not in allow-list | Set the flag to `"1"` for the cleanup window; sign in as an authorized role. |
| Logged-in user cannot execute cleanup despite flag = `"1"` | User role is CASHIER or empty | Sign in as ADMIN / GLOBAL_ADMIN / SUPER_ADMIN / SUPPORT / OWNER. Do not enable `operator_access_allow_missing_role` casually. |
| Wrapper rejects with `"Inventory export: file must live under …\logs\inventory\"` | Path is outside the expected directory, possibly a `inventory_evil\…` prefix-trick attempt | Use a fresh inventory export written via the dashboard's Export Inventory Report button. Do not type custom paths. |
| Wrapper rejects with `"file is older than 7 days …"` | Inventory file is stale | Click Export Inventory Report again, paste the new path. |
| Wrapper rejects with `"N pending sales exist."` | Cashier has unsynced sales | Run sync until pending count reaches 0. Reschedule the cleanup window. |
| Wrapper rejects with `"N poison sales exist."` | Sync has permanently failed sales | Open the POS Failed Sales panel. Resolve / requeue / document the disposition. Do not cleanup until poison count is 0. |
| Wrapper rejects with `"Retention cleanup requires the exact ConfirmationPhrase …"` | Typed phrase did not match | The phrase textbox has already been cleared. Re-open the secure runbook, retype carefully. The wrong typed value did not land in any log — it was discarded. |
| Outcome shows `NoOp` with `CleanupExecuted=false` and zero candidates | Retention preview found nothing to clean | This is the safe path. Nothing happened on disk. You can close the dashboard. |
| Outcome shows `NoOp` after every candidate was Skipped | All candidates were either category-disabled or hit a per-item safety rejection | Open the Items list. Read each `Skipped: Reason:` line. If the reason is "Category disabled by options.", retry with the matching checkbox ticked. If it's a safety rejection, **investigate before retrying** — the cleanup may be trying to touch a path the wrapper considers unsafe. |
| One item shows `Failed` while others Deleted | Target file was locked (antivirus, file explorer preview, another process), or permission denied | Close any external viewer / scanner on the file's directory. Retry cleanup later. Other deletions in the same run already succeeded; rerunning will pick up only the still-present candidates. |
| Latest backup file appears as a Candidate in the preview | Preview classification defect | **Stop. Do not execute cleanup. Export diagnostics and create a defect ticket.** The Phase 10.11B retention preview should always classify the newest backup as Protected. |
| Active `pos.db` appears as a Candidate in the preview | Preview classification defect | **Stop. Do not execute cleanup. Export diagnostics and create a defect ticket.** The Phase 10.16A wrapper would also reject this at the per-item safety check, but the preview itself surfacing it is a defect that must be investigated. |
| A live tenant DB (`tenants\<sub>\pos.db`) appears as a Candidate | Preview classification defect | **Stop. Do not execute cleanup. Export diagnostics and create a defect ticket.** The wrapper would also reject this at the per-item safety check, but the preview surfacing it is a defect. |
| An audit log under `logs\retention-cleanup\` contains a raw confirmation phrase | Redactor defect | **Stop. File a defect ticket.** Preserve the raw audit log offline for the defect report. Do not share the raw audit log externally until the redactor is fixed. |

## Emergency Notes

These notes assume the cleanup window has been initiated and something is
unexpected.

- If cleanup deleted an expected old log file and POS continues to work
  normally, no action is required. The cleanup did its job.
- If cleanup failed with one or more `Failed` items due to a locked file,
  close any external file viewer or scanner targeting that directory,
  then rerun cleanup later. The wrapper does not auto-retry.
- If the active `pos.db`, the live `tenants\` directory, a live tenant DB
  file, or the latest backup file is **missing** after cleanup, **stop
  POS immediately and escalate to engineering**. Preserve the entire
  `%LocalAppData%\PosSystem\` directory and every log directory off the
  machine before any further attempt to recover. The Phase 10.16A wrapper
  is designed never to delete those items; their absence after a cleanup
  run is a bug.
- Do not run migration / rollback / runtime cutover as a cleanup recovery
  step unless a senior operator is explicitly following the migration
  runbook for an unrelated reason. The migration/rollback executors are
  not failsafe substitutes for missing files.
- Keep every audit log and every external backup snapshot until the
  investigation closes — typically at least 90 days.

This document contains **no destructive commands, no restore scripts,
and no shell commands**. Backups, restores, and forensic capture are
operator-driven through normal file-management tooling.

## Manual Review Checklist

When reviewing this document or onboarding a new operator:

- [ ] Runbook exists at `docs/operator-retention-cleanup-runbook.md`.
- [ ] Required Access / Flags section documents the two flags and the
      role allow-list.
- [ ] All seven cleanup categories appear in the table.
- [ ] Protected items section lists active `pos.db`, live tenants
      directory, live tenant DBs, latest backup, newest logs, and
      prefix-trick rejection.
- [ ] Required Backups section requires off-machine backup and contains
      no destructive scripts.
- [ ] Required Exports section requires a fresh inventory `.json` under
      `logs\inventory\` less than 7 days old.
- [ ] Preview Retention Plan section explains what to check and what
      "stop and file a defect" looks like.
- [ ] Execute Guarded Retention Cleanup section is numbered, with the
      flag-on / flag-off bookends and explicit "Use the phrase from your
      internal secure runbook" reference.
- [ ] Post-Cleanup Validation section lists concrete checks including
      `pos.db` exists, live tenants directory still exists when
      applicable, latest backup still exists, newest logs still exist.
- [ ] Audit Logs section enumerates the retention-cleanup log directory,
      lists related directories, and documents redaction sentinels.
- [ ] Dangerous Operation Lock section explains the four-button mutual
      exclusion behaviour during cleanup.
- [ ] Common Failure Scenarios table includes the five "stop and file a
      defect" rows (latest backup as candidate, active DB as candidate,
      live tenant DB as candidate, audit log contains raw secret, NoOp
      due to all-skipped per-item safety).
- [ ] No raw confirmation phrase value appears in this document.
- [ ] No destructive shell command, PowerShell snippet, batch script,
      `rm`, `del`, `Remove-Item`, or `rd` appears in this document.
- [ ] No code files (.cs / .xaml / .csproj) were modified in the change
      that introduced this document.
- [ ] `dotnet build` succeeds in the project root.
