"""Troubleshooter: school seats (worklist item b1-education-edu-seat-extensions).

Elementary is 110% city-wide but 17 of 22 elementary schools are full: the seats sit in the new Downtown ES03s
and in Westbrook. Classroom extensions (+100 seats, $8k) go on full ES02s in Westside, Downtown and Northfield;
HS02 classroom extensions on the full/nearly full high schools. Clean slots first; where no side is free the
slot may replace at most 2-3 single-family homes (owner rule "make room"; residential demand is 100).
Every slot is checked (builders/troubleshooter_extcheck.py), buildings around it are compared before/after and
removals are logged to logs/council/demolitions.jsonl.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_schools.py [--build] [names...]
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

ES = "ElementarySchool02 Classroom Extension Building"
HS = "HighSchool02 Classroom Extension Building"
COL = None
JOBS = [
    # name, owner, prefab, placement, max zoned buildings it may replace (single-family only)
    ("es-440046", "440046:1", ES, {"side": "back"}, 0),
    ("hs-1341397", "1341397:157", HS, {"side": "back"}, 0),
    ("hs-525823", "525823:31", HS, {"side": "left"}, 0),
    ("hs-158054", "158054:1", HS, {"side": "left"}, 0),
    ("es-219649", "219649:149", ES, {"side": "left"}, 2),
    ("es-189068", "189068:55", ES, {"side": "back"}, 2),
    ("es-440048", "440048:1", ES, {"side": "left"}, 2),
    ("es-440047", "440047:1", ES, {"side": "right"}, 2),
    ("es-380486", "380486:1", ES, {"side": "right"}, 3),
    ("es-243587", "243587:229", ES, {"side": "left"}, 2),
    ("es-554390", "554390:23", ES, {"side": "left"}, 2),
]


def footprint_half(owner, prefab, slot, facing):
    """Half sizes (x, z) of the extension lot: width along the owner side, depth outwards."""
    info = get("/extensions", owner=owner)
    off = next(o for o in info["offered"] if o["name"] == prefab)
    w, d = off["lot"][0] * 4, off["lot"][1] * 4
    along_x = abs(info["facing"][0]) < 0.5
    if slot.split("@")[0] in ("left", "right"):
        along_x = not along_x
    return (w, d) if along_x else (d, w)


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
        hx, hz = footprint_half(owner, prefab, dry.get("slot", ""), dry.get("facing"))
        # count "maybe" hits too: a 2 m lot overlap is enough for the game to replace a building
        hits = buildings_hit(pos[0], pos[1], hx + 2, hz + 2, owner_ids={owner})
        print(f"[{name}] slot {pos} sure hits: {[(h[1], h[2]) for h in hits]}")
        if len(hits) > max_clear:
            return {"name": name, "ok": False, "why": f"{len(hits)} zoned buildings under the slot > {max_clear}"}
        if any("ResidentialLow" not in h[2] for h in hits):
            return {"name": name, "ok": False, "why": "slot would replace a workplace or a multi-family home"}
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
                                "why": "school seats: classroom extension on a full school, no free side"}) + "\n")
    return {"name": name, "ok": bool(res.get("applied")), "cost": res.get("cost"), "pos": pos,
            "removed": [b["id"] + " " + b["prefab"] for b in gone]}


out = []
for job in JOBS:
    if ONLY and job[0] not in ONLY:
        continue
    with paused("troubleshooter"):
        out.append(run(*job))
print(json.dumps(out, indent=1))
