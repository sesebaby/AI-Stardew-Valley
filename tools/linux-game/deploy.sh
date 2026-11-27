#!/bin/bash
# 开发专用：构建本 mod 和测试工具，放进实例的 Mods（保留实例里的 config.json 和 Data）。
# 用法：deploy.sh <实例名>；AI 默认指向本机假 AI（fakeai.py，端口 FAKEAI_PORT，默认 18080）。
set -euo pipefail
NAME="${1:?实例名}"
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../.." && pwd)"
GAME="${GAME:-$HOME/aisv-game}"; DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
RUN="/dev/shm/aisv/$NAME"; M="$RUN/Mods"
"$DOTNET" build "$REPO/AISV.sln" -p:GamePath="$GAME" -v q -nologo -clp:ErrorsOnly
"$DOTNET" build "$HERE/harness" -p:GamePath="$GAME" -v q -nologo -clp:ErrorsOnly -o "$RUN/harness-out"
mkdir -p "$M/AIStardewValley" "$M/AISV.TestHarness"
rsync -a --delete --exclude config.json --exclude Data/ "$REPO/bin/AIStardewValley/" "$M/AIStardewValley/"
rsync -a --delete "$RUN/harness-out/" "$M/AISV.TestHarness/"
C="$M/AIStardewValley/config.json"
if [ ! -f "$C" ]; then
	cat > "$C" <<J
{ "AiConnectionMode": "own", "CustomLlmBaseUrl": "http://127.0.0.1:${FAKEAI_PORT:-18080}/v1", "CustomLlmApiKey": "test", "CustomLlmModel": "fake", "UsageReportOn": false }
J
fi
echo "deployed: $M"
