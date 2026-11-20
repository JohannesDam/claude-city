"""builder-south / b2-education-b2-edu-southgate-south-high: South High campus in Southgate's south-west corner.

'School Lane' (Small Road, Trees+Grass) runs south from the Ring 2 / South Ring junction (-2780,-667.8) between the
SW Quarter's ES01 1295449:5 (west) and the new HighSchool01 (east, lot x -2778..-2650, z -936..-760: it ends exactly
at the metro reserve x -2650 and clears the post office). It ends at a schoolyard green; closing it into the
x=-2900 road is impossible without demolition (ES01 lot to z -1006, crematorium 190045:615 from z -1021).
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, short, building, find_spot

WHO = "builder-south"
LANE = [[-2780, -667.8], [-2790, -760], [-2791, -880], [-2780, -965]]

with paused(WHO):
    print("School Lane", short(road("Small Road", LANE, curve="smooth")))
    for up in ("Trees", "Grass"):
        print(up, post("/roadupgrade", {"polyline": LANE, "tolerance": 10, "prefabFilter": "Small Road", "upgrade": up, "side": "both"}).get("applied"))
    d = post("/building", {"prefab": "HighSchool01", "x": -2714, "z": -848, "angle": -90, "dryRun": True})
    print("HS dry", d.get("errors"), d.get("problems"))
    hs = building("HighSchool01", x=-2714, z=-848, angle=-90) if d.get("errors") == 0 and not d.get("wouldDemolish") else {}
    print("HighSchool01", short(hs))
    if hs.get("placedId"):
        ext = get("/extensions", owner=hs["placedId"])
        print("offered", [(o["name"], o.get("kind"), o.get("cost")) for o in ext.get("offered", [])])
        for o in ext.get("offered", []):
            if any(k in o["name"] for k in ("Extension Wing", "Library")):
                e = post("/extension", {"owner": hs["placedId"], "prefab": o["name"], "side": "auto", "dryRun": True}, timeout=120)
                print(" dry", o["name"], e.get("errors"), e.get("blocked"), [w.get("prefab") for w in e.get("wouldDemolish") or []])
                if e.get("errors") == 0 and not e.get("blocked") and not e.get("wouldDemolish"):
                    e = post("/extension", {"owner": hs["placedId"], "prefab": o["name"], "side": "auto"}, timeout=120)
                    print(" built", o["name"], e.get("applied"), e.get("cost"), e.get("pos"))
    for prefab, x, z in (("CityPark01", -2785, -960), ("Playground02", -2800, -930)):
        for c in find_spot(prefab, x, z, radius=80, n=8):
            b = dict(c["building"]); b.pop("prefab", None)
            dd = post("/building", {"prefab": prefab, **b, "dryRun": True})
            if dd.get("errors") == 0 and not dd.get("wouldDemolish"):
                print(prefab, short(building(prefab, **b)), (c["x"], c["z"]))
                break
