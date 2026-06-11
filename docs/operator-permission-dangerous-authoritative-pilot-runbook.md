# Operator Permission Dangerous DB-Authoritative — Pilot Runbook (Phase 10.21H)

The canonical runbook for the Phase 10.21F dangerous DB-authoritative
permission resolver pilot lives in the backend repository:

→ [`Ham-Pos/docs/operator-permission-dangerous-authoritative-pilot-runbook.md`](../../Ham-Pos/docs/operator-permission-dangerous-authoritative-pilot-runbook.md)

That document is the source of truth for the pilot procedure. It
covers purpose, scope, safety boundaries, participants, environment,
flag matrix, pre-pilot checklist, the 21-scenario test matrix
(T1–T21), the ordered procedure (18 steps), evidence collection
(12 files), SQL / API / desktop verification, rollback, stop
conditions, success criteria, and the §18 sign-off template.

## Desktop-side notes

- **The pilot exercises backend dangerous-key decisions.** Desktop
  runtime behaviour does NOT change for any key during this pilot.
- **Dangerous-operation buttons remain unchanged.** Real Migration /
  Cutover / Rollback / Retention Cleanup buttons keep the same
  `CanExecute` state before, during, and after the pilot. T20 of
  the test matrix is the explicit dangerous-button check;
  T2-T6 verify the dangerous-key `/validate` decision shape and
  metadata flags stay correct.
- **No dangerous button is clicked during the pilot.** The pilot
  is a permission-decision pilot, not an execution pilot. The
  desktop's guarded wrappers remain the only legitimate execution
  path; this pilot does not exercise them.
- **Confirmation phrases never leave the desktop.** The pilot's
  override mutations (`reason`, `approvalTicketId`) deliberately
  do not accept or display any of the six maintenance-execution
  phrases; the desktop scrub layer additionally redacts them on
  display if they ever appear in a backend response.
- **Phase 10.21G authoritative-status card** is the day-to-day
  operational view. The desktop flag
  `operator_permission_authoritative_status_ui_enabled` is flipped
  ON for the pilot window (per §9 step 5) and restored to OFF at
  pilot close (per §9 step 15).
- **POS sales flow** is exercised as a smoke check (T21). The
  pilot must not break it; if it does, stop per §15.

See also:

- [`desktop-backend-operator-permissions-integration.md`](./desktop-backend-operator-permissions-integration.md)
  — full contract for the desktop side of all Phase 10.19 / 10.20 /
  10.21 work.
- [`operator-permission-readonly-authoritative-pilot-runbook.md`](./operator-permission-readonly-authoritative-pilot-runbook.md)
  — Phase 10.21D read-only pilot pointer (separate workstream;
  prerequisite for this pilot per §7).
- [`operator-permission-admin-limited-pilot-runbook.md`](./operator-permission-admin-limited-pilot-runbook.md)
  — Phase 10.20J pilot pointer (admin-mutation workstream;
  separate).
- [`operator-permission-admin-controlled-rollout-runbook.md`](./operator-permission-admin-controlled-rollout-runbook.md)
  — Phase 10.20K rollout pointer (admin rollout; separate).
- [`operator-controlled-production-pilot-runbook.md`](./operator-controlled-production-pilot-runbook.md)
  — separate, unrelated pilot for the tenant-DB cutover pipeline.
- [`operator-pilot-signoff-rollout-decision-runbook.md`](./operator-pilot-signoff-rollout-decision-runbook.md)
  — sign-off / rollout decision flow for the tenant-DB pilot
  (separate workflow; the dangerous DB-authoritative pilot has its
  own §18 sign-off template in the backend canonical doc).
