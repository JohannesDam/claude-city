"""builder-south / b1-economy-osw-inputs-freight-fix: second freight road into Old Southern Works.

No Inputs is a truck-transport problem: Old Southern Works was reached only over the x=-1140 Small Road.
'Freight Avenue' continues the x=-1909 avenue: from the z=-420 street at (-1850,-420) (T next to the avenue end
(-1900,-420); that 50 m piece is upgraded to Medium) south between the Health Campus cemetery (x <= -1845) and the
wind turbine, through the 53 m gap between the Central Investigation Bureau lot (x <= -1835) and the offices
(x >= -1782), to the empty strip z~-945, then a short Small Road tie-in into the z=-1000 factory street (a Medium
T-junction there would also clip the factory lots south of the street).
Clearance (owner rule: worth it, logged): 2 small offices OfficeLow01_L2 at (-1820,-668)/(-1796,-668) on the z=-700
street and 1 warehouse IndustrialManufacturingWarehouse01_L3 at (-1819,-972).
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short

WHO = "builder-south"
MED = [[-1850, -420], [-1826, -500], [-1814, -600], [-1812, -700], [-1812, -820], [-1814, -945]]
TIE = [[-1814, -945], [-1814, -1000]]
ALLOWED = {"735768:91", "587559:163", "331930:25"}

with paused(WHO):
    log = []
    for prefab, pts in (("Medium Road", MED), ("Small Road", TIE)):
        d = post("/road", {"prefab": prefab, "points": pts, "curve": "smooth", "dryRun": True, "allowDemolish": True})
        ids = {w["id"] for w in d.get("wouldDemolish") or []}
        print(prefab, "dry", d.get("errors"), sorted(ids))
        if d.get("errors") != 0 or not ids <= ALLOWED:
            sys.exit("unexpected dry run - stop")
        r = post("/road", {"prefab": prefab, "points": pts, "curve": "smooth", "allowDemolish": True})
        print(prefab, short(r), r.get("newEdgeIds"))
        log += r.get("wouldDemolish") or []
        time.sleep(1)
    # the 50 m between the avenue end (-1900,-420) and the new T becomes Medium Road too
    seg = [s for s in get("/nets", x=-1875, z=-420, radius=20, filter="Small Road").get("segments") or []
           if abs(s["start"][1] + 420) < 2 and abs(s["end"][1] + 420) < 2]
    if seg:
        d = post("/upgrade", {"prefab": "Medium Road", "edges": [seg[0]["id"]], "dryRun": True})
        print("upgrade dry", short(d))
        if d.get("errors") == 0 and not d.get("wouldDemolish"):
            print("upgrade", short(post("/upgrade", {"prefab": "Medium Road", "edges": [seg[0]["id"]]})))
    u = post("/roadupgrade", {"polyline": MED, "tolerance": 12, "prefabFilter": "Medium Road", "upgrade": "Trees", "side": "both"})
    print("trees", u.get("applied"), u.get("cost"))

with open(r"C:\soft\cityskyline\logs\council\demolitions.jsonl", "a", encoding="utf-8") as f:
    for w in log:
        if w.get("status") == "removed":
            f.write(json.dumps({"when": time.strftime("%Y-%m-%d %H:%M:%S"), "builder": WHO,
                                "item": "b1-economy-osw-inputs-freight-fix", "reason": "second freight road into Old Southern Works",
                                "id": w["id"], "prefab": w["prefab"], "center": w["center"], "renters": w.get("renters")}) + "\n")
