# Evidence Bundle Lifecycle Scheduler Status UI (Phase 10.22P)

## Overview

Phase 10.22P adds a read-only desktop WPF monitoring card that displays evidence bundle lifecycle scheduler run history. It consumes the Phase 10.22N retention archive sweeper and Phase 10.22O expiration sweeper run-history backend endpoints.

**This card is monitoring-only.** It does not hard-delete, does not delete storage objects, does not archive or expire bundles directly, and does not trigger any local dangerous operation.

---

## Feature flags

| Flag key | Default | Effect when `"1"` |
|---|---|---|
| `operator_evidence_bundle_lifecycle_scheduler_status_ui_enabled` | OFF | Enables the card; HTTP calls are made on Refresh/Load. |
| `operator_evidence_bundle_lifecycle_scheduler_manual_run_ui_enabled` | OFF | Shows manual-run inputs and "Submit run" buttons for both sweepers. |

Both flags are read from `GlobalSettingsRepository` at call time. Missing / empty / `"0"` ⇒ `FEATURE_FLAG_OFF` outcome, zero HTTP calls.

---

## Backend endpoints consumed

All under `/api/v1/operator/evidence/bundles` (Phase 10.22N/O controller):

| Method | Path | Auth |
|---|---|---|
| `POST` | `/retention-sweeper/run-once` | `operator.evidence.bundle.retention.admin` |
| `GET`  | `/retention-sweeper/runs` | `operator.evidence.bundle.retention.admin` |
| `GET`  | `/retention-sweeper/runs/{runUuid}` | `operator.evidence.bundle.retention.admin` |
| `POST` | `/expiration-sweeper/run-once` | `operator.evidence.bundle.retention.admin` |
| `GET`  | `/expiration-sweeper/runs` | `operator.evidence.bundle.retention.admin` |
| `GET`  | `/expiration-sweeper/runs/{runUuid}` | `operator.evidence.bundle.retention.admin` |

---

## Desktop files

### New files

| File | Purpose |
|---|---|
| `Core/DTOs/EvidenceBundleSchedulerRunDtos.cs` | Six sealed DTOs matching backend Phase 10.22N/O record shapes: `RetentionSweepRunRequestDto`, `RetentionSweepRunResponseDto`, `RetentionSweepRunPageResponseDto`, `ExpirationSweepRunRequestDto`, `ExpirationSweepRunResponseDto`, `ExpirationSweepRunPageResponseDto`. |
| `Services/EvidenceBundleLifecycleScheduler/EvidenceBundleLifecycleSchedulerStatusService.cs` | Desktop service orchestrator. Gated by both flags. Calls `OperatorEvidenceBundleApiClient` wrapper methods. Returns typed `EvidenceBundleApiCallOutcome<T>` outcomes. |

### Modified files

| File | Change |
|---|---|
| `Services/ApiClient.cs` | 6 new typed-outcome methods after `ListEvidenceBundleRetentionCandidatesAsync`: `ListRetentionSweepRunsAsync`, `GetRetentionSweepRunAsync`, `RunRetentionSweepOnceAsync`, `ListExpirationSweepRunsAsync`, `GetExpirationSweepRunAsync`, `RunExpirationSweepOnceAsync`. All use `CallEvidenceBundleJsonAsync<T>`. |
| `Services/OperatorEvidenceBundleApiClient.cs` | 6 corresponding `SafeAsync`-wrapped public methods. |
| `App.xaml.cs` | `sc.AddSingleton<EvidenceBundleLifecycleSchedulerStatusService>()` registered after Phase 10.22H. |
| `ViewModels/MigrationOperationsViewModel.cs` | Field, flag constants, ctor param+assign, `RefreshLifecycleSchedulerFlag()` call, ~65 `[ObservableProperty]` fields, 6 `[RelayCommand]` methods, 2 private helpers (`ApplySelectedRetentionSweepRun`, `ApplySelectedExpirationSweepRun`). |
| `Views/MigrationOperationsWindow.xaml` | New `<Border>` card appended after Phase 10.22H retention card. |

---

## ViewModel observable properties

### Flag state

