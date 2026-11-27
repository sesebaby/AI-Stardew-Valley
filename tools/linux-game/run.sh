#!/bin/bash
# 开发专用：启动一个实例，后台运行，日志在 /dev/shm/aisv/<实例>/console.log。
# 用法：run.sh <实例名> [最长秒数，默认 3600]；停止：stop.sh <实例名>（只停这个实例）。
# 标准输入接一个 FIFO（stdin），测试工具的 console 命令往里写 SMAPI 控制台命令。
NAME="${1:?实例名}"; SECS="${2:-3600}"
RUN="/dev/shm/aisv/$NAME"; R="$RUN/game"
cd "$R" || exit 1
rm -rf "$RUN/io/in" "$RUN/io/out"; mkdir -p "$RUN/io/in" "$RUN/io/out"
rm -f "$RUN/stdin"; mkfifo "$RUN/stdin"
# 一直开着 FIFO 的写端，游戏读到的标准输入才不会结束。
nohup timeout "$SECS" sleep infinity > "$RUN/stdin" 2>/dev/null &
echo $! > "$RUN/stdin.pid"
nohup env DISPLAY="${DISPLAY:-:1}" SDL_AUDIODRIVER=dummy XDG_CONFIG_HOME="$RUN/xdg" AISV_HARNESS_DIR="$RUN/io" AISV_HARNESS_STDIN="$RUN/stdin" LD_LIBRARY_PATH="$R" TERM=xterm \
	timeout "$SECS" "${DOTNET:-$HOME/.dotnet/dotnet}" exec --runtimeconfig "$RUN/rc.json" StardewModdingAPI.dll --mods-path "$RUN/Mods" --use-current-shell \
	< "$RUN/stdin" > "$RUN/console.log" 2>&1 &
echo "started $NAME pid $!"
