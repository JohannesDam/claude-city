#!/bin/bash
# Builds the mod with the Roslyn compiler from Visual Studio Build Tools against the installed
# game's assemblies, then deploys:
#   ClaudeCityBridge.dll (core: HTTP server, systems, agent tool) -> <userdata>/Mods/ClaudeCityBridge/
#     (loaded by the game at startup; changing it needs a game restart)
#   ClaudeCityApi.dll    (endpoints, hot-reloadable)             -> <userdata>/ModsData/ClaudeCityBridge/
#     (after deploying, POST http://127.0.0.1:8787/reload to swap it in)
# Usage: ./build.sh [core|api|all] [--no-deploy]
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
WHAT="${1:-all}"
GAME="${CS2_GAME:-/c/XboxGames/Cities- Skylines II - PC Edition/Content}"
M="$GAME/Cities2_Data/Managed"
CSC="${CSC:-/c/Program Files (x86)/Microsoft Visual Studio/18/BuildTools/MSBuild/Current/Bin/Roslyn/csc.exe}"
OUT="$HERE/bin"
USERDATA="${CS2_USERDATA:-/c/Users/$USERNAME/AppData/LocalLow/Colossal Order/Cities Skylines II}"
mkdir -p "$OUT"

REFS=()
for r in mscorlib netstandard System System.Core System.Net.Http System.Runtime \
         Game Colossal.Core Colossal.Logging Colossal.Mathematics Colossal.Collections Colossal.IO \
         Colossal.IO.AssetDatabase Colossal.UI.Binding Colossal.UI Colossal.Localization Colossal.PSI.Common \
         Unity.Entities Unity.Collections Unity.Mathematics Unity.Burst Unity.Transforms \
         UnityEngine UnityEngine.CoreModule UnityEngine.InputLegacyModule; do
  if [ -f "$M/$r.dll" ]; then REFS+=("-r:$(cygpath -w "$M/$r.dll")"); else echo "warn: missing $r.dll"; fi
done

compile() { # name srcdir extra-refs...
  local name="$1" dir="$2"; shift 2
  local SRC=()
  for f in "$HERE/$dir"/*.cs; do SRC+=("$(cygpath -w "$f")"); done
  "$CSC" -nologo -noconfig -nostdlib+ -target:library -langversion:9.0 -optimize+ -deterministic \
    -nowarn:CS1701,CS1702 -out:"$(cygpath -w "$OUT/$name.dll")" "${REFS[@]}" "$@" "${SRC[@]}"
  echo "built $OUT/$name.dll"
}

if [ "$WHAT" = "core" ] || [ "$WHAT" = "all" ]; then compile ClaudeCityBridge ClaudeCityBridge; fi
if [ "$WHAT" = "api" ] || [ "$WHAT" = "all" ]; then
  compile ClaudeCityApi ClaudeCityApi "-r:$(cygpath -w "$OUT/ClaudeCityBridge.dll")"
fi

if [ "$2" != "--no-deploy" ] && [ "$1" != "--no-deploy" ]; then
  if [ "$WHAT" = "core" ] || [ "$WHAT" = "all" ]; then
    mkdir -p "$USERDATA/Mods/ClaudeCityBridge"
    cp "$OUT/ClaudeCityBridge.dll" "$USERDATA/Mods/ClaudeCityBridge/" && echo "deployed core (restart the game to load it)"
  fi
  if [ "$WHAT" = "api" ] || [ "$WHAT" = "all" ]; then
    mkdir -p "$USERDATA/ModsData/ClaudeCityBridge"
    cp "$OUT/ClaudeCityApi.dll" "$USERDATA/ModsData/ClaudeCityBridge/" && echo "deployed api (POST /reload to activate)"
  fi
fi
