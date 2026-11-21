"""builder-west, b2-housing-northlake-new-town-p1 + conductor jobs priority 2033-01: Northlake phase 1 (south half).
Tiles (-3117,2493) and (-3117,3117) bought 2033-01 (408k + 541k). Flat land, no water; Northwest's x=-2789 street on
the east (T-junctions every 110 m), Westside's corner node (-3009,2050) on the south.

  roads    - Northlake Avenue: Medium Road with Trees + Grass, a gentle S (minR ~400) from Westside (-3009,2050)
             north to (-3072,2790) (phase 2 continues north);
             Lake Street: Medium Road with Trees from the square (-3075,2450) east to Northwest junction (-2789,2490);
             Park Lane: Small Road with Trees from the square west to the future park;
             Northlake Square: medium roundabout at (-3075,2450) (the future tram stop).
  crescent - Northlake Crescent: Small Road with Trees, a horseshoe west of the avenue (z 2310..2600, apex x -3335),
             park in the middle at the end of Park Lane (placed before zoning).
  zone     - Office High: Lake Street both sides, the avenue z 2330..2620 both sides (the core);
             NA Residential Mixed: the avenue elsewhere; NA Commercial Low: Park Lane;
             crescent: NA Residential Medium inner, NA Residential Medium Row outer. overwrite=False.
Usage: python -I builders/builder-west_northlake.py roads|crescent|zone [dry]
"""
import json
import math
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[2:]
AVENUE = [[-3009, 2050], [-3018, 2190], [-3048, 2330], [-3075, 2450], [-3086, 2600], [-3072, 2790]]
LAKE_ST = [[-3075, 2450], [-2930, 2462], [-2789, 2490]]
PARK_LANE = [[-3075, 2450], [-3140, 2450], [-3195, 2452]]
CRESCENT = [[-3046, 2315], [-3150, 2300], [-3270, 2335], [-3335, 2450], [-3285, 2565], [-3180, 2602], [-3085, 2600]]
SQUARE = (-3075, 2450)
BOX = [-3429, 2040, -2795, 2800]


def segs(prefab=None):
    return get("/nets", x=-3110, z=2420, radius=420, **({"filter": prefab} if prefab else {})).get("segments") or []


def mid(s):
    return (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2


def dist_to_poly(p, poly):
    best = 1e9
    for (ax, az), (bx, bz) in zip(poly, poly[1:]):
        dx, dz = bx - ax, bz - az
        t = max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - az) * dz) / (dx * dx + dz * dz)))
        best = min(best, math.hypot(p[0] - ax - t * dx, p[1] - az - t * dz))
    return best


def on(poly, tol=25, prefab=None):
    out = []
    for s in segs(prefab):
        m = mid(s)
        if BOX[0] < m[0] < BOX[2] and BOX[1] < m[1] < BOX[3] and dist_to_poly(m, poly) < tol:
            out.append(s)
    return out


def fitroad(name, prefab, pts, min_r):
    body = {"prefab": prefab, "points": pts, "fit": {"tolerance": 15, "minRadius": min_r}}
    d = post("/roadfit", {**body, "dryRun": True})
    c = d.get("chosen") or {}
    res = c.get("result") or {}
    print(f"{name} dry: {c.get('variant')} minR {(c.get('geometry') or {}).get('minRadius')} demolish {res.get('demolition')} wd {len(res.get('wouldDemolish') or [])}", flush=True)
    if DRY or not c or res.get("wouldDemolish"):
        return False
    r = post("/roadfit", body)
    print(f"{name}: {r.get('applied')} {(r.get('chosen') or {}).get('variant')}", flush=True)
    return bool(r.get("applied"))


def trees(name, poly, prefab, grass=False):
    ids = [s["id"] for s in on(poly, prefab=prefab)]
    for up in (["Trees", "Grass"] if grass else ["Trees"]):
        t = post("/roadupgrade", {"edges": ids, "upgrade": up, "side": "both"})
        print(f"  {name} {up}: {len(ids)} edges {short(t, ('applied', 'cost', 'errors'))}", flush=True)
        ids = t.get("edgeIdsAfter") or [s["id"] for s in on(poly, prefab=prefab)]


