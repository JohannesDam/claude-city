"""builder-east / jobs push 2033-01: Skyway office park north of the Southern Parkway.

The wedge between the parkway and the tile edge z -935 (x 1885..2935) was empty. A tree-lined Medium Road
collector ('Skyway Boulevard') joins the Commons junction XL roundabout (1885,-1115) to the Foundry Gate
roundabout (2935,-1336) through the middle of the wedge (designed with /roadfit, min radius 160 m), and gets
Office High on both sides (office demand 70-100, 56k highly educated citizens underemployed).
Stages: dry | build | zone-dry | zone
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused  # noqa: E402

WHO = "builder-east"
LOG = r"C:\soft\cityskyline\logs\council\builder-east_jobs.json"
PTS = [[1885, -1115], [2010, -1035], [2300, -1060], [2600, -1100], [2860, -1150], [2935, -1336]]
FIT = {"prefab": "Medium Road", "points": PTS, "fit": {"tolerance": 30, "minRadius": 80}}
# polyline of the built course (from the dry run's chosen variant) for edge selection
COURSE = [[1885, -1114.5], [2003.8, -1064.4], [2293.8, -1089.4], [2593.8, -1129.4], [2853.8, -1179.4],
          [2935.1, -1335.6]]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def edges():
    from cc import get
    r = post("/roadupgrade", {"polyline": COURSE, "tolerance": 40, "prefabFilter": "Medium Road",
                              "upgrade": "Trees", "side": "both", "dryRun": True})
    return r


stage = sys.argv[1] if len(sys.argv) > 1 else "dry"
if stage == "dry":
    r = post("/roadfit", {**FIT, "dryRun": True})
    res = r.get("result") or {}
    print(json.dumps({k: res.get(k) for k in ("cost", "errors", "wouldDemolish")}), r.get("chosen", {}).get("variant"))
elif stage == "build":
    with paused(WHO):
        r = post("/roadfit", FIT)
        res = r.get("result") or {}
        out = {k: res.get(k) for k in ("applied", "cost", "errors", "wouldDemolish", "newEdgeIds", "builtEdges")}
        print("road", json.dumps(out)[:900])
        log("skyway_road", out)
        if res.get("applied"):
            for up in ("Trees", "Grass"):
                u = post("/roadupgrade", {"polyline": COURSE, "tolerance": 40, "prefabFilter": "Medium Road",
                                          "upgrade": up, "side": "both"})
                print(up, json.dumps({k: u.get(k) for k in ("applied", "cost", "edgeCount", "errors", "edgeIdsAfter")})[:600])
                if u.get("edgeIdsAfter"):
                    log("skyway_edges", u["edgeIdsAfter"])
elif stage in ("zone-dry", "zone"):
    ids = json.load(open(LOG)).get("skyway_edges")
    body = {"zone": "Office High", "mode": "road", "edges": ids, "depth": 6, "side": "both", "overwrite": False}
    if stage == "zone-dry":
        r = post("/zone", {**body, "dryRun": True})
    else:
        with paused(WHO):
            r = post("/zone", body)
    print(json.dumps({k: r.get(k) for k in ("applied", "cellsChanged", "cellsFromZone", "occupiedCellsRezoned", "errors", "error")}))
    if stage == "zone":
        log("skyway_zone", {k: r.get(k) for k in ("applied", "cellsChanged")})