| Property | Type | Description |
|---|---|---|
| `LifecycleSchedulerEnabled` | `bool` | Mirrors `IsEnabled()` |
| `LifecycleSchedulerStatusText` | `string` | Human-readable flag state |
| `LifecycleSchedulerManualRunEnabled` | `bool` | Mirrors `IsManualRunEnabled()` |
| `LifecycleSchedulerManualRunStatusText` | `string` | Human-readable manual-run flag state |
| `LifecycleSchedulerStatusMessage` | `string` | Last operation status |
| `LifecycleSchedulerErrorCode` | `string` | Last error code |
| `LifecycleSchedulerErrorMessage` | `string` | Last error message |

### Retention sweeper

| Property | Type |
|---|---|
| `RetentionSweepRuns` | `ObservableCollection<RetentionSweepRunResponseDto>` |
| `RetentionRunsTotalElements` | `long` |
| `RetentionRunsPage` | `int` |
| `RetentionRunLast*` | 13 string/bool fields for selected-run detail |
| `RetentionManualRunDryRun` | `bool` (default `true`) |
| `RetentionManualRunReason` | `string` |
| `RetentionManualRunTicketId` | `string` |
| `RetentionManualRunOutcome` | `string` |

### Expiration sweeper

Same structure with `Expiration` prefix; `ExpiredCount` instead of `ArchivedCount`.

---

## ViewModel commands

| Command | Action |
|---|---|
| `RefreshLifecycleSchedulerStatusCommand` | Re-reads both flags from `GlobalSettingsRepository`. |
| `ListRetentionSweepRunsCommand` | `GET /retention-sweeper/runs` → populates `RetentionSweepRuns`. |
| `SelectRetentionSweepRunCommand(run)` | `GET /retention-sweeper/runs/{uuid}` → populates `RetentionRunLast*` detail fields. |
| `RunRetentionSweepOnceCommand` | `POST /retention-sweeper/run-once` (requires both flags ON). |
| `ListExpirationSweepRunsCommand` | `GET /expiration-sweeper/runs` → populates `ExpirationSweepRuns`. |
| `SelectExpirationSweepRunCommand(run)` | `GET /expiration-sweeper/runs/{uuid}` → populates `ExpirationRunLast*` detail fields. |
| `RunExpirationSweepOnceCommand` | `POST /expiration-sweeper/run-once` (requires both flags ON). |

---

## Security constraints

The desktop never sends or displays:

- Storage key, bucket name, object endpoint
- Local absolute backend path
- Token, password, or API key
- Confirmation phrase
- Object bytes or ZIP content
- Raw machine name or per-bundle identifiers (beyond the run UUID returned by the backend)

All user-visible strings from backend responses pass through `ScrubForDisplay` before binding.

Manual run inputs (`reason`, `ticketId`) are trimmed and nullified if empty. No path-like or credential-like input is accepted.

---

## Failure modes

| Scenario | Result |
|---|---|
| Both flags OFF | `FEATURE_FLAG_OFF` outcome; zero HTTP calls; card shows disabled state text. |
| Backend unreachable | `NETWORK_FAILURE` from `SafeAsync` wrapper; `LifecycleSchedulerErrorMessage` updated; no crash. |
| HTTP 403 / permission denied | `FORBIDDEN` error code surfaced; card shows error; no retry. |
| Manual run flag OFF but status UI flag ON | Manual-run sections hidden; `RunRetentionSweepOnce`/`RunExpirationSweepOnce` commands return `FEATURE_FLAG_OFF` immediately. |
| Backend returns non-2xx | `ReadEvidenceBundleOutcomeAsync` converts to typed failure; same flow as above. |

---

## What this phase does NOT do

- Does NOT archive any bundle (that is the Phase 10.22N backend scheduler).
- Does NOT expire any bundle (that is the Phase 10.22O backend scheduler).
- Does NOT hard-delete anything (Phase 10.22L backend only; no desktop surface).
- Does NOT delete storage objects (no `EvidenceBundleStorageAdapter` call anywhere in Phase 10.22P).
- Does NOT change any existing guarded-operation, permission-preflight, or dangerous-operation-lock behaviour.

---

## Related documentation

- `Ham-Pos/docs/operator-evidence-bundle-retention-scheduler.md` — Phase 10.22N backend (archive sweeper)
- `Ham-Pos/docs/operator-evidence-bundle-expiration-scheduler.md` — Phase 10.22O backend (expiration sweeper)
- `PosSystem/docs/evidence-bundle-retention-legal-hold-ui.md` — Phase 10.22H desktop retention card
- `Ham-Pos/docs/operator-evidence-bundle-hard-delete.md` — why hard-delete has no desktop surface
