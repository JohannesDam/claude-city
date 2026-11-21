"""builder-west, b2-housing-northlake-new-town-p1 phase 1b: more curved streets in Northlake's empty northern pockets.

  - Willow Crescent: Small Road with Trees, a curved cul-de-sac from Northwest junction (-2789,2930) (the tie-in to
    Birch Lane would remove 3 new homes).
  - Aspen Crescent: Small Road with Trees, a horseshoe on Northwest junctions (-2789,3150) and (-2789,3260).
  - (Fern Crescent dropped: the parkway frontage is built up and its dead end points north, too close to the tile edge.)
  - A pocket park in each (before zoning), PostOffice01 near the parkway's north end (post coverage 5/9 there),
    Extension Floors on the square's ElementarySchool03 1176092:167.
  - Zoning (overwrite=False): NA Residential Medium inside / NA Residential Medium Row outside; Willow both Medium.
Usage: python -I builders/builder-west_northlake-crescents.py [dry]
"""
import json
import math
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[1:]
STREETS = [
    ("Willow Crescent", [[-2789, 2930], [-2900, 2925], [-2975, 2955]], None, ("PocketPark05", [-2900, 2970])),
    ("Aspen Crescent", [[-2789, 3150], [-2900, 3150], [-2985, 3205], [-2950, 3290], [-2860, 3300], [-2789, 3260]], (-2880, 3220),
     ("PocketPark08", [-2880, 3220])),
]


def dist_poly(p, poly):
    best = 1e9
    for (ax, az), (bx, bz) in zip(poly, poly[1:]):
        dx, dz = bx - ax, bz - az
        t = max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - az) * dz) / (dx * dx + dz * dz)))
        best = min(best, math.hypot(p[0] - ax - t * dx, p[1] - az - t * dz))
    return best


def edges(poly):
    xs, zs = [p[0] for p in poly], [p[1] for p in poly]
    cx, cz = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2
    r = max(max(xs) - min(xs), max(zs) - min(zs)) / 2 + 60
    out = []
    for s in get("/nets", x=cx, z=cz, radius=r, filter="Small Road").get("segments") or []:
        m = ((s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2)
        if m[0] < -2795 and dist_poly(m, poly) < 18:
            out.append(s)
    return out


def main():
    ok = {}
    for name, pts, _, _ in STREETS:
        d = post("/roadfit", {"prefab": "Small Road", "points": pts, "fit": {"tolerance": 15, "minRadius": 55}, "dryRun": True})
        c = d.get("chosen") or {}
        ok[name] = bool(c) and not (c.get("demolition") or {}).get("buildings")
        print(f"{name} dry: {c.get('variant')} minR {(c.get('geometry') or {}).get('minRadius')} {c.get('demolition')} {d.get('error') or ''}", flush=True)
    if DRY:
        return
    cells, placed = {}, {}
    with paused(WHO):
        for name, pts, _, _ in STREETS:
            if ok[name]:
                r = post("/roadfit", {"prefab": "Small Road", "points": pts, "fit": {"tolerance": 15, "minRadius": 55}})
                ok[name] = bool(r.get("applied"))
                print(f"{name}: {r.get('applied')} {(r.get('chosen') or {}).get('variant')}", flush=True)
                time.sleep(1)
        for name, pts, _, park in STREETS:
            if not ok[name]:
                continue
            t = post("/roadupgrade", {"edges": [s["id"] for s in edges(pts)], "upgrade": "Trees", "side": "both"})
            print(f"  {name} trees: {short(t, ('applied', 'cost', 'errors'))}", flush=True)
            x, z = park[1]
            p = post("/place", {"prefab": park[0], "near": [x, z], "within": [x - 80, z - 80, x + 80, z + 80], "radius": 80,
                                "maxHouseholds": 0, "maxCompanies": 0}, timeout=300)
            print(f"  {park[0]}: {p.get('applied')} {p.get('placedId')} {p.get('error') or ''}", flush=True)
            if p.get("applied"):
                placed[f"{name} {park[0]}"] = p.get("placedId")
        p = post("/place", {"prefab": "PostOffice01", "near": [-3140, 3300], "within": [-3260, 3150, -3000, 3420], "radius": 140,
                            "maxHouseholds": 0, "maxCompanies": 0}, timeout=300)
        print(f"  PostOffice01: {p.get('applied')} {p.get('placedId')} {p.get('error') or ''}", flush=True)
        if p.get("applied"):
            placed["PostOffice01"] = p.get("placedId")
        e = post("/extension", {"owner": "1176092:167", "prefab": "ElementarySchool03 Extension Floors", "side": "auto"}, timeout=120)
        print("  school floors:", short(e, ("applied", "errors", "cost")), flush=True)
        time.sleep(1)
        for name, pts, inner, _ in STREETS:
            if not ok[name]:
                continue
            for s in edges(pts):
                if inner is None:
                    jobs = [("NA Residential Medium", "both")]
                else:
                    (sx, sz), (ex, ez) = s["start"], s["end"]
                    mx, mz = (sx + ex) / 2, (sz + ez) / 2
                    ir = (ez - sz) * (inner[0] - mx) - (ex - sx) * (inner[1] - mz) > 0
                    jobs = [("NA Residential Medium", "right" if ir else "left"), ("NA Residential Medium Row", "left" if ir else "right")]
                for zn, side in jobs:
                    r = zone(zn, mode="road", edges=[s["id"]], depth=6, side=side, overwrite=False)
                    cells[zn] = cells.get(zn, 0) + (r.get("cellsChanged") or 0)
        print("zoned:", cells, flush=True)
    print(json.dumps({"built": ok, "placed": placed, "cells": cells}))


if __name__ == "__main__":
    main()
