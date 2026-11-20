"""Troubleshooter: junction programme (worklist item b1-mobility-junction-programme).

Small roundabouts at three bottleneck junctions (the item approves up to 3 zoned lots per junction; medium
ones would take 4), traffic lights at the two Downtown accident junctions (a roundabout there would take
3-4 homes), all-way stops at the two SW Quarter accident junctions (a roundabout would take 2-4 medium
apartment blocks). Every roundabout is dry-run first and refused if it would take more than 2 lots.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_junctions.py [--build]
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import paused, post  # noqa: E402

BUILD = "--build" in sys.argv
LOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "logs", "council", "demolitions.jsonl")

ROUNDABOUTS = [("379765:1", "small", 2), ("600213:23", "small", 2), ("244142:37", "small", 0)]
LIGHTS = ["379827:1", "380238:1"]
STOPS = ["1294079:1", "1112685:9"]

out = []
for node, size, max_lots in ROUNDABOUTS:
    with paused("troubleshooter"):
        d = post("/roundabout", {"node": node, "size": size, "dryRun": True}, timeout=120)
        lots = [(w["id"], w["prefab"]) for w in d.get("wouldDemolish") or []]
        print(node, size, "dry errors", d.get("errors"), "lots", lots)
        if d.get("errors") != 0 or len(lots) > max_lots or not BUILD:
            out.append({"node": node, "built": False, "lots": lots})
            continue
        r = post("/roundabout", {"node": node, "size": size}, timeout=120)
        time.sleep(1.0)
        print(node, "built", r.get("applied"), r.get("errors"), r.get("prefab"), r.get("cost"))
        out.append({"node": node, "built": bool(r.get("applied")), "lots": lots, "prefab": r.get("prefab")})
        if r.get("applied") and lots:
            with open(LOG, "a", encoding="utf-8") as f:
                f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": "troubleshooter",
                                    "prefab": r.get("prefab"), "node": node, "removed": lots,
                                    "why": "junction programme: roundabout at a bottleneck (item approves <= 3 lots)"}) + "\n")
if BUILD:
    with paused("troubleshooter"):
        for upgrade, nodes in (("Traffic Lights", LIGHTS), ("All-way Stop", STOPS)):
            r = post("/nodeupgrade", {"upgrade": upgrade, "nodes": nodes})
            print(upgrade, json.dumps(r)[:400])
            out.append({"upgrade": upgrade, "nodes": nodes, "applied": r.get("applied"), "errors": r.get("errors")})
print(json.dumps(out))
