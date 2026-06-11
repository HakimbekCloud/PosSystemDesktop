# Operator Permission Admin — Controlled Rollout Runbook (Phase 10.20K)

The canonical controlled-rollout runbook for the Phase 10.20A-I
Operator Permission Administration stack lives in the backend
repository:

→ [`Ham-Pos/docs/operator-permission-admin-controlled-rollout-runbook.md`](../../Ham-Pos/docs/operator-permission-admin-controlled-rollout-runbook.md)

That document is the source of truth for the rollout decision flow.
It covers purpose, prerequisites, scope, six rollout waves
(Wave 0–5), flag policy, role + permission policy, go/no-go gates,
per-wave procedure, operational monitoring, evidence requirements,
rollback plan, stop conditions, success criteria, communication
plan, decision matrix, post-rollout review, open risks, the
per-wave sign-off template, and the final rollout decision template.

## Desktop-side notes

- Both desktop flags
  (`operator_permission_admin_readonly_ui_enabled` and
  `operator_permission_admin_mutation_ui_enabled`) **remain default
  OFF outside of operational use**. The controlled rollout flips
  them only per the wave's documented window and restores them per
  the wave's exit criteria.
- The Phase 10.20I mutation UI must be enabled **only per the
  rollout wave's documented mutation window**. Operators should
  never enable the mutation flag opportunistically; every mutation
  window must trace back to a wave plan in
  `evidence/operator-permission-admin-rollout/YYYY-MM-DD/wave-N/00-wave-plan.md`.
- The dangerous-operation buttons on the Migration Operations
  dashboard (Real Migration / Cutover / Rollback / Retention
  Cleanup) must show identical `CanExecute` state throughout
  every wave. Wave sign-off (§18 of the canonical runbook)
  includes an explicit tick for this check.
- POS sales flow must remain unaffected. Cashier smoke (one login
  + one sale) is a wave-end check.
- Confirmation phrases continue to never leave the desktop. The
  mutation UI's `reason` and `approvalTicketId` fields are scrubbed
  before display on both the desktop and the backend.

See also:

- [`desktop-backend-operator-permissions-integration.md`](./desktop-backend-operator-permissions-integration.md)
  — full contract for the desktop side of every Phase 10.19/10.20
  surface.
- [`operator-permission-admin-limited-pilot-runbook.md`](./operator-permission-admin-limited-pilot-runbook.md)
  — Phase 10.20J limited pilot runbook (prerequisite for Phase 10.20K
  Wave 0).
- [`operator-controlled-production-pilot-runbook.md`](./operator-controlled-production-pilot-runbook.md)
  — separate, unrelated runbook for the tenant-DB cutover pilot.
- [`operator-pilot-signoff-rollout-decision-runbook.md`](./operator-pilot-signoff-rollout-decision-runbook.md)
  — sign-off / rollout decision flow for the tenant-DB pilot
  (separate workflow; the permission-admin rollout has its own §18
  and §19 templates in the backend canonical doc).
