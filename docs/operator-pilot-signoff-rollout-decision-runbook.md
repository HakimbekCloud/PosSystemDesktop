# POS Tenant DB Pilot Sign-Off and Rollout Decision Runbook

> Canonical post-pilot decision document. Used **after** the controlled
> production pilot window closes to decide whether the tenant DB
> architecture is safe to expand, hold, or recover.
>
> Companion runbooks:
> - [`operator-controlled-production-pilot-runbook.md`](operator-controlled-production-pilot-runbook.md) — running the pilot itself.
> - [`operator-tenant-db-migration-runbook.md`](operator-tenant-db-migration-runbook.md) — migration / cutover / rollback lifecycle.
> - [`operator-retention-cleanup-runbook.md`](operator-retention-cleanup-runbook.md) — retention cleanup (storage hygiene, not pilot recovery).
> - [`tenant-db-rollback.md`](tenant-db-rollback.md) — rollback deep-dive.
> - [`operator-rbac-permission-model-plan.md`](operator-rbac-permission-model-plan.md) — architecture audit and migration plan for backend-supported operator permissions (documentation-only; complements but does not replace the local guards).
> - [`desktop-backend-operator-permissions-integration.md`](desktop-backend-operator-permissions-integration.md) — desktop integration of the backend permission API. As of Phase 10.19E the Pilot Readiness Report's Area I and the Pilot Evidence Bundle's `backend-permission-summary.json` carry the backend state for support review.

---

## 1. Scope

This document is used **after** the controlled production pilot window
closes (whether successfully, with warnings, or having required rollback).

It decides whether the tenant DB architecture is safe to expand from one
pilot tenant/store to additional tenants. It applies to one pilot
tenant/store result at a time. It is informed by the artifacts produced
during the controlled pilot — pilot readiness reports, pilot evidence
bundles, audit logs, and business sign-off — and does **not** replace the
actual migration / cutover / rollback / cleanup runbooks.

If you reach this document without having executed the controlled pilot
runbook first, **stop here and go back** to
[`operator-controlled-production-pilot-runbook.md`](operator-controlled-production-pilot-runbook.md).

## 2. Non-Goals

This document does **not**:

- Perform migration. See the migration runbook.
- Perform runtime cutover. See the migration runbook.
- Perform rollback. See the rollback deep-dive and the migration runbook.
- Perform retention cleanup. See the cleanup runbook.
- Repair sales / payment / data corruption. The pilot fails fast on those
  conditions; this document only documents the decision afterwards.
- Replace the RBAC allow-list (ADMIN / GLOBAL_ADMIN / SUPER_ADMIN / SUPPORT
  / OWNER).
- Expose confirmation phrase values. Phrases live in the internal secure
  runbook.
- Contain destructive shell commands. Backup / restore / file manipulation
  is operator-driven via normal tooling, not commands documented here.
- Authorize mass rollout without evidence review.

## 3. Required Pilot Completion Evidence

The reviewer cannot approve, hold, or expand the rollout without first
collecting and inspecting every artifact in the table below. Missing
evidence collapses the decision to "Hold Rollout" or "Limited Next Pilot"
at best — never "Wider Rollout".

