"""builder-east / Commons civic: central green, lakefront plaza, park cafe, bus station (dry|build)."""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import find_spot, post, paused, short, building  # noqa: E402

WHO = "builder-east"
BUILD = len(sys.argv) > 1 and sys.argv[1] == "build"
PLAN = [  # prefab, near, candidate-centre box [x0, z0, x1, z1]
    ("CityPark11", (1420, -480), [1370, -560, 1480, -400]),          # Commons green inside the east crescent
    ("SunkenPlaza01", (1300, -85), [1200, -110, 1420, -60]),         # lakefront plaza at the foot of Commons Drive
    ("ParkCafe01", (1420, -95), [1340, -115, 1520, -60]),            # cafe on the lake side
    ("BusStation02", (1290, -330), [1200, -450, 1320, -250]),        # Commons bus station on Commons Drive (west side)
]
out = {}
with paused(WHO):
    for prefab, near, box in PLAN:
        cands = [c for c in find_spot(prefab, near[0], near[1], radius=250, n=40)
                 if box[0] <= c["x"] <= box[2] and box[1] <= c["z"] <= box[3]]
        cands.sort(key=lambda c: (c["x"] - near[0]) ** 2 + (c["z"] - near[1]) ** 2)
        for c in cands[:8]:
            b = dict(c["building"])
            b.pop("prefab", None)
            d = post("/building", {"prefab": prefab, **b, "dryRun": True})
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                print(prefab, round(c["x"]), round(c["z"]), c.get("angle"))
                if BUILD:
                    r = building(prefab, **b)
                    out[prefab] = r.get("placedId")
                    print("  ", short(r))
                break
        else:
            print(prefab, "no spot", len(cands))
print(json.dumps(out))
