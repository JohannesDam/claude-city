"""Troubleshooter: Eastern Works junction 556891:27 at (-300,3040) (z=3038 Medium Road x x=-300 Small Road).

x=-300 is the first north-south link between the off-ramp street (z=2930) and the on-ramp street (z=3150); its
approaches to the signalled crossing with the z=3038 Medium Road carried 2-5 bottleneck icons for more than a game
day although the node already had traffic lights. The conductor approved a roundabout that replaces ~4 factories
(industrial demand 58, free industrial zoning elsewhere). Small roundabout first; the lots are logged.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_ew_junction.py [--build]
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import paused, post  # noqa: E402

BUILD = "--build" in sys.argv
NODE, MAX_LOTS = "556891:27", 4
LOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "logs", "council", "demolitions.jsonl")

with paused("troubleshooter"):
    best = None
    for size in ("small", "medium"):
        d = post("/roundabout", {"node": NODE, "size": size, "dryRun": True, "allowDemolish": True}, timeout=120)
        lots = [(w["id"], w["prefab"]) for w in d.get("wouldDemolish") or []]
        print(size, "errors", d.get("errors"), d.get("problems"), "lots", lots)
        if d.get("errors") == 0 and len(lots) <= MAX_LOTS and (best is None or len(lots) <= len(best[1])):  # equal lots: the bigger one
            best = (size, lots)
    if BUILD and best:
        r = post("/roundabout", {"node": NODE, "size": best[0], "allowDemolish": True}, timeout=120)
        time.sleep(1.0)
        print("built", best[0], r.get("applied"), r.get("errors"), r.get("prefab"), r.get("cost"))
        if r.get("applied"):
            with open(LOG, "a", encoding="utf-8") as f:
                f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": "troubleshooter", "prefab": r.get("prefab"),
                                    "node": NODE, "removed": best[1],
                                    "why": "Eastern Works x=-300 junction bottleneck >1 game day; roundabout approved by the conductor"}) + "\n")
