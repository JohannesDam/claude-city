"""builder-east / industry push 2033-01 (industrial demand 61): Smelter Lane + Works Way south side.

1. Smelter Lane: tree-lined Medium Road from the Foundry Gate - J_N Large Road (T at (2956,-1462)) south along the
   free west strip of the works (x ~2870, between the Foundry Road lot row and the tile edge x 2805) to a cul-de-sac
   at (3060,-2140); Industrial Manufacturing on both sides (no demolition). Downwind of every home.
2. Works Way (Large, x 3960..4500): its south-west side was never zoned - Industrial Manufacturing there.
Stages: dry | build | zone
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused  # noqa: E402

WHO = "builder-east"
LOG = r"C:\soft\cityskyline\logs\council\builder-east_jobs.json"
PTS = [[2955.6, -1461.8], [2880, -1600], [2866, -1850], [2920, -2070], [3060, -2140]]
LANE = {"prefab": "Medium Road", "points": PTS, "curve": "smooth"}
WORKS_WAY = ["2178301:39", "2178197:41", "2178199:41", "2116810:53", "2178195:41"]
KEYS = ("applied", "cost", "errors", "problems", "error", "wouldDemolish")


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def lane_edges():
    ids = []
    for x, z in PTS[1:]:
        r = get("/nets", x=x, z=z, radius=120, filter="Medium Road", limit=50)
        for e in r["segments"]:
            xs = [e["start"][0], e["end"][0]]
            if max(xs) < 3070 and min(xs) > 2840 and e["id"] not in ids:
                ids.append(e["id"])
    return ids


stage = sys.argv[1] if len(sys.argv) > 1 else "dry"
if stage == "dry":
    r = post("/road", {**LANE, "dryRun": True})
    print("lane", json.dumps({k: r.get(k) for k in KEYS}), (r.get("geometry") or {}).get("minRadius"))
elif stage == "build":
    with paused(WHO):
        r = post("/road", LANE)
        print("lane", json.dumps({k: r.get(k) for k in KEYS}))
        if r.get("applied"):
            c = post("/roundabout", {"at": PTS[-1], "minDegree": 1, "radius": 20, "prefab": "CulDeSacLarge01"})
            print("culdesac", json.dumps({k: c.get(k) for k in KEYS}))
    time.sleep(1)
    ids = lane_edges()
    print("edges", ids)
    with paused(WHO):
        for up in ("Trees", "Grass"):
            u = post("/roadupgrade", {"edges": ids, "upgrade": up, "side": "both"})
            print(up, json.dumps({k: u.get(k) for k in ("applied", "cost", "errors")}))
    log("smelter_lane_edges", lane_edges())
elif stage == "zone":
    ids = lane_edges()
    with paused(WHO):
        z1 = post("/zone", {"zone": "Industrial Manufacturing", "mode": "road", "edges": ids, "depth": 6,
                            "side": "both", "overwrite": False})
        print("lane zone", json.dumps({k: z1.get(k) for k in ("applied", "cellsChanged", "occupiedCellsRezoned", "error")}))
        z2 = post("/zone", {"zone": "Industrial Manufacturing", "mode": "road", "edges": WORKS_WAY, "depth": 6,
                            "side": "right", "overwrite": False})
        print("works way zone", json.dumps({k: z2.get(k) for k in ("applied", "cellsChanged", "occupiedCellsRezoned", "error")}))
    log("smelter_zone", {"lane": z1.get("cellsChanged"), "worksWay": z2.get("cellsChanged")})
