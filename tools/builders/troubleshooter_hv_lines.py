"""Troubleshooter: route High-voltage Lines from idle power buildings to the new transformers (follow-up of
troubleshooter_hv_hookups.py). A straight line crossed the buildings' own lots, so this tries dog-leg routes
(L-shapes with offsets) and builds the first one the game accepts without overlaps or demolition.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_hv_lines.py [--build]
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import get, paused, post  # noqa: E402

BUILD = "--build" in sys.argv
LINKS = [("512995:49", "185572:293"), ("1503856:33", "185572:293"), ("954162:83", "185572:293"),
         ("379435:55", "419438:257")]


def hv_point(bid):
    s = get("/services", kind="electricity")["kinds"]["electricity"]["buildings"]
    b = next((x for x in s if x["id"] == bid), None)
    for c in ((b or {}).get("grid") or {}).get("connectionPoints") or []:
        if c.get("layers") == "PowerlineHigh":
            return c["pos"]
    return None


def candidates(s, e):
    sx, sz = s
    ex, ez = e
    yield []
    for dx in (0, 30, -30, 60, -60):
        for dz in (0, 30, -30, 60, -60):
            yield [[sx + dx, sz + dz], [sx + dx, ez]]
            yield [[sx + dx, sz + dz], [ex, sz + dz]]


for src, dst in LINKS:
    s = hv_point(src)
    if not s:
        print(src, "already connected or no HV point")
        continue
    e = None
    t = get("/buildings", kind="service", x=0, z=0, radius=1)  # placeholder to keep the session warm
    # transformer HV point: from the snapping report of a dry run
    done = False
    tried = 0
    with paused("troubleshooter"):
        for via in candidates(s, [0, 0]):
            pass
        probe = post("/road", {"prefab": "High-voltage Line", "points": [s, s], "connectTo": {"start": src, "end": dst},
                               "dryRun": True})
        snaps = probe.get("snapping") or []
        e = snaps[-1]["pos"] if snaps else None
        for via in candidates(s, e):
            tried += 1
            pts = [s] + via + [e]
            d = post("/road", {"prefab": "High-voltage Line", "points": pts, "connectTo": {"start": src, "end": dst},
                               "dryRun": True})
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                print(src, "route ok after", tried, "tries:", pts, "cost", d.get("cost"))
                if BUILD:
                    b = post("/road", {"prefab": "High-voltage Line", "points": pts,
                                       "connectTo": {"start": src, "end": dst}})
                    print("  built", b.get("applied"), b.get("errors"))
                    time.sleep(1.0)
                done = True
                break
            if tried >= 30:
                break
    if not done:
        print(src, "no clean route in", tried, "tries")
