"""builder-east / b2-mobility-southern-parkway: the Southern Parkway, phase 1.

East Gate's spine and the city's second highway gate: a curved, tree-lined Large Road from the end of the East
Bank town spine (905,-876) (builder-south; it continues west over the Lake Bridge to the x=-1140 arterial) east
across the empty plain to a diamond interchange on the SE highway pair near z=-1323.

- Tiles (8): (1247,-623), (1247,-1247), then east along the z=-1247 row to (4986,-1247).
- Diamond: a Medium Road overpass at 9 m perpendicular to the highway from the west junction J_W (s=-210 m)
  to the east junction J_E (s=+215 m); four 'Highway Oneway - 1 lane' ramps that join the carriageways at the
  existing highway nodes 274 m north and south of the crossing; roundabouts on J_W (4 legs) and J_E (3 legs).
- Parkway: Large Road with Trees+Grass, a long S-curve (R >= ~1 km) that dips south around the future airport
  apron and rises to meet the East Bank spine. Nodes at the future junctions: Commons Cross ~(1900,-1120) and
  Airport Gate ~(3900,-1360). No zoning on the parkway.
Coordinates: s = metres east of the southbound carriageway (perpendicular), along = metres north along it.

Stages: tiles | dry | interchange | parkway | trees | roundabouts | district | verify
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import call, get, post, paused, road, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-east"
LOG = r"C:\soft\cityskyline\logs\council\builder-east_southern-parkway.json"

TILES = [(1247, -623), (1247, -1247), (1870, -1247), (2493, -1247), (3117, -1247), (3740, -1247), (4363, -1247),
         (4986, -1247)]

# southbound carriageway edge 395412:1 runs (5012.3,-1231.9) -> (5001.6,-1414.3); crossing at its middle
C0 = (5006.95, -1323.1)
U = (10.7 / 182.71, 182.4 / 182.71)   # north along the highway
N = (U[1], -U[0])                     # perpendicular, east


def P(along, s, e=None):
    p = [round(C0[0] + along * U[0] + s * N[0], 1), round(C0[1] + along * U[1] + s * N[1], 1)]
    return p + [e] if e is not None else p


L2S = 20.0          # northbound carriageway lies 20 m east of the southbound one
AL = 274.0          # existing highway nodes 274 m north of the crossing (287304 southbound, 287302 northbound)
AS = 200.0          # southern joins: mid-edge, inside the owned tile (edge z=-1558)
J_W, J_E = P(0, -210), P(0, 215)
RAMP = "Highway Oneway - 1 lane"
OVER = "Medium Road"
DOWN = [-U[0], -U[1]]

def mix(au, an):
    """Unit direction: au parts along the highway (north +) and an parts across it (east +)."""
    n = math.hypot(au, an)
    return [round((au * U[0] + an * N[0]) / n, 4), round((au * U[1] + an * N[1]) / n, 4)]


# each ramp is a biarc between the carriageway (tangent) and its junction, meeting the junction at 30 degrees off
# the highway axis (R ~80 m; the junctions become roundabouts). Southern joins stay north of the tile edge z=-1558
# (AS = 200 m south of the crossing); northern joins use the existing nodes 274 m north (AL).
INTERCHANGE = [
    ("overpass", {"prefab": OVER, "points": [J_W + [0], P(0, -50, 9), P(0, 70, 9), J_E + [0]]}),
    ("ramp S-off", {"prefab": RAMP, "points": [P(AL, 0), J_W], "curve": "smooth",
                    "startDir": DOWN, "endDir": mix(-0.866, -0.5)}),
    ("ramp S-on", {"prefab": RAMP, "points": [J_W, P(-AS, 0)], "curve": "smooth",
                   "startDir": mix(-0.866, 0.5), "endDir": DOWN}),
    ("ramp N-off", {"prefab": RAMP, "points": [P(-AS, L2S), J_E], "curve": "smooth",
                    "startDir": list(U), "endDir": mix(0.866, 0.5)}),
    ("ramp N-on", {"prefab": RAMP, "points": [J_E, P(AL, L2S)], "curve": "smooth",
                   "startDir": mix(0.866, -0.5), "endDir": list(U)}),
]

SPINE_END = [905, -876]
X_COMMONS = [1900, -1120]
X_AIRPORT = [3900, -1360]
PARKWAY = [
    ("parkway west", {"prefab": "Large Road", "curve": "smooth",
                      "points": [SPINE_END, [1150, -905], [1500, -990], X_COMMONS]}),
    ("parkway middle", {"prefab": "Large Road", "curve": "smooth",
                        "points": [X_COMMONS, [2300, -1235], [2800, -1320], [3350, -1365], X_AIRPORT]}),
    ("parkway east", {"prefab": "Large Road", "curve": "smooth",
                      "points": [X_AIRPORT, [4300, -1345], [4600, -1322], J_W]}),
]
TREE_BOX = [915, -1460, 4800, -860]


def show(label, r):
    out = short(r, keys=("applied", "cost", "errors", "problems", "error", "warning", "wouldDemolish", "conflicts"))
    g = r.get("geometry") or {}
    if g:
        out["geo"] = {k: g.get(k) for k in ("length", "minRadius", "maxSlope", "warnings") if g.get(k) is not None}
    snaps = [s.get("snappedTo") for s in r.get("snapping") or []]
    if any(snaps):
        out["snap"] = snaps
    if r.get("newEdgeIds"):
        out["newEdges"] = len(r["newEdgeIds"])
    print(f"{label:<16} {json.dumps(out)[:900]}", flush=True)
    return r


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def tiles():
    owned = {tuple(c) for c in get("/tiles")["ownedCentres"]}
    spent = 0
    for t in TILES:
        if t in owned:
            print("owned", t)
            continue
        cand = [c for c in get("/tiles")["candidates"] if tuple(c["centre"]) == t]
        if not cand:
            print("not buyable yet", t)
            break
        c = cand[0]
        with paused(WHO):
            r = post("/buytile", {"x": t[0], "z": t[1]})
        print("buy", t, c["price"], c["upkeepIncrease"], short(r))
        log(f"tile {t}", {"price": c["price"], "upkeep": c["upkeepIncrease"], "result": short(r)})
        if r.get("error") or r.get("errors"):
            break
        spent += c["price"]
    print("tiles spent", spent)


def build_parts(parts, dry):
    for label, spec in parts:
        r = show(label + (" (dry)" if dry else ""), post("/road", {**spec, "dryRun": True}))
        if dry:
            continue
        if r.get("errors") != 0 or r.get("wouldDemolish"):
            sys.exit(f"stopped at {label}")
        r = show(label, post("/road", spec))
        log(label, {"cost": r.get("cost"), "edges": r.get("newEdgeIds"), "geometry": r.get("geometry")})


def roundabouts():
    for at, size in ((J_W, "medium"), (J_E, "small")):
        d = show(f"roundabout {size} (dry)", post("/roundabout", {"at": at, "size": size, "dryRun": True}))
        if d.get("errors") == 0 and not d.get("wouldDemolish"):
            log(f"roundabout {at}", short(show(f"roundabout {size}", post("/roundabout", {"at": at, "size": size}))))


def trees():
    for up in ("Trees", "Grass"):
        u = post("/roadupgrade", {"box": TREE_BOX, "prefabFilter": "Large Road", "upgrade": up, "side": "both"})
        print("Large Road", up, u.get("applied"), u.get("cost"), u.get("errors"))
        log(f"upgrade {up}", short(u))


if __name__ == "__main__":
    MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
    if MODE == "geo":
        print("J_W", J_W, "J_E", J_E)
        for label, spec in INTERCHANGE + PARKWAY:
            print(label, spec["points"])
    elif MODE == "tiles":
        tiles()
    elif MODE == "dry":
        with paused(WHO):
            build_parts(INTERCHANGE + PARKWAY, dry=True)
            for at, size in ((J_W, "medium"), (J_E, "small")):
                show(f"roundabout {size}", post("/roundabout", {"at": at, "size": size, "dryRun": True}))
    elif MODE == "interchange":
        with paused(WHO):
            build_parts(INTERCHANGE, dry=False)
    elif MODE == "parkway":
        with paused(WHO):
            build_parts(PARKWAY, dry=False)
    elif MODE == "roundabouts":
        with paused(WHO):
            roundabouts()
    elif MODE == "trees":
        with paused(WHO):
            trees()
    elif MODE == "build":
        with paused(WHO):
            build_parts(INTERCHANGE + PARKWAY, dry=False)
            roundabouts()
            trees()
    elif MODE == "verify":
        for x, z, rad in ((J_W[0], J_W[1], 120), (J_E[0], J_E[1], 120)):
            for s in get("/nets", x=x, z=z, radius=rad)["segments"]:
                print(s["id"], s["prefab"], s["start"], s["end"], s.get("startDegree"), s.get("endDegree"))
