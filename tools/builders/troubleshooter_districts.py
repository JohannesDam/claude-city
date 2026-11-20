"""Troubleshooter: draw the register districts that have no in-game district yet (worklist item
b1-housing-draw-missing-districts). Corners stay ~10 m inside owned tiles and clear of existing districts
(Westside, Downtown, Northfield, Lakeside Leisure, Lakeshore, Westpark, Old Southern Works, Eastern Works,
University Hill, Northeast Wind Fields, Southgate). Tiles that are not owned yet (Pearl Bay, Eastport,
East Bank, most of Port Claude) are skipped.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_districts.py [--build]
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import paused, post  # noqa: E402

BUILD = "--build" in sys.argv
DISTRICTS = {
    # built extent: homes x -2790..-2310 (the -2493 tile column; -3117 is not owned), z 2190..3420;
    # notch at x > -2503 keeps clear of Downtown (z <= 2198)
    "Northwest": [[-2794, 2190], [-2503, 2190], [-2503, 2206], [-2308, 2206], [-2308, 3418], [-2794, 3418]],
    "Southwest Quarter": [[-4030, -925], [-2816, -925], [-2816, -304], [-4030, -304]],
    "Waterfront": [[-1098, 132], [-600, 132], [-600, 1500], [-1098, 1500]],
    "South gateway": [[-2500, -2170], [-600, -2170], [-600, -1340], [-1098, -1340], [-1098, -1405], [-2500, -1405]],
    "NE utilities": [[-400, 3420], [315, 3420], [315, 4040], [-400, 4040]],
    "Science Ridge": [[-928, 3500], [-505, 3500], [-505, 4040], [-928, 4040]],
}

names = [a for a in sys.argv[1:] if not a.startswith("--")] or list(DISTRICTS)
with paused("troubleshooter"):
    for name in names:
        body = {"prefab": "district", "name": name, "points": DISTRICTS[name]}
        r = post("/area", {**body, "dryRun": True}, timeout=120)
        keep = ("applied", "errors", "problems", "error", "size", "id", "complete")
        print(name, "dry", json.dumps({k: r.get(k) for k in keep if r.get(k) is not None})[:400])
        if BUILD and r.get("errors") == 0 and not r.get("error"):
            r = post("/area", body, timeout=120)
            print(name, "built", json.dumps({k: r.get(k) for k in keep if r.get(k) is not None})[:400])
