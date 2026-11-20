"""builder-east / southern leg of the Southern Parkway (conductor-authorised 2032-01-07).

A tree-lined Large Road from the parkway's Commons junction (1885,-1115) south-west across East Gate's green belt,
west along East Bank's reserved south corridor (z ~ -1535, builder-south kept z < -1450 free), south-west through
tile (0,-1870), over the river on an 'Extradosed Bridge - Large Road Divided - 6 Lanes' at z ~ -2030..-2075 (deck
518 m = 11 m above the water, the bed is 63 m deep), then west through the South gateway between the prison and the
ore lots, north-west round the incinerator's power line to the freight road node (-1592,-1680) (Medium Road to OSW
and the cargo terminal access).

Elevations are per point = deck(absolute) - terrain; the deck profile ramps linearly from the ground points to 518.
Stages: tile | geo | dry | build | finish (roundabouts, trees, sound barrier)
"""
import json
import math
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import call, get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-east"
LOG = r"C:\soft\cityskyline\logs\council\builder-east_southern-leg.json"
TILE = (0, -1870)
DECK = 518.0
BRIDGE = "Extradosed Bridge - Large Road Divided - 6 Lanes"

N_C = [1885, -1114.5]                     # parkway node at the Commons junction
EAST = [N_C, [1700, -1260], [1450, -1430], [1200, -1515], [935, -1534]]
CORRIDOR = [[935, -1534], [600, -1536], [300, -1535], [100, -1540], [-30, -1610], [-120, -1720], [-160, -1800]]
# chain over the river: ground -> ramp -> span -> ramp -> ground; (x, z, role)
CHAIN = [(-160, -1800, "g"), (-205, -1890, "r"), (-250, -1975, "r"), (-295, -2030, "d"), (-385, -2052, "d"),
         (-475, -2062, "d"), (-565, -2068, "d"), (-655, -2072, "d"), (-745, -2076, "d"), (-835, -2080, "r"),
         (-925, -2082, "g")]
WEST = [[-925, -2082], [-1100, -2085], [-1200, -2075], [-1270, -2035], [-1300, -1960], [-1312, -1870],
        [-1345, -1805], [-1405, -1768], [-1470, -1750], [-1530, -1725], [-1592, -1680]]
# the last 70 m to the freight road node removes one small office (OfficeHigh01_L1_3x5, 1 renter; office demand is 0
# and Office High is 65% vacant) - accepted, logged to demolitions.jsonl
ALLOWED_DEMOLISH = {"1803409:49"}
FREIGHT_NODE = [-1592, -1680]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def chain_points():
    """[x, z, elevation] for the river chain: absolute deck profile linear in chain distance between the last ground
    point and the first deck point (and back down on the west side)."""
    pts = [(x, z) for x, z, _ in CHAIN]
    terr = call("POST", "/terrain", {"points": [[float(x), float(z)] for x, z in pts]})["points"]
    dist = [0.0]
    for i in range(1, len(pts)):
        dist.append(dist[-1] + math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]))
    roles = [r for _, _, r in CHAIN]
    first_d, last_d = roles.index("d"), len(roles) - 1 - roles[::-1].index("d")
    g0, g1 = 0, len(roles) - 1
    out = []
    for i, (x, z) in enumerate(pts):
        h = terr[i]["height"]
        if roles[i] == "g":
            absz = h
        elif first_d <= i <= last_d:
            absz = DECK
        elif i < first_d:
            f = (dist[i] - dist[g0]) / (dist[first_d] - dist[g0])
            absz = terr[g0]["height"] + f * (DECK - terr[g0]["height"])
        else:
            f = (dist[g1] - dist[i]) / (dist[g1] - dist[last_d])
            absz = terr[g1]["height"] + f * (DECK - terr[g1]["height"])
        out.append([x, z, round(max(0.0, absz - h), 1), roles[i], round(h, 1), round(terr[i]["waterDepth"], 1)])
    return out


