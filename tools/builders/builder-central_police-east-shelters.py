"""builder-central: Downtown safety (worklist b2-health_safety-downtown-police-east-shelters).

- PoliceStation03 east: every lot within 250 m of (-1450,950) replaces 3+ shops (~$27k/month), so it goes on
  the clean fallback lot on the vacant z=1230 strip at (-1307,1262), 250 m north of the crime cells and
  530 m from the central station 432128:5. Trees are already on that street.
- LargeEmergencyShelter01 near (-1652,399) on the Downtown/Office Park boundary street.
- EmergencyShelter01 at the zero-coverage cells (-1341,810), (-2141,610), (-1741,2210).
All placements are clean (no demolition).

Run: cd C:/soft/cityskyline/tools && python -I builders/builder-central_police-east-shelters.py [--dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short, find_spot, building  # noqa: E402

WHO = "builder-central"
DRY = "--dry" in sys.argv
PLACE = [
    ("police east", "PoliceStation03", -1307, 1262, 60),
    ("large shelter", "LargeEmergencyShelter01", -1652, 399, 250),
    ("shelter 1", "EmergencyShelter01", -1341, 810, 150),
    ("shelter 2", "EmergencyShelter01", -2141, 610, 150),
    ("shelter 3", "EmergencyShelter01", -1888, 2105, 20),
]

placed = []
with paused(WHO):
    for label, prefab, x, z, r in PLACE:
        ok = False
        for c in find_spot(prefab, x, z, radius=r, n=8):
            if c["z"] > 2195:  # stay in Downtown (Northfield starts at z=2202)
                continue
            b = dict(c["buildingXZ"])
            b.pop("prefab", None)
            d = post("/building", {"prefab": prefab, **b, "dryRun": True})
            if d.get("errors") or d.get("wouldDemolish"):
                continue
            ok = True
            if DRY:
                print(f"{label:<14} dry ok at {c['x']},{c['z']} ({c['road']})", flush=True)
                break
            res = building(prefab, **b)
            print(f"{label:<14} {short(res)}", flush=True)
            if res.get("applied"):
                placed.append({"label": label, "prefab": prefab, "id": res.get("placedId"), "x": c["x"], "z": c["z"]})
            break
        if not ok:
            print(f"{label:<14} no clean spot", flush=True)
print(json.dumps(placed))
