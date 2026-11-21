"""builder-south: East Bank 'Terraces' bus line (Lakeside Terraces step 7).

Transit square (376,-844) -> spine east -> East Gate Commons (Lakefront Drive N end, (790,-335)) -> back to the
spine -> Terrace Crescent south -> Terrace Walk -> crescent north -> spine west -> transit square.
usage: python -I builders/builder-south_terraces-bus.py dry|build
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused  # noqa: E402

DRY = (sys.argv[1] if len(sys.argv) > 1 else "dry") == "dry"
STOPS = [
    "283568:33",               # A transit square (Integrated Bus Stop, shared with lines 12, 15, 16)
    {"x": 640, "z": -906},     # B spine eastbound (south side)
    {"x": 750, "z": -386},     # C East Gate Commons, Lakefront Drive N end (NE-bound, SE side)
    {"x": 905, "z": -1150},    # D Terrace Crescent southbound (west side)
    {"x": 760, "z": -1380},    # E crescent south arc westbound (north side)
    {"x": 722, "z": -1286},    # F Terrace Walk north-east-bound (south-east side), by the signature block
    {"x": 640, "z": -888},     # G spine westbound (north side)
]
body = {"type": "bus", "stopPrefab": "EU_BusStop01", "stops": STOPS, "color": "#2E8B57", "dryRun": DRY}
if DRY:
    r = post("/line", body, timeout=300)
else:
    with paused("builder-south"):
        r = post("/line", body, timeout=300)
print(json.dumps({k: r.get(k) for k in ("lineId", "error", "blocked")}))
print(json.dumps(r.get("stops"))[:1500])
print(json.dumps(r.get("pathCheck"))[:2500])
