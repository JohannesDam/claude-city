"""builder-central: police capacity (worklist b1-health_safety-police-core-lake-works), the parts in my districts.

11 of 13 police stations have no free patrol cars. Two PoliceStation03 on clean lots (no demolition):
  - Waterfront south, facing the z~400 street at (-950,397) (outside protected Lakeside Leisure);
  - central Downtown, on the vacant strip north of the z=1230 street at (-1838,1262), 70 m east of the avenue.
The Old Southern Works station (builder-south's district, needs a factory replaced) and the HQ garage
extension (University Hill) are left to their owners.

Run: cd C:/soft/cityskyline/tools && python -I builders/builder-central_police.py [--dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short, building, find_spot  # noqa: E402

WHO = "builder-central"
DRY = "--dry" in sys.argv
SITES = [("Waterfront south", -950, 397, 150), ("Downtown centre", -1838, 1262, 60)]

placed = []
with paused(WHO):
    for label, x, z, r in SITES:
        for c in find_spot("PoliceStation03", x, z, radius=r, n=6):
            b = dict(c["buildingXZ"])
            b.pop("prefab", None)
            d = post("/building", {"prefab": "PoliceStation03", **b, "dryRun": True})
            if d.get("errors") or d.get("wouldDemolish"):
                continue
            if DRY:
                print(label, "dry ok", c["x"], c["z"], c["edge"])
                break
            res = building("PoliceStation03", **b)
            print(label, short(res))
            if res.get("applied"):
                placed.append({"label": label, "id": res.get("placedId"), "x": c["x"], "z": c["z"], "edge": c["edge"]})
                t = post("/roadupgrade", {"polyline": [[c["front"][0] - 40, c["front"][1]], [c["front"][0] + 40, c["front"][1]]]
                                          if abs(c["angle"]) in (0, 180) or abs(abs(c["angle"]) - 180) < 1 else
                                          [[c["front"][0], c["front"][1] - 40], [c["front"][0], c["front"][1] + 40]],
                                          "tolerance": 8, "prefabFilter": "Small Road", "upgrade": "Trees", "side": "both"})
                print("  frontage trees", short(t, keys=("applied", "cost", "errors", "problems")))
            break
print(json.dumps(placed))
