"""builder-central: metro M1 capacity.

M1 (2144837:13) runs 4 trains at the default vehicle count while 300+ passengers wait at Southgate.
The yard SubwayYard02 1642755:3 holds 7 trains (base + one Extra Rails). This script adds one more
Extra Rails module (capacity 9) and raises M1's Route Vehicle Count to 200% (8 trains).

usage: python -I builders/builder-central_metro-capacity.py dry|build
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

YARD = "1642755:3"
LINE = "2144837:13"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"


def try_extension(dry):
    # the plain sides overlap the yard's own tracks, the bus station and the park; a 60/90 m offset on the
    # right side (next to the existing Extra Rails) is clean
    for prefab, side, off in (("SubwayYard02 Extra Rails 01", "right", 60), ("SubwayYard02 Extra Rails 01", "right", 90),
                              ("SubwayYard02 Extra Rails 02", "left", -60)):
        r = post("/extension", {"owner": YARD, "prefab": prefab, "side": side, "offset": off, "dryRun": dry}, timeout=120)
        print(prefab, side, off, json.dumps(short(r))[:600])
        if not (r.get("errors") or r.get("problems") or r.get("blocked") or r.get("error") or r.get("wouldDemolish")):
            return prefab, side, off
    return None, None, None


if MODE == "dry":
    try_extension(True)
else:
    with paused("builder-central"):
        prefab, side, off = try_extension(True)
        if prefab:
            r = post("/extension", {"owner": YARD, "prefab": prefab, "side": side, "offset": off}, timeout=120)
            print("BUILT", prefab, side, off, json.dumps(short(r))[:600])
        r = post("/policy", {"name": "Route Vehicle Count", "target": LINE, "value": 200})
        print("policy", json.dumps(r)[:400])
    ext = get("/extensions", owner=YARD)
    print("capacity", ext.get("combined", {}).get("TransportDepotData", {}).get("vehicleCapacity"))
