# 底部滚动、复制与 Markdown 可视化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 精确对齐最新消息，支持详细对话跨段选择复制，并将 Markdown 表格等结构渲染成可读界面。

**Architecture:** 消息列表保留虚拟化但改用像素偏移，自动跟随在末项生成后由内部 `ScrollViewer.ScrollToEnd()` 精确收尾。Markdown 控件保持原有依赖属性接口，内部改为只读 `RichTextBox` 和 `FlowDocument`，由依赖无关的解析逻辑生成段落、列表、代码、引用、分隔线和表格。

**Tech Stack:** .NET 10、WPF、C#、XAML、自研 Markdown 解析、无第三方包、Git。

## Global Constraints

- 不增加第三方依赖。
- 不关闭 `MessageList` 虚拟化。
- 流式 Markdown 继续以 40 ms 合并刷新，完成时同步定稿。
- 所有提交信息使用中文。
- Release 版本升级为 1.2.0。
- 使用 121 MB、1251 项真实只读会话执行性能与视觉验收。

---

### Task 1: 像素级底部滚动

**Files:**
- Modify: `tests/PIHarness.Tests/ConversationUiContractTests.cs`
- Modify: `tests/PIHarness.Tests/ScrollQaContractTests.cs`
- Modify: `src/PIHarness.App/MainWindow.xaml`
- Modify: `src/PIHarness.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `MessageList`、内部 `ScrollViewer`、`ChatScrollCoordinator.IsFollowingLatest`。
- Produces: `ScrollUnit="Pixel"`、`ScrollMessageListToEnd()`、像素连续性和底边对齐 QA。

- [ ] **Step 1: 写入失败合同**

在 `ConversationUiContractTests` 将滚动单位断言改为 `Pixel`，并断言自动跟随代码同时包含 `ScrollIntoView` 与 `ScrollToEnd`。在 `ScrollQaContractTests` 断言报告包含 `TEST-UI-07`、`TEST-UI-08`、`BottomGap` 和 `SmallScrollDelta`。

- [ ] **Step 2: 运行并确认失败**

Run: `dotnet run --project tests/PIHarness.Tests/PIHarness.Tests.csproj -c Release`

Expected: 像素滚动和新增 QA 字段尚不存在，相关测试失败。

- [ ] **Step 3: 实现精确自动跟随**

将 XAML 改为：

```xml
VirtualizingPanel.ScrollUnit="Pixel"
```

在原有后台操作中先执行：

```csharp
MessageList.ScrollIntoView(_viewModel.Messages[^1]);
MessageList.UpdateLayout();
FindVisualChild<ScrollViewer>(MessageList)?.ScrollToEnd();
```

使“回到最新”和新增消息共用该路径。

- [ ] **Step 4: 增加运行时滚动验收**

QA 使用 80、320、160 DIP 的滚动距离。测量末项容器底边与 `ScrollViewer` 视口底边的差值作为 `BottomGap`；从底部上移 24 DIP，记录实际偏移差作为 `SmallScrollDelta`。`BottomGap <= 2` 且偏移差在 22–26 DIP 时通过。

- [ ] **Step 5: 测试并提交**

Run: `dotnet run --project tests/PIHarness.Tests/PIHarness.Tests.csproj -c Release`

Expected: 全部源码测试通过。

Commit: `修复：精确对齐最新消息并连续滚动`

---

### Task 2: 可选择 Markdown 与结构化表格

**Files:**
- Modify: `tests/PIHarness.Tests/MarkdownRenderingPolicyTests.cs`
- Modify: `src/PIHarness.App/Presentation/MarkdownInlineParser.cs`
- Modify: `src/PIHarness.App/MainWindow.xaml`

**Interfaces:**
- Consumes: `MarkdownTextBlock.Markdown`、`IsStreaming`、`Foreground`、`FontFamily`、`FontSize`。
- Produces: `MarkdownTextBlock.Document`、只读 `RichTextBox`、FlowDocument `Table`/`Paragraph`/`List`。

- [ ] **Step 1: 写入失败测试**

增加 STA 测试，断言控件为只读、允许文本选择，文档中的普通正文保留；输入：

```markdown
| 路径 | TLS 握手 | 评价 |
|---|---:|---|
| 代理端口 | 1.03s | 正常 |
| TUN | 10.39s | 严重异常 |
```

断言产生一个 `Table`、三列、一个表头和两个数据行，且文档文本不再包含分隔源码 `|---|`。再测试引用、分隔线、有序列表、粗体和行内代码对应的 FlowDocument 元素或样式。

- [ ] **Step 2: 运行并确认失败**

Run: `dotnet run --project tests/PIHarness.Tests/PIHarness.Tests.csproj -c Release`

Expected: 当前 StackPanel 控件不可选择且没有 FlowDocument 表格，测试失败。

- [ ] **Step 3: 改为只读 RichTextBox**

`MarkdownTextBlock` 继承 `RichTextBox`，构造函数固定：

```csharp
IsReadOnly = true;
IsReadOnlyCaretVisible = false;
BorderThickness = new Thickness(0);
Background = Brushes.Transparent;
Padding = new Thickness(0);
VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
Document.PagePadding = new Thickness(0);
Document.ColumnWidth = double.PositiveInfinity;
```

保留现有依赖属性与 40 ms 计时器，`RenderMarkdown()` 只重建 `Document.Blocks`。

- [ ] **Step 4: 实现 Markdown 块渲染**

逐行识别围栏代码、标题、无序/有序列表、引用、分隔线和普通段落。表格必须要求下一行是合法分隔行，按首尾竖线切列，列数不足时补空字符串，超出时并入最后一列；生成带主题边框、表头背景和 6×4 DIP 内边距的 WPF `Table`。

行内解析目标从 `TextBlock.Inlines` 改为 `InlineCollection`，继续生成 `Run`、`Bold` 和带等宽字体背景的行内代码。

- [ ] **Step 5: 测试、性能检查并提交**

Run: `dotnet run --project tests/PIHarness.Tests/PIHarness.Tests.csproj -c Release`

Expected: 全部测试通过，历史内容同步生成，流式内容仍合并刷新，主题更新后文档前景色正确。

Commit: `功能：支持对话选择复制与Markdown表格`

---

### Task 3: 真实会话验收与 1.2.0 发布

**Files:**
- Modify: `src/PIHarness.App/PIHarness.App.csproj`
- Modify: `src/PIHarness.App/ViewModels/MainViewModel.cs`
- Modify: `README.md`
- Generated: `artifacts/PI-Harness-win-x64.zip`

**Interfaces:**
- Consumes: `--qa-scroll-capture-dir`、`--qa-style-capture-dir`、`--capture-session`。
- Produces: 1.2.0 自包含包、滚动报告、Markdown 表格截图和文件哈希。

- [ ] **Step 1: 更新版本和使用说明**

将 `Version`、`AssemblyVersion`、`FileVersion`、`InformationalVersion` 统一更新为 1.2.0；README 说明像素滚动、选择复制和 Markdown 表格，并更新 QA 项目数量。

- [ ] **Step 2: 执行完整构建与测试**

Run: `dotnet build PI-Harness.sln -c Release`

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-tests.ps1`