def roads():
    if DRY:
        fitroad("Avenue", "Medium Road", AVENUE, 250)
        fitroad("Lake Street", "Medium Road", LAKE_ST, 150)
        return
    with paused(WHO):
        if not fitroad("Avenue", "Medium Road", AVENUE, 250):
            return
        time.sleep(1)
        fitroad("Lake Street", "Medium Road", LAKE_ST, 150)
        time.sleep(1)
        n = get("/nodes", x=SQUARE[0], z=SQUARE[1], radius=40, minDegree=3).get("nodes") or []
        sq = n[0]["pos"] if n else list(SQUARE)
        print("square node:", n[0]["id"] if n else None, sq, flush=True)
        fitroad("Park Lane", "Small Road", [sq] + PARK_LANE[1:], 60)
        time.sleep(1)
        r = post("/roundabout", {"at": sq, "size": "medium", "allowDemolish": True})
        print("roundabout:", short(r, ("applied", "cost", "errors")), flush=True)
        time.sleep(1)
        trees("Avenue", AVENUE, "Medium Road", grass=True)
        trees("Lake Street", LAKE_ST, "Medium Road")
        trees("Park Lane", PARK_LANE, "Small Road")


def crescent():
    if DRY:
        fitroad("Crescent", "Small Road", CRESCENT, 60)
        return
    with paused(WHO):
        if not fitroad("Crescent", "Small Road", CRESCENT, 60):
            return
        time.sleep(1)
        trees("Crescent", CRESCENT, "Small Road")
        p = post("/place", {"prefab": "CityPark03", "near": [-3240, 2450], "within": [-3300, 2370, -3185, 2530],
                            "radius": 80, "maxHouseholds": 0, "maxCompanies": 0}, timeout=300)
        print("park:", p.get("applied"), p.get("placedId"), (p.get("chosen") or {}).get("x"), (p.get("chosen") or {}).get("z"), p.get("error") or "", flush=True)
        if not p.get("applied"):
            p = post("/place", {"prefab": "CityPark02", "near": [-3240, 2450], "within": [-3300, 2370, -3185, 2530],
                                "radius": 80, "maxHouseholds": 0, "maxCompanies": 0}, timeout=300)
            print("park2:", p.get("applied"), p.get("placedId"), p.get("error") or "", flush=True)


def zone_all():
    cells = {}

    def z(name, ids, side, depth=6):
        if not ids:
            return
        r = zone(name, mode="road", edges=ids, depth=depth, side=side, overwrite=False)
        cells[name] = cells.get(name, 0) + (r.get("cellsChanged") or 0)

    with paused(WHO):
        av = on(AVENUE, prefab="Medium Road")
        core = [s["id"] for s in av if 2330 <= mid(s)[1] <= 2620]
        rest = [s["id"] for s in av if not 2330 <= mid(s)[1] <= 2620]
        z("Office High", core, "both")
        z("Office High", [s["id"] for s in on(LAKE_ST, prefab="Medium Road") if mid(s)[0] > -3050], "both")
        z("NA Commercial Low", [s["id"] for s in on(PARK_LANE, prefab="Small Road") if mid(s)[0] < -3100], "both", 4)
        for s in on(CRESCENT, tol=30, prefab="Small Road"):
            (sx, sz), (ex, ez) = s["start"], s["end"]
            mx, mz = mid(s)
            inner_right = (ez - sz) * (-3200 - mx) - (ex - sx) * (2450 - mz) > 0
            z("NA Residential Medium", [s["id"]], "right" if inner_right else "left")
            z("NA Residential Medium Row", [s["id"]], "left" if inner_right else "right")
        z("NA Residential Mixed", rest, "both")
        print("zoned:", cells, flush=True)
    print(json.dumps({"cells": cells}))


if __name__ == "__main__":
    stage = sys.argv[1] if len(sys.argv) > 1 else ""
    {"roads": roads, "crescent": crescent, "zone": zone_all}[stage]()
