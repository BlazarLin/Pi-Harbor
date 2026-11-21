#!/usr/bin/env bash
# 创建时间：2026-09-12
# 功能：构建 Linux x64 自包含发布目录、便携 tar.gz 与 Debian 安装包。
# 目的：在 Ubuntu 上提供与 scripts/build-release.ps1 对等的测试、发布、打包流程。

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifacts_root="$repo_root/artifacts"
publish_dir="$artifacts_root/Pi-Harbor-linux-x64"
tar_path="$artifacts_root/Pi-Harbor-linux-x64.tar.gz"
desktop_template="$repo_root/packaging/pi-harbor.desktop.in"
icon_source="$repo_root/packaging/icons/pi-harbor-256.png"
install_script="$repo_root/scripts/install-linux.sh"
app_project="$repo_root/src/PIHarness.Avalonia/PIHarness.Avalonia.csproj"
version_project="$repo_root/src/PIHarness.ViewModels/PIHarness.ViewModels.csproj"

skip_tests=0
skip_smoke=0
for arg in "$@"; do
    case "$arg" in
        --skip-tests) skip_tests=1 ;;
        --skip-smoke) skip_smoke=1 ;;
        *) echo "未知参数：$arg" >&2; exit 2 ;;
    esac
done

cd "$repo_root"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "未找到 dotnet。请安装 .NET 10 SDK 并将其加入 PATH。" >&2
    exit 1
fi

version="$(grep -oPm1 '(?<=<Version>)[^<]+' "$version_project")"
if [ -z "$version" ]; then
    echo "无法从 $version_project 读取版本号。" >&2
    exit 1
fi

