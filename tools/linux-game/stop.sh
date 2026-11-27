#!/bin/bash
# 只停这个实例自己的游戏（别的会话也在跑游戏，不能按进程名杀）。
# SMAPI 在游戏结束后会停在"按任意键退出"，进程不退、联机端口也不放：等几秒还在就强制结束。
NAME="${1:?实例名}"; RUN="/dev/shm/aisv/$NAME"
if pkill -f "$RUN/rc.json"; then
	for _ in 1 2 3 4 5 6 7 8 9 10; do pgrep -f "$RUN/rc.json" >/dev/null || break; sleep 0.5; done
	pkill -KILL -f "$RUN/rc.json" 2>/dev/null
	echo "stopped $NAME"
else
	echo "not running: $NAME"
fi
[ -f "$RUN/stdin.pid" ] && kill "$(cat "$RUN/stdin.pid")" 2>/dev/null; rm -f "$RUN/stdin.pid"; true
