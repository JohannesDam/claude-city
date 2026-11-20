"""builder-east: East Gate services cluster for the works and the airport.

On the free (unzoned) north side of the Foundry Loop between the loop and the parkway's tree belt: FireHouse01,
PoliceStation02, PostOffice02; RoadMaintenanceDepot01 on the unzoned east side of Works Way between the parkway and
Works Cross (facing Works Way). Each placement must fall inside its box (keeps them off the parkway and out of the zoned lots).
Usage: python -I builders/builder-east_services.py dry|build
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import find_spot, post, paused, short, building  # noqa: E402

WHO = "builder-east"
BUILD = len(sys.argv) > 1 and sys.argv[1] == "build"
# prefab, near, box [x0, z0, x1, z1] for the candidate centre
PLAN = [
    ("RoadMaintenanceDepot01", (3967, -1424), [3940, -1440, 3995, -1405]),   # east of Works Way, facing it (west)
    ("FireHouse01", (4120, -1440), [4060, -1460, 4420, -1400]),
    ("PoliceStation02", (4230, -1440), [4060, -1460, 4420, -1400]),
    ("PostOffice02", (4340, -1440), [4060, -1460, 4420, -1400]),
]
placed = {}
with paused(WHO):
    for prefab, near, box in PLAN:
        cands = [c for c in find_spot(prefab, near[0], near[1], radius=300, n=30)
                 if box[0] <= c["x"] <= box[2] and box[1] <= c["z"] <= box[3]]
        cands.sort(key=lambda c: (c["x"] - near[0]) ** 2 + (c["z"] - near[1]) ** 2)
        done = False
        for c in cands[:6]:
            b = dict(c["building"])
            b.pop("prefab", None)
            d = post("/building", {"prefab": prefab, **b, "dryRun": True})
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                print(prefab, "spot", round(c["x"]), round(c["z"]), c.get("angle"), "roadConnected", d.get("roadConnected"))
                if BUILD:
                    r = building(prefab, **b)
                    placed[prefab] = r.get("placedId")
                    print("  built", short(r))
                done = True
                break
        if not done:
            print(prefab, "no spot", len(cands))
print(json.dumps(placed))
