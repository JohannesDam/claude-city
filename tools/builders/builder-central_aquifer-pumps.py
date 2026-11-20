"""builder-central: water margin (worklist b1-utilities-north-aquifer-pumps).

Survey 2032-01-06: capacity 876,850 vs 598,868 used. The two board sites near (-1030,1950) were already taken in
round 2 (pumps 409804:9 and 1846607:3); the remaining clean 8-9 cells east of the highway (-940,1960) have no
road access without demolishing homes. One GroundwaterPumpingStation01 goes on the untapped southern 9-value
lobe, facing the x=-1140 Medium Road at (-1163,1537) - 270 m from the nearest pump. The shallow surface pump
322364:1 (-687.5,945; WaterDepth 0.47) cannot be moved to deeper water: every shore lot reachable from the
x=-720 road ends in 1-4 m of water (POST /terrain), 15 m depth only starts ~60 m offshore.

Run: cd C:/soft/cityskyline/tools && python -I builders/builder-central_aquifer-pumps.py [--dry]
"""
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short, building  # noqa: E402

WHO = "builder-central"
DRY = "--dry" in sys.argv
SITE = {"x": -1162.84, "z": 1536.54, "angle": 179.95}

with paused(WHO):
    d = post("/building", {"prefab": "GroundwaterPumpingStation01", **SITE, "dryRun": True})
    print("dry", short(d))
    if d.get("errors") == 0 and not d.get("wouldDemolish") and not DRY:
        print("GroundwaterPumpingStation01", short(building("GroundwaterPumpingStation01", **SITE)))
