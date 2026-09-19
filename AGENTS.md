# Repository Guidelines

## Project Structure & Module Organization

Pi Harbor is a Windows x64 WPF client for local Pi conversations.

- `src/PIHarness.Core/`: session discovery, JSONL parsing, search, local names, and Pi RPC processes.
- `src/PIHarness.App/`: XAML views, view models, presentation helpers, themes, and `Assets/Pi-Harbor.ico`.
- `tests/PIHarness.Tests/`: executable regression suite and fake RPC infrastructure.
- `scripts/`: build, test, icon generation, and package verification scripts.
- `packaging/`: Inno Setup installer; `.github/workflows/`: CI and release automation.
- `docs/`: design records, guides, and sanitized screenshots. Generated outputs belong in ignored `artifacts/`.

## Build, Test, and Development Commands

Run from the repository root on Windows with .NET 10 SDK:

```powershell
dotnet build src/PIHarness.App/PIHarness.App.csproj -c Release
dotnet run --project src/PIHarness.App/PIHarness.App.csproj -c Release --no-build
powershell -NoProfile -File scripts/run-tests.ps1
git diff --check
```

These commands build, launch, test, and check whitespace, respectively. Real conversations require configured Pi; tests need no model credentials.

To refresh the standalone executable without packaging:

```powershell
dotnet publish src/PIHarness.App/PIHarness.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/Pi-Harbor-win-x64
```

## Coding Style & Naming Conventions

Use four-space indentation, file-scoped C# namespaces, PascalCase types/members, `_camelCase` private fields, and `Async` suffixes for asynchronous methods. Follow surrounding formatting. C# 14, nullable analysis, implicit usings, and warnings-as-errors are configured in `Directory.Build.props`. No dedicated formatter is configured. Keep filesystem/RPC logic in Core and UI updates on the WPF dispatcher.

## Testing Guidelines

Tests use repository-defined `[TestCase]` and `AssertEx`, not xUnit or `dotnet test`. Add `*Tests.cs` methods with unique `TEST-…` identifiers and descriptive behavior names. Use temporary directories and fake processes. Cover changed behavior, especially cancellation, corrupt/large sessions, persistence, and process cleanup. No numeric coverage threshold is configured. Run the full suite for code changes and appropriate UI checks; documentation-only changes need no application rebuild.

## Commit & Pull Request Guidelines

History favors concise Chinese action summaries, such as `修复云端测试对本机 Pi 安装的依赖`; clear English is also accepted. Follow the PR template: explain before/after behavior, validation, related issues when applicable, limitations, and sanitized UI screenshots.

## Agent Workflow & Security

Unless explicitly requested, retain the version number and skip release packaging, publishing, and installer verification. After code changes, refresh the generated executable and report its path. If that output is running, use a separate `artifacts/` directory instead of stopping it or overwriting loaded files. Reserve `build-release.ps1 -IncludeInstaller` for requested releases.

Never commit real session JSONL, credentials, `.pi/`, logs, build outputs, or unsanitized screenshots. Report vulnerabilities via `SECURITY.md`.
