"""Phase 24 (75k people, milestone 13): power at 96% (wind only - no coal near the city again),
243 hearse notices (ageing city), high-density demand 100 -> towers along the new x=-1909 arterial."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import get, paused, place_in_area, short, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")


def put(label, prefab, x, z, radius=250, tries=20):
    r, info = place_in_area(prefab, x, z, radius=radius, max_tries=tries)
    ok = bool(r and r.get("applied"))
    print(f"{label:<30} {short(r) if r else ('no spot', short(info or {}))}", flush=True)
    return ok


with paused():
    n = 0
    for (x, z) in ((360, 2870), (580, 2870), (800, 2870), (800, 3260), (360, 3380), (580, 3380), (-300, 3380),
                   (-1140, -1440), (-1469, -1500), (-2129, -1500), (-2700, -1500), (-3000, -1220), (-1140, -1250), (140, 3900)):
        if n >= 12:
            break
        n += put(f"wind {x},{z}", "WindTurbine01", x, z, radius=200, tries=16)
    print("turbines built:", n)

    for label, prefab, x, z in (("cemetery NE", "Cemetery01", 500, 3600), ("cemetery SW", "Cemetery01", -2750, -1440),
                                ("cemetery west", "Cemetery02", -3400, 1500), ("cemetery centre", "Cemetery02", -1600, 700),
                                ("cemetery north-3", "Cemetery02", -1689, 3100), ("cemetery far-west S", "Cemetery02", -3100, -100)):
        put(label, prefab, x, z, radius=350, tries=40)

    segs = [s["id"] for zc in (600, 1000, 1300) for s in get("/nets", x=-1909, z=zc, radius=220, filter="Medium Road")["segments"]
            if abs(s["start"][0] + 1909) < 2 and abs(s["end"][0] + 1909) < 2]
    segs = list(dict.fromkeys(segs))
    r = zone("NA Residential High", mode="road", edges=segs, depth=4)
    print(f"{'towers along x=-1909':<30} {short(r)} ({len(segs)} segments)")
print(get("/status"))
