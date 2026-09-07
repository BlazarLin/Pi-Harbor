# 参与贡献

欢迎通过 Issue 或 Pull Request 提交问题和改进。

## 开发与验证

1. 使用 Windows x64 与 .NET 10 SDK 克隆仓库。
2. 执行 `powershell -NoProfile -File scripts/run-tests.ps1`。自动测试使用伪 RPC，不需要 Pi 或模型密钥。
3. 实机验证前安装并配置 Pi，记录 `pi --version`、Windows 版本和复现步骤。
4. UI 改动同时提供发布版截图；公开截图遮挡项目名、路径、会话标题和私人正文。
5. 提交前执行 `git diff --check`，保留会话大文件、滚动稳定性、子进程回收和暗色样式相关回归测试。

每个 PR 聚焦一个问题，说明变化前后的行为、验证方法和已知限制。使用清楚的中文或英文提交说明。不要提交 `artifacts/`、真实 JSONL、API Key、`.pi` 配置或未脱敏截图。

## 范围

Pi Harbor 当前使用 WPF，目标为 Windows x64。新增跨平台界面、多会话并行、自动重试、会话删除等行为前，建议先在 Issue 中讨论数据边界与恢复策略。

## 许可证

向此仓库提交代码，表示同意所提交的贡献按项目 MIT 许可证分发，并确认你有权提交这些内容。
