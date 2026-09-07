# Pi Harbor 品牌更名实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 PI-Harness 的用户可见品牌、Windows 程序名和发布包统一为 `Pi Harbor`，并以 `Pi Session Desk` 作为副标题交付 1.2.1。

**Architecture:** 仅替换应用身份层和交付层，保留 `PIHarness.*` 命名空间、项目目录、会话数据与 RPC 行为。通过现有无依赖测试程序锁定名称契约，再修改 WPF 品牌区、程序集元数据、构建脚本和当前文档，最后从自包含 Release 包完成视觉与大会话回归。

**Tech Stack:** C#、WPF、.NET 10、PowerShell、Git

## Global Constraints

- 主品牌固定为 `Pi Harbor`，副标题固定为 `Pi Session Desk`。
- 版本固定为 `1.2.1`，文件版本固定为 `1.2.1.0`。
- 新 EXE 为 `Pi-Harbor.exe`，新 ZIP 为 `Pi-Harbor-win-x64.zip`。
- 不修改 `PIHarness.App`、`PIHarness.Core`、`PIHarness.Tests` 命名空间和项目目录。
- 不增加第三方 NuGet 或运行服务依赖。
- 不删除旧 `artifacts/PI-Harness-*` 历史产物。
- 所有 Git 提交使用中文，只暂存任务文件。

---

### Task 1: 锁定应用身份契约

**Files:**
- Modify: `tests/PIHarness.Tests/ApplicationFeatureContractTests.cs`

**Interfaces:**
- Consumes: `MainViewModel.ApplicationVersion`、`MainViewModel.WindowTitle` 和应用 csproj/XAML 文件。
- Produces: `TEST-22D` 品牌与版本回归契约，供后续实现验证。

- [ ] **Step 1: 先把现有身份测试改为新品牌期望**

将 `TEST-22D` 扩展为以下断言：

```csharp
AssertEx.Equal("Pi-Harbor", values["AssemblyName"], "可执行程序集必须使用新品牌名");
AssertEx.Equal("Pi Harbor", values["Product"], "Windows 产品元数据必须使用主品牌");
AssertEx.True(values["Description"].Contains("Pi Session Desk", StringComparison.Ordinal), "产品描述必须包含副标题");
AssertEx.Equal("1.2.1", values["Version"], "包版本必须为 1.2.1");
AssertEx.Equal("1.2.1.0", values["FileVersion"], "文件版本必须为 1.2.1.0");
AssertEx.Equal("Pi Harbor 1.2.1 — Pi Session Desk", MainViewModel.WindowTitle, "窗口标题必须同时表达主品牌和副标题");
AssertEx.True(xaml.Contains("Text=\"Pi Harbor\"", StringComparison.Ordinal), "侧栏必须显示主品牌");
AssertEx.True(xaml.Contains("Text=\"Pi Session Desk\"", StringComparison.Ordinal), "侧栏必须显示功能副标题");
```

- [ ] **Step 2: 运行测试确认先失败**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1`

Expected: `TEST-22D` 因程序集、版本或 XAML 仍为旧品牌而失败。

- [ ] **Step 3: 提交测试节点**

```powershell
git add -- tests/PIHarness.Tests/ApplicationFeatureContractTests.cs
git commit -m '测试：锁定Pi Harbor应用身份契约'
```

### Task 2: 更新 WPF 品牌与程序集身份

**Files:**
- Modify: `src/PIHarness.App/PIHarness.App.csproj`
- Modify: `src/PIHarness.App/ViewModels/MainViewModel.cs`
- Modify: `src/PIHarness.App/MainWindow.xaml`
- Modify: `scripts/generate-app-icon.ps1`
- Move: `src/PIHarness.App/Assets/PI-Harness.ico` to `src/PIHarness.App/Assets/Pi-Harbor.ico`
- Move: `PI-Harness.sln` to `Pi-Harbor.sln`

**Interfaces:**
- Consumes: Task 1 的身份契约。
- Produces: `Pi-Harbor.exe` 程序集、`Pi Harbor`/`Pi Session Desk` 界面品牌和 `Pi-Harbor.sln` 构建入口。

- [ ] **Step 1: 修改程序集元数据和版本**

将应用项目属性改为：

```xml
<AssemblyName>Pi-Harbor</AssemblyName>
<Product>Pi Harbor</Product>
<Description>Pi Session Desk - 集中停靠、浏览并继续本机 Pi 会话的桌面港湾</Description>
<Version>1.2.1</Version>
<AssemblyVersion>1.2.1.0</AssemblyVersion>
<FileVersion>1.2.1.0</FileVersion>
<InformationalVersion>1.2.1</InformationalVersion>
<ApplicationIcon>Assets\Pi-Harbor.ico</ApplicationIcon>
```

- [ ] **Step 2: 修改窗口标题与侧栏品牌层级**

`MainViewModel` 使用：

```csharp
public const string ProductName = "Pi Harbor";
public const string ProductSubtitle = "Pi Session Desk";
public string WindowTitle => $"{ProductName} {ApplicationVersion} — {ProductSubtitle}";
```

侧栏品牌区改为主标题/版本第一行、副标题第二行；副标题使用 `MutedTextBrush` 和 11px 字号。

- [ ] **Step 3: 重命名解决方案和图标资源**

确认源、目标都位于仓库内后，将 `PI-Harness.sln` 更名为 `Pi-Harbor.sln`，将图标更名为 `Pi-Harbor.ico`，并同步 XAML、csproj 和图标脚本默认输出路径。

- [ ] **Step 4: 运行测试确认身份契约通过**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1`

