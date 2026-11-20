"""Council round 1 - Education - east-highschool.

HighSchool01 on the empty buffer land between the diagonal highway (x ~-970..-930) and the
East-lakeside homes (lots on x=-712 end at x -736), fronting the z=2160 road (edges 728937:15 / 752399:9).

Usage: python -I council/r1_education_east_highschool.py dry|build|trees
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, building, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"

# primary spot, then the planned fallback (west edge must stay east of -925; east edge west of -736)
SPOTS = [dict(x=-830, z=2232.5, angle=180), dict(x=-834, z=2232.5, angle=180)]
ROAD_EDGES = ["1552873:13", "752400:9"]  # merged by the game when the school was placed (was 728937:15 / 752399:9)
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "facing", "pos")


def show(label, r):
    print(f"{label:<34} {json.dumps(short(r, keys=KEYS))}", flush=True)
    return r


if __name__ == "__main__":
    if MODE == "dry":
        with paused("education"):
            for s in SPOTS:
                show(f"HS01 {s} (dry)", building("HighSchool01", **s, dryRun=True))
    elif MODE == "build":
        res = None
        with paused("education"):
            for s in SPOTS:
                d = show(f"HS01 {s} (dry)", building("HighSchool01", **s, dryRun=True))
                if d.get("errors") == 0 and not d.get("error"):
                    res = show("HS01 east lakeside", building("HighSchool01", **s))
                    break
        print("SPENT", (res or {}).get("cost") if (res or {}).get("applied") else 0)
        json.dump(res, open(r"C:\soft\cityskyline\logs\council\r1_education_east_highschool.json", "w"), indent=1)
    elif MODE == "trees":
        with paused("education"):
            d = show("trees z=2160 (dry)", post("/roadupgrade", {"edges": ROAD_EDGES, "upgrade": "Trees", "side": "both", "dryRun": True}))
            if d.get("errors") in (0, None) and not d.get("error"):
                show("trees z=2160", post("/roadupgrade", {"edges": ROAD_EDGES, "upgrade": "Trees", "side": "both"}))