# 只清理 artifacts 下的本脚本产物，避免误删其他目录。
cleanup_owned_path() {
    local target="$1"
    case "$target" in
        "$artifacts_root"/*) ;;
        *) echo "拒绝删除 artifacts 之外的路径：$target" >&2; exit 1 ;;
    esac
    rm -rf -- "$target"
}

mkdir -p "$artifacts_root"

if [ "$skip_tests" -eq 0 ]; then
    bash "$repo_root/scripts/run-tests.sh"
fi

cleanup_owned_path "$publish_dir"
cleanup_owned_path "$tar_path"

dotnet publish "$app_project" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=false \
    -p:DebugType=portable \
    -p:DebugSymbols=true \
    -o "$publish_dir"

if [ ! -x "$publish_dir/Pi-Harbor-Avalonia" ]; then
    echo "发布目录缺少可执行文件 Pi-Harbor-Avalonia。" >&2
    exit 1
fi

cp -f "$repo_root/LICENSE" "$publish_dir/LICENSE"
cp -f "$repo_root/THIRD-PARTY-NOTICES.md" "$publish_dir/THIRD-PARTY-NOTICES.md"
cp -f "$install_script" "$publish_dir/install-linux.sh"
chmod +x "$publish_dir/install-linux.sh"
cp -f "$icon_source" "$publish_dir/pi-harbor.png"

# 自包含发布随包附带 .NET 运行时许可证，与 Windows 发布脚本保持一致。
runtime_pack_root="${NUGET_PACKAGES:-$HOME/.nuget/packages}/microsoft.netcore.app.runtime.linux-x64"
runtime_license_dir="$publish_dir/licenses/microsoft.netcore.app.runtime.linux-x64"
mkdir -p "$runtime_license_dir"
if [ -d "$runtime_pack_root" ]; then
    runtime_pack_version="$(ls -1 "$runtime_pack_root" | sort -V | tail -1)"
    for notice in LICENSE.TXT THIRD-PARTY-NOTICES.TXT; do
        if [ -f "$runtime_pack_root/$runtime_pack_version/$notice" ]; then
            cp -f "$runtime_pack_root/$runtime_pack_version/$notice" "$runtime_license_dir/"
        else
            echo "警告：未找到 .NET 运行时许可证 $notice" >&2
        fi
    done
else
    echo "警告：未找到 .NET 运行时包目录，跳过运行时许可证拷贝。" >&2
fi

if [ "$skip_smoke" -eq 0 ]; then
    smoke_output="$("$publish_dir/Pi-Harbor-Avalonia" --version)"
    case "$smoke_output" in
        *"$version"*) ;;
        *) echo "发布产物版本校验失败：$smoke_output" >&2; exit 1 ;;
    esac
    echo "启动冒烟通过：$smoke_output"
fi

# 便携包：解压后直接运行，或执行包内 install-linux.sh 做用户级安装。
tar -C "$artifacts_root" -czf "$tar_path" "Pi-Harbor-linux-x64"

# Debian 包：安装到 /opt/pi-harbor，提供 /usr/bin/pi-harbor 与桌面入口。
deb_root="$artifacts_root/deb-root/pi-harbor_${version}_amd64"
deb_path="$artifacts_root/pi-harbor_${version}_amd64.deb"
cleanup_owned_path "$(dirname "$deb_root")"
cleanup_owned_path "$deb_path"
mkdir -p "$deb_root/DEBIAN" \
    "$deb_root/opt/pi-harbor" \
    "$deb_root/usr/bin" \
    "$deb_root/usr/share/applications" \
    "$deb_root/usr/share/icons/hicolor/256x256/apps" \
    "$deb_root/usr/share/doc/pi-harbor"

cp -a "$publish_dir/." "$deb_root/opt/pi-harbor/"
rm -f "$deb_root/opt/pi-harbor/install-linux.sh"
ln -sf /opt/pi-harbor/Pi-Harbor-Avalonia "$deb_root/usr/bin/pi-harbor"
cp -f "$icon_source" "$deb_root/usr/share/icons/hicolor/256x256/apps/pi-harbor.png"
sed 's|@EXEC@|/usr/bin/pi-harbor|g' "$desktop_template" > "$deb_root/usr/share/applications/pi-harbor.desktop"
cp -f "$repo_root/LICENSE" "$deb_root/usr/share/doc/pi-harbor/copyright"
cp -f "$repo_root/THIRD-PARTY-NOTICES.md" "$deb_root/usr/share/doc/pi-harbor/THIRD-PARTY-NOTICES.md"

# 统一权限：目录/数据 755/644，可执行文件 755，避免沿用源码树的组写权限。
find "$deb_root/opt/pi-harbor" -type d -exec chmod 0755 {} +
find "$deb_root/opt/pi-harbor" -type f -exec chmod 0644 {} +
chmod 0755 "$deb_root/opt/pi-harbor/Pi-Harbor-Avalonia" "$deb_root/opt/pi-harbor/createdump"
find "$deb_root" -type d -exec chmod 0755 {} +
chmod 0644 "$deb_root/usr/share/applications/pi-harbor.desktop" \
    "$deb_root/usr/share/icons/hicolor/256x256/apps/pi-harbor.png" \
    "$deb_root/usr/share/doc/pi-harbor/copyright" \
    "$deb_root/usr/share/doc/pi-harbor/THIRD-PARTY-NOTICES.md"

installed_size="$(du -sk "$deb_root" | cut -f1)"
cat > "$deb_root/DEBIAN/control" <<EOF
Package: pi-harbor
Version: ${version}
Section: utils
Priority: optional
Architecture: amd64
Installed-Size: ${installed_size}
Depends: libx11-6, libice6, libsm6, libfontconfig1, libgl1, libicu74 | libicu72 | libicu70
Maintainer: Pi Harbor Maintainers
Homepage: https://github.com/BlazarLin/Pi-Harbor
Description: Pi Session Desk - 集中停靠、浏览并继续本机 Pi 会话
 Pi Harbor 是 Pi 的桌面客户端：聚合本机会话、搜索历史、继续对话，
 并在 Windows 与 Linux 上共享同一套数据层与视图模型。
EOF

cat > "$deb_root/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1 || true
fi
exit 0
EOF

cat > "$deb_root/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1 || true
fi
exit 0
EOF

chmod 0755 "$deb_root/DEBIAN/postinst" "$deb_root/DEBIAN/postrm"
dpkg-deb --root-owner-group --build "$deb_root" "$deb_path" >/dev/null

# 校验产物完整性并生成校验和清单。
"$publish_dir/Pi-Harbor-Avalonia" --version >/dev/null
dpkg-deb --info "$deb_path" >/dev/null
tar -tzf "$tar_path" >/dev/null

(
    cd "$artifacts_root"
    sha256sum "$(basename "$tar_path")" "$(basename "$deb_path")" > SHA256SUMS-linux.txt
)

echo "Linux 发布完成（版本 $version）"
echo "便携目录：$publish_dir"
echo "便携包：  $tar_path"
echo "Debian 包：$deb_path"
echo "校验和：  $artifacts_root/SHA256SUMS-linux.txt"
