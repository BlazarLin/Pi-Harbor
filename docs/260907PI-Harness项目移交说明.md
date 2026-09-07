# PI-Harness 项目移交说明

- 移交日期：2026-09-07
- 仓库目录：`G:\Code\PI-Harness`
- 当前分支：`main`
- 移交基线：`3f8c033 测试：隔离消息区滚动稳定性验收`
- 软件版本：`1.2.0`（文件版本 `1.2.0.0`）
- 当前结论：既定功能已完成，自动测试、真实大会话滚动验收和 Markdown 视觉验收均通过，可进入交付或后续维护阶段。

## 1. 接手时先做

```powershell
Set-Location 'G:\Code\PI-Harness'
git status --short --branch
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1
```

预期结果：分支为 `main`，没有非预期改动；测试总计 50，通过 50，失败 0。

如需重新生成自包含发布包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

本机开发基线为 .NET SDK `10.0.300`，目标框架为 `net10.0-windows`，应用使用 WPF；项目没有第三方 NuGet 依赖。

## 2. 当前交付内容

功能、运行要求、使用方式、架构和明确边界统一以 [`README.md`](../README.md) 为准，避免在本说明中复制后产生两份不一致的功能清单。

代码职责：

- `src/PIHarness.Core/`：pi 会话发现、JSONL 解析、轻量历史读取、pi 定位和 RPC 生命周期。
- `src/PIHarness.App/`：WPF 界面、会话状态、模型切换、滚动协调、可选择 Markdown 显示和视觉 QA 入口。
- `tests/PIHarness.Tests/`：无第三方测试框架的中文验收程序。
- `scripts/run-tests.ps1`：完整自动测试入口。
- `scripts/build-release.ps1`：图标生成、测试、自包含发布、冒烟检查和 ZIP 打包入口。

原始调研与立项背景见 [`handoff-pi-desktop-gui-260903.md`](../handoff-pi-desktop-gui-260903.md)。其中“尚未决定自研”等内容已经过时，仅用于追溯方案来源；当前仓库即最终选定的自研实现。

## 3. 设计与实施记录索引

详细决策已经记录在下列文档中，后续不要把其全文复制到新的说明：

- 初始桌面端设计：[`260906pi-harness桌面端设计.md`](superpowers/specs/260906pi-harness桌面端设计.md)
- 大会话渲染性能：[`260906详细会话渲染性能设计.md`](superpowers/specs/260906详细会话渲染性能设计.md)
- 滚动 Top 10 优化：[`260906详细对话滚动Top10优化设计.md`](superpowers/specs/260906详细对话滚动Top10优化设计.md)
- 对话统计、模型与应用标识：[`260907对话统计模型切换与应用标识设计.md`](superpowers/specs/260907对话统计模型切换与应用标识设计.md)
- 交互样式一致性：[`260907交互样式一致性修复设计.md`](superpowers/specs/260907交互样式一致性修复设计.md)
- 当前会话高亮与滚动条：[`260907当前会话高亮与滚动条稳定设计.md`](superpowers/specs/260907当前会话高亮与滚动条稳定设计.md)
- 底部滚动、复制与 Markdown：[`260907底部滚动复制与Markdown可视化设计.md`](superpowers/specs/260907底部滚动复制与Markdown可视化设计.md)

相应实施步骤位于 `docs/superpowers/plans/`，文件名与上述设计文档一一对应。

## 4. 已复核的验收证据

2026-09-07 移交前重新执行 `scripts/run-tests.ps1`：

- 自动测试：50/50 通过。
- 覆盖范围：会话扫描与排序、损坏记录隔离、RPC 容错和回收、历史分支恢复、大图片字段跳过、Token/耗时归并、模型切换、目录快捷打开、应用版本与图标、样式一致性、当前会话高亮、固定滑块、可选择复制和 Markdown 表格等。

现有自包含发布包：

- 路径：`artifacts\PI-Harness-win-x64.zip`
- 大小：63,070,457 字节
- SHA-256：`0A1708ABA0FA8363DB7CA8533C99553CA690F9BE0E57DE1227ECEA0ADCD56C56`

