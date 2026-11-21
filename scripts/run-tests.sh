#!/usr/bin/env bash
# 创建时间：2026-09-12
# 功能：在 Linux 上构建并运行 Pi Harbor 跨平台测试套件。
# 目的：为 Ubuntu 提供与 scripts/run-tests.ps1 对等的测试入口。

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "未找到 dotnet。请安装 .NET 10 SDK 并将其加入 PATH。" >&2
    exit 1
fi

dotnet run --project tests/PIHarness.Tests/PIHarness.Tests.csproj -c Release -- "$@"
