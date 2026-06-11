# Operator Permission Read-Only DB-Authoritative — Pilot Runbook (Phase 10.21D)

The canonical runbook for the Phase 10.21C read-only DB-authoritative
permission resolver pilot lives in the backend repository:

→ [`Ham-Pos/docs/operator-permission-readonly-authoritative-pilot-runbook.md`](../../Ham-Pos/docs/operator-permission-readonly-authoritative-pilot-runbook.md)

That document is the source of truth for the pilot procedure. It
covers purpose, scope, safety boundaries, participants, environment,
flag matrix, pre-pilot checklist, the 21-scenario test matrix
(T1-T21), the ordered procedure, evidence collection (10 files),
SQL/API/desktop verification, rollback, stop conditions, success
criteria, and the §18 sign-off template.

## Desktop-side notes

- The pilot exercises **backend runtime decisions** for read-only
  operator permission keys when
  `operator.permission.db.authoritative.readonly.enabled=true`. The
  desktop's runtime behaviour does not change for any key in this
  pilot:
  - Dangerous-operation buttons on the Migration Operations
    dashboard keep the same `CanExecute` state before, during, and
    after the pilot (T19 of the scenario matrix is the explicit
    check).
  - The Phase 10.20G read-only admin card may be flipped on briefly
    in §9 step 12 of the runbook to observe persisted grants, then
    restored to default OFF.
  - The Phase 10.20I mutation card stays default OFF unless the
    pilot's T8 / T9 scenarios specifically exercise it. The
    Phase 10.20J admin-mutation pilot is the canonical place to
    exercise the mutation surface broadly.
- POS sales flow is exercised as a smoke check (T21). The pilot
  must not break it; if it does, stop per §15 of the backend
  runbook.
- **Confirmation phrases never leave the desktop.** The pilot's
  override mutations (`reason`, `approvalTicketId`) deliberately
  do not accept or display any of the six maintenance-execution
  phrases; the desktop scrub layer additionally redacts them on
  display if they ever appear in a backend response.
- **Dangerous decisions remain code-only** throughout this pilot.
  The Phase 10.21F dangerous DB-authoritative flag is NOT
  implemented and NOT exercised here. Dangerous-key `/validate`
  decisions remain byte-identical to the Phase 10.21B baseline.

See also:

- [`desktop-backend-operator-permissions-integration.md`](./desktop-backend-operator-permissions-integration.md)
  — full contract for the desktop side of all Phase 10.19/10.20/10.21
  work.
- [`operator-permission-admin-limited-pilot-runbook.md`](./operator-permission-admin-limited-pilot-runbook.md)
  — Phase 10.20J pilot pointer (admin-mutation workstream, separate).
- [`operator-permission-admin-controlled-rollout-runbook.md`](./operator-permission-admin-controlled-rollout-runbook.md)
  — Phase 10.20K pointer (admin rollout, separate workstream).
- [`operator-controlled-production-pilot-runbook.md`](./operator-controlled-production-pilot-runbook.md)
  — separate, unrelated pilot for the tenant-DB cutover pipeline.
- [`operator-pilot-signoff-rollout-decision-runbook.md`](./operator-pilot-signoff-rollout-decision-runbook.md)
  — sign-off / rollout decision flow for the tenant-DB pilot
  (separate workflow; the read-only DB-authoritative pilot has its
  own §18 sign-off template in the backend canonical doc).
