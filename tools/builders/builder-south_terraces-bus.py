"""builder-south: East Bank 'Terraces' bus line (Lakeside Terraces step 7).

Transit square (376,-844) -> spine east -> East Gate Commons (Lakefront Drive N end, (790,-335)) -> back to the
spine -> Terrace Crescent south -> Terrace Walk -> crescent north -> spine west -> transit square.
usage: python -I builders/builder-south_terraces-bus.py dry|rebuild (rebuild deletes line 1226691:93 first)
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused  # noqa: E402

DRY = (sys.argv[1] if len(sys.argv) > 1 else "dry") == "dry"
STOPS = [
    "283568:33",               # A transit square (Integrated Bus Stop, shared with lines 12, 15, 16)
    "184680:661",              # B spine eastbound (south side), x=640
    "997470:81",               # D Terrace Crescent southbound (west side)
    "398309:101",              # E crescent south arc westbound (north side)
    "780205:187",              # F Terrace Walk north-east-bound, by the signature block
    {"x": 930, "z": -780},     # R1 Terrace Rise stem northbound (east side)
    {"x": 919, "z": -630},     # R2 Terrace Rise loop northbound (east side)
    {"x": 853, "z": -440},     # L Lakeside Lane northbound (east side), lakefront green
    {"x": 820, "z": -328},     # C Lakefront Drive N westbound (north side), East Gate Commons
    {"x": 739, "z": -373},     # C2 Medium Road south-west-bound (north-west side)
    "996455:163",              # G spine westbound (north side), x=640
]
body = {"type": "bus", "stopPrefab": "EU_BusStop01", "stops": STOPS, "color": "#2E8B57", "dryRun": DRY}
if DRY:
    r = post("/line", body, timeout=300)
else:
    with paused("builder-south"):
        print("delete old", json.dumps(post("/line/delete", {"line": "1226691:93"}))[:300])
        r = post("/line", body, timeout=300)
print(json.dumps({k: r.get(k) for k in ("lineId", "error", "blocked")}))
print(json.dumps(r.get("stops"))[:1500])
print(json.dumps(r.get("pathCheck"))[:2500])
