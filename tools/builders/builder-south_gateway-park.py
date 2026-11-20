"""builder-south / b2-economy-econ-gateway-business-park: Gateway Business Park (Office High) at the South gateway.

Free owned land east of the stone quarry area (Ore Lot polygon x -1990..-1841, z -1840..-1500) and west of the
SE utilities (x -1560). Quarry Lane (Medium, (-1689,-1330) -> (-1745,-1830)) is the park's spine; widening the
OSW streets towards Freight Avenue would demolish 8-13 L4/L5 factories, so trucks and buses reach the park over
the existing z=-1000 / x=-1689 streets until the OSW 'Campus Gate' renewal rebuilds that band.
- Gateway Crescent (Medium, Trees): arc east of Quarry Lane from its node (-1700,-1470) to (-1745,-1830).
- Medium roundabout on the gateway square at (-1700,-1470); BusStation02 and a short Commercial High strip there.
- Central green inside the crescent (CityPark01 + Playground01), Office High depth 5 on the crescent and Quarry Lane.
Stages: build | zone
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, short, find_spot, building, zone

WHO = "builder-south"
CRESCENT = [[-1700, -1470], [-1640, -1505], [-1597, -1585], [-1592, -1680], [-1625, -1770], [-1690, -1818], [-1745, -1830]]
QUARRY = [[-1689, -1330], [-1700, -1470], [-1735, -1600], [-1760, -1720], [-1745, -1830]]
BOX = (-1840, -1880, -1565, -1340)


def place_near(label, prefab, x, z, radius=150, box=BOX):
    for c in find_spot(prefab, x, z, radius=radius, n=10):
        if not all(box[0] <= p[0] <= box[2] and box[1] <= p[1] <= box[3] for p in c["corners"]):
            continue
        b = dict(c["building"]); b.pop("prefab", None)
        d = post("/building", {"prefab": prefab, **b, "dryRun": True})
        if d.get("errors") == 0 and not d.get("wouldDemolish"):
            r = building(prefab, **b)
            print(f"{label:<18} {short(r)} at ({c['x']},{c['z']})", flush=True)
            return r
    print(f"{label:<18} no spot", flush=True)


def dist_to_poly(p, poly):
    best = 1e9
    for a, b in zip(poly, poly[1:]):
        vx, vz = b[0] - a[0], b[1] - a[1]
        t = max(0.0, min(1.0, ((p[0] - a[0]) * vx + (p[1] - a[1]) * vz) / (vx * vx + vz * vz or 1)))
        best = min(best, math.hypot(p[0] - a[0] - t * vx, p[1] - a[1] - t * vz))
    return best


def edges_on(poly, prefab, tol=20):
    d = get("/nets", x=-1690, z=-1620, radius=400, filter=prefab)
    return [s["id"] for s in d.get("segments") or [] if s.get("prefab") == prefab and
            dist_to_poly(((s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2), poly) <= tol]


stage = sys.argv[1] if len(sys.argv) > 1 else ""
if stage == "build":
    with paused(WHO):
        r = road("Medium Road", CRESCENT, curve="smooth")
        print("Gateway Crescent", short(r))
        d = post("/roundabout", {"at": [-1700, -1470], "size": "medium", "dryRun": True})
        print("roundabout", short(post("/roundabout", {"at": [-1700, -1470], "size": "medium"})) if d.get("errors") == 0 and not d.get("wouldDemolish") else (d.get("errors"), d.get("error"), d.get("wouldDemolish")))
        for poly in (CRESCENT, QUARRY):
            print("trees", post("/roadupgrade", {"polyline": poly, "tolerance": 14, "prefabFilter": "Medium Road", "upgrade": "Trees", "side": "both"}).get("applied"))
        place_near("bus station", "BusStation02", -1660, -1500, radius=120)
        place_near("central green", "CityPark01", -1660, -1650, radius=120)
        place_near("playground", "Playground01", -1680, -1700, radius=120)

if stage == "zone":
    with paused(WHO):
        sq = [s for s in edges_on([[-1700, -1470], [-1640, -1505]], "Medium Road")]
        z = zone("NA Commercial High", mode="road", edges=sq, depth=3, side="both")
        print("square commercial", sq, short(z, ("applied", "errors", "quads")))
        e = edges_on(CRESCENT, "Medium Road") + edges_on(QUARRY[1:], "Medium Road")
        z = zone("Office High", mode="road", edges=sorted(set(e)), depth=5, side="both")
        print("office high", len(set(e)), short(z, ("applied", "errors", "quads")))
