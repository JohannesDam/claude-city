"""builder-west, b2-housing-northlake-new-town-p1 phase 1b: Northlake north - curved streets and crescents.

  - Northlake Parkway: Medium Road with Trees + Grass from Northwest junction (-2789,2820) (the future tram street)
    west, then north as the lake approach to (-3100,3380).
  - Lakeview Crescent: Small Road with Trees, a curved link from Northwest junction (-2789,2710) to the parkway.
  - North Crescent: Small Road with Trees, a horseshoe west of the parkway (apex x -3300), CityPark04 inside
    (placed before zoning).
  - Birch Lane: Small Road with Trees, a curved link from Northwest junction (-2789,3040) to the parkway.
  - Zoning (overwrite=False): parkway z<2900 NA Residential Mixed, 2900..3150 NA Residential High, beyond
    NA Residential Medium; Lakeview Crescent Medium; North Crescent Medium inside / Medium Row outside;
    Birch Lane Medium Row.
Usage: python -I builders/builder-west_northlake-north.py roads|zone [dry]
"""
import json
import math
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[2:]
PARKWAY = [[-2789, 2820], [-2900, 2824], [-3020, 2852], [-3110, 2930], [-3150, 3050], [-3140, 3200], [-3100, 3380]]
LAKEVIEW = [[-2789, 2710], [-2900, 2702], [-3000, 2700], [-3065, 2745], [-3060, 2870]]
NORTH_CR = [[-3125, 2965], [-3230, 2975], [-3300, 3060], [-3260, 3160], [-3148, 3150]]
BIRCH = [[-2789, 3040], [-2900, 3020], [-3020, 3015], [-3147, 3040]]
ROADS = [("Parkway", "Medium Road", PARKWAY, 120), ("Lakeview Crescent", "Small Road", LAKEVIEW, 60),
         ("North Crescent", "Small Road", NORTH_CR, 60), ("Birch Lane", "Small Road", BIRCH, 60)]


def dist_poly(p, poly):
    best = 1e9
    for (ax, az), (bx, bz) in zip(poly, poly[1:]):
        dx, dz = bx - ax, bz - az
        t = max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - az) * dz) / (dx * dx + dz * dz)))
        best = min(best, math.hypot(p[0] - ax - t * dx, p[1] - az - t * dz))
    return best


def edges(poly, prefab, tol=18):
    out = []
    xs, zs = [p[0] for p in poly], [p[1] for p in poly]
    cx, cz = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2
    r = max(max(xs) - min(xs), max(zs) - min(zs)) / 2 + 60
    for s in get("/nets", x=cx, z=cz, radius=r, filter=prefab).get("segments") or []:
        m = ((s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2)
        if m[0] < -2795 and dist_poly(m, poly) < tol:
            out.append(s)
    return out


def fitroad(name, prefab, pts, min_r):
    body = {"prefab": prefab, "points": pts, "fit": {"tolerance": 15, "minRadius": min_r}}
    d = post("/roadfit", {**body, "dryRun": True})
    c = d.get("chosen") or {}
    print(f"{name} dry: {c.get('variant')} minR {(c.get('geometry') or {}).get('minRadius')} {c.get('demolition')} "
          f"anchors {d.get('anchors', {}).get('start', {}).get('kind')}/{d.get('anchors', {}).get('end', {}).get('kind')} {d.get('error') or ''}", flush=True)
    if DRY or not c or (c.get("demolition") or {}).get("buildings"):
        return False
    r = post("/roadfit", body)
    print(f"{name}: {r.get('applied')} {(r.get('chosen') or {}).get('variant')}", flush=True)
    return bool(r.get("applied"))


def roads():
    if DRY:
        for name, prefab, pts, mr in ROADS:
            fitroad(name, prefab, pts, mr)
        return
    with paused(WHO):
        built = {}
        for name, prefab, pts, mr in ROADS:
            built[name] = fitroad(name, prefab, pts, mr)
            time.sleep(1)
        for name, prefab, pts, mr in ROADS:
            if not built[name]:
                continue
            ups = ["Trees", "Grass"] if prefab == "Medium Road" else ["Trees"]
            for up in ups:
                t = post("/roadupgrade", {"polyline": pts, "tolerance": 15, "prefabFilter": prefab, "upgrade": up, "side": "both"})
                print(f"  {name} {up}: {short(t, ('applied', 'cost', 'errors'))}", flush=True)
        if built.get("North Crescent"):
            for prefab in ("CityPark04", "CityPark02", "CityPark03"):
                p = post("/place", {"prefab": prefab, "near": [-3215, 3065], "within": [-3275, 3000, -3150, 3135], "radius": 70,
                                    "maxHouseholds": 0, "maxCompanies": 0}, timeout=300)
                print(f"  park {prefab}: {p.get('applied')} {p.get('placedId')} {p.get('error') or ''}", flush=True)
                if p.get("applied"):
                    break
    print(json.dumps(built))


def zone_all():
    cells = {}

    def z(name, ids, side):
        if ids:
            r = zone(name, mode="road", edges=ids, depth=6, side=side, overwrite=False)
            cells[name] = cells.get(name, 0) + (r.get("cellsChanged") or 0)

    with paused(WHO):
        for prefab in ("CityPark04", "CityPark02", "CityPark03", "PocketPark05"):
            p = post("/place", {"prefab": prefab, "near": [-3240, 3065], "within": [-3305, 2960, -3150, 3165], "radius": 110,
                                "maxHouseholds": 0, "maxCompanies": 0}, timeout=300)
            ch = p.get("chosen") or {}
            print(f"  park {prefab}: {p.get('applied')} {p.get('placedId')} at {ch.get('x')},{ch.get('z')} {p.get('error') or ''}", flush=True)
            if p.get("applied"):
                break
        p = post("/place", {"prefab": "Playground04", "near": [-3060, 2790], "within": [-3110, 2720, -2990, 2860], "radius": 80,
                            "maxHouseholds": 0, "maxCompanies": 0}, timeout=300)
        print(f"  playground: {p.get('applied')} {p.get('placedId')} {p.get('error') or ''}", flush=True)
        pk = edges(PARKWAY, "Medium Road")
        mz = lambda s: (s["start"][1] + s["end"][1]) / 2  # noqa: E731
        z("NA Residential Mixed", [s["id"] for s in pk if mz(s) < 2900], "both")
        z("NA Residential High", [s["id"] for s in pk if 2900 <= mz(s) < 3150], "both")
        z("NA Residential Medium", [s["id"] for s in pk if mz(s) >= 3150], "both")
        z("NA Residential Medium", [s["id"] for s in edges(LAKEVIEW, "Small Road")], "both")
        for s in edges(NORTH_CR, "Small Road"):
            (sx, sz), (ex, ez) = s["start"], s["end"]
            mx, mzz = (sx + ex) / 2, (sz + ez) / 2
            inner_right = (ez - sz) * (-3215 - mx) - (ex - sx) * (3065 - mzz) > 0
            z("NA Residential Medium", [s["id"]], "right" if inner_right else "left")
            z("NA Residential Medium Row", [s["id"]], "left" if inner_right else "right")
        z("NA Residential Medium Row", [s["id"] for s in edges(BIRCH, "Small Road")], "both")
        print("zoned:", cells, flush=True)
    print(json.dumps({"cells": cells}))


if __name__ == "__main__":
    {"roads": roads, "zone": zone_all}[sys.argv[1]]()
