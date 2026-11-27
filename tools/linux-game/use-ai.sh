#!/bin/bash
# 开发专用：把实例的 AI 换成假 AI 或真网关（改实例里的 config.json，要先 stop 再 run）。
# 用法：use-ai.sh <实例名> fake|real
# real 从 ~/.config/aisv/chat-gateway.env 读 AISV_LLM_BASE、AISV_LLM_KEY、AISV_LLM_MODEL（OpenAI 兼容），密钥不进仓库。
set -euo pipefail
NAME="${1:?实例名}"; MODE="${2:?fake 或 real}"
C="/dev/shm/aisv/$NAME/Mods/AIStardewValley/config.json"
[ -f "$C" ] || { echo "没有 $C，先 deploy.sh $NAME"; exit 1; }
case "$MODE" in
	fake) export AISV_LLM_BASE="http://127.0.0.1:${FAKEAI_PORT:-18080}/v1" AISV_LLM_KEY=test AISV_LLM_MODEL=fake ;;
	real) set -a; . "${AISV_GATEWAY_ENV:-$HOME/.config/aisv/chat-gateway.env}"; set +a ;;
	*) echo "fake 或 real"; exit 1 ;;
esac
python3 -I - "$C" <<'P'
import json, os, sys
p = sys.argv[1]
d = json.load(open(p, encoding='utf-8-sig'))
d.update(AiConnectionMode='own', CustomLlmBaseUrl=os.environ['AISV_LLM_BASE'], CustomLlmApiKey=os.environ['AISV_LLM_KEY'], CustomLlmModel=os.environ['AISV_LLM_MODEL'])
json.dump(d, open(p, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
print(f"{p}: {d['CustomLlmModel']} @ {d['CustomLlmBaseUrl']}")
P
