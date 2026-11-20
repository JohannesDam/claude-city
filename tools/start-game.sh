#!/bin/bash
# Starts Cities: Skylines II (Xbox/Game Pass build), gets past the "press any key" engagement
# screen with a real click (the game waits for it before loading mods), and waits until the
# ClaudeCityBridge HTTP API answers. Prints the bridge status when ready.
HERE="$(cd "$(dirname "$0")" && pwd)"
APP='shell:AppsFolder\ParadoxInteractive.CitiesSkylinesII-PCEdition_zfnrdv2de78ny!Cities2'
BRIDGE=http://127.0.0.1:8787
INPUT="$(cygpath -w "$HERE/input.ps1")"

if curl -s -m 2 $BRIDGE/ping >/dev/null; then echo "bridge already up"; curl -s $BRIDGE/ping; exit 0; fi
if ! tasklist //FI "IMAGENAME eq Cities2.exe" | grep -q Cities2; then
  powershell -NoProfile -Command "Start-Process '$APP'"
  echo "launching..."
fi
for i in $(seq 1 120); do
  tasklist //FI "IMAGENAME eq Cities2.exe" | grep -q Cities2 && break; sleep 2
done
# The engagement screen accepts input once the asset library is loaded; click until mods load.
for i in $(seq 1 90); do
  if curl -s -m 2 $BRIDGE/ping >/dev/null; then break; fi
  if [ $((i % 3)) -eq 0 ]; then powershell -NoProfile -File "$INPUT" -FX 0.5 -FY 0.6 >/dev/null 2>&1; fi
  sleep 3
done
for i in $(seq 1 60); do
  s=$(curl -s -m 3 $BRIDGE/status) && echo "$s" | grep -q '"mode"' && { echo "$s"; curl -s $BRIDGE/ping; echo; exit 0; }
  sleep 3
done
echo "bridge did not come up"; exit 1
