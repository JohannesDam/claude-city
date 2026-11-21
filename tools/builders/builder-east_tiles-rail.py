"""builder-east / Southern Rail: buy tiles (623,-1870) and (1247,-1870) for the freight line and East Gate's cargo
terminal, then redraw the in-game district 'East Gate' (delete + draw) to include them.
Stages: buy | redraw
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused  # noqa: E402

WHO = "builder-east"
TILES = [(623, -1870), (1247, -1870)]
POLY = [[945, 302], [1548, 302], [1548, -945], [3438, -945], [3438, 302], [5288, 302], [5288, -1548], [4665, -1548],
        [4665, -2171], [2815, -2171], [2815, -1548], [1548, -1548], [1548, -2171], [321, -2171], [321, -1568],
        [945, -1568]]

stage = sys.argv[1] if len(sys.argv) > 1 else ""
if stage == "buy":
    with paused(WHO):
        for x, z in TILES:
            r = post("/buytile", {"x": x, "z": z})
            print("tile", x, z, json.dumps({k: r.get(k) for k in ("applied", "cost", "price", "error", "upkeepIncrease")}))
elif stage == "redraw":
    d = get("/districts")
    old = [x for x in d.get("districts", []) if x.get("name") == "East Gate"]
    print("old", [(x["id"], x.get("bbox")) for x in old])
    # the full polygon overlaps the old district until it is deleted: dry-run the added part only
    dry = post("/area", {"prefab": "district", "name": "East Gate (new part)", "dryRun": True,
                         "points": [[1538, -1558], [1538, -2171], [321, -2171], [321, -1568]]})
    print("dry", json.dumps({k: dry.get(k) for k in ("errors", "problems", "error")}))
    if dry.get("errors") == 0 and old:
        with paused(WHO):
            dl = post("/area/delete", {"id": old[0]["id"]})
            print("delete", json.dumps({k: dl.get(k) for k in ("applied", "error")}))
            r = post("/area", {"prefab": "district", "name": "East Gate", "points": POLY})
            print("draw", json.dumps({k: r.get(k) for k in ("applied", "errors", "problems", "error", "id", "areaId")}))
