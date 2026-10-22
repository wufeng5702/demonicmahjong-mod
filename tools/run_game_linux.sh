#!/usr/bin/env bash
# 在 Linux(Proton) 下启动《我在地府打麻将》，并保证 BepInEx 真正生效。
#
# 背景：Proton/Wine 默认用 builtin 的 winhttp.dll，会**忽略**游戏目录里的 doorstop 代理
#      （BepInEx 靠它注入）。结果就是游戏能正常玩，但 BepInEx 根本不启动——
#      所有 mod 静默失效，日志 BepInEx/LogOutput.log 连时间戳都不更新，最容易误判成
#      "mod 装坏了 / 环境不兼容"。
#      解法：让 Wine 优先加载原生（= 游戏目录里的）winhttp.dll。
#            写进前缀的 DllOverrides 后，Steam 图标启动、`steam -applaunch`、
#            本脚本直启，全都生效。
#
# 用法：
#   tools/run_game_linux.sh                 # 用默认路径启动
#   DEMONIC_MAHJONG_DIR=/path/to/game tools/run_game_linux.sh
set -euo pipefail

APP_ID=3444020
GAME_DIR="${DEMONIC_MAHJONG_DIR:-$HOME/.local/share/Steam/steamapps/common/DemonicMahjong}"
COMPAT_DIR="$HOME/.local/share/Steam/steamapps/compatdata/$APP_ID"
PROTON="$HOME/.local/share/Steam/steamapps/common/Proton - Experimental/proton"
USER_REG="$COMPAT_DIR/pfx/user.reg"

if [ ! -d "$GAME_DIR" ]; then
    echo "[run_game] 找不到游戏目录：$GAME_DIR" >&2
    exit 1
fi
if [ ! -x "$PROTON" ]; then
    echo "[run_game] 找不到 Proton：$PROTON" >&2
    exit 1
fi

# 1) 把 winhttp=n,b 写进前缀的 Wine DLL 覆盖（对所有启动方式生效）
#    注意：只在全局段 [Software\Wine\DllOverrides] 里查——AppDefaults\xxx.exe\DllOverrides
#    段里的同名值只对那个 exe 生效，不能当成全局已设置。
has_global_override() {
    # 先把反斜杠去掉再比：Wine 写 .reg 时段名是双反斜杠（[Software\\Wine\\DllOverrides]），
    # 直接拿它当正则会被当成「一个反斜杠」，永远匹配不上。
    awk '
        { line = $0; gsub(/\\/, "", line) }
        line ~ /^\[SoftwareWineDllOverrides\]/ { insec = 1; next }
        line ~ /^\[/                        { insec = 0 }
        insec && /"winhttp"="n,b"/          { found = 1 }
        END { exit(found ? 0 : 1) }
    ' "$1"
}

if [ -f "$USER_REG" ] && ! has_global_override "$USER_REG"; then
    {
        printf '\n[Software\\\\Wine\\\\DllOverrides] 1792595806\n'
        printf '#time=1dd5fa156b96c6c\n'
        printf '"winhttp"="n,b"\n'
    } >> "$USER_REG"
    echo "[run_game] 已写入 winhttp=n,b -> $USER_REG"
fi

# 2) 启动游戏（env 里的覆盖再兜一层，防前缀被重建时丢失注册表项）
export STEAM_COMPAT_DATA_PATH="$COMPAT_DIR"
export STEAM_COMPAT_CLIENT_INSTALL_PATH="$HOME/.local/share/Steam"
export SteamAppId="$APP_ID"
export SteamGameId="$APP_ID"
export WINEDLLOVERRIDES="winhttp=n,b"

cd "$GAME_DIR"
exec "$PROTON" run "$GAME_DIR/Demonic Mahjong.exe" "$@"
