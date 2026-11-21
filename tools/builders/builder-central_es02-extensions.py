"""builder-central: ES02 classroom extensions in Downtown (conductor-approved 2033-01: each may remove 2-5 low homes)
to close the remaining ~350-seat elementary shortfall after the Learning Square schools.

Each ElementarySchool02 Classroom Extension Building adds 100 seats ($8k). /extensions lists free slots, but every
Downtown slot that validates removes low homes, so this passes allowDemolish only when the dry run removes
<= 5 buildings, all of them NA Residential Low, and logs them to logs/council/demolitions.jsonl.

usage: python -I builders/builder-central_es02-extensions.py <owner>[:<side>] ... [dry]  (no side: tries all, least removal)
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused  # noqa: E402

WHO = "builder-central"
ITEM = "b2-education-b2-edu-downtown-learning-square"
PREFAB = "ElementarySchool02 Classroom Extension Building"
DRY = "dry" in sys.argv[1:]
JOBS = [a.split(":", 2) for a in sys.argv[1:] if a != "dry"]  # "440047:1:right" -> ["440047", "1", "right"]

SIDES = ("right", "back", "front", "left", "auto")


def check(owner, side):
    d = post("/extension", {"owner": owner, "prefab": PREFAB, "side": side, "allowDemolish": True, "dryRun": True},
             timeout=180)
    w = d.get("wouldDemolish") or []
    low = all("Residential Low" in (x.get("zone") or "") for x in w)
    ok = len(w) <= 5 and low and not d.get("problems")  # "Overlap Existing" = a road, mast or stop in the slot: real build fails
    print(f"  {owner} {side}: {len(w)} to remove, {'low only' if low else 'NOT only low'}, problems {d.get('problems')}",
          flush=True)
    return ok, w


for parts in JOBS:
    owner = ":".join(parts[:2])
    sides = [parts[2]] if len(parts) > 2 else SIDES
    with paused(WHO):  # one queue wait for the dry runs and the build (dry runs do not pause the game)
        best = None
        for side in sides:
            ok, w = check(owner, side)
            if ok and (best is None or len(w) < len(best[1])):
                best = (side, w)
        if best is None or DRY:
            print(owner, "->", "no acceptable slot" if best is None else f"best {best[0]} ({len(best[1])})", flush=True)
            continue
        side, w = best
        r = post("/extension", {"owner": owner, "prefab": PREFAB, "side": side, "allowDemolish": True}, timeout=180)
    print(owner, side, json.dumps({k: r.get(k) for k in ("applied", "errors", "cost", "problems")}), flush=True)
    if r.get("applied"):
        with open(r"C:\soft\cityskyline\logs\council\demolitions.jsonl", "a", encoding="utf-8") as f:
            f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M"), "by": WHO, "item": ITEM,
                                "prefab": f"{PREFAB} on {owner} ({side})", "ids": [x.get("id") for x in w],
                                "cost": {"buildings": len(w), "households": sum(x.get("renters") or 0 for x in w),
                                         "companies": 0, "byKind": {"home low": len(w)}},
                                "note": "+100 elementary seats; conductor approved 2-5 low homes per extension"}) + "\n")
