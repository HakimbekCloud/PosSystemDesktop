# Tenant DB Cutover Rollback — moved

The canonical runbook is now at **[tenant-db-rollback.md](./tenant-db-rollback.md)**.

This file is kept as a stable redirect so existing bookmarks / support
tickets / runbook references continue to resolve.

Please update any tooling, documentation, or links to point at the new
path. The new document covers the same procedure with:

- Explicit four-value status enum (`NotInTenantRuntimeMode`, `Ready`,
  `ReadyWithWarnings`, `Blocked`) that mirrors
  `TenantDbRollbackReadinessChecker.Status`.
- Safer manual steps — archive/rename `tenants\` instead of deleting.
- Backup full `%LocalAppData%\PosSystem` rather than just `tenants\`.
- Retention guidance (≥ 30 days for archived tenant DBs).
- Stronger guidance against hand-editing DPAPI-encrypted tokens or
  removing migration markers.