| Artifact | Required? | Source | What reviewer checks | Store in support ticket? |
|---|---|---|---|---|
| Off-machine backup proof | Yes | Operator's external storage (drive / server / off-site zip) | Backup exists, integrity verified by a second person, captured before each destructive sub-window | Yes — link or attestation |
| Pre-pilot diagnostics export | Yes | Dashboard → Export Diagnostics JSON before migration | Pending / poison sales were 0; warnings were accepted | Yes |
| Pre-pilot preflight export | Yes | Dashboard → Export Preflight Report before migration | `OverallStatus` was Ready or ReadyWithWarnings; side-effect=Passed in dry-runs | Yes |
| Pre-pilot inventory export | Yes | Dashboard → Export Inventory Report before migration | Inventory state matched expectations; categories accounted for | Yes |
| Migration dry-run preview | Yes | Dashboard → Preview Migration Dry-Run | Side-effect check Passed; no filesystem mutation | Yes (screenshot or audit log) |
| Rollback dry-run preview | Yes | Dashboard → Preview Rollback Dry-Run | Side-effect check Passed; no filesystem mutation | Yes (screenshot or audit log) |
| Pilot readiness report (pre) | Yes | Dashboard → Generate Pilot Readiness Report before migration | Status Ready / ReadyWithWarnings | Yes — JSON under `logs\pilot-readiness\` |
| Pilot evidence bundle (pre-migration) | Yes | Dashboard → Export Pilot Evidence Bundle before migration | Bundle complete; manifest present; no DB files / backups / tokens / raw phrases | Yes — ZIP or folder |
| Real migration result / audit log | Yes | `logs\migrations\*.json` + `logs\migration-executor\*.json` | Outcome=Success; AllVerified=true; no raw phrases; redaction sentinels intact | Yes |
| Post-migration diagnostics | Yes | Dashboard → Export Diagnostics JSON after migration | Migration marker present; sales counts still 0 / 0; cache populated | Yes |
| Post-migration verification result | Yes | Dashboard Verification card / verifier JSON | `AllVerified=true`; per-tenant Verified=true; no orphan source rows | Yes |
| Runtime cutover result / audit log | Yes | `logs\runtime-cutover\*.json` | Outcome=Success; `RuntimeFlagAfter=True`; `RuntimeFlagChanged=True`; no raw phrases | Yes |
| Post-restart diagnostics (proving tenant-scoped runtime) | Yes | Dashboard → Export Diagnostics JSON after restart/re-login | `IsTenantScoped=True`; `ActiveDbPath` ends in `…\tenants\<tenant>\pos.db` | Yes |
| Post-cutover validation evidence | Yes | Validation checklist (Section 5), screenshots, ticket notes | Every mandatory row Pass | Yes |
| Test sale evidence | Yes | Screenshot or audit log of a low-value end-to-end sale | Sale completed, synced, stock decreased | Yes |
| Rollback readiness status (final) | Yes | Dashboard Readiness card / `logs\rollback-executor\` if used | Status known: Ready / ReadyWithWarnings / NotInTenantRuntimeMode / Blocked. If rollback was used, audit log attached. | Yes |
| Retention preview (informational) | Yes | Dashboard → Preview Retention Plan | Active DB / live tenant DB / latest backup / newest logs are all in Protected list | Yes |
| Pilot evidence bundle (post-cutover) | Yes | Dashboard → Export Pilot Evidence Bundle after cutover | Bundle complete; matches the pre-migration shape | Yes — ZIP |
| Incident / defect tickets | Conditional | Engineering / Security ticket tracker | Each linked from the pilot ticket with severity and owner | Yes — link |
| Store Manager / Business Owner sign-off | Yes | Signed text in pilot ticket with timestamp | Manager confirms business flow is acceptable | Yes |

## 4. Pilot Outcome Summary Template

Fill the following template at the close of the pilot window and paste it
into the pilot ticket. Every field is required.

```
Pilot tenant/store:                <store name / tenant subdomain>
Pilot date:                        <YYYY-MM-DD (local timezone)>
Pilot operator:                    <name>
Support engineer:                  <name>
Store manager / business owner:    <name>
Backend/platform engineer:         <name>
Migration outcome:                 <Success / Failed / Rejected / Not executed>
Cutover outcome:                   <Success / Failed / Rejected / Not executed>
Rollback used?:                    <Yes / No>
Cleanup used?:                     <Yes / No (default No during pilot window)>
Total downtime:                    <HH:MM (from sales-paused to sales-resumed)>
Test sale result:                  <Pass / Fail — value, payment method, sync state>
Pending sales after pilot:         <count — should be 0>
Poison sales after pilot:          <count — should be 0>
Known issues:                      <bullet list with severity>
Evidence bundle path / ticket:     <attachment id or path>
Final recommendation:              <Approve Wider / Approve Limited Next / Hold / Rollback>
Approver names:                    <Support: …; Store Manager: …>
Approver timestamps:               <Support: <UTC>; Store Manager: <UTC>>
```

## 5. Mandatory Validation Checklist

This is the **hard gate** for any "Approve Wider Rollout" decision. Each
row is PASS or FAIL. If any row is FAIL, wider rollout must **not** be
approved.

| # | Check | Result (Pass / Fail) | Evidence reference |
|---|---|---|---|
| 1 | App opens after restart | | |
| 2 | Login works as the pilot user | | |
| 3 | Active DB path is tenant-scoped after cutover (`IsTenantScoped=True`) | | |
| 4 | Products load on the POS view | | |
| 5 | Customers load | | |
| 6 | Price list loads | | |
| 7 | Sales screen works (line items, totals, taxes if applicable) | | |
| 8 | Mixed payment works (if mixed-payment flow is part of the store's usage) | | |
| 9 | Cash / Card / Debt (Qarz) flow works for the payment methods the store uses | | |
| 10 | Test sale saves locally | | |
| 11 | Sync works (sale reaches backend; queue drains) | | |
| 12 | Pending sales = 0 after sync | | |
| 13 | Poison sales = 0 | | |
| 14 | Inventory / stock view is sane (no negative drift, reconcile marker behaves) | | |
| 15 | Post-pilot Diagnostics export generated | | |
| 16 | Post-pilot Pilot Evidence Bundle generated | | |
| 17 | Audit logs contain no raw confirmation phrases (grep returns only `<redacted-confirmation-phrase>`) | | |
| 18 | Latest backup file is still present under `backups\` | | |
| 19 | Rollback readiness is known (status recorded) | | |
| 20 | Rollback was not required OR rollback completed successfully with sign-off | | |
| 21 | Store Manager confirms business flow is acceptable | | |

> **Rule:** if any mandatory validation row is **FAIL**, wider rollout
> must **not** be approved. Proceed to the Decision Matrix and select
> Hold Rollout, Rollback / Recovery Required, or the relevant defect path.

## 6. Business Validation Checklist

These rows are not absolute hard-fails by themselves, but each must be
explicitly acknowledged by the Store Manager.

- [ ] Cashier can continue normal sales without operator intervention.
- [ ] Receipt / print flow works if the store uses it.
- [ ] Customer / debt (Qarz) flow works if the store uses it.
- [ ] End-of-day close process is not broken.
- [ ] Reported stock numbers are acceptable to the Store Manager.
- [ ] No duplicate sales obvious to the cashier.
- [ ] No missing sales reported by the cashier.
- [ ] No pricing mismatch reported.
- [ ] Store Manager accepts the pilot result and provides written sign-off.

If three or more rows are unchecked or contested, the rollout decision
should be at most "Approve Limited Next Pilot" pending a follow-up
investigation.

## 7. Technical Validation Checklist

These rows are checked against the dashboard, audit logs, and evidence
bundle.

- [ ] `tenant_db_runtime_enabled = "1"` in `global_settings.json` after cutover.
- [ ] Path provider is tenant-scoped after restart (`IsTenantScoped=True`).
- [ ] Tenant DB file exists at `%LocalAppData%\PosSystem\tenants\<tenant>\pos.db`.
- [ ] Migration marker `shared_to_tenant_migrated_at` is present.
- [ ] Verifier `AllVerified=true`.
- [ ] Cutover readiness gate no longer reports `Blocked`.
- [ ] Rollback readiness is recorded and reviewed.
- [ ] `PendingSalesCount = 0`.
- [ ] `PoisonSalesCount = 0`.
- [ ] No unhandled exceptions in the dashboard's Errors card.
- [ ] Dangerous Operation Lock behaved correctly during every destructive sub-window (running flag flipped on for exactly one operation at a time, then cleared).
- [ ] No automatic logout / restart / DB switch happened during the migration command itself.
- [ ] Pilot Evidence Bundle contains only sanitized JSON files — no DB files, no backups, no raw logs, no tokens.

## 8. Evidence Review Checklist

Reviewer inspects each artifact:

- [ ] Pilot readiness report (pre and post). Both populated, status acceptable.
- [ ] Pilot evidence bundle manifest. Lists every expected JSON; no DB / backup / raw log mention.
- [ ] `diagnostics-summary.json`. Active DB path correct for the stage of the pilot. Sales counts correct.
- [ ] `migration-verification-summary.json`. `AllVerified=true`; per-tenant rows reviewed; orphans = 0.
- [ ] `runtime-cutover-readiness-summary.json`. Status was Allowed / AllowedWithWarnings at cutover time.
- [ ] `rollback-readiness-summary.json`. Recorded final state of rollback readiness.
- [ ] `retention-preview-summary.json`. Active DB / live tenant DBs / latest backup / newest logs all present in Protected list, not Candidate list.
- [ ] `logs\migrations\*.json`. Migration audit log present with Outcome=Success.
- [ ] `logs\migration-executor\*.json`. Wrapper audit log present.
- [ ] `logs\runtime-cutover\*.json`. Cutover wrapper audit log present.
- [ ] Support ticket notes contain approver names + UTC timestamps.
- [ ] Store Manager business sign-off is on file.

> **STOP rule:** if **any** artifact above is missing or inconsistent with
> the others, the reviewer must select either "Hold Rollout" or "Approve
> Limited Next Pilot" — **never** "Approve Wider Rollout".

## 9. Audit and Redaction Review

Expected redaction sentinels inside every JSON file produced by the dashboard:

| Sensitive value class | Expected sentinel in audit JSON |
|---|---|
| Any confirmation phrase (migration / cutover / rollback / cleanup) | `<redacted-confirmation-phrase>` |
| Any JWT-shaped value | `<redacted-jwt>` |
| Any DPAPI `enc:v1:…` blob | `<redacted-encrypted>` |
| Any sensitive-named JSON property | `<redacted-by-key>` |

Inspect the audit logs and the pilot evidence bundle for these sentinels.
The raw values must never appear.

> **STOP rule (security):**
>
> - If a raw confirmation phrase appears anywhere in the evidence bundle,
>   the readiness reports, the cutover/rollback audit logs, or the
>   migrator audit logs — **stop the rollout decision** and file a
>   **security defect** (severity S1).
> - If a JWT-shaped value, DPAPI blob, or raw token appears anywhere —
>   same action: **stop and file a security defect**.
> - Do **not** paste raw audit logs into public chats, public tickets, or
>   email distribution lists if they contain the sentinel-violating raw
>   values. Preserve the leaking file offline; share only via a secure
>   channel with the security team.

This document deliberately does not list the raw phrase values. They live
only in the internal secure runbook.

## 10. Decision Matrix

Use this table to pick the post-pilot decision.

| Condition | Decision | Required action |
|---|---|---|
| All mandatory technical and business checks pass; no blocker; evidence complete; Store Manager signed off | Approve Wider Rollout | Proceed to Section 11 (Approve Wider Rollout) and follow the recommended rollout pace. |
| All mandatory checks pass but minor warnings remain (cosmetic UI, isolated non-critical observation) | Approve Limited Next Pilot | Proceed to Section 12 (Approve Limited Next Pilot). Document warnings in the pilot ticket and choose the next pilot store profile. |
| Evidence missing (one or more required artifacts) | Hold Rollout | Proceed to Section 13. Open a ticket. Re-run the missing collection step. Re-review. |
| `PendingSalesCount > 0` after the pilot window | Hold Rollout | Drain pending sales. Investigate cause. Re-validate before any expansion. |
| `PoisonSalesCount > 0` after the pilot window | Hold Rollout | Open POS Failed Sales panel. Resolve or document each. No expansion until 0. |
| Migration verifier reported `AllVerified=false` post-migration | Rollback / Recovery Required | Proceed to Section 16. Decide whether rollback restores a working state. |
| Cutover failed (Outcome=Failed) | Rollback / Recovery Required | Proceed to Section 16. Coordinate Support + Store Manager approval. |
| Rollback was required AND completed successfully | Hold Rollout | Wider rollout blocked until root cause is fixed. Open Engineering Defect. |
| Rollback was required AND failed | Engineering Defect Required + Rollback / Recovery Required | Open S1 defect. Preserve all evidence offline. Escalate to engineering. |
| Store Manager rejects the business result | Hold Rollout | Document business concerns. Open Engineering Defect if rooted in product behaviour. Re-pilot only after fix. |
| Raw confirmation phrase or token / JWT / DPAPI value found in any audit log | Security Defect Required | Stop. File S1 security defect. Preserve leaking file offline. Do not share externally. |
| Unexpected DB switch / app restart / logout observed during the pilot | Engineering Defect Required | Stop. File defect. Wider rollout blocked. The dashboard's documented behaviour is no auto-switch / no auto-restart / no auto-logout — divergence is a bug. |
| Latest backup file is missing under `backups\` after cutover | Hold Rollout | Investigate. Re-capture off-machine backup. Open Engineering Defect if a wrapper inadvertently moved/deleted it. |
| Pilot evidence bundle missing or incomplete | Hold Rollout | Re-export from the dashboard. If bundle cannot be exported reliably, open Engineering Defect for the bundle service. |
| Multiple stores would be affected by the same observed issue if the pilot were repeated | Hold Rollout | Stop. The pilot is meant to surface single-store concerns first. Multi-store impact requires a fix before any expansion. |

The decision options resolve to:

- **Approve Wider Rollout** (Section 11)
- **Approve Limited Next Pilot** (Section 12)
- **Hold Rollout** (Section 13)
- **Rollback / Recovery Required** (Section 14)
- **Security Defect Required** (Section 15)
- **Engineering Defect Required** (Section 15)

## 11. Approve Wider Rollout

Strict criteria — ALL must be true:

- [ ] Every row in the Mandatory Validation Checklist (Section 5) is Pass.
- [ ] `PendingSalesCount = 0` and `PoisonSalesCount = 0` post-pilot.
- [ ] Verifier reported `AllVerified=true`.
- [ ] Tenant runtime mode works after restart (`IsTenantScoped=True`).
- [ ] No rollback was required.
- [ ] Pilot evidence bundle is complete; manifest lists every expected
      file; no DB / backup / token / raw phrase present.
- [ ] Store Manager business sign-off is complete in the ticket.
- [ ] No security redaction issue (no raw phrases / JWTs / DPAPI blobs
      anywhere in audit logs).
- [ ] No critical defect open against the pilot.

Recommended rollout pace:

1. Do **not** jump from one pilot directly to "all stores".
2. Next step is **2–3 controlled pilots** at intentionally different
   store profiles (e.g. one high-volume + one offline-heavy + one
   small-store). Each follows the controlled pilot runbook end-to-end.
3. After those pilots sign off, expand to a **small batch** (5–10
   stores), still following the controlled pilot procedure per store,
   but accepting that the operator may execute several pilots in a
   single business week.
4. After the small batch signs off, expand to a **broader rollout** —
   still on a per-store-window basis with the dashboard's Dangerous
   Operation Lock active for each store.
5. Even at broader rollout, each individual store still requires the
   pre-migration evidence bundle and the off-machine backup. The
   safeguards never relax across rollout phases.

## 12. Approve Limited Next Pilot

Use when:

- The pilot succeeded with only **minor** warnings.
- Evidence is complete.
- No data loss occurred.
- No rollback was required.
- The business flow is acceptable to the Store Manager.
- The team wants more confidence before broader rollout.

Recommendations:

- Pick one store with a **different profile** than the pilot store (e.g.
  if the pilot store was small-and-rural, choose an urban-high-volume
  store next).
- Repeat the controlled pilot runbook end-to-end.
- Compare the post-cutover evidence bundle against the first pilot's
  evidence bundle. Differences worth investigating include: candidate
  category counts, migration verifier issues, sync-time, retention
  composition.
- If the second pilot succeeds cleanly, the next decision can be Approve
  Wider Rollout.

## 13. Hold Rollout

Use when:

- Evidence is missing.
- Warnings are unclear or unresolved.
- A performance issue surfaced (e.g. sync slower than expected).
- Operator confusion arose during the pilot (training gap).
- Documentation is incomplete in a way that affected the pilot.
- A minor bug needs a fix before expanding to more stores.

Required actions:

- Create a ticket in the project tracker.
- Attach the pilot evidence bundle, all audit logs, and the Pilot
  Outcome Summary.
- Decide ownership: Engineering / Documentation / Operator-training.
- Define the retest condition (e.g. "after fix X ships, repeat the
  controlled pilot on the same store").
- Update the controlled pilot runbook if the gap is procedural.

## 14. Rollback / Recovery Decision

Use when:

- A business-critical flow is broken (cashier cannot sell, products
  unavailable, prices wrong, mixed payment fails, debt flow broken).
- Data mismatch between legacy backup and the post-cutover tenant DB.
- Sync cannot recover within an acceptable window.
- Pending / poison sales cannot be resolved.
- Tenant runtime mode is unusable on the pilot machine.
- The store cannot continue operation.

Mandatory rules:

- Rollback **must** use the **Guarded Rollback Execution** section of the
  dashboard. No direct executor call. No `tenant_db_runtime_enabled`
  manual edit. No DB file manipulation.
- A fresh off-machine backup of `%LocalAppData%\PosSystem\` must be
  captured **before** rollback runs, in addition to the pre-pilot
  backup.
- Joint Support + Store Manager approval is required. Record names and
  UTC timestamps in the pilot ticket.
- Post-rollback validation per the migration runbook's Post-Rollback
  Validation section is required.
- After rollback succeeds, the wider rollout is blocked until the root
  cause of the rollback is fixed. Re-pilot is then required.

This document does not duplicate the rollback procedure itself. See
[`tenant-db-rollback.md`](tenant-db-rollback.md) and the Execute Rollback
section of
[`operator-tenant-db-migration-runbook.md`](operator-tenant-db-migration-runbook.md).

## 15. Defect Ticket Requirements

When any decision routes to a defect ticket, file it with the following
template and severity.

```
Title:                       <one-line concrete symptom>
Severity:                    <S1 / S2 / S3 / S4>
Tenant / store:              <name / subdomain>
Pilot date/time:             <YYYY-MM-DD HH:MM local + UTC equivalent>
Operator:                    <name>
Observed issue:              <what the operator saw>
Expected result:             <what the runbook said would happen>
Actual result:               <what actually happened>
Business impact:             <can the cashier sell? data at risk? customer-facing?>
Evidence bundle path:        <ZIP attachment id or path>
Diagnostics export path:     <attachment id or path>
Relevant audit log paths:    <e.g. logs\migration-executor\…, logs\rollback-executor\…>
Screenshots if any:          <attachment ids>
Steps to reproduce:          <numbered list>
Rollback used?:              <Yes / No>
Current status:              <Open / Investigating / Mitigated / Fixed>
Owner:                       <name>
Target fix version:          <release tag>
Retest checklist:            <bullet list of what must pass to close>
```

Severity guidance:

| Severity | Examples |
|---|---|
| **S1** | Data loss; store cannot sell; rollback failed; raw confirmation phrase / token / DPAPI blob leaked to a log file; wrapper inadvertently deleted active DB or backup; security secret exposed in any artifact. |
| **S2** | Cutover failed; migration verifier reported `AllVerified=false`; sync blocker that requires engineering; pilot evidence bundle export fails reproducibly; gate verdict diverges from the wrapper's per-item check. |
| **S3** | Minor UI defect; operator-procedure ambiguity; runbook step needs clarification; readiness report row mislabelled. |
| **S4** | Cosmetic issue; typo in a label; sub-pixel layout drift. |

S1 issues stop the rollout. S2 issues hold the rollout pending fix and
re-pilot. S3/S4 issues do not block rollout but must be tracked.

## 16. Communication Templates

Use these short templates in the pilot ticket so stakeholders stay
aligned and the audit trail is searchable. None of the templates
contains a raw confirmation phrase value.

### Pilot Completed Successfully

```
PILOT COMPLETED SUCCESSFULLY
Tenant/store:                  <name>
Window:                        <start → end (local + UTC)>
Migration outcome:             Success
Cutover outcome:               Success
Rollback used?:                No
Mandatory validation:          All Pass
Pilot Evidence Bundle (final): <ticket attachment id>
Next decision (per matrix):    <Approve Wider Rollout / Approve Limited Next Pilot>
Sign-off (Support):            <name + UTC>
Sign-off (Store Manager):      <name + UTC>
```

### Pilot Completed With Warnings

```
PILOT COMPLETED WITH WARNINGS
Tenant/store:                  <name>
Window:                        <start → end>
Migration outcome:             Success
Cutover outcome:               <Success / Success with warnings>
Rollback used?:                No
Mandatory validation:          All Pass; minor warnings accepted (see below)
Accepted warnings:             - <bullet 1>
                               - <bullet 2>
