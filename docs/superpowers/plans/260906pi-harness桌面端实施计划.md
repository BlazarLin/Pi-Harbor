# PI-Harness 桌面端实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建一款能自动发现、新建并继续本机 pi 会话的 Windows 桌面端应用。

**Architecture:** 使用 .NET 10 将会话索引、RPC 协议和 WPF 界面分成独立单元。核心库负责 JSONL 容错解析与 pi 子进程生命周期，WPF 应用只负责交互和呈现，无第三方 NuGet 依赖。

**Tech Stack:** C# 14，.NET 10，WPF，`System.Text.Json`，`System.Diagnostics.Process`，`FileSystemWatcher`，PowerShell，Git。

## Global Constraints

- 目标系统为 Windows 10/11 x64。
- 用户已安装并配置的 pi 是唯一外部前置条件。
- 不依赖 pi-dashboard，不启动本地 HTTP/WebSocket 服务。
- 不引入第三方 NuGet 包。
- 发布包为 `win-x64` 自包含 Release 目录，解压后运行 `PI-Harness.exe`。
- 第一版仅允许一个活动 RPC 会话，生成期间禁止切换。
- 输入为纯文本，不支持图片附件、模型切换、删除、重命名、分叉编辑和会话导出。
- UI 文案和测试输出使用中文。
- 每个可独立验收的任务完成后创建一个 Git 提交。

---

## 文件结构

```text
PI-Harness.sln
Directory.Build.props
src/
  PIHarness.Core/
    PIHarness.Core.csproj
    Models/SessionModels.cs
    Models/ChatModels.cs
    Sessions/SessionParser.cs
    Sessions/SessionCatalog.cs
    Rpc/RpcLineParser.cs
    Rpc/PiProcessLocator.cs
    Rpc/PiRpcClient.cs
    Rpc/PiRpcEvents.cs
  PIHarness.App/
    PIHarness.App.csproj
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    ViewModels/ObservableObject.cs
    ViewModels/RelayCommand.cs
    ViewModels/MainViewModel.cs
    Presentation/MarkdownInlineParser.cs
    Presentation/ChatMessageTemplateSelector.cs
    Themes/Colors.xaml
tests/
  PIHarness.Tests/
    PIHarness.Tests.csproj
    Program.cs
    TestFramework.cs
    SessionParserTests.cs
    SessionCatalogTests.cs
    RpcLineParserTests.cs
    PiProcessLocatorTests.cs
    PiRpcClientTests.cs
    MainViewModelTests.cs
scripts/
  build-release.ps1
  run-tests.ps1
```

`PIHarness.Core` 不引用 WPF，使会话解析与 RPC 可由独立控制台测试程序验证。`PIHarness.App` 引用核心库并实现桌面交互。`PIHarness.Tests` 使用自有轻量断言和测试运行器，避免为测试引入 NuGet 依赖。

### Task 1: 解决方案骨架与无依赖测试运行器

**Files:**
- Create: `PI-Harness.sln`
- Create: `Directory.Build.props`
- Create: `src/PIHarness.Core/PIHarness.Core.csproj`
- Create: `src/PIHarness.App/PIHarness.App.csproj`
- Create: `tests/PIHarness.Tests/PIHarness.Tests.csproj`
- Create: `tests/PIHarness.Tests/TestFramework.cs`
- Create: `tests/PIHarness.Tests/Program.cs`
- Create: `scripts/run-tests.ps1`

**Interfaces:**
- Produces: `TestCaseAttribute`，`AssertEx.True(bool, string)`，`AssertEx.Equal<T>(T, T, string)`，以及返回进程退出码的测试运行器。

- [ ] **Step 1: 创建项目和统一构建属性**

`Directory.Build.props` 固定 `net10.0`、`x64`、nullable 和警告级别：

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <DebugType>portable</DebugType>
    <DebugSymbols>true</DebugSymbols>
  </PropertyGroup>
