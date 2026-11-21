"""builder-northeast - standing list (residential demand 100 at every density): Cove Lane, Pearl Bay.

A curved Small Road with Trees from the post-office stub end (1228,2076) north-west to the x~855 Medium Road
at (862,2275), on the free strip between the bay quarter and the farms (Agriculture Lots start at z 2290),
north of HighSchool03. It gives the stub a second way out. NA Residential Medium on its free frontage via
/zone rect (only road-adjacent blocks get zoned, overwrite off).

Usage: python -I builders/builder-northeast_cove_lane.py dry|build|zone-dry|zone|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short, access_check  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_cove_lane.json"
PTS = [[1228, 2076], [1150, 2170], [1000, 2245], [862, 2275]]
RECT = [[915, 2150], [1240, 2285]]

if MODE in ("dry", "build"):
    body = {"prefab": "Small Road", "points": PTS, "curve": "smooth", "dryRun": MODE == "dry"}
    if MODE == "dry":
        print(json.dumps(short(post("/road", body))))
    else:
        with paused(WHO):
            r = post("/road", body)
            ids = r.get("newEdgeIds") or []
            print(json.dumps(short(r)), ids)
            for i in range(0, len(ids), 4):
                for up in ("Trees", "Grass"):
                    post("/roadupgrade", {"edges": ids[i:i + 4], "upgrade": up, "side": "both"})
            json.dump({"road": ids}, open(LOG, "w"))
elif MODE in ("zone-dry", "zone"):
    body = {"zone": "NA Residential Medium", "mode": "rect", "points": RECT, "dryRun": MODE == "zone-dry"}
    if MODE == "zone":
        with paused(WHO):
            r = post("/zone", body)
    else:
        r = post("/zone", body)
    print(json.dumps({k: r.get(k) for k in ("applied", "cellsChanged", "cellsFromZone", "occupiedCellsRezoned", "errors")}))
elif MODE == "check":
    b = get("/buildings", kind="all", x=1060, z=2210, radius=200)
    bl = [x for x in b.get("buildings", []) if 2140 < x["pos"][1] < 2290 and 900 < x["pos"][0] < 1240 and "Resid" in x["prefab"]]
    print("homes", len(bl))
    print("access", access_check([x["id"] for x in bl]))
    for t in ("Water Notification", "Sewage Notification", "Electricity Notification"):
        p = (get("/problems", type=t).get("problems") or [{}])[0]
        print(t, len([e for e in p.get("examples", []) if 900 < e["pos"][0] < 1240 and 2140 < e["pos"][1] < 2290]))
