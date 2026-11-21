#!/usr/bin/env bash
# 创建时间：2026-09-12
# 功能：把 Pi Harbor 安装到用户目录（默认 ~/.local），创建启动器并可选加入 GNOME 快捷启动栏。
# 目的：在没有 root 权限的 Ubuntu 桌面上完成本地安装与快速启动。

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# 发布包内执行时脚本与产物同目录；源码树内执行时默认取 artifacts 发布目录。
if [ -x "$script_dir/Pi-Harbor-Avalonia" ]; then
    default_source="$script_dir"
else
    default_source="$(cd "$script_dir/.." && pwd)/artifacts/Pi-Harbor-linux-x64"
fi

source_dir="$default_source"
prefix="${HOME}/.local"
add_to_dock=0
uninstall=0

usage() {
    cat <<'EOF'
用法：install-linux.sh [选项]

  --source <目录>   指定发布目录（默认 artifacts/Pi-Harbor-linux-x64）
  --prefix <目录>   安装前缀（默认 ~/.local）
  --dock            安装后加入 GNOME 快捷启动栏（收藏夹）
  --uninstall       卸载用户级安装
  -h, --help        显示帮助
EOF
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --source) source_dir="${2:?缺少目录参数}"; shift 2 ;;
        --prefix) prefix="${2:?缺少目录参数}"; shift 2 ;;
        --dock) add_to_dock=1; shift ;;
        --uninstall) uninstall=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "未知参数：$1" >&2; usage >&2; exit 2 ;;
    esac
done

prefix="$(cd "$prefix" 2>/dev/null && pwd || { mkdir -p "$prefix" && cd "$prefix" && pwd; })"
app_dir="$prefix/share/pi-harbor"
bin_link="$prefix/bin/pi-harbor"
apps_dir="$prefix/share/applications"
desktop_file="$apps_dir/pi-harbor.desktop"
icon_file="$prefix/share/icons/hicolor/256x256/apps/pi-harbor.png"

refresh_desktop_database() {
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q "$apps_dir" || true
    command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -f -t "$prefix/share/icons/hicolor" >/dev/null 2>&1 || true
}

remove_from_dock() {
    command -v gsettings >/dev/null 2>&1 || return 0
    command -v python3 >/dev/null 2>&1 || return 0
    local current
    current="$(gsettings get org.gnome.shell favorite-apps 2>/dev/null || true)"
    [ -n "$current" ] || return 0
    local updated
    updated="$(python3 - "$current" <<'PY'
import ast, sys
items = ast.literal_eval(sys.argv[1])
print(repr([item for item in items if item != 'pi-harbor.desktop']))
PY
)"
    [ "$updated" != "$current" ] && gsettings set org.gnome.shell favorite-apps "$updated" || true
}

add_favorite_to_dock() {
    if ! command -v gsettings >/dev/null 2>&1; then
        echo "未检测到 gsettings，跳过快捷启动栏（桌面环境不是 GNOME）。"
        return 0
    fi
    if ! command -v python3 >/dev/null 2>&1; then
        echo "未检测到 python3，跳过快捷启动栏。"
        return 0
    fi
    local current
    current="$(gsettings get org.gnome.shell favorite-apps 2>/dev/null || true)"
    if [ -z "$current" ]; then
        echo "无法读取 GNOME 收藏夹，跳过快捷启动栏。"
        return 0
    fi
    local updated
    updated="$(python3 - "$current" <<'PY'
import ast, sys
items = ast.literal_eval(sys.argv[1])
if 'pi-harbor.desktop' not in items:
    items.append('pi-harbor.desktop')
print(repr(items))
PY
)"
    if [ "$updated" != "$current" ]; then
        gsettings set org.gnome.shell favorite-apps "$updated"
        echo "已加入 GNOME 快捷启动栏：pi-harbor.desktop"
    else
        echo "快捷启动栏中已存在 pi-harbor.desktop。"
    fi
}

if [ "$uninstall" -eq 1 ]; then
    rm -f "$bin_link" "$desktop_file" "$icon_file"
    rm -rf "$app_dir"
    remove_from_dock
    refresh_desktop_database
    echo "已卸载用户级安装：$prefix"
    exit 0
fi

if [ ! -x "$source_dir/Pi-Harbor-Avalonia" ]; then
    echo "发布目录无效：$source_dir" >&2
    echo "请先运行 scripts/build-release-linux.sh。" >&2
    exit 1
fi

mkdir -p "$app_dir" "$prefix/bin" "$apps_dir" "$(dirname "$icon_file")"
# 用固定目标目录同步，重复安装不会残留旧版本文件。
rm -rf "$app_dir"
mkdir -p "$app_dir"
cp -a "$source_dir/." "$app_dir/"
chmod +x "$app_dir/Pi-Harbor-Avalonia"
ln -sfn "$app_dir/Pi-Harbor-Avalonia" "$bin_link"

# 图标优先取发布包内的 pi-harbor.png，源码树内则回落到 packaging/icons。
if [ -f "$source_dir/pi-harbor.png" ]; then
    cp -f "$source_dir/pi-harbor.png" "$icon_file"
elif [ -f "$script_dir/../packaging/icons/pi-harbor-256.png" ]; then
    cp -f "$script_dir/../packaging/icons/pi-harbor-256.png" "$icon_file"
else
    echo "警告：未找到应用图标，启动器将使用默认图标。" >&2
fi

cat > "$desktop_file" <<EOF
[Desktop Entry]
Type=Application
Name=Pi Harbor
GenericName=Pi Session Desk
Comment=集中停靠、浏览并继续本机 Pi 会话
Exec=$bin_link
TryExec=$bin_link
Icon=$icon_file
Terminal=false
Categories=Development;
Keywords=pi;ai;session;chat;harbor;
StartupNotify=true
EOF
chmod 0644 "$desktop_file"
refresh_desktop_database

echo "已安装：$app_dir"
echo "命令：  $bin_link"
echo "启动器：$desktop_file"

if [ "$add_to_dock" -eq 1 ]; then
    add_favorite_to_dock
fi

case ":$PATH:" in
    *":$prefix/bin:"*) ;;
    *) echo "提示：$prefix/bin 不在 PATH 中，命令行使用前请先加入 PATH。" ;;
esac
