"""builder-south / b1-mobility-bus-south-depot

Rebuild the south bus depot in the Health Campus (empty block between the z=-700 and z=-870 streets,
x -1470..-1327) so line 3 (green, terminus at (-1140,-850)) gets vehicles from nearby, then add two
Extra Garages. Line 7 East Lakeshore is a separate step (see builder-south_line7.py).
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, building, short, find_spot

WHO = "builder-south"

# survey (no lock): best clean spot from /findspot, front on the z=-870 street
cands = find_spot("BusDepot01", -1395, -825, radius=300, n=4)
if not cands:
    sys.exit("no candidate for BusDepot01")
spot = dict(cands[0]["building"])
spot.pop("prefab", None)
print("candidate", cands[0]["x"], cands[0]["z"], spot)

with paused(WHO):
    dry = post("/building", {"prefab": "BusDepot01", **spot, "dryRun": True})
    print("dry", short(dry))
    if dry.get("errors") != 0 or dry.get("wouldDemolish"):
        sys.exit("dry run not clean")
    res = building("BusDepot01", **spot)
    print("depot", short(res))
depot = res.get("placedId")
if not depot:
    sys.exit("depot not placed: " + json.dumps(res)[:500])

ext = get("/extensions", owner=depot)
print("extensions", json.dumps(ext)[:1500])
