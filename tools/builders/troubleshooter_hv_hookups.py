"""Troubleshooter: connect idle power buildings to the grid (worklist item b1-ts-hv-hookups).

EmergencyBatteryStation01 512995:49 and 1503856:33 (10 MWh, idle) and the incinerators 954162:83 (south) and
379435:55 (north) show 'Powerline Not Connected': their HIGH-voltage connection point has no line, and the city
has no high-voltage grid near them (the outside HV line ends at (-2022,1516)). Each cluster gets a small
TransformerStation02 on a clean roadside lot (its low-voltage side feeds the road network) and short
High-voltage Lines from each building's HV point to the transformer (/road connectTo {start, end}).

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_hv_hookups.py [--build]
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import get, paused, place_smart, post  # noqa: E402

BUILD = "--build" in sys.argv
CLUSTERS = [
    {"name": "south", "near": (-1100, -1650), "buildings": ["512995:49", "1503856:33", "954162:83"]},
    {"name": "north", "near": (-120, 3660), "buildings": ["379435:55"]},
]


def hv_point(bid):
    s = get("/services", kind="electricity")["kinds"]["electricity"]["buildings"]
    b = next((x for x in s if x["id"] == bid), None)
    if not b:
        return None
    for c in (b.get("grid") or {}).get("connectionPoints") or []:
        if c.get("layers") == "PowerlineHigh":
            return c["pos"]
    return None


for cl in CLUSTERS:
    with paused("troubleshooter"):
        r, c = place_smart("TransformerStation02", cl["near"][0], cl["near"][1], radius=350, dry=not BUILD)
        res = r or c or {}
        tid = (r or {}).get("placedId")
        tpos = [c.get("x"), c.get("z")] if isinstance(c, dict) and c.get("x") is not None else None
        print(cl["name"], "transformer", (r or {}).get("applied"), (r or {}).get("errors"), tid, tpos)
        if not BUILD:
            continue
        if not tid:
            print("  no transformer placed:", json.dumps(res)[:300])
            continue
        time.sleep(1.0)
        for bid in cl["buildings"]:
            p = hv_point(bid)
            if not p:
                print("  ", bid, "no HV point")
                continue
            body = {"prefab": "High-voltage Line", "points": [p, tpos], "connectTo": {"start": bid, "end": tid}}
            d = post("/road", {**body, "dryRun": True})
            print("  ", bid, "line dry", d.get("errors"), d.get("problems"), d.get("wouldDemolish"),
                  json.dumps(d.get("snapping"))[:200])
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                b = post("/road", body)
                print("  ", bid, "line", b.get("applied"), b.get("errors"), b.get("cost"))
                time.sleep(1.0)
