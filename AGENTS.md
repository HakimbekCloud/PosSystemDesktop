# Repository Guidelines

## Project Structure & Module Organization

This repository contains a .NET 8 WPF POS application in a single project, `PosSystem.csproj`.

- `App.xaml`, `MainWindow.xaml`, and matching `.cs` files define application startup and shell behavior.
- `Views/` contains WPF screens and code-behind, grouped by feature such as `Views/Pos/` and `Views/Game/`.
- `ViewModels/` contains MVVM view models. Keep feature-specific view models in matching subfolders.
- `Core/Entities/` and `Core/DTOs/` hold domain models and API transfer objects.
- `Data/` contains `AppDbContext` and repository classes for persistence.
- `Services/`, `Helpers/`, and `Converters/` contain application services, UI helpers, and WPF binding converters.
- `bin/` and `obj/` are build outputs and should not be edited manually.

## Build, Test, and Development Commands

Run commands from the repository root:

```powershell
dotnet restore
dotnet build PosSystem.csproj
dotnet run --project PosSystem.csproj
dotnet clean PosSystem.csproj
```

`dotnet restore` downloads NuGet packages. `dotnet build` compiles the WPF app for `net8.0-windows`. `dotnet run` launches the local desktop application. `dotnet clean` removes generated build outputs when local artifacts become stale.

## Coding Style & Naming Conventions

Use C# with nullable reference types enabled and implicit usings. Follow standard .NET naming: `PascalCase` for public types, properties, methods, and view model classes; `camelCase` for local variables and private fields. Match existing four-space indentation in C# and XAML. Keep XAML names descriptive, and use `*View.xaml` with matching `*View.xaml.cs` for screens and user controls. Prefer MVVM logic in `ViewModels/`; keep code-behind limited to view wiring.

## Testing Guidelines

No test project is currently present. For new tests, add a separate project such as `PosSystem.Tests` and use `dotnet test` as the standard entry point. Name test files after the class under test, for example `ProductRepositoryTests.cs`, and use clear behavior names such as `AddAsync_PersistsProduct`. Prioritize repositories, services, and view model logic before UI automation.

## Commit & Pull Request Guidelines

Recent commits use short messages like `new feature`, so there is no strict project convention yet. Use more specific imperative messages, for example `Add sale history filter` or `Fix POS cart total calculation`.

Pull requests should include a concise description, affected areas, manual test steps, and screenshots or screen recordings for UI changes. Link related issues when available. Mention any database, API, or configuration changes explicitly.

## Security & Configuration Tips

Do not commit secrets, local database files, or machine-specific settings. Keep API endpoints and credentials out of source files; prefer configuration values that can vary by environment.