Expected: 总计 50，通过 50，失败 0。

- [ ] **Step 5: 构建新解决方案**

Run: `dotnet build .\Pi-Harbor.sln -c Release`

Expected: 生成 `Pi-Harbor.exe`，0 个错误。

- [ ] **Step 6: 提交应用身份节点**

```powershell
git add -- Pi-Harbor.sln src/PIHarness.App/PIHarness.App.csproj src/PIHarness.App/ViewModels/MainViewModel.cs src/PIHarness.App/MainWindow.xaml src/PIHarness.App/Assets/Pi-Harbor.ico scripts/generate-app-icon.ps1
git add -u -- PI-Harness.sln src/PIHarness.App/Assets/PI-Harness.ico
git commit -m '功能：将应用品牌更名为Pi Harbor'
```

### Task 3: 更新发布流程和当前文档

**Files:**
- Modify: `scripts/build-release.ps1`
- Modify: `scripts/run-tests.ps1`
- Modify: `tests/PIHarness.Tests/Program.cs`
- Modify: `README.md`
- Move and modify: `docs/260907PI-Harness项目移交说明.md` to `docs/260907PiHarbor项目移交说明.md`

**Interfaces:**
- Consumes: Task 2 生成的 `Pi-Harbor.exe`。
- Produces: `artifacts/Pi-Harbor-win-x64/`、`artifacts/Pi-Harbor-win-x64.zip` 和新品牌交付说明。

- [ ] **Step 1: 修改发布脚本的全部交付文件名**

使用以下固定值：

```powershell
$publishDir = Join-Path $artifactsRoot 'Pi-Harbor-win-x64'
$zipPath = Join-Path $artifactsRoot 'Pi-Harbor-win-x64.zip'
$smokeCapture = Join-Path $artifactsRoot 'Pi-Harbor-release-smoke.png'
$mainExecutable = Join-Path $publishDir 'Pi-Harbor.exe'
```

异常和输出信息同步使用 `Pi Harbor` 或 `Pi-Harbor.exe`。

- [ ] **Step 2: 更新 README 和当前移交说明**

README 首段写明：

```markdown
# Pi Harbor

**Pi Session Desk** —— 集中停靠、浏览并继续本机 Pi 会话的桌面港湾。
```

运行、构建、产物和 QA 命令全部指向 `Pi-Harbor` 新名称。移交说明更新当前品牌、1.2.1、最新提交基线和新发布包；历史 specs/plans 的旧名称不改。

- [ ] **Step 3: 完整测试并构建自包含包**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1`

Expected: 测试 50/50 通过，生成 `artifacts\Pi-Harbor-win-x64\Pi-Harbor.exe` 和 `artifacts\Pi-Harbor-win-x64.zip`。

- [ ] **Step 4: 校验程序集和 ZIP**

Run:

```powershell
(Get-Item .\artifacts\Pi-Harbor-win-x64\Pi-Harbor.exe).VersionInfo | Format-List ProductName,FileDescription,FileVersion,ProductVersion
Get-FileHash .\artifacts\Pi-Harbor-win-x64.zip -Algorithm SHA256
```

Expected: 产品为 `Pi Harbor`，描述包含 `Pi Session Desk`，版本为 `1.2.1.0`，ZIP 有有效 SHA-256。

- [ ] **Step 5: 提交发布与文档节点**

```powershell
git add -- scripts/build-release.ps1 scripts/run-tests.ps1 tests/PIHarness.Tests/Program.cs README.md docs/260907PiHarbor项目移交说明.md
git add -u -- docs/260907PI-Harness项目移交说明.md
git commit -m '发布：交付Pi Harbor 1.2.1品牌版本'
```

### Task 4: 发布包视觉与大会话回归

**Files:**
- Generated, ignored: `artifacts/qa/pi-harbor-1.2.1-*`

**Interfaces:**
- Consumes: Task 3 的自包含 Release 包和已有只读 QA 参数。
- Produces: 品牌截图、大会话滚动报告和最终 Git 洁净状态。

- [ ] **Step 1: 从发布 EXE 捕获品牌界面**

Run:

```powershell
.\artifacts\Pi-Harbor-win-x64\Pi-Harbor.exe --capture-ui .\artifacts\qa\pi-harbor-1.2.1-brand.png
```

Expected: 截图显示 `Pi Harbor`、`Pi Session Desk` 和 `v1.2.1`，文字无截断且侧栏保持暗色统一。

- [ ] **Step 2: 对真实大会话执行只读滚动 QA**

Run:

```powershell
$sessionRoot = Join-Path $env:USERPROFILE '.pi\agent\sessions'
$largeSession = Get-ChildItem -LiteralPath $sessionRoot -Recurse -Filter '*.jsonl' -File |
    Sort-Object Length -Descending |
    Select-Object -First 1
.\artifacts\Pi-Harbor-win-x64\Pi-Harbor.exe --capture-session $largeSession.FullName --qa-scroll-capture-dir .\artifacts\qa\pi-harbor-1.2.1-scroll
```

Expected: `TEST-UI-01` 至 `TEST-UI-08` 全部通过，不发送模型请求。

- [ ] **Step 3: 检查 Git 与提交历史**

Run:

```powershell
git diff --check
git status --short --branch
git log -6 --oneline
```

Expected: 无 diff 检查错误，工作区干净，品牌设计、计划、测试、实现和发布节点均为中文提交。
