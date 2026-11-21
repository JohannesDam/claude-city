"""builder-west, jobs priority 2033-01 (unemployment is the biggest happiness penalty; the west has ~100k residents
and few jobs): "Office Row" on the free owned strip between SWQ/Westpark and the South Boulevard office belt
(x -4430..-3848, z -1040..-882, no road yet).

  - Office Row: Medium Road with Trees, a gentle S-curve via /roadfit from SWQ node (-3840,-986) on the x=-3840
    street west to Westbrook corner node (-4380,-870). It links Westbrook and the SWQ to the business park belt.
  - Zoning (overwrite=False): Office High on the south side (towards the boulevard office belt), Office Low on the
    north side, clipped by a rect at z -899 so nothing is zoned inside protected Westpark (bbox z >= -898).
Usage: python -I builders/builder-west_office-row.py [dry]
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[1:]
POINTS = [[-3840, -986], [-4040, -972], [-4220, -962], [-4325, -935], [-4380, -870]]
FIT = {"tolerance": 8, "minRadius": 90}


def edges_on_course():
    """Medium Road segments of the new course (midpoints within the strip, not the existing boulevard/streets)."""
    out = []
    for s in get("/nets", x=-4110, z=-940, radius=330, filter="Medium Road").get("segments") or []:
        (sx, sz), (ex, ez) = s["start"], s["end"]
        mx, mz = (sx + ex) / 2, (sz + ez) / 2
        if -4385 < mx < -3835 and -995 < mz < -865:
            out.append(s)
    return out


def main():
    d = post("/roadfit", {"prefab": "Medium Road", "points": POINTS, "fit": FIT, "dryRun": True})
    c = d.get("chosen") or {}
    print("dry:", c.get("variant"), (c.get("geometry") or {}).get("minRadius"), json.dumps((c.get("result") or {}).get("demolition")), flush=True)
    if DRY or not c:
        return
    cells = {}
    with paused(WHO):
        r = post("/roadfit", {"prefab": "Medium Road", "points": POINTS, "fit": FIT})
        print("road:", r.get("applied"), (r.get("chosen") or {}).get("variant"), short(r.get("result") or {}, ("applied", "cost", "errors")), flush=True)
        if not r.get("applied"):
            print(json.dumps(r)[:1500])
            return
        time.sleep(1)
        segs = edges_on_course()
        ids = [s["id"] for s in segs]
        print("edges:", ids, flush=True)
        t = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"})
        print("trees:", short(t, ("applied", "cost", "errors")), flush=True)
        segs = edges_on_course()
        for s in segs:
            (sx, sz), (ex, ez) = s["start"], s["end"]
            mx, mz = (sx + ex) / 2, (sz + ez) / 2
            north_is_right = (ez - sz) * (mx - mx) - (ex - sx) * ((mz + 50) - mz) > 0
            south = "left" if north_is_right else "right"
            zr = zone("Office High", mode="road", edges=[s["id"]], depth=6, side=south, overwrite=False)
            cells["Office High"] = cells.get("Office High", 0) + (zr.get("cellsChanged") or 0)
        zr = zone("Office Low", mode="rect", points=[[-4380, -1000], [-3848, -899]], overwrite=False)
        cells["Office Low"] = cells.get("Office Low", 0) + (zr.get("cellsChanged") or 0)
        print("zone rect:", short(zr, ("cellsChanged", "occupiedCellsRezoned", "errors")), flush=True)
    print(json.dumps({"cells": cells, "edges": [s["id"] for s in segs]}))


if __name__ == "__main__":
    main()
