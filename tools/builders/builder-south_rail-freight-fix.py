"""builder-south / b1-growth-south-rail-freight-fix

1. Join RailYard02's second (north-west) track: its south-west end at (-2507.9,-1762.5) gets a Twoway Train
   Track that bends ~26 deg (min radius 115 m, two smooth spans) and merges into the existing yard connector at
   node 391894:35 (-2548.8,-1863.6), where the connector heads 252 deg towards the main line. The connector
   cannot be joined earlier: the second track lies on the outside of the connector's left curve.
2. Remove the dead-end stub 523893:1 (Double Train Track, (-2387.5,-1877.3) -> (-2212.6,-1718.8)) that runs
   beside the terminal branch and carries the 'Track Not Connected' icons.
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, road, short

WHO = "builder-south"
TRACK = {"prefab": "Twoway Train Track",
         "points": [[-2507.9, -1762.5], [-2522, -1785], [-2548.8, -1863.6]], "curve": "smooth"}

with paused(WHO):
    dry = post("/road", {**TRACK, "dryRun": True})
    print("dry track", short(dry), json.dumps(dry.get("geometry", {}).get("minRadius")))
    if dry.get("errors") == 0:
        res = post("/road", TRACK)
        print("track", short(res), json.dumps(res.get("snapping")), res.get("newEdgeIds") or res.get("builtEdges"))
    stub = post("/bulldoze", {"edges": ["523893:1"], "dryRun": True})
    if stub.get("errors") == 0 and stub.get("edgeCount") == 1 and not (stub.get("impact") or {}).get("buildingsFrontingDeletedEdges"):
        res = post("/bulldoze", {"edges": ["523893:1"]})
        print("stub", short(res), res.get("edgeCount"))