</Project>
```

WPF 项目覆盖为 `net10.0-windows`，保留 Release PDB：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <AssemblyName>PI-Harness</AssemblyName>
    <RootNamespace>PIHarness.App</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\PIHarness.Core\PIHarness.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 实现轻量测试运行器**

`Program.cs` 通过反射查找 `[TestCase]` 方法，逐项输出 `[通过] TEST-xx` 或 `[失败] TEST-xx`，全部通过时返回 `0`，任意失败时返回 `1`。

- [ ] **Step 3: 验证 Debug/Release 构建和测试运行器**

Run:

```powershell
dotnet build .\PI-Harness.sln -c Debug
dotnet build .\PI-Harness.sln -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1
```

Expected: Debug/Release 均为 `0 个警告，0 个错误`，测试运行器输出总数且退出码为 `0`。

- [ ] **Step 4: 提交骨架节点**

```powershell
git add -- PI-Harness.sln Directory.Build.props src tests scripts
git commit -m "build: scaffold PI-Harness solution"
```

### Task 2: pi 会话解析、分组与自动刷新

**Files:**
- Create: `src/PIHarness.Core/Models/SessionModels.cs`
- Create: `src/PIHarness.Core/Sessions/SessionParser.cs`
- Create: `src/PIHarness.Core/Sessions/SessionCatalog.cs`
- Create: `tests/PIHarness.Tests/SessionParserTests.cs`
- Create: `tests/PIHarness.Tests/SessionCatalogTests.cs`

**Interfaces:**
- Produces: `SessionSummary(string SessionPath, string Cwd, string Title, DateTimeOffset CreatedAt, DateTimeOffset LastActivityAt)`.
- Produces: `ProjectGroup(string Cwd, string DisplayName, IReadOnlyList<SessionSummary> Sessions, DateTimeOffset LastActivityAt)`.
- Produces: `SessionParser.ParseAsync(string path, CancellationToken) -> Task<SessionParseResult>`.
- Produces: `SessionCatalog.ScanAsync(string root, CancellationToken) -> Task<CatalogSnapshot>` 和 `Changed` 事件。

- [ ] **Step 1: 先写 TEST-01 至 TEST-04 的合成数据测试**

测试在 `%TEMP%\PIHarness.Tests\<guid>` 创建临时会话根，覆盖：

```csharp
[TestCase("TEST-01", "自动发现全部有效 JSONL")]
public static async Task DiscoversValidSessionsAsync();

[TestCase("TEST-02", "Windows cwd 大小写只建立一个项目组")]
public static async Task GroupsCwdCaseInsensitivelyAsync();

[TestCase("TEST-03", "会话命名优先级与活动时间排序正确")]
public static async Task ResolvesTitleAndSortOrderAsync();

[TestCase("TEST-04", "损坏 JSONL 和孤立 meta 不影响其他会话")]
public static async Task IsolatesMalformedSessionsAsync();
```

- [ ] **Step 2: 运行测试并确认因类型不存在而失败**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1`

Expected: TEST-01 至 TEST-04 至少一项编译或运行失败。

- [ ] **Step 3: 实现逐行容错解析**

`SessionParser` 只保留建立索引所需数据，用最新 `session_info.name` 覆盖首条用户文本，用最后有效条目时间作为活动时间。单行损坏时记录警告；会话头缺失 `cwd` 时整个会话视为无效。

- [ ] **Step 4: 实现分组扫描和监视合并**

`SessionCatalog` 递归查找 `*.jsonl`，限制并行解析数，使用 `StringComparer.OrdinalIgnoreCase` 分组。`FileSystemWatcher` 变化在 300 ms 稳定窗口后合并为一次 `Changed`。