Pilot Evidence Bundle (final): <ticket attachment id>
Next decision (per matrix):    Approve Limited Next Pilot
Sign-off (Support):            <name + UTC>
Sign-off (Store Manager):      <name + UTC>
```

### Wider Rollout Approved

```
WIDER ROLLOUT APPROVED
Pilot reference:               <pilot ticket id>
Rollout pace:                  2-3 controlled pilots → small batch → broader rollout
Per-store safeguards retained: backup, evidence bundle, readiness report, guarded UI, dangerous-operation lock
Approver (Support):            <name + UTC>
Approver (Engineering Lead):   <name + UTC>
Approver (Business):           <name + UTC>
```

### Limited Next Pilot Approved

```
LIMITED NEXT PILOT APPROVED
Source pilot:                  <ticket id>
Next pilot tenant/store:       <name / subdomain>
Profile rationale:             <why this store profile differs from the pilot>
Planned window:                <YYYY-MM-DD HH:MM>
Approver (Support):            <name + UTC>
Approver (Business):           <name + UTC>
```

### Rollout Held

```
ROLLOUT HELD
Reason:                        <concrete observation>
Ticket / defect:               <link>
Owner:                         <name>
Retest condition:              <what must change before another pilot>
Pilot Evidence Bundle:         <attachment id>
Approver (Support):            <name + UTC>
```

### Rollback / Recovery Required

```
ROLLBACK / RECOVERY REQUIRED
Tenant/store:                  <name>
Reason:                        <observed symptom>
Approver (Support):            <name + UTC>
Approver (Store Manager):      <name + UTC>
Backup re-captured:            Yes (path / attestation)
Rollback outcome:              <Success / Failed>
Post-rollback validation:      <Pass / Fail>
Wider rollout status:          Blocked until root cause fixed.
Defect ticket:                 <link>
```

### Security Defect Found

```
SECURITY DEFECT (S1)
Type:                          <raw confirmation phrase / JWT / DPAPI blob / token leak>
Where observed:                <file path inside an evidence bundle / audit log>
Action taken:                  Preserved offline. Not shared externally.
Pilot evidence bundle:         Quarantined offline; do not re-share.
Reporting channel:             <secure security ticket id>
Approver (Security):           <name + UTC>
Wider rollout status:          Blocked until redactor defect is fixed and bundles re-validated.
```

## 17. Rollout Expansion Guardrails

These guardrails apply at every rollout stage, not just the first
expansion after pilot sign-off.

- Enable dangerous UI flags (`operator_real_migration_ui_enabled`,
  `operator_runtime_cutover_ui_enabled`, `operator_rollback_ui_enabled`,
  `operator_retention_cleanup_ui_enabled`) **only** during the specific
  operation window. Turn them back off as the final step of every sub-
  window.
- Collect a pilot evidence bundle **before and after** every store's
  migration/cutover. The bundle is not optional at any rollout stage.
- Never run retention cleanup during a store's initial migration/cutover
  window. Cleanup is storage hygiene, not migration recovery, and has
  its own runbook.
- Preserve archived `tenants.before-rollback-*\` directories and
  `pos.db.broken-*` files for at least **30 days** regardless of any
  retention threshold the dashboard may suggest. The retention preview
  is informational only; cleanup deletion of archived directories
  requires a separate deliberate sub-window.
- Always preserve the off-machine backup until the store has been
  signed off as successful AND has run normally for at least one full
  day on the tenant DB.
- Never batch multiple stores' migrations into one event without
  reviewing evidence between stores. Each store's evidence bundle must
  be reviewed before the next store starts.
- The first observed blocker stops the rollout batch immediately.
  Investigate before proceeding.
- The Store Manager validates the business flow for every store.
  Without that validation, no store's rollout is considered complete.
- The Support Engineer reviews the evidence bundle for every store. The
  review is a hard gate, not a courtesy.
- The Backend / Platform Engineer reviews systemic errors that appear
  across multiple stores (e.g. backend-side sync failures, idempotency
  key collisions, server-side rejection patterns).

## 18. Manual Review Checklist

The reviewer of this document confirms:

- [ ] All required sections (1–18) exist and are populated.
- [ ] No raw confirmation phrase value appears anywhere — the six known
      phrase constants (migration / runtime cutover / rollback wrapper /
      rollback inner / rollback legacy / retention cleanup) are not
      present in this document. References point to "the internal
      secure runbook" instead.
- [ ] No destructive shell command appears (`Remove-Item`, `rd /s`,
      `del /q`, `rm`, `del`, `Set-Content -Force`, `New-Item -Force` on
      a file, `Move-Item` of any DB file, etc.).
- [ ] Required evidence list (Section 3) is complete: pre-pilot backup,
      diagnostics, preflight, inventory, readiness report, evidence
      bundle pre + post, migration audit, cutover audit, post-restart
      diagnostics, business sign-off.
- [ ] Decision Matrix (Section 10) exists with at least the 15 listed
      conditions.
- [ ] Defect ticket template (Section 15) is present with severity
      guidance.
- [ ] Communication templates (Section 16) are present for the seven
      named events (success / with warnings / wider rollout / limited
      next pilot / hold / rollback / security defect).
- [ ] Cross-reference added to
      [`operator-controlled-production-pilot-runbook.md`](operator-controlled-production-pilot-runbook.md)
      pointing at this document.
- [ ] No source code (`.cs` / `.xaml` / `.csproj`) was changed by the
      commit that introduced this document.

---

## Phase 10.19G — Backend evidence registration note

When the optional `operator_backend_evidence_registration_enabled="1"`
flag is set on the operator's machine, the desktop may register
sanitized metadata about the sign-off Pilot Evidence Bundle with the
backend after a successful export and surface a `registrationId` in the
"Backend audit / evidence integration" sub-card on the Migration
Operations window.

The backend `registrationId` is **informational only**. The locally
exported Pilot Evidence Bundle (folder + optional ZIP under
`%LocalAppData%\PosSystem\logs\pilot-evidence\...`) **remains the
authoritative artifact** that sign-off stakeholders review for the
rollout decision. Backend rejection or unavailability does not
invalidate the local bundle and does not change the rollout decision
logic in this document.

Full contract:
[`desktop-backend-operator-permissions-integration.md`](desktop-backend-operator-permissions-integration.md)
section "Phase 10.19G — Audit intent & evidence registration".

---

## Phase 10.19J — Backend audit/evidence review (optional, default OFF)

When `operator_backend_audit_review_ui_enabled="1"`, sign-off
stakeholders may use the read-only review card on the Migration
Operations dashboard to confirm that the sign-off Pilot Evidence
Bundle's `registrationId` exists in the backend forensic trail and that
the operator's audit-intent record matches the documented operation
window. This is a verification convenience, not a sign-off requirement:
the local Pilot Evidence Bundle remains the authoritative artifact for
the rollout decision. Backend rejection / 404 / network failure does
not invalidate the local bundle and does not alter the rollout
decision logic above.

---

## Phase 10.20G — Operator Permission Admin Read-Only UI (optional, default OFF)

When `operator_permission_admin_readonly_ui_enabled="1"`, sign-off
stakeholders may use the new read-only permission admin card on the
Migration Operations dashboard to inspect persisted permission
definitions, role grants, user overrides, and DB-shadow effective
permissions for a target user / tenant. Useful for confirming that
the operator account performing the pilot holds exactly the
permissions the runbook expects, and that no surprise overrides are
in effect.

The card is **read-only** — it does not grant, revoke, approve,
execute, or otherwise mutate any record. It does not approve a sign-
off. The desktop guarded wrappers, the local feature flags, the
confirmation phrases, and the dangerous-operation lock remain the
authoritative gates for execution. Full contract:
[`desktop-backend-operator-permissions-integration.md`](desktop-backend-operator-permissions-integration.md)
section "Phase 10.20G — Operator Permission Admin Read-Only UI".

---

## Phase 10.20I — Operator Permission Admin Mutation UI (optional, default OFF)

A separate mutation card (Phase 10.20I) can create or revoke DB
permission overrides and role grants. **It should remain disabled
during sign-off review.** Sign-off is about confirming the local
Pilot Evidence Bundle and the prepared rollout plan; it is never the
right moment to be changing permission grants. If a sign-off
discovers that an operator's permissions are wrong, fix the grant
through this UI **before** the next pilot window opens, not during
sign-off itself. Every mutation is audited by the backend.

DB permissions are still not authoritative — a mutation does NOT
change runtime permission decisions and does NOT alter the rollout
decision logic above.

---

## Phase 10.20J — Operator Permission Admin Limited Pilot (separate)

The Operator Permission Administration stack (Phases 10.20A-I) has
its **own** limited-pilot runbook with its own §18 sign-off template:

→ [`operator-permission-admin-limited-pilot-runbook.md`](./operator-permission-admin-limited-pilot-runbook.md)

The permission-admin pilot sign-off is a **separate** workflow from
the tenant-DB-cutover sign-off documented in this file. The two
should not be interleaved. The permission-admin pilot sign-off does
not approve a tenant-DB cutover; the tenant-DB cutover sign-off does
not approve permission-admin mutation rollout. Each pilot has its
own evidence bundle, its own sign-off form, and its own reviewer.

The Phase 10.20K **controlled-rollout** runbook for the
permission-admin stack — which succeeds a signed-off 10.20J pilot —
is also separate:

→ [`operator-permission-admin-controlled-rollout-runbook.md`](./operator-permission-admin-controlled-rollout-runbook.md)

It defines six waves with per-wave sign-offs and a final rollout
decision template. The per-wave and final decisions are
independent of the tenant-DB rollout decisions in this file.

## Phase 10.21D — Read-Only DB-Authoritative Permission Pilot (separate)

A further, **independent** pilot exists for the Phase 10.21C
read-only DB-authoritative permission resolver:

→ [`operator-permission-readonly-authoritative-pilot-runbook.md`](./operator-permission-readonly-authoritative-pilot-runbook.md)

That pilot exercises the backend runtime resolver path for
read-only operator permission decisions when
`operator.permission.db.authoritative.readonly.enabled=true`. It
ships with its own §18 sign-off template (21-scenario test matrix,
five-role signature block, PROCEED / REPEAT / STOP decision).

The Phase 10.21D sign-off is **separate** from both the tenant-DB
cutover sign-off in this file and the Phase 10.20J permission-admin
sign-off. None of these three sign-offs approves the others;
each has its own evidence bundle, its own sign-off form, and its
own reviewer. Do not interleave them.

## Phase 10.21H — Dangerous DB-Authoritative Permission Pilot (separate)

A further, **independent** pilot exists for the Phase 10.21F
dangerous DB-authoritative permission resolver:

→ [`operator-permission-dangerous-authoritative-pilot-runbook.md`](./operator-permission-dangerous-authoritative-pilot-runbook.md)

That pilot exercises the backend runtime resolver path for
dangerous operator permission decisions when
`operator.permission.db.authoritative.dangerous.enabled=true`
(with the Phase 10.21E preflight as a hard dependency). It ships
with its own §18 sign-off template (21-scenario test matrix,
six-role signature block including a non-waivable Security
Reviewer, PROCEED / REPEAT / STOP decision).

The Phase 10.21H sign-off is **separate** from the tenant-DB
cutover sign-off in this file, the Phase 10.20J permission-admin
sign-off, and the Phase 10.21D read-only DB-authoritative sign-off.
None of these four sign-offs approves the others; each has its own
evidence bundle, its own sign-off form, and its own reviewer. Do
not interleave them.

The pilot does NOT execute any dangerous operation. The desktop's
guarded wrappers remain the only legitimate execution path for
migration / cutover / rollback / retention-cleanup; the Phase
10.21H pilot only verifies that the backend can answer the
permission *decision* safely.

## Phase 10.21I — DB-Authoritative Permission Controlled Rollout (separate)

A controlled-rollout runbook exists for the DB-authoritative
operator permission stack (Phases 10.21C / 10.21E / 10.21F):

→ [`operator-permission-db-authoritative-controlled-rollout-runbook.md`](./operator-permission-db-authoritative-controlled-rollout-runbook.md)

It defines seven waves (Wave 0 staging rehearsal → Wave 6
steady-state decision) and ships with two templates (wave evidence
+ final decision). Per-wave sign-off (§18 of the canonical runbook)
+ final-decision (§19) are the canonical outputs.

The Phase 10.21I sign-off is **separate** from:

- the tenant-DB cutover sign-off in this file,
- the Phase 10.20J permission-admin sign-off,
- the Phase 10.21D read-only DB-authoritative sign-off,
- the Phase 10.21H dangerous DB-authoritative sign-off.

None of these five sign-offs approves the others. Each has its
own evidence bundle, its own sign-off form, and its own reviewer.
Do not interleave them.

Phase 10.21I depends on signed-off Phase 10.21D + Phase 10.21H
pilot evidence as mandatory prerequisites (per §2 of the canonical
runbook). The rollout does NOT execute any dangerous operation; it
exercises only backend authorisation decisions across a staged
sequence of wave-specific flag combinations.

---

## Phase 10.22P — Lifecycle Scheduler Status (2026-06-02)

A read-only desktop monitoring card for evidence bundle lifecycle
scheduler run history has been added. It does not execute any
dangerous operation, does not hard-delete, and does not delete
storage objects.

When `operator_evidence_bundle_lifecycle_scheduler_status_ui_enabled=1`
the desktop reads retention-sweeper and expiration-sweeper run history.
When `operator_evidence_bundle_lifecycle_scheduler_manual_run_ui_enabled=1`
operators may trigger manual dry-run or live sweeper runs from the desktop
(backend permission `operator.evidence.bundle.retention.admin` required).

This sign-off is **separate** from all previous sign-offs. Enabling this
flag does not approve any dangerous operation and does not change any
guarded-wrapper or confirmation-phrase behaviour.

Full specification: [`evidence-bundle-lifecycle-scheduler-status-ui.md`](evidence-bundle-lifecycle-scheduler-status-ui.md)
