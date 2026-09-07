# 安全问题报告

优先维护最新发布版本。请在报告中提供 Pi Harbor、Pi、Windows 版本和不含敏感数据的最小复现。

涉及命令执行、凭据泄露或会话数据泄露的问题，请使用仓库 **Security → Report a vulnerability** 私密报告；如果入口未开启，先提交只包含“请求私密安全联系渠道”的 Issue，不要公开漏洞利用细节或真实凭据。

Pi Harbor 使用本地 Pi RPC。Pi 本身可以读写项目文件和运行命令，模型访问权限由你的 Pi 环境控制。软件不是沙箱；请只打开你信任的项目，确认 Pi 的模型和扩展配置。

应用不直接读取或保存 API Key，但 Pi 子进程会使用自己的认证配置。日志、会话 JSONL、截图和导出的文本仍可能包含私人项目内容，请在公开前检查。

维护者应在 GitHub 仓库设置中开启 Private vulnerability reporting、Secret scanning / Push protection（若可用），并对默认分支启用 CI 检查。
