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

**Local database & "migrations."** SQLite at `%LOCALAPPDATA%/PosSystem/pos.db`, created with `EnsureCreated()` — there are **no EF migrations**. Schema changes for existing installs go in `App.xaml.cs:ApplySchemaMigrations()` as raw idempotent SQL (`CREATE TABLE IF NOT EXISTS`, `ALTER TABLE ADD COLUMN` wrapped in try/catch). Add any new column/table there as well as to the entity. Demo data is seeded only when there's no authenticated session.

**Offline-first sales & sync.** Sales are written locally with a client-generated `LocalId` and `Synced = false`; `SyncService` (5-minute timer + manual triggers) pushes pending sales first (so backend stock is decremented before products are re-fetched), then pulls reference data, products, and customers. Server-side identity is tracked via `RemoteUuid`/`ServerUuid` columns on entities — local int IDs never leave the device. Each sync section catches its own errors so one failure doesn't abort the rest.

**API client.** `Services/ApiClient.cs` is the single HTTP gateway. Multi-tenant: every request carries an `X-Tenant-ID` header (tenant subdomain) plus a Bearer token. All authenticated calls go through `Get/Post/PutWithRefreshAsync`, which auto-refresh the access token on 401 (guarded by a semaphore) and broadcast `SessionExpiredMessage` if refresh fails. Server URL, tenant, tokens, and user info are persisted as key/value rows in the `Settings` table via `SettingsRepository` — there is no config file for these. List endpoints are paginated (`PageResponse<T>`); follow the existing loop pattern when adding new ones. HTTP traffic is logged through `NetworkLogHandler` into `NetworkLogService` (viewable in `NetworkLogWindow`).

**Logout wipes data.** `AuthService.Logout()` deletes all business rows (sales, products, customers, etc.) and tenant/user settings, keeping only machine-level settings like `api_base_url`. Bear this in mind when adding new tables or settings keys — decide whether they belong in the wipe list.
