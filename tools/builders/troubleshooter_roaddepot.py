"""Troubleshooter: road maintenance (worklist item b1-troubleshooter-road-maintenance-depot).

All 4 depots had 10/10 trucks out and 23 requests failed to find a truck; half of the failing targets are in
Westside / SW Quarter / Westbrook, where no depot exists (depots: -1084,-812; -2211,-1528; -2432,-156; -940,3456).
1) Extra Garage (+5 trucks, $4k, fixed upgrade) on all 4 depots.
2) A fifth depot in the middle of the western cluster (around -3450,250). No clean roadside lot there (the clean
   finds are inside protected Westpark), so make_room places it over the cheapest set of single-family homes
   within the owner's limits (logged to demolitions.jsonl), and it gets an Extra Garage too.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_roaddepot.py [--build]
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import make_room, paused, post  # noqa: E402

BUILD = "--build" in sys.argv
GARAGE = "RoadMaintenanceDepot01 Extra Garage"
DEPOTS = ["575227:23", "1165664:73", "201256:229", "1617822:3"]

with paused("troubleshooter"):
    for o in DEPOTS:
        r = post("/extension", {"owner": o, "prefab": GARAGE, "dryRun": not BUILD}, timeout=120)
        print(o, "garage", r.get("applied"), r.get("errors"), r.get("cost"), r.get("wouldDemolish"))
    r = make_room("RoadMaintenanceDepot01", -3450, 250, radius=600, dry=not BUILD, log_as="troubleshooter")
    res = r.get("result") or {}
    print("depot", r.get("mode"), r.get("cost"), r.get("candidate"),
          {k: res.get(k) for k in ("applied", "errors", "placedId", "pos", "roadConnected")})
    new_id = res.get("placedId")
    if BUILD and new_id:
        g = post("/extension", {"owner": new_id, "prefab": GARAGE}, timeout=120)
        print(new_id, "garage", g.get("applied"), g.get("errors"), g.get("cost"))
