"""builder-central: Boulevard Tram (worklist b1-mobility-tram-boulevard, also covers b1-growth-avenue-tram).

The city's first tram network:
  - Depot Road: a Medium Road west from the avenue's south end node (-1900,-420) to x=-2165, tree-lined,
    with TramDepot01 on its south side (free land between Office Park and the Health Campus).
  - T1 "Avenue": Double Tram Track on the x=-1909 avenue from the Depot Road (z=-420) to z=2985.
  - T2 "West Arterial": Double Tram Track on the z=900 Medium Road from x=-2463.7 to x=-1193.4
    (stops short of the x=-1138 roundabout).
  - Stops mid-block every ~400 m on both sides (right-hand traffic), away from the bus line 1 stops,
    and the T1/T2 interchange at the (-1909,900) crossing.

Stages (run in order; each is idempotent enough to re-run after a failure):
  python -I builders/builder-central_tram-boulevard.py road     [--dry]
  python -I builders/builder-central_tram-boulevard.py depot    [--dry]
  python -I builders/builder-central_tram-boulevard.py track    [--dry]
  python -I builders/builder-central_tram-boulevard.py stops    [--dry]
  python -I builders/builder-central_tram-boulevard.py lines    [--dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short, find_spot, building, road  # noqa: E402

WHO = "builder-central"
DRY = "--dry" in sys.argv
STAGE = next((a for a in sys.argv[1:] if not a.startswith("--")), "road")

DEPOT_ROAD = [[-1900, -420], [-2165, -420]]

# tram track (polyline selections; the upgrade keeps existing Trees/Grass)
TRACKS = [
    ("Depot Road", [[-2165, -420], [-1900, -420]], "Medium Road"),
    ("avenue south link", [[-1900, -420], [-1909, -340]], "Small Road"),
    ("avenue T1 south", [[-1909, -340], [-1909, 200]], "Medium Road"),
    ("avenue T1 campus", [[-1909, 200], [-1909, 320]], "Small Road"),
    ("avenue T1 north", [[-1909, 320], [-1909, 2985]], "Medium Road"),
    ("z=900 T2", [[-2463.7, 900], [-1193.4, 900]], "Medium Road"),
]

# T1 stops (z on the avenue, mid-block, not in the bus line 1 blocks); northbound = east side
T1_Z = [-300, -20, 375, 830, 1175, 1530, 1885, 2325, 2765]
# T2 stops (x on z=900, mid-block); eastbound = south side
T2_X = [-2405, -1966, -1530, -1225]


def stage_road():
    with paused(WHO):
        r = road("Medium Road", DEPOT_ROAD, dryRun=DRY)
        print("depot road", short(r, keys=("applied", "cost", "errors", "problems", "wouldDemolish", "builtEdges", "newEdgeIds")))
        if not DRY and r.get("applied"):
            u = post("/roadupgrade", {"polyline": DEPOT_ROAD, "tolerance": 6, "prefabFilter": "Medium Road",
                                      "upgrade": "Trees", "side": "both"})
            print("depot road trees", short(u, keys=("applied", "cost", "errors")))


def stage_depot():
    cands = find_spot("TramDepot01", -2040, -500, radius=250, n=10)
    for c in cands:
        print("cand", c["score"], c["road"], c["x"], c["z"], c["angle"], c["edge"], c["corners"][0], c["corners"][2])
    with paused(WHO):
        for c in cands:
            if c["z"] > -420:  # keep the depot south of the Depot Road (north side is the office quarter)
                continue
            b = dict(c["buildingXZ"])
            b.pop("prefab", None)
            d = post("/building", {"prefab": "TramDepot01", **b, "dryRun": True})
            print("dry", c["x"], c["z"], short(d))
            if d.get("errors") or d.get("wouldDemolish"):
                continue
            if DRY:
                return
            r = building("TramDepot01", **b)
            print("TramDepot01", short(r))
            return


def stage_track():
    with paused(WHO):
        for label, line, pf in TRACKS:
            r = post("/roadupgrade", {"polyline": line, "tolerance": 6, "prefabFilter": pf,
                                      "upgrade": "Double Tram Track", "side": "both", "dryRun": DRY})
            edges = [e["id"] for e in r.get("edges", [])]
            print(f"track {label:<18} applied={r.get('applied')} cost={r.get('cost')} err={r.get('errors')} "
                  f"demolish={r.get('wouldDemolish')} edges={len(edges)} {r.get('error') or ''}", flush=True)


def stage_stops():
    """Place one stop first (it unlocks the Tram Line), then report the unlock state."""
    with paused(WHO):
        r = post("/busstop", {"type": "tram", "prefab": "NA_TramStop01", "x": -1899, "z": T1_Z[3], "dryRun": DRY})
        print("first tram stop", json.dumps(r)[:800])
    print(json.dumps(get("/unlockinfo", name="Tram Line"))[:600])


def t1_stops():
    north = [{"x": -1899, "z": z} for z in T1_Z]
    south = [{"x": -1919, "z": z} for z in reversed(T1_Z)]
    return north + south


def t2_stops():
    east = [{"x": x, "z": 891} for x in T2_X]
    west = [{"x": x, "z": 909} for x in reversed(T2_X)]
    return east + west


def stage_lines():
    with paused(WHO):
        for name, stops, color in (("T1", t1_stops(), "#1FA37A"), ("T2", t2_stops(), "#E0A21B")):
            r = post("/busline", {"type": "tram", "stopPrefab": "NA_TramStop01", "stops": stops, "color": color,
                                  "dryRun": DRY}, timeout=300)
            print(name, json.dumps(r)[:2500], flush=True)


# Trams cannot reverse at a dead-end track ("Pathfind Failed" on the end stops, "Track Not Connected" at the
# track ends), so every terminus needs a turning loop beyond its end stops.
#  - T1 south: the "Tram Loop Crescent", a curved Small Road (r ~ 90 m) on the free land north of the Depot
#    Road, from the Depot Road's west end node to a T-junction east of the depot; its inner lawn becomes a green.
#  - T1 north: block loop z=2930 -> x=-1689 -> z=2820 (Northfield streets, track only, no widening).
#  - T2 east: track on the approach edge into the x=-1138 roundabout so trams can turn around it (test).
CRESCENT = [[-2165, -420], [-2139.1, -373.6], [-2070, -345], [-2000.9, -373.6], [-1975, -420]]
LOOP_TRACKS = [
    ("T1 north z=2930", [[-1909, 2930], [-1689, 2930]], "Small Road"),
    ("T1 north x=-1689", [[-1689, 2930], [-1689, 2820]], "Small Road"),
    ("T1 north z=2820", [[-1689, 2820], [-1909, 2820]], "Small Road"),
    ("T2 east roundabout", [[-1193.4, 900], [-1137.8, 900]], "Medium Road"),
]
STUBS = [("T1 north stub", [[-1909, 2930], [-1909, 2985]], "Medium Road")]


def stage_loops():
    with paused(WHO):
        r = post("/road", {"prefab": "Small Road", "points": CRESCENT, "curve": "smooth", "dryRun": DRY})
        print("crescent", short(r, keys=("applied", "cost", "errors", "problems", "wouldDemolish", "newEdgeIds")), flush=True)
        if r.get("errors") or r.get("wouldDemolish"):
            return
        for up in ("Double Tram Track", "Trees"):
            u = post("/roadupgrade", {"polyline": CRESCENT, "tolerance": 8, "prefabFilter": "Small Road", "upgrade": up,
                                      "side": "both", "dryRun": DRY})
            print(f"crescent {up}", short(u, keys=("applied", "cost", "errors")), len(u.get("edges", [])), flush=True)
        for label, line, pf in LOOP_TRACKS:
            u = post("/roadupgrade", {"polyline": line, "tolerance": 6, "prefabFilter": pf, "upgrade": "Double Tram Track",
                                      "side": "both", "dryRun": DRY})
            print(f"track {label:<20}", short(u, keys=("applied", "cost", "errors", "error")), [e["id"] for e in u.get("edges", [])], flush=True)
        for label, line, pf in STUBS:
            u = post("/roadupgrade", {"polyline": line, "tolerance": 6, "prefabFilter": pf, "upgrade": "Double Tram Track",
                                      "side": "both", "remove": True, "dryRun": DRY})
            print(f"remove {label:<20}", short(u, keys=("applied", "cost", "errors", "error")), [e["id"] for e in u.get("edges", [])], flush=True)


if __name__ == "__main__":
    {"road": stage_road, "depot": stage_depot, "track": stage_track, "stops": stage_stops, "lines": stage_lines,
     "loops": stage_loops}[STAGE]()
