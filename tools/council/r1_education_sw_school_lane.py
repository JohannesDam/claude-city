"""Council round 1 - Education - sw-school-lane.

ElementarySchool01 on the empty land between the SW homes (end z -811) and the old industry (z ~-1050),
on a new 140 m cul-de-sac school lane at x=-2900 running south from corner node 1294488:1 (-2900,-870).
The lane is deliberately NOT connected to the industrial road at z~-1100 (keeps trucks out).

Usage: python -I council/r1_education_sw_school_lane.py dry|build|trees
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, building, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"

LANE = [[-2900, -870], [-2900, -940], [-2900, -1010]]
ES01 = dict(x=-2859.5, z=-934, angle=-90)
BOX = (-2894, -2800, -1012, -850)  # accepted lot corners for a findspot fallback
BAD_EDGE = "1294925:1"  # findspot top spot there demolishes 7 homes

spent = 0


def show(label, r):
    print(f"{label:<34} {json.dumps(short(r, keys=('applied', 'cost', 'errors', 'problems', 'placedId', 'roadConnected', 'error', 'warning', 'facing', 'pos')))}", flush=True)
    return r


def build_step(label, fn, *a, **kw):
    global spent
    d = show(label + " (dry)", fn(*a, **kw, dryRun=True))
    if d.get("errors") not in (0, None) or d.get("error"):
        return None
    r = show(label, fn(*a, **kw))
    if r.get("applied"):
        spent += r.get("cost") or 0
        return r
    return None


if __name__ == "__main__":
    if MODE == "dry":
        with paused("education"):
            show("lane (dry)", road("Small Road", LANE, dryRun=True))
            show("ES01 (dry)", building("ElementarySchool01", **ES01, dryRun=True))
    elif MODE == "build":
        out = {}
        with paused("education"):
            out["lane"] = build_step("school lane", road, "Small Road", LANE)
            if out["lane"]:
                out["es01"] = build_step("ES01 SW lane", building, "ElementarySchool01", **ES01)
        print("SPENT", spent)
        json.dump(out, open(r"C:\soft\cityskyline\logs\council\r1_education_sw_school_lane.json", "w"), indent=1)
    elif MODE == "trees":
        segs = get("/nets", x=-2900, z=-940, radius=80).get("segments") or []
        lane = [s["id"] for s in segs if abs(s["start"][0] + 2900) < 1 and abs(s["end"][0] + 2900) < 1
                and min(s["start"][1], s["end"][1]) >= -1015 and max(s["start"][1], s["end"][1]) <= -869]
        print("lane edges", lane)
        with paused("education"):
            d = show("trees lane (dry)", post("/roadupgrade", {"edges": lane, "upgrade": "Trees", "side": "both", "dryRun": True}))
            if d.get("errors") in (0, None) and not d.get("error"):
                show("trees lane", post("/roadupgrade", {"edges": lane, "upgrade": "Trees", "side": "both"}))
