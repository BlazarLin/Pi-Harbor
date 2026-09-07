# Pi Harbor 品牌更名设计

## 目标

将用户可见产品品牌从 `PI-Harness` 统一更名为 `Pi Harbor`，副标题固定为 `Pi Session Desk`。`Pi Harbor` 表达“集中停靠、浏览并继续本机 Pi 会话的桌面港湾”，副标题直接说明产品类别。

## 命名规则

- 主品牌：`Pi Harbor`
- 功能副标题：`Pi Session Desk`
- 中文解释：`集中停靠、浏览并继续本机 Pi 会话的桌面港湾`
- Windows 可执行文件：`Pi-Harbor.exe`
- 发布目录：`artifacts/Pi-Harbor-win-x64/`
- 发布压缩包：`artifacts/Pi-Harbor-win-x64.zip`
- 解决方案：`Pi-Harbor.sln`
- 图标资源：`Assets/Pi-Harbor.ico`
- 新发布版本：`1.2.1`，文件版本 `1.2.1.0`

## 界面

侧栏品牌区使用两行层级：第一行显示 `Pi Harbor` 和版本号，第二行以弱化文字显示 `Pi Session Desk`。窗口标题显示 `Pi Harbor 1.2.1 — Pi Session Desk`。副标题不参与会话状态，不随模型或项目变化。

现有图标图形继续使用，不重新引入视觉风格；只调整资源文件名和品牌描述，避免不必要的图标重设计。

## 工程边界

为了避免高风险、无用户收益的全仓库重构，下列内部标识保持不变：

- `PIHarness.App`、`PIHarness.Core`、`PIHarness.Tests` 命名空间和项目目录。
- pi 会话目录、JSONL 格式、RPC 协议和用户配置。
- 当前 UI 布局、颜色、交互与功能行为。

历史 specs、plans 和原始调研 handoff 保留原名称，作为对应版本的事实记录；当前 `README.md` 和项目移交说明更新为新品牌，并注明仓库物理目录仍为 `G:\Code\PI-Harness`。

## 构建与兼容

`scripts/build-release.ps1` 只生成新的 `Pi-Harbor` 发布目录和 ZIP。旧的 `artifacts/PI-Harness-*` 是被 Git 忽略的历史产物，不由本次任务删除。

发布包继续保持 Windows x64 自包含、无第三方 NuGet 依赖；目标机器仍只需要已经安装并配置好的 pi。

## 验收

1. 自动测试明确校验主品牌、副标题、窗口标题、程序集名称、产品元数据、图标路径和 1.2.1 版本。
2. `scripts/run-tests.ps1` 全部通过，Release 构建零错误。
3. `scripts/build-release.ps1` 生成 `Pi-Harbor.exe` 和 `Pi-Harbor-win-x64.zip`，旧名称不是新包入口。
4. 从新 EXE 捕获 UI，人工确认侧栏品牌层级、间距、暗色主题与版本显示统一。
5. 复用真实大会话滚动 QA，确认品牌区高度调整未引入滚动或底部对齐回归。
6. README、移交说明、构建命令和校验值指向新名称；Git 提交使用中文且最终工作区干净。
