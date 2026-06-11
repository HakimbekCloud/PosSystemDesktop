# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 8 WPF point-of-sale desktop app (`PosSystem.csproj`, single project, target `net8.0-windows`) for cashier terminals, backed by the Ham-Pos/ShefPos backend at `https://shefpos.uz`. It is offline-first: all data lives in a local SQLite database and syncs with the backend in the background. User-facing strings are in Uzbek.

`AGENTS.md` in the repo root contains additional repository guidelines (style, commit conventions) — follow it.

## Commands

```bash
dotnet restore
dotnet build PosSystem.csproj
dotnet run --project PosSystem.csproj
```

WPF only compiles/runs on Windows; on macOS/Linux you can edit code but not build it. There is no test project yet (if adding one, use a separate `PosSystem.Tests` and `dotnet test`).

`pos-cashier-ui/` is a standalone React + Vite + Tailwind UI prototype (not part of the .NET build): `npm run dev` / `npm run build` from that folder. `design-system/` holds the design tokens (`tokens.json` is the source of truth) and layout specs that both the prototype and the WPF UI follow.

## Architecture

**MVVM + DI.** CommunityToolkit.Mvvm throughout. The entire composition root is `App.xaml.cs:BuildServices()`: repositories and services are singletons, ViewModels and Views are transient, `MainWindow` is a singleton. Repositories are safe as singletons because they use `IDbContextFactory<AppDbContext>` internally — never inject `AppDbContext` directly.

**Navigation by messenger, not a framework.** `Messages.cs` defines `LoginSuccessMessage`, `LogoutMessage`, `SessionExpiredMessage`. `MainWindow` listens via `WeakReferenceMessenger.Default` and swaps `MainContent.Content` between `LoginView` and `PosView`. Anything (e.g. `ApiClient` on a failed token refresh) can send `SessionExpiredMessage` to force logout.

**Local database & migrations.** SQLite at `%LOCALAPPDATA%/PosSystem/pos.db` (or a per-tenant DB file when `tenant_db_runtime_enabled` is on — see below), managed by **real EF Core Migrations** (`Migrations/` folder). `App.xaml.cs:EnsureDatabase()` first runs `BaselineExistingDatabaseIfNeeded()` (detects legacy EnsureCreated-era DBs, additively repairs columns, stamps `InitialCreate` as applied) then `db.Database.Migrate()`. Schema changes = new migration + Designer + snapshot update, all three kept exactly consistent with the entity (the EF CLI cannot run on macOS for this WPF target, so migrations may be hand-authored mirroring the `InitialCreate` pair). One-time data cleanups live in `CleanupLegacySeedRows()`, guarded by the `legacy_seed_cleanup_done` settings marker. There is no demo seed data.

**Per-tenant DB switching (flag-gated, default OFF).** `TenantAwareLocalDatabasePathProvider` + `TenantAwareDbContextFactory` allow each tenant its own DB file, switched at login via `TenantScopeService` behind `TenantCutoverReadinessGate`. While a switch is in progress, `TenantAuthHeaderHandler` fails fast any settings-dependent HTTP request.

**Offline-first sales & sync.** Sales are written locally with a client-generated `LocalId` and `Synced = false`; `SyncService` (5-minute timer + manual triggers) pushes pending sales first (so backend stock is decremented before products are re-fetched), then pulls reference data, products, and customers. Server-side identity is tracked via `RemoteUuid`/`ServerUuid` columns on entities — local int IDs never leave the device. Each sync section catches its own errors so one failure doesn't abort the rest.

**API client.** `Services/ApiClient.cs` is the single HTTP gateway. Multi-tenant: every request carries an `X-Tenant-ID` header (tenant subdomain) plus a Bearer token, stamped **per-request** by `TenantAuthHeaderHandler` (a DelegatingHandler reading settings at send time — never mutate `DefaultRequestHeaders`; requests that pre-set their own headers, like refresh and the logout revocation, are preserved). All authenticated calls go through `Get/Post/PutWithRefreshAsync`, which auto-refresh the access token on 401 (guarded by a semaphore) and broadcast a **debounced** `SessionExpiredMessage` if refresh fails. Tokens are stored DPAPI-encrypted via `SettingsRepository.SetEncrypted/GetDecrypted` (`TokenProtector`, `enc:v1:` prefix); other settings are plain key/value rows in the `Settings` table — there is no config file. List endpoints are paginated (`PageResponse<T>`); follow the existing loop pattern when adding new ones. HTTP traffic is logged through `NetworkLogHandler` into `NetworkLogService` (viewable in `NetworkLogWindow`).

**Logout keeps unsynced sales.** `AuthService.Logout()` captures tokens+tenant first and fires a best-effort server-side revocation, then `ClearUserData()`: in legacy shared-DB mode it wipes the catalog, tenant routing settings, and **synced** sales only — unsynced sales always survive and sync after re-login (their `LocalId` doubles as the order idempotency key, so never regenerate it). Session-expiry auto-logout skips revocation. Bear the wipe list in mind when adding settings keys.

**Sales ↔ shifts.** Checkout requires an open POS shift; each `Sale` stores `ShiftUuid` and `SyncSaleAsync` sends it as `shiftUuid` on the order POST (omitted when null for legacy rows) so the backend Z-report can reconcile the drawer. Money math invariant: `sum(transactions) == apiTotal` exactly, with the cart-wide discount distributed into line `discountPrice` (largest-remainder) and a 0.01 epsilon for rounding drift in the mixed-payment split.
