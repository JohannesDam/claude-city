#!/bin/bash
# Recovers after a crash: starts the game, loads the most recent save ("Continue"),
# turns tutorials off and resumes at full speed.
HERE="$(cd "$(dirname "$0")" && pwd)"
B=http://127.0.0.1:8787
"$HERE/start-game.sh" || exit 1
for i in $(seq 1 60); do curl -s $B/status | grep -q '"MainMenu"' && break; sleep 3; done
for i in $(seq 1 20); do r=$(curl -s -X POST $B/load -d "{}"); echo "$r"; echo "$r" | grep -q loading && break; sleep 5; done
for i in $(seq 1 80); do
  s=$(curl -s $B/status); echo "$s" | grep -q '"mode":"Game","loading":false' && break; sleep 3
done
sleep 5
curl -s -X POST $B/ui -d '{"action":"notutorials"}' >/dev/null
curl -s -X POST $B/ui -d '{"action":"resume"}' >/dev/null
curl -s -X POST $B/speed -d '{"speed":3}' >/dev/null
curl -s $B/status; echo
