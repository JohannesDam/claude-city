"""Troubleshooter: deathcare relief (worklist item b1-health_safety-deathcare-relief-now).

- Burial lots on the three full Cemetery02s and on Northfield's nearly full one. No side is free there, so the
  back (or right) slot replaces a few single-family homes (owner rule "make room": <= 60 households; homes
  are cheap to clear with residential demand at 100). Removed buildings are listed and logged to
  logs/council/demolitions.jsonl.
- Clean extensions: Columbarium in a free back corner of 278811:85 (SW), Burial Lot behind 622066:29
  (lakeside, back side away from the protected promenade), Refrigerator Expansion on the SW crematorium.

POST /extension does not report demolition, so every slot is checked first (builders/troubleshooter_extcheck.py)
and the buildings around it are compared before and after.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_deathcare.py [--build] [names...]
"""
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)
from cc import get, paused, post  # noqa: E402
from troubleshooter_extcheck import buildings_hit  # noqa: E402

BUILD = "--build" in sys.argv
ONLY = [a for a in sys.argv[1:] if not a.startswith("--")]
LOG = os.path.join(HERE, "..", "..", "logs", "council", "demolitions.jsonl")

BL, COL = "Cemetery02 Additional Burial Lot", "Cemetery02 Columbarium"
JOBS = [
    # name, owner, prefab, placement, max zoned buildings it may replace
    ("downtown-267744", "267744:17", BL, {"side": "back"}, 6),
    ("westside-403759", "403759:183", BL, {"side": "back"}, 5),
    ("westside-1154205", "1154205:25", BL, {"side": "right"}, 5),
    ("northfield-783803", "783803:65", BL, {"side": "back"}, 5),
    ("sw-278811-col", "278811:85", COL, {"side": "back", "x": -3160, "z": -168}, 0),
    ("sw-278811-col-e", "278811:85", COL, {"side": "back", "x": -3076, "z": -168}, 0),
    ("downtown-267744-l", "267744:17", BL, {"side": "left"}, 5),
    ("downtown-267744-r", "267744:17", BL, {"side": "right"}, 5),
    ("westside-403759-l", "403759:183", BL, {"side": "left"}, 5),
    ("westside-403759-r", "403759:183", BL, {"side": "right"}, 5),
    ("northfield-783803-l", "783803:65", BL, {"side": "left"}, 5),
    ("northfield-783803-r", "783803:65", BL, {"side": "right"}, 5),
    ("northfield-783803-col", "783803:65", COL, {"x": -1866.9, "z": 2579.5}, 1),
    ("lake-622066", "622066:29", BL, {"side": "back"}, 0),
    ("sw-crem-fridge", "316408:141", "Crematorium01 Refrigerator Expansion", {}, 0),
]


def around(x, z, r=75):
    res = get("/buildings", kind="all", x=round(x), z=round(z), radius=r)
    return {b["id"]: b for b in res.get("buildings", [])}


def run(name, owner, prefab, place, max_clear):
    body = {"owner": owner, "prefab": prefab, **place}
    dry = post("/extension", {**body, "dryRun": True}, timeout=120)
    keep = ("errors", "problems", "slot", "pos", "cost", "error", "tried")
    print(f"[{name}] dry", json.dumps({k: dry.get(k) for k in keep if dry.get(k) is not None})[:600])
    if dry.get("errors") != 0 or dry.get("error"):
        return {"name": name, "ok": False, "why": dry.get("error") or dry.get("problems") or dry.get("tried")}
    pos = dry.get("pos")
    hits = []
    if pos:
        half = 12 if prefab == COL else 24
        # count "maybe" hits too: a 2 m lot overlap is enough for the game to replace a building
        hits = buildings_hit(pos[0], pos[1], half + 2, half + 2, owner_ids={owner})
        print(f"[{name}] slot {pos} sure hits: {[(h[1], h[2]) for h in hits]}")
        if len(hits) > max_clear:
            return {"name": name, "ok": False, "why": f"{len(hits)} zoned buildings under the slot > {max_clear}"}
        if any("Residential" not in h[2] for h in hits):
            return {"name": name, "ok": False, "why": "slot would replace a workplace"}
    if not BUILD:
        return {"name": name, "ok": True, "dry": True, "pos": pos, "hits": [h[1] for h in hits]}
    before = around(*pos) if pos else {}
    res = post("/extension", body, timeout=120)
    time.sleep(1.0)
    after = around(*pos) if pos else {}
    gone = [before[i] for i in before if i not in after]
    print(f"[{name}] built applied={res.get('applied')} errors={res.get('errors')} cost={res.get('cost')} "
          f"removed={[(b['id'], b['prefab']) for b in gone]}")
    if gone:
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": "troubleshooter", "prefab": prefab,
                                "owner": owner, "pos": pos, "removed": [[b["id"], b["prefab"]] for b in gone],
                                "why": "deathcare relief: burial lot on a full/nearly full cemetery, no free side"}) + "\n")
    return {"name": name, "ok": bool(res.get("applied")), "cost": res.get("cost"), "pos": pos,
            "removed": [b["id"] + " " + b["prefab"] for b in gone]}


out = []
for job in JOBS:
    if ONLY and job[0] not in ONLY:
        continue
    with paused("troubleshooter"):
        out.append(run(*job))
print(json.dumps(out, indent=1))
