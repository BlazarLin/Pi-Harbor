# PI-Harness

PI-Harness 是一款 Windows 桌面端 pi 会话管理工具。它会自动扫描本机 pi 会话，按项目文件夹组织为两级树，并可在同一界面中新建或继续对话。

## 功能

- 自动扫描 `%USERPROFILE%\.pi\agent\sessions`。
- 按 JSONL 会话头中的真实 `cwd` 对项目分组。
- 会话名优先使用 pi 会话名，其次使用首条用户消息。
- 选择任意本地项目文件夹创建 pi 对话。
- 加载已有会话的当前有效分支并继续交互。
- 流式显示助手文本，折叠显示思考与工具调用。
- 支持常用 Markdown 标题、列表、粗体、行内代码和代码块。
- pi 生成期间可点击“停止”。
- 监视会话目录变化并自动刷新侧边栏。

## 运行要求

- Windows 10/11 x64。
- pi 已通过 npm 全局安装，且在终端中执行 `pi --version` 成功。
- pi 的模型与认证已配置。

发布包已携带 .NET 运行组件，用户无需安装 .NET、pi-dashboard 或其他服务。

## 使用

1. 解压 `PI-Harness-win-x64.zip`。
2. 运行 `PI-Harness.exe`。
3. 点击左侧会话可继续交互。
4. 点击“新对话”，选择项目文件夹后可创建对话。
5. 输入框中按 `Ctrl+Enter` 或点击“发送”。

同一时刻只运行一个 pi 会话。pi 正在生成时，需要等待完成或先点击“停止”，才能切换会话。

## 本地开发

开发环境需要 .NET 10 SDK。项目无第三方 NuGet 依赖。

```powershell
dotnet build .\PI-Harness.sln -c Debug
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1
```

构建自包含 Release 发布包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

产物：

- `artifacts\PI-Harness-win-x64\`
- `artifacts\PI-Harness-win-x64.zip`

## 架构

- `PIHarness.Core`：会话 JSONL 解析、目录扫描、pi 定位和 RPC 子进程管理。
- `PIHarness.App`：WPF 界面、聊天状态编排和 Markdown 显示。
- `PIHarness.Tests`：不依赖第三方测试包的中文验收程序。

PI-Harness 直接启动 `pi --mode rpc --approve`，通过 stdin/stdout 的 UTF-8 JSONL 交互。它不启动本地网络服务，不读取或保存 API Key。

## 当前边界

第一版不包含图片附件、模型切换、会话删除/重命名/导出、分支树编辑和多会话并行生成。

## 排查

- 显示“未找到 pi.cmd”：先在普通终端执行 `pi --version`，确认 npm 全局目录已加入 `PATH`。
- 项目下没有会话：点击“刷新会话”，并检查 `%USERPROFILE%\.pi\agent\sessions`是否存在 JSONL。
- 会话无法打开：确认会话记录的项目 `cwd` 仍然存在。
- 模型错误：PI-Harness 显示 pi 返回的错误，认证与模型修复应在 pi 配置中完成。
