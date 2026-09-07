# Pi Harbor

**Pi Session Desk** —— 集中停靠、浏览并继续本机 Pi 会话的桌面港湾。

Pi Harbor 是一款 Windows 桌面端 pi 会话管理工具。它会自动扫描本机 pi 会话，按项目文件夹组织为两级树，并可在同一界面中新建或继续对话。

当前版本：1.2.1。

## 功能

- 自动扫描 `%USERPROFILE%\.pi\agent\sessions`。
- 按 JSONL 会话头中的真实 `cwd` 对项目分组。
- 会话名优先使用 pi 会话名，其次使用首条用户消息。
- 选择任意本地项目文件夹创建 pi 对话。
- 加载已有会话的当前有效分支并继续交互。
- 大型会话直接读取轻量文字历史，跳过图片 Base64，并在后台等待 pi 完整上下文就绪。
- 流式显示助手文本，折叠显示思考与工具调用。
- 每轮结束后显示该轮耗时、输入/输出/缓存 Token 与总 Token。
- 会话空闲时可从标题栏选择并切换 pi 已配置的模型。
- 右键项目目录可在 Windows 文件资源管理器中直接打开。
- 当前打开的会话在侧栏持续显示独立紫灰高亮，目录自动刷新后仍保留。
- 大会话使用像素级连续滚动，滑块保持固定长度；“回到最新”会精确对齐末条内容，不留下假空白。
- 历史 Markdown 同步布局，思考与工具展开状态在滚出视口后仍保留。
- 详细内容可用鼠标跨段选择，并通过 `Ctrl+C` 或右键菜单复制；正文聚焦后滚轮仍控制对话列表。
- 支持 Markdown 标题、列表、粗体、行内代码、代码块、引用、分隔线和结构化表格。
- pi 生成期间可点击“停止”。
- 监视会话目录变化并自动刷新侧边栏。

## 运行要求

- Windows 10/11 x64。
- pi 已通过 npm 全局安装，且在终端中执行 `pi --version` 成功。
- pi 的模型与认证已配置。

发布包已携带 .NET 运行组件，用户无需安装 .NET、pi-dashboard 或其他服务。

## 使用

1. 解压 `Pi-Harbor-win-x64.zip`。
2. 运行 `Pi-Harbor.exe`。
3. 点击左侧会话可继续交互。
4. 点击“新对话”，选择项目文件夹后可创建对话。
5. 输入框中按 `Ctrl+Enter` 或点击“发送”。
6. 在右上角模型下拉框切换当前会话模型；生成期间会自动禁用。
7. 右键左侧项目名称，选择“在文件资源管理器中打开”。

同一时刻只运行一个 pi 会话。pi 正在生成时，需要等待完成或先点击“停止”，才能切换会话。

## 本地开发

开发环境需要 .NET 10 SDK。项目无第三方 NuGet 依赖。

```powershell
dotnet build .\Pi-Harbor.sln -c Debug
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\generate-app-icon.ps1
```

构建自包含 Release 发布包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

产物：

- `artifacts\Pi-Harbor-win-x64\`
- `artifacts\Pi-Harbor-win-x64.zip`

真实大会话滚动验收（只读，不发送模型请求）：

```powershell
artifacts\Pi-Harbor-win-x64\Pi-Harbor.exe `
  --capture-session <session.jsonl> `
  --qa-scroll-capture-dir artifacts\qa\scroll-top10
```

该入口输出六组静置双帧和 `scroll-qa.txt`，共验证八项：上下滚动方向、阅读位置稳定性、响应耗时、“回到最新”状态、滑块可见长度、末条底边对齐及 24 DIP 微量滚动连续性。

## 架构

- `PIHarness.Core`：会话 JSONL 解析、目录扫描、pi 定位和 RPC 子进程管理。
- `PIHarness.App`：WPF 界面、聊天状态编排和 Markdown 显示。
- `PIHarness.Tests`：不依赖第三方测试包的中文验收程序。

Pi Harbor 直接启动 `pi --mode rpc --approve`，通过 stdin/stdout 的 UTF-8 JSONL 交互。它不启动本地网络服务，不读取或保存 API Key。

## 当前边界

当前不包含图片附件、会话删除/重命名/导出、分支树编辑和多会话并行生成。

## 排查

- 显示“未找到 pi.cmd”：先在普通终端执行 `pi --version`，确认 npm 全局目录已加入 `PATH`。
- 项目下没有会话：点击“刷新会话”，并检查 `%USERPROFILE%\.pi\agent\sessions`是否存在 JSONL。
- 会话无法打开：确认会话记录的项目 `cwd` 仍然存在。
- 模型错误：Pi Harbor 显示 pi 返回的错误，认证与模型修复应在 pi 配置中完成。
