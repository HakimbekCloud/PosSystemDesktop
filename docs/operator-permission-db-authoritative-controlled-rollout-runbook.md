# Operator Permission DB-Authoritative — Controlled Rollout Runbook (Phase 10.21I)

The canonical controlled-rollout runbook for the Phase 10.21
DB-authoritative operator permission stack lives in the backend
repository:

→ [`Ham-Pos/docs/operator-permission-db-authoritative-controlled-rollout-runbook.md`](../../Ham-Pos/docs/operator-permission-db-authoritative-controlled-rollout-runbook.md)

That document is the source of truth for the rollout decision flow.
It covers purpose, prerequisites (signed-off Phase 10.21D + 10.21H
evidence bundles required), rollout scope, safety boundaries,
feature-flag policy (6 flags), seven rollout waves (Wave 0 staging
rehearsal → Wave 6 steady-state decision), go/no-go gates, the
ordered 15-step per-wave procedure, operational monitoring (14
signals), evidence requirements (12-file folder per wave +
final-decision folder for Wave 6), rollback / pause plan (14
ordered steps, dangerous flag OFF first), stop conditions (14
items), success criteria, decision matrix (9 outcomes),
communication plan with copy-paste templates, post-rollout review,
open risks, the per-wave sign-off template (§18), and the final
rollout decision template (§19).

## Desktop-side notes

- The rollout exercises **backend runtime decisions**. Desktop
  runtime behaviour does NOT change for any key during the
  rollout, beyond the Phase 10.21G status card observation
  surface.
- **Dangerous-operation buttons remain unchanged.** Real
  Migration / Cutover / Rollback / Retention Cleanup keep the
  same `CanExecute` state before, during, and after every wave.
  Step 11 of the per-wave procedure is the explicit
  dangerous-button check; STOP-condition #11 watches for any
  drift.
- **No dangerous button is clicked during any wave.** The
  rollout is a permission-decision rollout, not an execution
  rollout. The desktop's guarded wrappers remain the only
  legitimate execution path for migration / cutover / rollback /
  retention cleanup.
- **Confirmation phrases never leave the desktop.** No backend
  field, no audit metadata, no rollout artifact accepts or
  echoes a phrase value. The six known maintenance-execution
  phrases NEVER appear in any wave artifact.
- **Phase 10.21G authoritative-status card** is the day-to-day
  operational view of the rollout's flag state, parity health,
  dangerous preflight health, and risk-list. The desktop flag
  `operator_permission_authoritative_status_ui_enabled` is
  flipped ON for waves that require observation (Waves 0+) and
  restored to baseline at wave end unless the §19 final decision
  keeps it ON in steady state.
- **POS sales flow** is exercised as a smoke check (step 12 of
  the per-wave procedure). The rollout must not break it; if it
  does, stop per §12 of the canonical runbook.
- **The dangerous-authoritative flag flip discipline** is
  strictly windowed (Waves 4-5 only, ≤ 1-hour windows, ≥ 24h
  between windows per operator). The desktop status card lets
  operators observe the current backend flag state without
  exposing any control to flip it.

See also:

- [`desktop-backend-operator-permissions-integration.md`](./desktop-backend-operator-permissions-integration.md)
  — full contract for the desktop side of all Phase 10.19 / 10.20 /
  10.21 work.
- [`operator-permission-readonly-authoritative-pilot-runbook.md`](./operator-permission-readonly-authoritative-pilot-runbook.md)
  — Phase 10.21D pilot pointer (prerequisite for this rollout).
- [`operator-permission-dangerous-authoritative-pilot-runbook.md`](./operator-permission-dangerous-authoritative-pilot-runbook.md)
  — Phase 10.21H pilot pointer (prerequisite for this rollout).
- [`operator-permission-admin-controlled-rollout-runbook.md`](./operator-permission-admin-controlled-rollout-runbook.md)
  — Phase 10.20K admin-mutation rollout pointer (separate
  workstream; do not interleave).
- [`operator-controlled-production-pilot-runbook.md`](./operator-controlled-production-pilot-runbook.md)
  — separate, unrelated tenant-DB cutover pilot.
- [`operator-pilot-signoff-rollout-decision-runbook.md`](./operator-pilot-signoff-rollout-decision-runbook.md)
  — sign-off / rollout decision flow for the tenant-DB pilot
  (separate workflow; the DB-authoritative rollout has its own
  §18 sign-off template + §19 final-decision template in the
  backend canonical doc).