真实大会话滚动验收：

- 报告：`artifacts\qa\package-1.2.0-scroll-final\scroll-qa.txt`
- 结果：`TEST-UI-01` 至 `TEST-UI-08` 全部通过。
- 样本显示项：1251。
- 六个位置静置双帧稳定，固定滑块均为 56 DIP。
- “回到最新”底部间距为 0.0 DIP；向上微移为 24.0 DIP。

Markdown 视觉证据：

- 截图：`artifacts\qa\package-1.2.0-markdown.png`
- 已人工确认暗色主题下标题、列表、粗体、行内代码、代码块、引用、分隔线和表格具有清晰层次，正文可选择复制。

注意：`artifacts/` 被 `.gitignore` 排除，不会随 Git 提交传播。移交源码仓库后，应单独复制当前发布包与 QA 证据，或在接手机器上执行 `scripts/build-release.ps1` 重建。重建 ZIP 后哈希和文件时间可能变化，应重新记录校验值。

## 5. 运行依赖与数据边界

- 用户机器需已全局安装并配置 pi，普通终端执行 `pi --version` 应成功。
- 发布包已经携带 .NET 运行组件，不要求目标机器另装 .NET，也不依赖 pi-dashboard 或本地网络服务。
- 会话只从 `%USERPROFILE%\.pi\agent\sessions` 读取；PI-Harness 不读取或保存 API Key。
- 同一时刻只运行一个 pi 会话。生成期间需等待完成或先停止，才能安全切换会话。
- 图片附件、会话删除/重命名/导出、分支树编辑和多会话并行生成不属于 1.2.0 范围，详见 README 的“当前边界”。

## 6. Git 与变更规范

- 当前仓库未配置 Git remote；移交时需要直接传递完整仓库目录，或由接手方另行配置远端。
- 后续提交信息继续使用中文。
- 只暂存当前任务文件，不吸收无关改动或 `artifacts/` 下生成物。
- 每个可交付节点独立提交；提交前至少执行完整测试和 `git diff --check`。
- 修复 UI 问题时先添加可复现的自动验收，再在 Release 包上运行对应视觉或实机会话 QA；不能只以 Debug 构建成功作为结论。
- 不删除本机 pi 会话、项目目录或非本任务生成的文件。

## 7. 已知维护重点

当前没有阻止交付的已知缺陷。后续最需要守住的回归点：

1. 大型 JSONL 不得把图片 Base64 放入 UI 文本，也不得在打开时请求巨型完整消息列表。
2. 滚动必须保持用户阅读意图；异步布局不能把视口拉回、闪跳或留下底部假空白。
3. 当前会话高亮必须在悬停、目录刷新和会话切换后保持唯一且稳定。
4. 可选择的 `RichTextBox/FlowDocument` 获得焦点后，滚轮仍应驱动外层对话列表。
5. RPC 进程必须随会话切换和窗口关闭完整回收，不能遗留 `pi` 子进程。
6. 新增控件必须复用现有暗色资源与交互状态，避免局部使用系统默认样式。

## 8. 建议技能

接手代理建议按任务类型调用：

- `brainstorming`：任何新增功能或行为修改前，先收敛用户意图、状态和视觉交互。
- `writing-plans`：跨多个文件或包含 UI、RPC、测试、发布联动的改动，编码前先形成可验证实施计划。
- `karpathy-guidelines`：实现或评审代码时保持修改聚焦，避免不必要抽象，并为每个结论保留可验证证据。
- `agent-browser`：仅当需要自动化测试 Web/Electron 页面时使用；当前 WPF 应用优先沿用内置截图/QA 入口和 Windows UI 实测。

## 9. 下一位维护者的完成标准

后续任务只有同时满足以下条件才算完成：需求对应的代码和测试已实现；完整自动测试全通过；Release 构建成功；受影响界面在实际发布包中完成视觉验收；生成物和校验值已复核；仅任务相关文件以中文提交；`git status` 最终干净。
