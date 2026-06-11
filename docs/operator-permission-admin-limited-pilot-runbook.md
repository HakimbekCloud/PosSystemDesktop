# Operator Permission Admin — Limited Pilot Runbook (Phase 10.20J)

The canonical runbook for the Phase 10.20A-I Operator Permission
Administration limited pilot lives in the backend repository:

→ [`Ham-Pos/docs/operator-permission-admin-limited-pilot-runbook.md`](../../Ham-Pos/docs/operator-permission-admin-limited-pilot-runbook.md)

That document is the source of truth for the pilot procedure. It
covers purpose, scope, safety boundaries, participants, environment,
flag matrix, pre-pilot checklist, the 23-scenario test matrix, the
ordered procedure, evidence collection, SQL/API/desktop verification,
rollback, stop conditions, success criteria, and the sign-off
template.

## Desktop-side notes

- Both desktop flags (`operator_permission_admin_readonly_ui_enabled`
  and `operator_permission_admin_mutation_ui_enabled`) stay default
  OFF outside the pilot window. They are flipped briefly during the
  pilot per the runbook's §9 step 2 (read-only) and step 7
  (mutation), and restored to OFF in §9 step 11.
- The Phase 10.20G read-only card and the Phase 10.20I mutation card
  share the Migration Operations dashboard with the existing
  dangerous-operation cards. The pilot must verify (T22 of the
  scenario matrix) that the dangerous-operation `CanExecute` state is
  byte-identical before / during / after the pilot. No flag flipped
  by this pilot affects those buttons.
- POS sales flow is exercised as a smoke check (T23). The pilot must
  not break it. If it does, stop per §15.
- Confirmation phrases never leave the desktop. The pilot's mutation
  forms (`reason`, `approvalTicketId`) deliberately do not accept or
  display the six maintenance-execution phrases; the desktop scrub
  layer additionally redacts them on display if they ever appear in a
  backend response.

See also:

- [`desktop-backend-operator-permissions-integration.md`](./desktop-backend-operator-permissions-integration.md)
  — full contract for the desktop side of all Phase 10.19/10.20 work.
- [`operator-controlled-production-pilot-runbook.md`](./operator-controlled-production-pilot-runbook.md)
  — separate, unrelated pilot for the tenant-DB cutover pipeline.
- [`operator-pilot-signoff-rollout-decision-runbook.md`](./operator-pilot-signoff-rollout-decision-runbook.md)
  — sign-off / rollout decision flow for the tenant-DB pilot
  (separate workflow; the permission-admin pilot has its own §18
  sign-off template in the backend canonical doc).
