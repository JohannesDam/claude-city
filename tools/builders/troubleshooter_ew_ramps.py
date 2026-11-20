"""Troubleshooter: Eastern Works ramp roads (worklist item b1-ts-eastern-works-ramps).

The west highway ramps of Eastern Works end on the Small Roads z=2930 (off-ramp) and z=3150 (on-ramp), which
cross the whole estate to the Medium Road collector at x=1530. All freight arrives by truck, so these become
tree-lined Medium Roads: /upgrade in batches of <= 4 edges (dry run first; batches that would demolish a
building are skipped), then Trees on both sides via /roadupgrade on the new edge ids.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_ew_ramps.py [--build] [z...]
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import get, paused, post  # noqa: E402

BUILD = "--build" in sys.argv
LINES = [int(a) for a in sys.argv[1:] if not a.startswith("--")] or [2930, 3150]


def street(z, x0=-505, x1=1535):
    segs = {}
    for x in range(x0, x1 + 1, 250):
        for e in get("/nets", x=x, z=z, radius=140, filter="Small Road").get("segments", []):
            if abs(e["start"][1] - z) < 6 and abs(e["end"][1] - z) < 6:
                segs[e["id"]] = e
    return sorted(segs.values(), key=lambda e: min(e["start"][0], e["end"][0]))


report = []
for z in LINES:
    segs = street(z)
    ids = [e["id"] for e in segs]
    print(f"z={z}: {len(ids)} Small Road edges")
    for i in range(0, len(ids), 4):
        batch = ids[i:i + 4]
        xs = [round(min(e["start"][0], e["end"][0])) for e in segs[i:i + 4]]
        with paused("troubleshooter"):
            d = post("/upgrade", {"prefab": "Medium Road", "edges": batch, "dryRun": True})
            wd = [(w["id"], w["prefab"]) for w in d.get("wouldDemolish") or []]
            ok = d.get("errors") == 0 and not wd
            line = {"z": z, "x": xs, "errors": d.get("errors"), "problems": d.get("problems"), "wouldDemolish": wd}
            if ok and BUILD:
                r = post("/upgrade", {"prefab": "Medium Road", "edges": batch})
                time.sleep(1.0)
                new = r.get("newEdgeIds") or [b["id"] for b in r.get("builtEdges") or []]
                line.update(applied=r.get("applied"), cost=r.get("cost"), newEdges=new)
                if new:
                    t = post("/roadupgrade", {"edges": new, "upgrade": "Trees", "side": "both"})
                    line.update(trees=t.get("applied"), treesErrors=t.get("errors"))
            print("  ", json.dumps(line)[:400])
            report.append(line)
json.dump(report, sys.stdout if False else open(os.devnull, "w"))