- [ ] **Step 5: 运行合成数据与本机只读扫描**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1
dotnet run --project .\tests\PIHarness.Tests\PIHarness.Tests.csproj -c Release -- --live-session-root "$env:USERPROFILE\.pi\agent\sessions"
```

Expected: TEST-01 至 TEST-04 通过；本机扫描报告项目数、有效会话数和跳过数，不改写会话文件。

- [ ] **Step 6: 提交会话目录节点**

```powershell
git add -- src/PIHarness.Core/Models src/PIHarness.Core/Sessions tests/PIHarness.Tests
git commit -m "feat: discover and group local pi sessions"
```

### Task 3: pi RPC 帧解析与子进程生命周期

**Files:**
- Create: `src/PIHarness.Core/Models/ChatModels.cs`
- Create: `src/PIHarness.Core/Rpc/RpcLineParser.cs`
- Create: `src/PIHarness.Core/Rpc/PiProcessLocator.cs`
- Create: `src/PIHarness.Core/Rpc/PiRpcEvents.cs`
- Create: `src/PIHarness.Core/Rpc/PiRpcClient.cs`
- Create: `tests/PIHarness.Tests/RpcLineParserTests.cs`
- Create: `tests/PIHarness.Tests/PiProcessLocatorTests.cs`
- Create: `tests/PIHarness.Tests/PiRpcClientTests.cs`

**Interfaces:**
- Produces: `RpcLineParser.TryParse(string line, out JsonDocument? document)`.
- Produces: `PiLaunchInfo(string FileName, IReadOnlyList<string> Arguments)`.
- Produces: `PiProcessLocator.Find() -> PiLaunchResult`.
- Produces: `PiRpcClient.StartAsync(PiStartOptions, CancellationToken)`，`RequestAsync(string, object?, TimeSpan, CancellationToken)`，`SendPromptAsync(string, CancellationToken)`，`AbortAsync(CancellationToken)` 和 `DisposeAsync()`.
- Produces: `EventReceived`、`DiagnosticReceived`、`Exited` 事件。

- [ ] **Step 1: 先写 TEST-05、TEST-06、TEST-10、TEST-12 和 TEST-14**

```csharp
[TestCase("TEST-05", "RPC 解析忽略普通日志并保留后续有效 JSON")]
public static void IgnoresNonJsonDiagnostics();

[TestCase("TEST-06", "启动参数使用指定项目作为工作目录")]
public static void UsesSelectedWorkingDirectory();

[TestCase("TEST-10", "中止顺序为 clear_queue 后 abort")]
public static async Task ClearsQueueBeforeAbortAsync();

[TestCase("TEST-12", "进程缺失、退出和超时使请求以中文错误失败")]
public static async Task ReportsLifecycleFailuresInChineseAsync();

[TestCase("TEST-14", "释放 RPC 客户端后子进程退出")]
public static async Task DisposesOwnedProcessAsync();
```

- [ ] **Step 2: 实现 pi 定位与参数安全传递**

优先查找 `%APPDATA%\npm\pi.cmd`，再遍历 `PATH` 中的 `pi.cmd`。Windows 上通过 `%ComSpec% /d /s /c` 启动，所有动态参数由独立引号和内部转义函数处理，不拼接到 PowerShell。

- [ ] **Step 3: 实现 JSONL 协议客户端**

stdout 按 LF 分帧；非 JSON 行进入最多 200 条的诊断环形缓存。响应按 `id` 完成对应 `TaskCompletionSource`，事件立即投递。请求表和诊断缓存均有上限，进程退出时终止所有等待。

- [ ] **Step 4: 实现中止与可控回收**

`AbortAsync` 顺序请求 `clear_queue` 和 `abort`。`DisposeAsync` 先关闭 stdin 并等待 2 s，再仅对当前客户端持有的 `Process` 调用 `Kill(entireProcessTree: true)`。

- [ ] **Step 5: 使用伪 RPC 进程和本机 pi 进行验证**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1
dotnet run --project .\tests\PIHarness.Tests\PIHarness.Tests.csproj -c Release -- --live-pi-probe
```

Expected: 伪进程测试验证帧乱序、超时和回收；本机探测使用 `--mode rpc --no-session --approve --offline` 成功获取 `get_state`，不发送 LLM 请求。

- [ ] **Step 6: 提交 RPC 节点**

