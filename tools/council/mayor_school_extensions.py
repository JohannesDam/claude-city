"""Mayor quick win (2031-01): every school gets its seat extension (Classroom Extension Building / Extension
Wing) - /education showed 2,633 high-school and 522 elementary seats short."""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import get, paused, post, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
SEAT = ("Classroom Extension", "Extension Wing")
todo = []
for b in get("/extensions", filter="School", onlyBuildable=1).get("buildings", []):
    for name in b.get("buildable", []):
        if any(s in name for s in SEAT):
            todo.append((b["id"], b["prefab"], b["pos"], name.rsplit(" (", 1)[0]))
print(len(todo), "extensions to build")
spent = 0
for i in range(0, len(todo), 6):
    with paused("mayor schools"):
        for owner, prefab, pos, ext in todo[i:i + 6]:
            d = post("/extension", {"owner": owner, "prefab": ext, "side": "auto", "dryRun": True}, timeout=120)
            if d.get("errors") != 0:
                print(f"{prefab} {pos} {ext}: dry run failed {short(d)}", flush=True)
                continue
            r = post("/extension", {"owner": owner, "prefab": ext, "side": "auto"}, timeout=120)
            spent += r.get("cost") or 0
            print(f"{prefab} {pos} {ext}: {short(r)}", flush=True)
print("spent", spent)
e = get("/education")
for lv in e.get("levels", []):
    print(lv["level"], "seats", lv["seats"], "eligible", lv["eligible"], "shortfall", lv["shortfall"])
