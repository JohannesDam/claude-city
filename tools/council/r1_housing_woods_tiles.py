"""Council round 1 - Housing - westbrook-woods-tiles.

Buy the western tile column (x=-4986; z=-623, 0, 623, 1247, 1870), then extend the three Westbrook
village streets (z=-650, 760, 1720) west from the west lane x=-4600 to x=-5250 as cul-de-sac villa
streets, zone them NA Residential Low and plant trees. The rest of the new tiles stays unzoned
(meadow / woodland edge for Parks).

Usage: python -I council/r1_housing_woods_tiles.py buy|streets|zone|trees|verify
"""
import json
import os
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, zone, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "verify"
WHO = "housing"
LOG = r"C:\soft\cityskyline\logs\council\r1_housing_woods_tiles.json"
CAP = 1_760_000           # approved capex 1.6 M + 10 %
TILE_X = -4986
TILE_Z = [-623, 0, 623, 1247, 1870]
STREET_Z = [-650, 760, 1720]
KEYS = ("applied", "cost", "errors", "problems", "error", "warning", "snapping", "tile", "upkeep", "bought", "status")

STATE = {"spent": 0, "steps": {}}
if os.path.exists(LOG):
    STATE = json.load(open(LOG, encoding="utf-8"))


def save():
    json.dump(STATE, open(LOG, "w", encoding="utf-8"), indent=1)


def show(label, r):
    s = short(r, keys=KEYS)
    if "snapping" in s:
        s["snapping"] = [(x.get("snappedTo"), x.get("id")) for x in s["snapping"]]
    print(f"{label:<40} {json.dumps(s)}", flush=True)
    return r


def record(label, r, cost_key="cost"):
    STATE["steps"][label] = short(r, keys=KEYS)
    if r.get("applied") or r.get("bought"):
        STATE["spent"] += r.get(cost_key) or 0
    save()


def done(label):
    s = STATE["steps"].get(label) or {}
    return s.get("applied") or s.get("bought")


def street_edges():
    seen = {}
    for z in STREET_Z:
        for x in (-4700, -4925, -5150):
            for s in get("/nets", x=x, z=z, radius=140, filter="Road").get("segments") or []:
                mz = (s["start"][1] + s["end"][1]) / 2
                mx = (s["start"][0] + s["end"][0]) / 2
                if abs(mz - z) < 2 and -5255 <= mx <= -4600:
                    seen[s["id"]] = s
    return seen


if __name__ == "__main__":
    if MODE == "buy":
        for z in TILE_Z:
            label = f"buytile {TILE_X},{z}"
            if done(label):
                continue
            with paused(WHO):
                d = show(label + " (dry)", post("/buytile", {"x": TILE_X, "z": z, "dryRun": True}))
                if d.get("error") or d.get("status") not in (None, "None"):
                    record(label + " SKIP", d)
                    continue
                if STATE["spent"] + (d.get("cost") or 0) > CAP:
                    show(label + " SKIP (capex cap)", d)
                    continue
                r = show(label, post("/buytile", {"x": TILE_X, "z": z}))
                record(label, r)
        t = get("/tiles")
        print("owned tiles", t.get("owned"), "x=-4986 column owned:",
              [c for c in t.get("ownedCentres") or [] if c[0] == TILE_X])
    elif MODE == "streets":
        with paused(WHO):
            for z in STREET_Z:
                for a, b in ((-4600, -4925), (-4925, -5250)):
                    label = f"villa street z={z} x{a}..{b}"
                    if done(label):
                        continue
                    d = show(label + " (dry)", road("Small Road", [[a, z], [b, z]], dryRun=True))
                    if d.get("errors") not in (0, None) or d.get("error"):
                        record(label + " FAILED", d)
                        break
                    r = show(label, road("Small Road", [[a, z], [b, z]]))
                    record(label, r)
    elif MODE == "zone":
        with paused(WHO):
            for z in STREET_Z:
                pts = [[-5250, z - 56], [-4608, z + 56]]
                label = f"zone NA Residential Low {pts}"
                if done(label):
                    continue
                d = show(label + " (dry)", zone("NA Residential Low", mode="rect", points=pts, overwrite=False, dryRun=True))
                if d.get("errors") not in (0, None) or d.get("error"):
                    record(label + " FAILED", d)
                    continue
                r = show(label, zone("NA Residential Low", mode="rect", points=pts, overwrite=False))
                record(label, r)
    elif MODE == "trees":
        ids = sorted(street_edges())
        print("street edges", ids, flush=True)
        with paused(WHO):
            d = show("trees (dry)", post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both", "dryRun": True}))
            if d.get("errors") in (0, None) and not d.get("error"):
                r = show("trees", post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"}))
                record("trees", r)
    elif MODE == "verify":
        es = street_edges()
        for s in sorted(es.values(), key=lambda s: (s["start"][1], s["start"][0])):
            print(s["id"], [round(v) for v in s["start"]], [round(v) for v in s["end"]], s["startDegree"], s["endDegree"])
        m = get("/map", x=-4950, z=640, size=3200, cell=50)
        print(m.get("grid"))
    print("SPENT so far", STATE["spent"])