```powershell
git add -- src/PIHarness.Core/Rpc src/PIHarness.Core/Models/ChatModels.cs tests/PIHarness.Tests
git commit -m "feat: add resilient pi RPC client"
```

### Task 4: 聊天状态编排与流式事件归并

**Files:**
- Create: `src/PIHarness.App/ViewModels/ObservableObject.cs`
- Create: `src/PIHarness.App/ViewModels/RelayCommand.cs`
- Create: `src/PIHarness.App/ViewModels/MainViewModel.cs`
- Create: `tests/PIHarness.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `SessionCatalog`，`PiRpcClient`，`SessionSummary`，`ChatMessage`.
- Produces: `MainViewModel.InitializeAsync()`，`CreateSessionAsync(string)`，`OpenSessionAsync(SessionSummary)`，`SendAsync()`，`StopAsync()`，`ShutdownAsync()`.
- Produces: `ObservableCollection<ProjectGroupViewModel> Projects` 和 `ObservableCollection<ChatItemViewModel> Messages`.

- [ ] **Step 1: 先写 TEST-07 至 TEST-09、TEST-11 和 TEST-13**

```csharp
[TestCase("TEST-07", "打开会话后加载当前有效分支的历史消息")]
public static async Task LoadsExistingMessagesAsync();

[TestCase("TEST-08", "助手增量文本归并到同一条消息")]
public static async Task CoalescesAssistantDeltasAsync();

[TestCase("TEST-09", "思考、工具、错误和中止使用正确显示类型")]
public static async Task MapsRpcEventsToChatItemsAsync();

[TestCase("TEST-11", "目录变化刷新侧边栏且保留当前选中")]
public static async Task RefreshesCatalogWithoutLosingSelectionAsync();

[TestCase("TEST-13", "连续切换空闲会话始终只保留一个 RPC 客户端")]
public static async Task KeepsOnlyOneActiveClientAsync();
```

- [ ] **Step 2: 实现明确状态机**

`Idle -> Starting -> Ready -> Streaming -> Stopping -> Ready`；任意失败转为 `Faulted`，可通过重新选择会话恢复。仅 `Ready` 允许发送和切换，仅 `Streaming` 允许停止。

- [ ] **Step 3: 实现历史映射和流式归并**

`get_messages` 映射 user、assistant、toolResult、bashExecution、custom、branchSummary 和 compactionSummary。流式 `message_update` 只更新当前消息对应的文本/思考块，不重建整个集合；UI 更新以约 33 ms 节流到主线程。

- [ ] **Step 4: 运行状态编排测试**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1`

Expected: TEST-07 至 TEST-13 中已实现的测试通过，测试后活动伪进程数为 `0`。

- [ ] **Step 5: 提交聊天编排节点**

```powershell
git add -- src/PIHarness.App/ViewModels tests/PIHarness.Tests/MainViewModelTests.cs
git commit -m "feat: orchestrate chat session state"
```

### Task 5: Codex 风格 WPF 两栏界面与聊天排版

**Files:**
- Create: `src/PIHarness.App/App.xaml`
- Create: `src/PIHarness.App/App.xaml.cs`
- Create: `src/PIHarness.App/MainWindow.xaml`
- Create: `src/PIHarness.App/MainWindow.xaml.cs`
- Create: `src/PIHarness.App/Presentation/MarkdownInlineParser.cs`
- Create: `src/PIHarness.App/Presentation/ChatMessageTemplateSelector.cs`
- Create: `src/PIHarness.App/Themes/Colors.xaml`

**Interfaces:**
- Consumes: `MainViewModel` 公开命令和可观察集合。
- Produces: 无边框黑暗主窗口，左侧两级项目/会话树，右侧会话头、消息列表和输入区。

- [ ] **Step 1: 实现视觉资源和主窗口布局**

颜色资源固定为深色背景、中性分隔线和低饱和选中色。侧边栏默认宽度 300 px，最小 240 px，最大 420 px；主窗口最小尺寸 960x640，默认 1280x820。

- [ ] **Step 2: 实现项目/会话两级树**

