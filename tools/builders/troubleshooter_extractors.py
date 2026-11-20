"""Troubleshooter: Stone/Ore extractors next to the factory estates (conductor-approved after the No Inputs
diagnosis: the icon means the input's buy cost per unit > 5, and all 65 affected companies buy Stone or Ore by truck
from the far road outside connections).

South (Old Southern Works smelters, Ore): main building on the new Medium Road at about (-1606,-1826), work area on
the rich, empty ore band x -1555..-1340, z -1990..-2120 (east of the existing Ore Lot 308233:171).
North (Eastern Works concrete/mineral plants, Stone): main building on the Medium Road north of the off-ramp T
(-797,3455), work area on the free strip x -757..-562, z 3436..3512 (rich ore, clear of the wind-gate turbines).

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_extractors.py south|north [--build]
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import find_spot, get, paused, post  # noqa: E402

BUILD = "--build" in sys.argv
WHICH = [a for a in sys.argv[1:] if not a.startswith("--")]

SITES = {
    "south": {"prefab": "Ore Area Placeholder - Ore", "near": (-1450, -1950), "pick": (-1606, -1826),
              "area": [[-1555, -1990], [-1340, -1990], [-1340, -2120], [-1555, -2120]]},
    "north": {"prefab": "Ore Area Placeholder - Stone", "near": (-797, 3455), "pick": (-797, 3455),
              # free ore-rich strip between the Medium Road exit and the highway, south of the z=3528
              # turbine service road (turbines and their roads stay untouched)
              "area": [[-757, 3436], [-572, 3436], [-562, 3512], [-757, 3512]]},
}

for name in WHICH:
    s = SITES[name]
    cands = find_spot(s["prefab"], s["near"][0], s["near"][1], radius=450, n=12)
    cands.sort(key=lambda k: (k["x"] - s["pick"][0]) ** 2 + (k["z"] - s["pick"][1]) ** 2)
    with paused("troubleshooter"):
        placed = None
        for k in cands[:5]:
            b = dict(k["building"])
            b.pop("prefab", None)
            d = post("/building", {"prefab": s["prefab"], **b, "dryRun": True})
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                print(name, "spot", round(k["x"]), round(k["z"]), k["angle"])
                if BUILD:
                    r = post("/building", {"prefab": s["prefab"], **b})
                    placed = r.get("placedId")
                    print(name, "built", r.get("applied"), placed)
                break
        if placed and s["area"]:
            time.sleep(1.0)
            a = post("/area", {"owner": placed, "points": s["area"], "dryRun": True})
            print(name, "area dry", json.dumps({k: a.get(k) for k in ("errors", "problems", "size", "preview", "error")})[:400])
            if a.get("errors") == 0 and not a.get("error"):
                a = post("/area", {"owner": placed, "points": s["area"]})
                print(name, "area", a.get("applied"), a.get("errors"), json.dumps(a.get("preview"))[:200])
