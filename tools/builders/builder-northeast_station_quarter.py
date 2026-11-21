"""builder-northeast - b2-housing-northfield-station-quarter-p1, sub-phase 1a.

Phase box x -2150..-1700, z 2450..2800 (demolition approved inside it only). Sub-phase 1a = the block
between the z=2710 and z=2820 streets, both sides of the x=-1909 avenue (T1 stop at z~2765): 36 low homes.
No street rebuilt in 1a (a Medium 'Station Walk' on z=2710 would also clear the next block's north row -
that belongs to 1b). Rezone with overwrite, per street edge and side (all edges run west, so
north side of z=2710 = "right", south side of z=2820 = "left"), depth 6:
- avenue-adjacent: NA Residential Mixed (shops below),
- second row: NA Residential High, one edge Residential LowRent (~10%),
- box edges facing the remaining low homes: NA Residential Medium (step-down).
Trees both sides on the rezoned streets.

Usage: python -I builders/builder-northeast_station_quarter.py dry|build|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, access_check  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_station_quarter.json"
PLAN = [
    ("NA Residential Mixed", ["364405:3", "364399:3"], "right"),
    ("NA Residential High", ["364411:3", "364302:3"], "right"),
    ("NA Residential Medium", ["364418:3", "364296:3"], "right"),
    ("NA Residential High", ["210766:59", "210758:133"], "left"),
    ("Residential LowRent", ["210786:43"], "left"),
    ("NA Residential Medium", ["217286:75", "1406643:7"], "left"),
]
BOX = [-2125, 2722, -1695, 2816]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def zone_all(dry):
    out = []
    for zname, edges, side in PLAN:
        r = post("/zone", {"zone": zname, "mode": "road", "edges": edges, "depth": 6, "side": side,
                           "overwrite": True, "allowDemolish": True, "dryRun": dry})
        o = {k: r.get(k) for k in ("applied", "cellsChanged", "occupiedCellsRezoned", "cellsFromZone", "errors", "error")}
        print(zname, edges, side, json.dumps(o), flush=True)
        out.append(o)
    return out


if MODE == "dry":
    hh = get("/buildings", kind="all", x=-1910, z=2769, radius=230)
    inbox = [b for b in hh.get("buildings", []) if BOX[0] <= b["pos"][0] <= BOX[2] and BOX[1] <= b["pos"][1] <= BOX[3]]
    print("buildings in 1a box", len(inbox), sorted({b["prefab"].split("_L")[0] for b in inbox}))
    zone_all(True)
elif MODE == "build":
    with paused(WHO):
        log("zone", zone_all(False))
        ids = [e for _, edges, _ in PLAN for e in edges]
        for i in range(0, len(ids), 4):
            t = post("/roadupgrade", {"edges": ids[i:i + 4], "upgrade": "Trees", "side": "both"})
            print("trees", t.get("applied"), t.get("errors"))
elif MODE == "check":
    hh = get("/buildings", kind="all", x=-1910, z=2769, radius=230)
    inbox = [b for b in hh.get("buildings", []) if BOX[0] <= b["pos"][0] <= BOX[2] and BOX[1] <= b["pos"][1] <= BOX[3]]
    from collections import Counter
    print(len(inbox), Counter(b["prefab"].split("_L")[0] for b in inbox))
    print("access", access_check([b["id"] for b in inbox]))

# Stop square at the T1 stop (z~2765): PocketPark01 on the west side of the avenue, BicycleParkingArea01 east.
if MODE == "square":
    with paused(WHO):
        for pf, near in (("PocketPark01", [-1925.5, 2765]), ("BicycleParkingArea01", [-1893, 2765])):
            r = post("/place", {"prefab": pf, "near": near, "within": [-2125, 2722, -1695, 2816], "radius": 40,
                                "maxHouseholds": 0, "maxCompanies": 0}, timeout=300)
            c = r.get("chosen") or {}
            print(pf, r.get("placedId"), c.get("x"), c.get("z"), r.get("error"))
            log("square_" + pf, {"placedId": r.get("placedId"), "pos": [c.get("x"), c.get("z")]})
