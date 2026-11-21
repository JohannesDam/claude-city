"""builder-west, b2-housing-northlake-new-town-p1 phase 1b: "Pylon Meadow", the park strip along the HV line.

  - Meadow Lane: Small Road with Trees from Lake Street at the dezoned line crossing (-3026,2416) north-west,
    30 m north-east of the line (pylons (-3073,2474) (-3189,2600) (-3312,2719)), to (-3290,2739).
  - Parks on the line side of the lane (placed before any zoning): CityPark05, ChirperPark01, PocketParks.
  - NA Residential Medium Row on the far (north-east) side of the lane only. overwrite=False.
Usage: python -I builders/builder-west_northlake-meadow.py [dry]
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[1:]
LANE = [[-3026, 2416], [-3051, 2494], [-3167, 2620], [-3290, 2739]]
PARKS = [("CityPark05", [-3110, 2545]), ("ChirperPark01", [-3225, 2665]), ("PocketPark05", [-3050, 2470]),
         ("PocketPark08", [-3280, 2715])]


def lane_edges():
    out = []
    for s in get("/nets", x=-3160, z=2580, radius=200, filter="Small Road").get("segments") or []:
        (sx, sz), (ex, ez) = s["start"], s["end"]
        mx, mz = (sx + ex) / 2, (sz + ez) / 2
        # the lane runs NW along x + z ~ -550..-610 (bearing -0.74, 0.68 offset); keep segments near that band
        if -3300 < mx < -3020 and 2420 < mz < 2745 and abs((mx + 3026) * 0.68 + (mz - 2416) * 0.74 - 0) < 400:
            d = abs((mx - (-3051)) * 0.74 + (mz - 2494) * 0.68 - ((-3167 + 3051) * 0.74 + (2620 - 2494) * 0.68) * 0)
            if d < 60:
                out.append(s)
    return out


def main():
    body = {"prefab": "Small Road", "points": LANE, "fit": {"tolerance": 10, "minRadius": 80}}
    d = post("/roadfit", {**body, "dryRun": True})
    c = d.get("chosen") or {}
    print("Meadow Lane dry:", c.get("variant"), (c.get("geometry") or {}).get("minRadius"), c.get("demolition"), flush=True)
    if DRY or not c or (c.get("demolition") or {}).get("buildings"):
        return
    placed, cells = {}, 0
    with paused(WHO):
        r = post("/roadfit", body)
        print("Meadow Lane:", r.get("applied"), (r.get("chosen") or {}).get("variant"), flush=True)
        if not r.get("applied"):
            return
        time.sleep(1)
        t = post("/roadupgrade", {"polyline": LANE, "tolerance": 15, "prefabFilter": "Small Road", "upgrade": "Trees", "side": "both"})
        print("trees:", short(t, ("applied", "cost", "errors")), flush=True)
        for prefab, (x, z) in PARKS:
            p = post("/place", {"prefab": prefab, "near": [x, z], "within": [x - 70, z - 70, x + 70, z + 70], "radius": 70,
                                "maxHouseholds": 0, "maxCompanies": 0}, timeout=300)
            ch = p.get("chosen") or {}
            print(f"  {prefab}: {p.get('applied')} {p.get('placedId')} at {ch.get('x')},{ch.get('z')} {p.get('error') or ''}", flush=True)
            if p.get("applied"):
                placed[prefab] = p.get("placedId")
        time.sleep(1)
        lane = [s for s in get("/nets", x=-3160, z=2580, radius=200, filter="Small Road").get("segments") or []]
        for s in lane:
            (sx, sz), (ex, ez) = s["start"], s["end"]
            mx, mz = (sx + ex) / 2, (sz + ez) / 2
            # distance of the midpoint from the lane polyline
            best = 1e9
            for (ax, az), (bx, bz) in zip(LANE, LANE[1:]):
                dx, dz = bx - ax, bz - az
                tt = max(0.0, min(1.0, ((mx - ax) * dx + (mz - az) * dz) / (dx * dx + dz * dz)))
                best = min(best, ((mx - ax - tt * dx) ** 2 + (mz - az - tt * dz) ** 2) ** 0.5)
            if best > 15:
                continue
            # north-east side = side of the point (mx + 50*0.74, mz + 50*0.68)
            px, pz = mx + 37, mz + 34
            ne_right = (ez - sz) * (px - mx) - (ex - sx) * (pz - mz) > 0
            zr = zone("NA Residential Medium Row", mode="road", edges=[s["id"]], depth=6, side="right" if ne_right else "left", overwrite=False)
            cells += zr.get("cellsChanged") or 0
        print("zoned Medium Row:", cells, flush=True)
    print(json.dumps({"placed": placed, "cells": cells}))


if __name__ == "__main__":
    main()
