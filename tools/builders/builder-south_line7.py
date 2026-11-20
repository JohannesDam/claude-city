"""builder-south / b1-mobility-bus-south-depot, step 4: bus line 7 East Lakeshore.

Closed loop: outbound north on the x=-680 Lakeshore street (stops on the east side, x=-674.3), return south
(west side, x=-685.7), then west over the z=1500 highway crossing to a transfer pair on line 4's x=-1469
avenue at z=1747.5 (existing stops 286223:109 northbound and 460419:17 southbound). The board's r2 layout used
line 4's z=2242.5 pair instead; z=1747.5 is the first line-4 stop pair north of the only highway crossing
(z=1500), so the bus duplicates 250 m of line 4 instead of 740 m.
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short

WHO = "builder-south"
stops = [
    {"x": -674.3, "z": 1700}, {"x": -674.3, "z": 2078}, {"x": -674.3, "z": 2400}, {"x": -674.3, "z": 2700},
    {"x": -685.7, "z": 2700}, {"x": -685.7, "z": 2400}, {"x": -685.7, "z": 2078}, {"x": -685.7, "z": 1700},
    "286223:109",  # line 4 northbound, (-1462.7, 1747.5)
    "460419:17",   # line 4 southbound, (-1475.3, 1747.5)
]
dry = post("/busline", {"stops": stops, "color": "#6D4C41", "stopPrefab": "NA_BusStop01", "dryRun": True}, timeout=300)
print("dry", json.dumps(dry)[:1500])
if dry.get("error"):
    sys.exit(1)
with paused(WHO):
    res = post("/busline", {"stops": stops, "color": "#6D4C41", "stopPrefab": "NA_BusStop01"}, timeout=300)
print("line", json.dumps(res)[:2500])