Expected: 0 警告、0 错误，全部中文测试通过。

- [ ] **Step 3: 使用最大真实会话验收滚动**

Run: `PI-Harness.exe --capture-session <121MB-jsonl> --qa-scroll-capture-dir artifacts/qa/v1.2.0-scroll`

Expected: 原 TEST-UI-01 至 TEST-UI-06 以及新增底边、微量滚动测试全部通过；双帧哈希一致，单步小于 3000 ms，滑块长度固定。

- [ ] **Step 4: 捕获 Markdown 专项界面**

使用包含标题、段落、列表、代码块、引用和表格的本地只读会话捕获窗口。视觉检查表格不显示分隔源码、边界清楚、没有裁切；自动测试验证 `RichTextBox.IsReadOnly == true` 且选择接口可用。

- [ ] **Step 5: 发布、校验并提交**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-release.ps1`

Expected: ZIP 包含 EXE、DLL、PDB 和 runtimeconfig；EXE 文件版本为 1.2.0.0；`git diff --check` 通过，工作区干净。

Commit: `发布：交付PI-Harness 1.2.0阅读体验升级`

## 自审结果

- 三项用户要求均有源码测试、运行时 QA 和视觉证据。
- 未引入第三方依赖，未关闭虚拟化，未维护两套“回到最新”逻辑。
- 接口名称在任务间一致；没有占位项或与范围无关的重构。
