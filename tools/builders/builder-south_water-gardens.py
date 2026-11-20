"""builder-south / b2-utilities-b2-utilities-east-bank-water-field: East Bank Water Gardens.

A curved tree-lined Small Road service lane from Ring Road south (300,-1155) through the clean East Bank aquifer
(GET /groundwater: cells of 9 at x 370..630, z -1460..-1200), three GroundwaterPumpingStation01 >=150 m apart on
the best cells, framed by a park. The lane entry removes one new L1 home (EU_ResidentialMedium01_L1_4x4, 16 households)
- every Ring Road frontage there has already been built on. The reserve x 250..750, z -1500..-1100 stays free of
industry.
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short, find_spot, building

WHO = "builder-south"
LANE = [[300.0, -1155.0], [318, -1225], [400, -1265], [490, -1300], [550, -1360], [520, -1430], [440, -1445]]
PUMPS = [(400, -1245), (565, -1340), (440, -1460)]

with paused(WHO):
    d = post("/road", {"prefab": "Small Road", "points": LANE, "curve": "smooth", "dryRun": True, "allowDemolish": True})
    ids = [w["id"] for w in d.get("wouldDemolish") or []]
    print("lane dry", d.get("errors"), ids)
    if d.get("errors") == 0 and ids == ["556861:185"]:
        r = post("/road", {"prefab": "Small Road", "points": LANE, "curve": "smooth", "allowDemolish": True})
        print("lane", short(r))
        print("trees", post("/roadupgrade", {"polyline": LANE, "tolerance": 12, "prefabFilter": "Small Road", "upgrade": "Trees", "side": "both"}).get("applied"))
    placed = []
    for (x, z) in PUMPS:
        for c in find_spot("GroundwaterPumpingStation01", x, z, radius=70, n=8):
            if any(math.hypot(c["x"] - px, c["z"] - pz) < 150 for px, pz in placed):
                continue
            b = dict(c["building"]); b.pop("prefab", None)
            dd = post("/building", {"prefab": "GroundwaterPumpingStation01", **b, "dryRun": True})
            if dd.get("errors") == 0 and not dd.get("wouldDemolish"):
                r = building("GroundwaterPumpingStation01", **b)
                print("pump", short(r), (c["x"], c["z"]))
                placed.append((c["x"], c["z"]))
                break
        else:
            print("pump: no spot near", x, z)
    for prefab, x, z in (("CityPark03", 470, -1330), ("Playground01", 340, -1240)):
        for c in find_spot(prefab, x, z, radius=100, n=8):
            b = dict(c["building"]); b.pop("prefab", None)
            dd = post("/building", {"prefab": prefab, **b, "dryRun": True})
            if dd.get("errors") == 0 and not dd.get("wouldDemolish"):
                print(prefab, short(building(prefab, **b)))
                break