def parts():
    cp = chain_points()
    first_d = [p[3] for p in cp].index("d")
    last_d = len(cp) - 1 - [p[3] for p in cp][::-1].index("d")
    east_ramp = [p[:3] for p in cp[:first_d + 1]]
    span = [p[:3] for p in cp[first_d:last_d + 1]]
    west_ramp = [p[:3] for p in cp[last_d:]]
    return [
        ("leg east", {"prefab": "Large Road", "points": EAST, "curve": "smooth"}),
        ("leg corridor", {"prefab": "Large Road", "points": CORRIDOR, "curve": "smooth"}),
        ("east ramp", {"prefab": "Large Road", "points": east_ramp, "curve": "smooth"}),
        ("river bridge", {"prefab": BRIDGE, "points": span, "curve": "smooth"}),
        ("west ramp", {"prefab": "Large Road", "points": west_ramp, "curve": "smooth"}),
        ("leg west", {"prefab": "Large Road", "points": WEST, "curve": "smooth"}),
    ], cp


def show(label, r):
    out = short(r, keys=("applied", "cost", "errors", "problems", "error", "wouldDemolish", "conflicts"))
    if out.get("conflicts"):
        out["conflicts"] = [{k: c.get(k) for k in ("error", "at", "prefab", "id")} for c in out["conflicts"][:4]]
    g = r.get("geometry") or {}
    if g:
        out["geo"] = {k: g.get(k) for k in ("length", "minRadius", "maxSlope", "warnings") if g.get(k) is not None}
    snaps = [x.get("snappedTo") for x in r.get("snapping") or []]
    if any(snaps):
        out["snap"] = snaps
    print(f"{label:<14} {json.dumps(out)[:900]}", flush=True)
    return r


if __name__ == "__main__":
    MODE = sys.argv[1] if len(sys.argv) > 1 else "geo"
    if MODE == "tile":
        tl = get("/tiles")
        c = [k for k in tl["candidates"] if tuple(k["centre"]) == TILE]
        print(c)
        if c:
            with paused(WHO):
                r = post("/buytile", {"x": TILE[0], "z": TILE[1]})
            print(short(r))
            log("tile", {"price": c[0]["price"], "upkeep": c[0]["upkeepIncrease"], "result": short(r)})
    elif MODE == "geo":
        P, cp = parts()
        for p in cp:
            print("chain", p)
    elif MODE in ("dry", "build"):
        P, cp = parts()
        with paused(WHO):
            for label, spec in P:
                d = show(label + " dry", post("/road", {**spec, "dryRun": True}))
                if MODE == "build":
                    wd = {w.get("id") for w in d.get("wouldDemolish") or []}
                    if d.get("errors") != 0 or not wd <= ALLOWED_DEMOLISH:
                        sys.exit(f"stopped at {label}")
                    if wd:
                        spec = {**spec, "allowDemolish": True}
                        with open(r"C:\soft\cityskyline\logs\council\demolitions.jsonl", "a", encoding="utf-8") as f:
                            f.write(json.dumps({"builder": WHO, "item": "b2-mobility-southern-parkway (southern leg)",
                                                "why": "Large Road link to the South gateway freight road node (-1592,-1680)",
                                                "removed": d.get("wouldDemolish"), "time": time.strftime("%Y-%m-%d %H:%M")}) + "\n")
                    r = show(label, post("/road", spec))
                    log(label, {"cost": r.get("cost"), "edges": r.get("newEdgeIds"), "geometry": r.get("geometry")})
                    time.sleep(1.0)
    elif MODE == "finish":
        with paused(WHO):
            for at, size in ((N_C, "large"), (FREIGHT_NODE, "medium")):
                d = show(f"rb {at} dry", post("/roundabout", {"at": at, "size": size, "dryRun": True}))
                if d.get("errors") == 0 and not d.get("wouldDemolish"):
                    log(f"roundabout {at}", short(show(f"rb {at}", post("/roundabout", {"at": at, "size": size}))))
            for box in ([-1600, -2100, 1890, -1110],):
                for up in ("Trees", "Grass"):
                    u = post("/roadupgrade", {"box": box, "prefabFilter": "Large Road", "upgrade": up, "side": "both"})
                    print("Large Road", up, u.get("applied"), u.get("cost"), u.get("errors"))
