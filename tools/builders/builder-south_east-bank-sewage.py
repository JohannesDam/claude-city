"""builder-south / b2-south-east-bank-sewage: sewage treatment for East Bank.

WastewaterTreatmentPlant01 in the empty south-east corner of East Bank (x 760..930, z -950..-1150): downwind (NE)
of the town core, ~370 m from the nearest Water Gardens pump and far from the lake intakes (north-west shore).
Access: a short tree-lined Small Road 'Works Lane' from the spine's east extension (925,-878) south and west.
The plant needs no shoreline here (dry runs pass inland); it joins the town's pipe network through the road.
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, short, find_spot, building

WHO = "builder-south"
LANE = [[925, -878], [922, -950], [895, -1000], [830, -1015], [760, -1010]]
PUMPS = [(400.26, -1230.17), (573.17, -1329.87), (451.14, -1480.56)]

with paused(WHO):
    r = road("Small Road", LANE, curve="smooth")
    print("Works Lane", short(r))
    print("trees", post("/roadupgrade", {"polyline": LANE, "tolerance": 10, "prefabFilter": "Small Road", "upgrade": "Trees", "side": "both"}).get("applied"))
    for c in find_spot("WastewaterTreatmentPlant01", 840, -1060, radius=160, n=10):
        cs = c["corners"]
        if not all(560 <= p[0] <= 928 and -1250 <= p[1] <= -940 for p in cs):
            continue
        if min(math.hypot(c["x"] - px, c["z"] - pz) for px, pz in PUMPS) < 300:
            continue
        b = dict(c["building"]); b.pop("prefab", None)
        d = post("/building", {"prefab": "WastewaterTreatmentPlant01", **b, "dryRun": True})
        print("cand", c["x"], c["z"], d.get("errors"), d.get("problems"))
        if d.get("errors") == 0 and not d.get("wouldDemolish"):
            print("WWTP", short(building("WastewaterTreatmentPlant01", **b)), cs)
            break
