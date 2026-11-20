"""Troubleshooter: NE highway southbound bottleneck (395355:1 / 395354:1 / 395312:1, around (-367,3615)).

Southbound traffic from the NE interchange (2 lanes, widening to 3 at (-100,3990)) splits at (-408,3542) into the
3-lane spine and a 1-lane off-ramp (1803996:7 .. 1804002:7) that ends at (-800,3400) on a 50 m two-way Small
Road (881808:55) into a T-junction at (-850,3400) (west to University Hill / Northfield, north to Science Ridge).
The ramp is the most worn road in the city and its queue backs up onto the highway. Fix without touching the
park (a Medium Road there overlaps CityPark11 and the road depot):
1) ramp 1 lane -> 'Highway Oneway - 2 lanes' (direction kept by /upgrade, clean dry run);
2) the ramp-end street only ever carries westbound traffic (its east node is the one-way ramp end), so it is
   rebuilt as 'Small Road Oneway - 3 lanes' drawn west from (-800,3400) to (-850,3400), same 16 m width.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_ne_offramp.py [--build]
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import paused, post  # noqa: E402

BUILD = "--build" in sys.argv
RAMP = ["1803996:7", "1803997:7", "1803998:7", "1803999:7", "1804000:7", "1804001:7", "1804002:7"]
STREET = "881808:55"
PTS = [[-800, 3400], [-850, 3400]]

with paused("troubleshooter"):
    for i in range(0, len(RAMP), 4):
        b = RAMP[i:i + 4]
        r = post("/upgrade", {"prefab": "Highway Oneway - 2 lanes", "edges": b, "dryRun": not BUILD})
        print("ramp", b, r.get("applied"), r.get("errors"), r.get("cost"), r.get("wouldDemolish"))
        time.sleep(1.0)
    if BUILD:
        b = post("/bulldoze", {"edges": [STREET]})
        print("bulldoze", b.get("applied"), b.get("errors"))
        time.sleep(1.0)
    d = post("/road", {"prefab": "Small Road Oneway - 3 lanes", "points": PTS, "dryRun": True})
    print("road dry", d.get("errors"), d.get("problems"), d.get("wouldDemolish"), json.dumps(d.get("snapping"))[:300])
    if BUILD:
        if d.get("errors") == 0:
            r = post("/road", {"prefab": "Small Road Oneway - 3 lanes", "points": PTS})
        else:  # put the old street back
            r = post("/road", {"prefab": "Small Road", "points": PTS})
        print("road", r.get("applied"), r.get("errors"), r.get("newEdgeIds"), json.dumps(r.get("snapping"))[:300])
