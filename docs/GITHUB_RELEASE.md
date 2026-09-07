# GitHub 开源与 Windows 发布

## 已配置的流程

- `.github/workflows/ci.yml`：`main` 更新及 Pull Request 自动运行完整测试，使用只读权限。
- `.github/workflows/release.yml`：推送 `v*` 标签后校验标签与项目版本一致，生成自包含 ZIP、安装 EXE 和 SHA-256 文件，上传到 **Release 草稿**。公开的 Release 不会被重复运行覆盖。
- Actions → Windows Release → Run workflow：手动构建；选择分支时只生成 Actions artifacts，选择有效版本标签时创建/更新 Release 草稿。
- 依赖的 Actions 固定到完整 commit SHA，由 Dependabot 每月检查更新。

GitHub Windows 2025 runner 目前包含 Inno Setup 6；脚本会查找 `ISCC.exe`，缺失时明确失败。参考 [runner 软件清单](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md)。GitHub Release 使用仓库自动提供的 `GITHUB_TOKEN`，不需要把个人 PAT 写入配置。

## 第一次发布

1. 将本地代码推送到 `BlazarLin/Pi-Harbor`，确认仓库 Settings → Actions 允许工作流运行。发布 job 已声明 `contents: write`，组织策略不得禁止此权限。
2. 确认 `src/PIHarness.App/PIHarness.App.csproj` 中 Version、AssemblyVersion、FileVersion、InformationalVersion 一致，README 与 CHANGELOG 更新。当前为 `1.3.1`。
3. 本机运行完整测试、Release UI 验收和安装/卸载验收；公开截图只使用 `docs/images/` 的脱敏图。
4. 提交并推送代码后，创建与版本一致的标签：

```powershell
git tag -a v1.3.1 -m "Pi Harbor 1.3.1"
git push origin v1.3.1
```

5. 在 Actions 查看 Windows Release 成功，再进入 Releases 检查草稿说明与附件，点击 **Publish release**。
6. 用户即可从 Releases 下载 `Pi-Harbor-Setup-win-x64.exe` 或 `Pi-Harbor-win-x64.zip`；GitHub 自动提供的 Source code ZIP 只是源码，不是可执行包。

以上步骤是维护者操作说明；本轮本地工作不会自动推送标签、公开仓库或发布 Release。参考 [GitHub 标签触发工作流](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow)、[gh release create](https://cli.github.com/manual/gh_release_create)。

## 本地打包

安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 与 [Inno Setup 6](https://jrsoftware.org/isdl.php)，然后执行：

```powershell
powershell -NoProfile -File scripts/build-release.ps1 -IncludeInstaller
```

编译器不在默认位置时添加 `-IsccPath 'C:\Tools\Inno Setup 6\ISCC.exe'`。只需要便携 ZIP 时省略 `-IncludeInstaller`。`-SkipSmokeCapture` 供无桌面的 CI 使用；它仍然运行全部自动测试。

若旧发布目录内的程序仍在运行，脚本会在清理前停止。可增加 `-PublishDirectory artifacts/Pi-Harbor-1.3.1-win-x64`，在独立目录构建而不打断已打开的程序。

安装器使用固定 AppId，默认装入 `%LOCALAPPDATA%\Programs\Pi Harbor`，无需管理员权限。开始菜单快捷方式自动创建，桌面快捷方式可选。卸载不删除 `.pi` 会话、项目或凭据；Pi 与 Node.js 需用户另行安装。参考 [Inno Setup 权限模式](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)。

```powershell
Get-FileHash .\Pi-Harbor-Setup-win-x64.exe -Algorithm SHA256
```

将输出与 Release 的 `SHA256SUMS.txt` 对照。当前安装器未配置代码签名；有签名证书后应在打包前签署应用、生成安装器后签署安装器，最后生成校验文件。证书和密码只放 GitHub Secrets，不进入仓库。

## 公开前需完成的事项

- **隐私历史**：旧 `piweb-eval` 图片含真实项目与对话，本地副本归档于被忽略的 `artifacts/private-archive`，当前源码不再分发。旧 Git 提交仍保留副本；公开前决定清理历史或从干净快照初始化公开仓库。不要误以为新增 `.gitignore` 会清除历史。
- **历史说明文件**：移交与设计记录包含本机路径和工程示例，维护者需确认可公开范围。不要将 `.pi` 目录、API Key、会话 JSONL 或私人日志加入仓库。
- **仓库设置**：填写 Description、Topics、下载链接；开启 Issues、Private vulnerability reporting、Secret scanning / Push protection（若可用）；默认分支要求 CI 通过。
- **许可证与归属**：MIT 已加入。确认所有历史代码、图标与文档都可按该许可证发布；运行库和安装器遵循各自第三方许可。
- **发布验证**：在干净 Windows 环境覆盖首次安装、覆盖升级、卸载、非 ASCII 路径、高 DPI、中文输入法、Pi 未安装/未认证、终端生成新会话。
- **后续版本**：优先补齐持久化草稿、历史图片按需预览、扩展 RPC 表单支持与代码签名。
