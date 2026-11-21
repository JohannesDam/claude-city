"""builder-east / jobs push 2033-01: Foundry Ring - industry in the empty heart of the works east block.

The works east block (x 3960..4640, z -1460..-2040) is ringed by Ore Lane (north/east, Medium) and Works Way
(south-west, Large) with one lot row of industry along them; its ~600 x 500 m interior was empty. Every way in
crosses the unbroken lot row, so the cheapest entry is a 150 m Small Road at x 4258 off Ore Lane that removes two
small IndustrialManufacturing01_L3_2 lots (2 companies). It leads to 'Foundry Ring', a Medium Road circle
(centre (4290,-1735), R 135 m, trees) with Industrial Manufacturing on both sides; the ring's core stays green.
Stages: dry | build | zone-dry | zone
"""
import json
import math
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused  # noqa: E402

WHO = "builder-east"
LOG = r"C:\soft\cityskyline\logs\council\builder-east_jobs.json"
DEMO_LOG = r"C:\soft\cityskyline\logs\council\demolitions.jsonl"
C, R = (4290, -1735), 135


def P(a):
    return [round(C[0] + R * math.cos(math.radians(a)), 1), round(C[1] + R * math.sin(math.radians(a)), 1)]


SPINE = {"prefab": "Small Road", "start": [4258, -1462], "end": P(104), "startDir": [0, -1],
         "fit": {"tolerance": 6, "minRadius": 80}, "allowDemolish": True, "maxCompanies": 2, "maxHouseholds": 0}
ARC_A = [P(a) for a in (104, 45, 0, -45, -90)]
ARC_B = [P(a) for a in (-90, -135, 180, 135, 104)]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def ring_edges():
    ids, seen = [], set()
    r = get("/nets", x=C[0], z=C[1], radius=R + 30, limit=200)
    for e in r["segments"]:
        mid = [(e["start"][0] + e["end"][0]) / 2, (e["start"][1] + e["end"][1]) / 2]
        if e["prefab"] in ("Medium Road", "Small Road") and e["id"] not in seen:
            d0 = math.dist(e["start"], C)
            if (e["prefab"] == "Medium Road" and abs(d0 - R) < 15) or (e["prefab"] == "Small Road" and mid[0] > 4240 and mid[0] < 4275 and mid[1] > -1610):
                ids.append(e["id"]); seen.add(e["id"])
    return ids


stage = sys.argv[1] if len(sys.argv) > 1 else "dry"
if stage == "dry":
    r = post("/roadfit", {**SPINE, "dryRun": True})
    ch = r.get("chosen") or {}
    print("spine", ch.get("variant"), json.dumps(ch.get("demolition"))[:300])
    for nm, pts in (("A", ARC_A), ("B", ARC_B)):
        a = post("/road", {"prefab": "Medium Road", "points": pts, "curve": "smooth", "dryRun": True})
        print(nm, a.get("cost"), a.get("errors"), a.get("wouldDemolish"))
elif stage == "build":
    with paused(WHO):
        r = post("/roadfit", SPINE)
        res = r.get("result") or {}
        ch = r.get("chosen") or {}
        print("spine", json.dumps({k: res.get(k) for k in ("applied", "cost", "errors")}), json.dumps(ch.get("demolition"))[:300])
        if not res.get("applied"):
            sys.exit(1)
        dem = res.get("wouldDemolish") or ch.get("demolition")
        with open(DEMO_LOG, "a") as f:
            f.write(json.dumps({"date": get("/city").get("date"), "builder": WHO, "item": "conductor: jobs push 2033-01",
                                "what": "Foundry Ring entry (Small Road x 4258 off Ore Lane) into the empty works east block",
                                "wouldDemolish": dem}) + "\n")
        for nm, pts in (("A", ARC_A), ("B", ARC_B)):
            a = post("/road", {"prefab": "Medium Road", "points": pts, "curve": "smooth"})
            print(nm, json.dumps({k: a.get(k) for k in ("applied", "cost", "errors", "problems")}))
    time.sleep(1)
    ids = ring_edges()
    print("ring edges", ids)
    with paused(WHO):
        u = post("/roadupgrade", {"edges": [i for i in ids], "upgrade": "Trees", "side": "both"})
        print("trees", json.dumps({k: u.get(k) for k in ("applied", "cost", "errors", "edgeIdsAfter")})[:500])
    log("foundry_ring_edges", ring_edges())
elif stage in ("zone-dry", "zone"):
    ids = ring_edges()
    body = {"zone": "Industrial Manufacturing", "mode": "road", "edges": ids, "depth": 6, "side": "both",
            "overwrite": False}
    if stage == "zone-dry":
        r = post("/zone", {**body, "dryRun": True})
    else:
        with paused(WHO):
            r = post("/zone", body)
        log("foundry_ring_zone", {k: r.get(k) for k in ("applied", "cellsChanged")})
    print(ids, json.dumps({k: r.get(k) for k in ("applied", "cellsChanged", "cellsFromZone", "occupiedCellsRezoned", "errors", "error")}))
