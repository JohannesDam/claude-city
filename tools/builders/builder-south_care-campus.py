"""builder-south / b1-health_safety-sw-care-campus: South-West Care Campus on the empty west end of Old Southern Works.

Memorial Drive: a curved, tree-lined Medium Road from the troubleshooter's x=-3020 link road (entrance at
(-3020,-1071), which leads to the x=-2900 SWQ street) west to (-3390,-1010).
North of the drive (strip z -905..-1060): Crematorium01 + Hearse Garage (deathcare is short: the SW and
Waterfront crematoria run at ~100%). South of the drive: DiseaseControlCenter01 and the Cemetery01 memorial garden.
The education tree belt x -3010..-2830, z -1010..-1050 stays free. No water pumps here (polluted groundwater).
Stages: drive | crematorium | dcc | cemetery
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, short, find_spot, building

WHO = "builder-south"
DRIVE = [[-3020, -1071], [-3120, -1092], [-3240, -1092], [-3335, -1068], [-3390, -1010]]
CAMPUS = (-3430, -1560, -3025, -900)  # x0, z0, x1, z1: every lot corner must stay inside


def place_near(label, prefab, x, z, radius=250, box=CAMPUS, avoid=None):
    for c in find_spot(prefab, x, z, radius=radius, n=10):
        if not all(box[0] <= p[0] <= box[2] and box[1] <= p[1] <= box[3] for p in c["corners"]):
            continue
        if avoid and any(avoid(p) for p in c["corners"]):
            continue
        b = dict(c["building"])
        b.pop("prefab", None)
        d = post("/building", {"prefab": prefab, **b, "dryRun": True})
        if d.get("errors") == 0 and not d.get("wouldDemolish"):
            r = building(prefab, **b)
            print(f"{label:<24} {short(r)} at ({c['x']},{c['z']}) corners={c['corners']}", flush=True)
            return r
    print(f"{label:<24} no clean spot", flush=True)
    return None


stage = sys.argv[1] if len(sys.argv) > 1 else ""

if stage == "drive":
    with paused(WHO):
        r = road("Medium Road", DRIVE, curve="smooth")
        print("Memorial Drive", short(r), r.get("newEdgeIds"))
        u = post("/roadupgrade", {"polyline": DRIVE, "tolerance": 15, "prefabFilter": "Medium Road", "upgrade": "Trees", "side": "both"})
        print("trees", u.get("applied"), u.get("cost"), u.get("errors"))

if stage == "crematorium":
    with paused(WHO):
        r = place_near("Crematorium01", "Crematorium01", -3150, -1000, radius=200,
                       box=(-3430, -1090, -3025, -905))
        if r and r.get("placedId"):
            e = post("/extension", {"owner": r["placedId"], "prefab": "Crematorium01 Hearse Garage", "side": "auto", "dryRun": True}, timeout=120)
            print("garage dry", json.dumps({k: e.get(k) for k in ("errors", "problems", "slot", "pos", "wouldDemolish", "blocked")}))
            if e.get("errors") == 0 and not e.get("wouldDemolish") and not e.get("blocked"):
                e = post("/extension", {"owner": r["placedId"], "prefab": "Crematorium01 Hearse Garage", "side": "auto"}, timeout=120)
                print("garage", json.dumps({k: e.get(k) for k in ("applied", "cost", "errors", "slot", "pos")}))

if stage == "cemetery":
    with paused(WHO):
        place_near("Cemetery01 memorial", "Cemetery01", -3320, -1200, radius=200, box=(-3430, -1560, -3200, -1060))

if stage == "dcc":
    with paused(WHO):
        place_near("DiseaseControlCenter01", "DiseaseControlCenter01", -3130, -1190, radius=250, box=(-3240, -1560, -3025, -1060))
