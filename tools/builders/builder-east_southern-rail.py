"""builder-east / b2-growth-south-rail-east-gate: Southern Rail, phase 1 (freight).

Route (west -> east), Double Train Track:
 A  branch off the SW mainline (edge 227838:171, (-2607,-2076)->(-2387,-1877)) at (-2560,-2034), S-curve
    (R >= 250 m) to the city's southern edge strip z -2160 at x -2150;
 C  the strip x -1860..-1262 is taken down to z -2175 by three ore/stone extractor lots, so the track dives into a
    tunnel (-15 m) from x -1930 to -1230 and comes back up at x -1050;
 D  ramp up to a TrussArchBridge01 (deck 520 m abs., ~13 m above the water, one 600 m span x -860..-260) and down
    again east of the river at x -110 (parallel to and ~100 m south of the leg's road bridge);
 E  east through tile (0,-1870) (owned) and the bought tiles (623,-1870), (1247,-1870) at z ~ -1650, then
    north-east into the owned triangle south of the southern leg, where CargoTrainTerminal02 stands.
Stages: geo | dry | tiles | build-west | build-east | terminal | route
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import call, get, post, paused  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-east"
LOG = r"C:\soft\cityskyline\logs\council\builder-east_southern-rail.json"
TRACK = "Double Train Track"
BRIDGE = "TrussArchBridge01"
DECK = 520.0
Z = -2160.0

BRANCH = {"prefab": TRACK, "start": [-2560, -2034], "end": [-2150, Z], "startDir": [0.743, 0.669], "endDir": [1, 0],
          "fit": {"tolerance": 40, "minRadius": 250}}
TUNNEL = [[-2150, Z, 0], [-1930, Z, -15], [-1230, Z, -15], [-1050, Z, 0]]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def deck_pts(xs):
    t = call("POST", "/terrain", {"points": [[float(x), Z] for x in xs]})["points"]
    return [[x, Z, round(DECK - q["height"], 1)] for x, q in zip(xs, t)]


def river():
    b = deck_pts([-860, -260])
    west = [[-1050, Z, 0], b[0]]
    east = [b[1], [-110, Z, 0]]
    return [("west ramp", {"prefab": TRACK, "points": west}), ("bridge", {"prefab": BRIDGE, "points": b}),
            ("east ramp", {"prefab": TRACK, "points": east})]


def show(label, r):
    keys = ("applied", "cost", "errors", "problems", "error", "wouldDemolish")
    out = {k: r.get(k) for k in keys if r.get(k) not in (None, [])}
    if r.get("conflicts"):
        out["conflicts"] = [(c.get("error"), c.get("prefab"), c.get("at")) for c in r["conflicts"][:4]]
    g = r.get("geometry") or {}
    if g.get("warnings"):
        out["warn"] = g["warnings"]
    print(f"{label:<12} {json.dumps(out)[:700]}", flush=True)


stage = sys.argv[1] if len(sys.argv) > 1 else "dry"
if stage == "dry":
    r = post("/roadfit", {**BRANCH, "dryRun": True})
    ch = r.get("chosen") or {}
    print("branch", ch.get("variant"), (ch.get("geometry") or {}).get("minRadius"), ch.get("errors"), r.get("error"))
    show("tunnel", post("/road", {"prefab": TRACK, "points": TUNNEL, "dryRun": True}))
    for nm, body in river():
        show(nm, post("/road", {**body, "dryRun": True}))

# ---------------------------------------------------------------- terminal access + CargoTrainTerminal02
WAY = {"prefab": "Medium Road", "points": [[1080, -1650], [1240, -1650], [1460, -1650]], "curve": "smooth"}
LINK = {"prefab": "Medium Road", "start": [1239, -1506], "end": [1240, -1650], "fit": {"tolerance": 20, "minRadius": 60}}
EAST = {"prefab": "Medium Road", "start": [1460, -1650], "end": [1450, -1430], "startDir": [1, 0],
        "fit": {"tolerance": 30, "minRadius": 60}}
TERMINAL_BOX = [321, -2171, 1548, -1568]

if stage == "access":
    with paused(WHO):
        show("way", post("/road", WAY))
        for nm, body in (("link", LINK), ("east", EAST)):
            r = post("/roadfit", body)
            show(nm, r.get("result") or r)
        post("/roundabout", {"at": [1080, -1650], "minDegree": 1, "radius": 20, "prefab": "CulDeSacLarge01"})
        for up in ("Trees", "Grass"):
            u = post("/roadupgrade", {"box": [1070, -1660, 1480, -1420], "prefabFilter": "Medium Road", "upgrade": up,
                                      "side": "both"})
            show(up, u)
elif stage == "terminal":
    body = {"prefab": "CargoTrainTerminal02", "near": [1250, -1740], "within": TERMINAL_BOX, "radius": 300,
            "maxHouseholds": 0, "maxCompanies": 0}
    dry = post("/place", {**body, "dryRun": True})
    ch = dry.get("chosen") or {}
    print("dry", json.dumps({k: ch.get(k) for k in ("x", "z", "angle", "errors", "roadConnected", "corners")})[:600], dry.get("error"))
    if len(sys.argv) > 2 and sys.argv[2] == "go" and ch:
        with paused(WHO):
            r = post("/place", body)
        print("placed", r.get("placedId"))
        log("terminal", {"id": r.get("placedId"), "chosen": ch})

# ---------------------------------------------------------------- east: river -> terminal (2549899:55)
# terminal tracks: north platform track ends (1115.6,-1730.5)/(1395.6,-1730.5), south (1115.6,-1776.5)/(1395.6,-1776.5)
EAST_MAIN = [[-110, Z], [200, -2120], [550, -1930], [850, -1768], [940, -1753], [985, -1753]]
SPLIT_N = {"prefab": TRACK, "start": [985, -1753], "end": [1115.6, -1730.5], "startDir": [1, 0], "endDir": [1, 0],
           "fit": {"tolerance": 5, "minRadius": 150}}
SPLIT_S = {"prefab": TRACK, "start": [985, -1753], "end": [1115.6, -1776.5], "startDir": [1, 0], "endDir": [1, 0],
           "fit": {"tolerance": 5, "minRadius": 150}}


def east_parts():
    return [("east main", "/road", {"prefab": TRACK, "points": EAST_MAIN, "curve": "smooth"}),
            ("split N", "/roadfit", SPLIT_N), ("split S", "/roadfit", SPLIT_S)]


if stage == "dry-east":
    for nm, path, body in east_parts():
        r = post(path, {**body, "dryRun": True})
        if path == "/roadfit":
            print(nm, (r.get("chosen") or {}).get("variant"), r.get("anchors"), r.get("error"))
            r = r.get("result") or r
        show(nm, r)
        print("   ", (r.get("geometry") or {}).get("minRadius"), json.dumps(r.get("snapping"))[:300])
elif stage == "build":
    with paused(WHO):
        r = post("/roadfit", BRANCH)
        res = r.get("result") or {}
        show("branch", res)
        log("branch", {k: res.get(k) for k in ("applied", "cost", "newEdgeIds")})
        if not res.get("applied"):
            sys.exit(1)
        r = post("/road", {"prefab": TRACK, "points": TUNNEL})
        show("tunnel", r)
        for nm, body in river():
            show(nm, post("/road", body))
    with paused(WHO):
        for nm, path, body in east_parts():
            r = post(path, body)
            if path == "/roadfit":
                print(nm, (r.get("chosen") or {}).get("variant"), r.get("anchors"))
                r = r.get("result") or r
            show(nm, r)
            print("   ", json.dumps(r.get("snapping"))[:300])

# ---------------------------------------------------------------- fix: the first branch left the mainline at 83 deg
# (roadfit's moveT variant ignored startDir): replace it by an S-curve tangent to the mainline (R ~220/250) that
# descends straight into the tunnel node 1595154:39 (-1930,-2160,-15).
OLD_BRANCH = ["693817:303", "960715:47", "1138811:45", "1618313:61", "1622391:251", "1626032:31", "1630880:37",
              "1631094:81"]
S_CURVE = [
    {"start": [-2542, -2018, 0], "control": [-2412, -1901, 0], "end": [-2269, -2001, 0]},
    {"control": [-2188, -2058, -4.2], "end": [-2107, -2115, -8.4]},
    {"control": [-2042, -2160, -12], "end": [-1963, -2160, -15]},
    {"control": [-1946.5, -2160, -15], "end": [-1930, -2160, -15]},
]

if stage == "fix-branch":
    dry = post("/bulldoze", {"edges": OLD_BRANCH, "dryRun": True})
    print("bulldoze dry", json.dumps({k: dry.get(k) for k in ("edgeCount", "errors", "impact")})[:400])
    if len(sys.argv) > 2 and sys.argv[2] == "go":
        with paused(WHO):
            b = post("/bulldoze", {"edges": OLD_BRANCH})
            print("bulldoze", json.dumps({k: b.get(k) for k in ("applied", "edgeCount", "errors")}))
            d = post("/road", {"prefab": TRACK, "segments": S_CURVE, "dryRun": True})
            show("s dry", d)
            print("   ", (d.get("geometry") or {}).get("minRadius"), json.dumps(d.get("snapping"))[:300])
            if d.get("errors") == 0:
                r = post("/road", {"prefab": TRACK, "segments": S_CURVE})
                show("s-curve", r)
                log("s_curve", {k: r.get(k) for k in ("applied", "cost", "newEdgeIds")})

# ---------------------------------------------------------------- cargo routes for CargoTrainTerminal02
ROUTES = [("2572963:5", "435072:1", "#795548"), ("2572964:5", "435073:1", "#5D4037")]
if stage == "route":
    out = []
    with paused(WHO):
        for stop, oc, color in ROUTES:
            r = post("/line", {"type": "train", "cargo": True, "stops": [stop, oc], "color": color}, timeout=300)
            line = r.get("line") or {}
            print(stop, oc, json.dumps({k: r.get(k) for k in ("applied", "error", "lineId")}), line.get("lineId") or line.get("id"),
                  json.dumps(r.get("pathCheck"))[:200])
            out.append({"stop": stop, "oc": oc, "line": line.get("lineId") or line.get("id") or r.get("lineId")})
    log("routes", out)
