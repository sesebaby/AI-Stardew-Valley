#!/bin/bash
# 开发专用：在 Linux 上拼一份能跑的游戏实例（只读游戏目录，所有东西放进 /dev/shm/aisv/<实例>）。
# 用法：setup.sh <实例名> [min|full]；说明见 README.md。
set -euo pipefail
NAME="${1:?实例名}"; SET="${2:-min}"
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../.." && pwd)"
GAME="${GAME:-$HOME/aisv-game}"; BASE="${BASE_MODS:-$HOME/aisv-base-mods}"
RUN="/dev/shm/aisv/$NAME"; R="$RUN/game"; DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"; N="$HOME/.nuget/packages"
FW="$(ls -d "$HOME"/.dotnet/shared/Microsoft.NETCore.App/8.* | tail -1)"
rm -rf "$R" "$RUN/Mods"
mkdir -p "$R" "$RUN/smapi-linux" "$RUN/Mods" "$RUN/io" "$RUN/xdg"

# SMAPI 和玩家同版本（4.5.2）；没有安装包就退回游戏目录里的 4.1.10。
if [ -f "$HOME/aisv-smapi/SMAPI-4.5.2-installer.zip" ]; then
	T="$RUN/smapi-zip"; rm -rf "$T"; mkdir -p "$T"
	unzip -q -o "$HOME/aisv-smapi/SMAPI-4.5.2-installer.zip" "SMAPI 4.5.2 installer/internal/linux/install.dat" -d "$T"
	unzip -q -o "$T/SMAPI 4.5.2 installer/internal/linux/install.dat" -d "$RUN/smapi-linux"
else
	unzip -q -o "$GAME/SMAPI 4.1.10 installer/internal/linux/install.dat" -d "$RUN/smapi-linux"
fi

cd "$GAME"
for f in *; do
	case "$f" in
		System.*|Microsoft.*|api-ms-*|ucrtbase*|hostfxr*|hostpolicy*|coreclr*|clr*|mscor*|netstandard*|WindowsBase*|*.deps.json|*.runtimeconfig.json|StardewModdingAPI*|smapi-internal|Mods|"SMAPI 4.1.10 installer"|Data|*.xml|*.exe|*.config|*.pdb) ;;
		*) ln -sfn "$GAME/$f" "$R/$f" ;;
	esac
done
for f in System.*.dll Microsoft.*.dll; do [ -e "$FW/$f" ] || ln -sfn "$GAME/$f" "$R/$f"; done
cp -r "$RUN/smapi-linux/smapi-internal" "$R/"
# SMAPI 4.5 用 SkiaSharp 读 png，游戏只带了 Windows 版的原生库。
[ -f "$HOME/aisv-smapi/libSkiaSharp.so" ] && cp "$HOME/aisv-smapi/libSkiaSharp.so" "$R/"

# .NET 8 上 SMAPI 自带的 Harmony 会段错误，换成支持 .NET 8 的 HarmonyX（Cecil 留 SMAPI 自己的）。
I="$R/smapi-internal"
cp "$N/harmonyx/2.16.1/lib/netstandard2.0/0Harmony.dll" "$I/"
for p in monomod.core monomod.utils monomod.backports monomod.ilhelpers monomod.runtimedetour; do
	d="$(ls -d "$N/$p"/* | sort -V | tail -1)"; cp "$d/lib/net8.0/"*.dll "$I/"
done
# SMAPI 针对旧 Harmony 的临时补丁读不到新版的内部类，只在这份副本里跳过。
"$DOTNET" build "$HERE/patcher" -c Release -v q -o "$RUN/patcher-out" >/dev/null
"$DOTNET" "$RUN/patcher-out/patcher.dll" "$RUN/smapi-linux/StardewModdingAPI.dll" "$R/StardewModdingAPI.dll" "MonoMod.Utils.MiniMonoModHotfix::Apply"
for f in "$RUN/smapi-linux/"StardewModdingAPI.*; do
	b="$(basename "$f")"; [ "$b" = StardewModdingAPI.dll ] || cp "$f" "$R/"
done

cd "$R"
python3 -I - <<'P'
import json,os
managed=["BmFont","CPExtBmFont","GalaxyCSharp","Lidgren.Network","MonoGame.Framework","SkiaSharp","Stardew Valley","StardewValley.GameData","Steamworks.NET","TextCopy","xTile","StardewModdingAPI"]
managed+=[f[:-4] for f in os.listdir('.') if f.endswith('.dll') and f.startswith(('System.','Microsoft.'))]
s=json.dumps({"runtimeTarget":{"name":".NETCoreApp,Version=v8.0"},"targets":{".NETCoreApp,Version=v8.0":{"app/1.0.0":{"runtime":{m+".dll":{} for m in managed}}}},"libraries":{"app/1.0.0":{"type":"project","serviceable":False,"sha512":""}}})
open("Stardew Valley.deps.json","w").write(s); open("StardewModdingAPI.deps.json","w").write(s)
P
cat > "$RUN/rc.json" <<'J'
{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"},"rollForward":"LatestMajor","configProperties":{"System.Runtime.TieredCompilation":false,"System.Reflection.Metadata.MetadataUpdater.IsSupported":false}}}
J

# 测试 Mods：min 只放联动要测的几个，full 是玩家的全套。
M="$RUN/Mods"
if [ "$SET" = full ]; then
	cp -r "$BASE/." "$M/"
else
	for d in ContentPatcher ConsoleCommands CJBCheatsMenu "Law and Order SV 1.4.12" StardewThief StardewThief_CP "The Stardew Squad" "[CP] TheStardewSquad.Haley" "[CP] TheStardewSquad.Harvey" "【前置】SpaceCore太空核心 - SpaceCore" "【前置】农场类型管理器 FTM - FarmTypeManager"; do
		[ -e "$BASE/$d" ] && cp -r "$BASE/$d" "$M/"
	done
fi
python3 -I "$HERE/fix-case.py" "$M"
"$HERE/deploy.sh" "$NAME"

# 存档目录共用 ~/.config 里的测试存档（只读复制进实例，测试改动不回写）。
mkdir -p "$RUN/xdg/StardewValley/Saves"
for s in "$HOME/.config/StardewValley/Saves"/AISVTest*; do [ -d "$s" ] && cp -r "$s" "$RUN/xdg/StardewValley/Saves/"; done
[ -f "$HOME/.config/StardewValley/startup_preferences" ] && cp "$HOME/.config/StardewValley/startup_preferences" "$RUN/xdg/StardewValley/"
echo "ready: $RUN"
