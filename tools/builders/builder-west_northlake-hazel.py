"""builder-west, b2-housing-northlake-new-town-p1 phase 1b: "Hazel Crescent" in Northlake's east wedge.

The wedge x -2990..-2806, z 2180..2390 is cut by the HV line (pylons (-2861,2207) and (-2964,2344)). Hazel Crescent is
a Small Road with Trees horseshoe on Northwest junctions (-2789,2380) and (-2789,2270), apex x -2905, north-east of the
line. PocketPark05 on the meadow side first; NA Residential Medium inside (depth 6), NA Residential Medium Row outside
with depth 3 only, so no zoning reaches under the wires. overwrite=False.
Usage: python -I builders/builder-west_northlake-hazel.py [dry]
"""
import json
import math
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[1:]
PTS = [[-2789, 2380], [-2865, 2376], [-2905, 2330], [-2878, 2282], [-2789, 2270]]
INNER = (-2840, 2325)


def dist_poly(p, poly):
    best = 1e9
    for (ax, az), (bx, bz) in zip(poly, poly[1:]):
        dx, dz = bx - ax, bz - az
        t = max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - az) * dz) / (dx * dx + dz * dz)))
        best = min(best, math.hypot(p[0] - ax - t * dx, p[1] - az - t * dz))
    return best


def edges():
    return [s for s in get("/nets", x=-2850, z=2325, radius=110, filter="Small Road").get("segments") or []
            if ((s["start"][0] + s["end"][0]) / 2) < -2795
            and dist_poly(((s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2), PTS) < 18]


def main():
    body = {"prefab": "Small Road", "points": PTS, "fit": {"tolerance": 12, "minRadius": 45}}
    d = post("/roadfit", {**body, "dryRun": True})
    c = d.get("chosen") or {}
    print("Hazel dry:", c.get("variant"), (c.get("geometry") or {}).get("minRadius"), c.get("demolition"), d.get("error") or "", flush=True)
    if not c:
        for x in (d.get("candidates") or [])[:3]:
            print("  ", x["variant"], x["geometry"]["minRadius"], x.get("reason"), x.get("demolition"))
    if DRY or not c or (c.get("demolition") or {}).get("buildings"):
        return
    cells = {}
    with paused(WHO):
        r = post("/roadfit", body)
        print("Hazel:", r.get("applied"), (r.get("chosen") or {}).get("variant"), flush=True)
        if not r.get("applied"):
            return
        time.sleep(1)
        t = post("/roadupgrade", {"edges": [s["id"] for s in edges()], "upgrade": "Trees", "side": "both"})
        print("trees:", short(t, ("applied", "cost", "errors")), flush=True)
        p = post("/place", {"prefab": "PocketPark05", "near": [-2925, 2310], "within": [-2960, 2270, -2895, 2360], "radius": 60,
                            "maxHouseholds": 0, "maxCompanies": 0}, timeout=300)
        print("park:", p.get("applied"), p.get("placedId"), p.get("error") or "", flush=True)
        for s in edges():
            (sx, sz), (ex, ez) = s["start"], s["end"]
            mx, mz = (sx + ex) / 2, (sz + ez) / 2
            ir = (ez - sz) * (INNER[0] - mx) - (ex - sx) * (INNER[1] - mz) > 0
            for zn, side, depth in (("NA Residential Medium", "right" if ir else "left", 6),
                                    ("NA Residential Medium Row", "left" if ir else "right", 3)):
                zr = zone(zn, mode="road", edges=[s["id"]], depth=depth, side=side, overwrite=False)
                cells[zn] = cells.get(zn, 0) + (zr.get("cellsChanged") or 0)
        print("zoned:", cells, flush=True)
    print(json.dumps({"cells": cells, "park": p.get("placedId")}))


if __name__ == "__main__":
    main()
