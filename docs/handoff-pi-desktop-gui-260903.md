# Handoff：为 pi 寻找/实现"ChatGPT 桌面版"式会话管理工具

- 日期：2026-09-03
- 交接原因：用户已试用多款 pi 桌面管理工具，**均不符合需求**，需要新会话继续寻找替代方案或自行实现
- 语言：中文（与用户交流请用中文）

---

## 1. 用户背景（恒定约束）

- 工业视觉算法项目经理，C++17 / Qt 5.14 / OpenCV 4.5.5 / HALCON 21.05，Windows + VS2019
- 强偏好：单 exe、ms 级性能、简单直接、避免过度设计
- pi 全局安装：v0.84.4（npm 全局），模型 ark/glm-5.3 与 zai 系列
- 会话目录：`~/.pi/agent/sessions/`（约 29 个项目目录、78+ 会话 JSONL）
- 用户机器上有 clash-verge（TUN 模式，Meta Tunnel/Wintun 网卡）

## 2. 核心需求（下一会话的目标）

用户原话诉求：**"类似 ChatGPT 桌面版，在一个 exe 内管理本机所有 pi 对话，可直接交互，侧边栏简洁"**

期望的侧边栏形态（用户明确画过示意）：

```
--项目目录
------xxxx对话1
------xxxx对话2
```

即：两级树（目录 → 会话名），不要卡片堆叠、消息预览、插件 badge 等复杂元素。

## 3. 已试用的方案与结论（均不满足）

| 方案 | 结论 | 关键缺陷 |
|---|---|---|
| pi-dashboard 0.8.0（BlackBelt，已装于 `D:\Program Files (x86)\PIDashboard\pi-dashboard\`） | 不符合 | 侧边栏=目录卡片+会话胶囊+插件扩展点，层级复杂；无"简洁树形"设置项（已翻查 0.8.0 前端源码确认）；"尚不是 pi 项目"横幅等概念多余 |
| Stella pi-workbench 0.5.0（ZY-LI-F，源码 clone 于 `G:\Code\pi-workbench`） | 不符合 | 单人项目成熟度低；重心在多 Agent 团队/看板而非简洁聊天；实测有 React StrictMode 并发初始化竞态（"Pi RPC 已停止"），需手工修补才能跑 |
| pi-session-manager（npm, kowssari） | 不符合 | TUI 扩展，非桌面 GUI |
| pi-acp | 不符合 | 编辑器内嵌方案，非独立 exe |
| pi 原生 `pi -r` / `/resume` | 不符合 | 纯 TUI，无 GUI |

## 4. 本次会话已解决的环境问题（重要，避免重查）

1. **pi-dashboard 新旧 server 冲突**：旧 bridge（0.5.4，路径在 `~/.pi/agent/settings.json` 的 packages，指向旧目录 `D:\Program Files (x86)\PI-Dashboard-win32-x64\`）会自动拉起旧 server 占 8000 端口。已处理；`settings.json` 的 packages 仍指向旧路径，若 pi-dashboard 行为异常先查这里。
2. **clash-verge TUN 污染 IPv6 loopback**：本机所有 `::1` 出站连接 EACCES（127.0.0.1 正常）。任何 dev server（vite 等）默认绑 `::1` 都会连不上，必须显式 `host: "127.0.0.1"`。已在 `G:\Code\pi-workbench\electron.vite.config.ts` 修复。
3. **Stella 启动脚本**：`G:\Code\pi-workbench\启动Stella.bat` / `停止Stella.bat`（ASCII 注释、find.exe 全路径、ping 延时，规避 cmd 代码页与 PATH 污染）。Electron 二进制用 npmmirror 镜像补装。
4. **Stella 的 StrictMode 竞态**：已临时移除 `src/renderer/src/main.tsx` 里的 `<StrictMode>`（带注释），根因是主进程 initialize 无互斥，StrictMode 双执行 useEffect 导致第二次 stop() 杀掉第一次的 Pi RPC 子进程。

## 5. 已确认的技术事实（供下一会话直接使用）

- pi 会话 = `~/.pi/agent/sessions/<编码后的cwd>/*.jsonl + *.meta.json`，meta 含 cwd/firstMessage/model/tokens 等；很多 meta 孤儿（无 jsonl，即空会话）
- pi RPC：`node @earendil-works/pi-coding-agent/dist/rpc-entry.js --approve`，stdin/stdout JSON Lines 协议（`get_state`/`get_messages`/`get_entries`/`get_tree` 等命令），ELECTRON 应用可用 `ELECTRON_RUN_AS_NODE=1` + electron.exe 直接跑，已实测稳定
- pi-dashboard 0.8.0 的 REST API（localhost:8000）可列全部会话/发 prompt，健康检查 `/api/health`
- 无现成工具满足需求 → 下一会话的可能方向：
  a. 基于 pi RPC 协议（`rpc-entry.js`）自研一个极简 Electron exe（会话列表树 + 聊天窗，两三百行级别）
  b. 用 pi-dashboard 作为后端、自写极简前端（它 REST/WebSocket 齐全）
  c. 关注 pi 官方生态（@earendil-works）是否出官方 GUI
- 用户倾向 a 或 b（未最终确认，建议先问）

## 6. 遗留/未完成事项

- 用户尚未明确：自研 vs 继续找现成的（关键决策点，先问）
- `~/.pi/agent/settings.json` 的 packages 路径仍指向已废弃的旧 dashboard 目录，未清理
- 旧目录 `D:\Program Files (x86)\PI-Dashboard-win32-x64\` 确认无用后可删（删除前需用户确认）
- pi-dashboard 的 tunnel.enabled 仍为 true（安全风险，建议关闭，用户未处理）
- Stella 的 StrictMode 修复是临时补丁，git 状态未提交

## 7. Suggested Skills（建议下一会话调用）

- 无需强制调用任何技能。若走自研路线（方向 a），可考虑：
  - `writing-plans`（在动手前写实现计划）
  - `karpathy-guidelines`（避免过度设计——本需求尤其重要，用户要的就是"简单"）
- 若走 pi-dashboard 前端替换（方向 b），先读 `D:\Program Files (x86)\PIDashboard\pi-dashboard\resources\server\node_modules\@blackbelt-technology\pi-dashboard-extension\.pi\skills\pi-dashboard\SKILL.md`（REST API 全集）