项目行只显示文件夹图标和末级名称；会话行缩进且单行省略。不放置卡片、badge 和消息摘要。顶部仅保留“新对话”和“刷新”。

- [ ] **Step 3: 实现聊天消息模板与轻量 Markdown**

`MarkdownInlineParser` 将纯文本分解为 `Run`、`Bold`、`InlineUIContainer` 和代码块 `Border/TextBlock`，它不解析或执行 HTML。工具和思考消息使用可折叠的弱化模板。

- [ ] **Step 4: 实现键盘、滚动和文件夹选择**

`Ctrl+Enter` 调用发送命令，`Enter` 保留换行。使用 `OpenFolderDialog` 选择 cwd。只当滚动条原本接近底部时随流式内容自动滚动。

- [ ] **Step 5: 验证构建并提交 UI 节点**

Run: `dotnet build .\PI-Harness.sln -c Release`

Expected: `0 个警告，0 个错误`，WPF 生成资源和 XAML 均编译成功。

```powershell
git add -- src/PIHarness.App
git commit -m "feat: build Codex-style desktop chat interface"
```

### Task 6: Release 发布、实机联调与最终验收

**Files:**
- Create: `scripts/build-release.ps1`
- Modify: `tests/PIHarness.Tests/Program.cs`
- Modify: `README.md`

**Interfaces:**
- Produces: `artifacts/PI-Harness-win-x64/PI-Harness.exe` 和同目录自包含运行文件。
- Produces: `scripts/build-release.ps1` 一键清理目标发布目录、测试、发布并检查主 EXE。

- [ ] **Step 1: 实现发布脚本**

```powershell
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot 'artifacts\PI-Harness-win-x64'
dotnet run --project (Join-Path $repoRoot 'tests\PIHarness.Tests\PIHarness.Tests.csproj') -c Release
dotnet publish (Join-Path $repoRoot 'src\PIHarness.App\PIHarness.App.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $publishDir
if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'PI-Harness.exe'))) { throw '未生成 PI-Harness.exe' }
```

- [ ] **Step 2: 执行全量自动化测试和本机只读探测**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1
dotnet run --project .\tests\PIHarness.Tests\PIHarness.Tests.csproj -c Release -- --live-session-root "$env:USERPROFILE\.pi\agent\sessions" --live-pi-probe
```

Expected: TEST-01 至 TEST-14 自动化项目通过，本机探测不调用 LLM，不改写已有会话。

- [ ] **Step 3: 执行 TEST-07/08/10 的隔离实机会话联调**

在 `tests/runtime/live-project` 临时项目中创建新会话，发送一条明确的低成本测试提示，验证流式回复、停止后恢复和再次打开。不在用户生产项目中写入测试会话。

- [ ] **Step 4: 执行 TEST-15 发布验证**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1`

Expected: 发布目录包含 `PI-Harness.exe`、.NET 自包含组件和 PDB，从发布目录可直接启动。

- [ ] **Step 5: 执行 TEST-16 视觉检查**

使用本机 100% 和 150% DPI 分别检查 1280x820 与 960x640，覆盖空态、多项目列表、长中文、多行代码块、生成中和故障提示。保存截图到忽略的 `artifacts/qa/`。

- [ ] **Step 6: 检查仓库完整性并提交交付节点**

Run:

```powershell
git diff --check
git status --short
git log --oneline --decorate -10
```

Expected: 无冲突标记、无空白错误，待提交内容只有脚本、README 和验收调整。

```powershell
git add -- README.md scripts tests/PIHarness.Tests/Program.cs
git commit -m "release: validate PI-Harness desktop package"
```

## 最终完成标准

- TEST-01 至 TEST-16 均有可复核结果。
- Release 发布包可启动、可新建会话、可继续会话、可停止生成。
- 关闭应用后无由 PI-Harness 创建的残留 RPC 子进程。
- Git 历史按设计、骨架、会话目录、RPC、聊天编排、UI 和发布验收等节点划分。
- 工作树清洁，构建产物和运行日志未进入 Git。
