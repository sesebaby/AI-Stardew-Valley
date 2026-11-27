#!/bin/bash
# 只停这个实例自己的游戏（别的会话也在跑游戏，不能按进程名杀）。
NAME="${1:?实例名}"; RUN="/dev/shm/aisv/$NAME"
pkill -f "$RUN/rc.json" && echo "stopped $NAME" || echo "not running: $NAME"
[ -f "$RUN/stdin.pid" ] && kill "$(cat "$RUN/stdin.pid")" 2>/dev/null; rm -f "$RUN/stdin.pid"; true
