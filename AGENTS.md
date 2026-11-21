# Repository Guidelines

## Project Structure & Module Organization

Pi Harbor is a desktop client for local Pi conversations: a Windows x64 WPF app and a cross-platform Avalonia app on Linux that share one Core + ViewModels layer.

- `src/PIHarness.Core/`: session discovery, JSONL parsing, search, local names, and Pi RPC processes.
- `src/PIHarness.ViewModels/`: platform-neutral view models and presentation state shared by both frontends.
- `src/PIHarness.App/`: WPF-only XAML views, presentation helpers, themes, and `Assets/Pi-Harbor.ico`.
- `src/PIHarness.Avalonia/`: Avalonia frontend used on Linux (also builds on Windows).
- `tests/PIHarness.Tests/`: executable regression suite and fake RPC infrastructure.
- `scripts/`: build, test, icon generation, and package verification scripts, including `run-tests.sh`, `build-release-linux.sh`, and `install-linux.sh`.
- `packaging/`: Inno Setup installer, Linux `.desktop` template, and launcher icon; `.github/workflows/`: CI and release automation.
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

On Ubuntu 24.04 with .NET 10 SDK, build and test the Avalonia frontend directly (the solution contains the Windows-only WPF project):

```bash
dotnet build src/PIHarness.Avalonia/PIHarness.Avalonia.csproj -c Release
./scripts/run-tests.sh
./scripts/build-release-linux.sh --skip-tests
./scripts/install-linux.sh --dock
```

The test project targets `net10.0-windows` on Windows and `net10.0` on Linux, so WPF-only tests are compiled only on Windows.

## Coding Style & Naming Conventions

Use four-space indentation, file-scoped C# namespaces, PascalCase types/members, `_camelCase` private fields, and `Async` suffixes for asynchronous methods. Follow surrounding formatting. C# 14, nullable analysis, implicit usings, and warnings-as-errors are configured in `Directory.Build.props`. No dedicated formatter is configured. Keep filesystem/RPC logic in Core, shared presentation state in ViewModels, and UI updates on the owning UI dispatcher. WPF-only code must not enter Core or ViewModels.

## Testing Guidelines

Tests use repository-defined `[TestCase]` and `AssertEx`, not xUnit or `dotnet test`. Add `*Tests.cs` methods with unique `TEST-…` identifiers and descriptive behavior names. Mark assertions that only hold on one OS with `[PlatformOnly("windows")]` or `[PlatformOnly("linux")]` so the suite stays green on both platforms. Use temporary directories and fake processes. Cover changed behavior, especially cancellation, corrupt/large sessions, persistence, and process cleanup. No numeric coverage threshold is configured. Run the full suite for code changes and appropriate UI checks; documentation-only changes need no application rebuild.

## Commit & Pull Request Guidelines

History favors concise Chinese action summaries, such as `修复云端测试对本机 Pi 安装的依赖`; clear English is also accepted. Follow the PR template: explain before/after behavior, validation, related issues when applicable, limitations, and sanitized UI screenshots.

## Agent Workflow & Security

Review each feature from the user's workflow before acceptance: check information hierarchy, space usage, unnecessary labels/actions, scrolling, focus, and state continuity. For related sidebar lists, prefer one scrolling surface and collapsible groups. Verify realistic item counts and minimum window size with actual UI interaction; control existence alone is insufficient.

Unless explicitly requested, retain the version number and skip release packaging, publishing, and installer verification. After code changes, refresh the generated executable and report its path. If that output is running, use a separate `artifacts/` directory instead of stopping it or overwriting loaded files. Reserve `build-release.ps1 -IncludeInstaller` for requested releases.

Never commit real session JSONL, credentials, `.pi/`, logs, build outputs, or unsanitized screenshots. Report vulnerabilities via `SECURITY.md`.
