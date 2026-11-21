"""builder-northeast - jobs push (conductor 2033-01: unemployment, office demand ~100, industrial 61).

Office High on University Hill next to the universities and Science Ridge:
- Research Way (Medium, trees) from the z=4250 campus road at x -1060 south to z 3575, Research Link (Small) west
  to the x=-1249 road at z 3700; Ridge Way (Small) from z=4250 to Medical Drive at x -1300.
- Office High on both sides of these roads and on the free road frontage of University Hill.
Industrial Manufacturing on free road frontage downwind: Port Claude (Harbour Road north side, Riverside edge),
Riverside, Eastport.

Usage: python -I builders/builder-northeast_jobs.py roads|zone-dry|zone
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "zone-dry"
ROADS = {
    "research way": {"prefab": "Medium Road", "curve": "smooth",
                     "points": [[-1060, 4250], [-1056, 4100], [-1058, 3950], [-1062, 3700], [-1066, 3575]]},
    "research link": {"prefab": "Small Road", "points": [[-1062, 3700], [-1160, 3702], [-1264, 3700]]},
    "ridge way": {"prefab": "Small Road", "curve": "smooth", "points": [[-1300, 4250], [-1305, 4380], [-1300, 4502]]},
}
RECTS = [
    ("Office High", [[-1404, 4264], [-1220, 4488]]),
    ("Office High", [[-1460, 3856], [-1308, 4040]]),
    ("Office High", [[-1240, 3544], [-972, 4240]]),
    ("Office High", [[-2180, 3712], [-2084, 4232]]),
    ("Industrial Manufacturing", [[2010, 4008], [2354, 4216]]),
    ("Industrial Manufacturing", [[1666, 3416], [2034, 3552]]),
    ("Industrial Manufacturing", [[2130, 3200], [2298, 3352]]),
    ("Industrial Manufacturing", [[1592, 2946], [1784, 3034]]),
    ("Industrial Manufacturing", [[2676, 2220], [2828, 2804]]),
    ("Industrial Manufacturing", [[3188, 2188], [3420, 2372]]),
    ("Industrial Manufacturing", [[1544, 2810], [2008, 2858]]),
]

if __name__ == "__main__":
    if MODE == "roads":
        with paused(WHO):
            for k, b in ROADS.items():
                r = post("/road", b)
                print(k, short(r), flush=True)
                for up in ("Trees", "Grass"):
                    u = post("/roadupgrade", {"polyline": b["points"], "tolerance": 15, "prefabFilter": b["prefab"],
                                              "upgrade": up, "side": "both"})
                    print("  ", up, u.get("applied"), u.get("errors"))
    elif MODE in ("zone-dry", "zone"):
        dry = MODE == "zone-dry"
        total = {}

        def run():
            for zname, rect in RECTS:
                r = post("/zone", {"zone": zname, "mode": "rect", "points": rect, "dryRun": dry})
                total[zname] = total.get(zname, 0) + (r.get("cellsChanged") or 0)
                print(zname, rect, r.get("cellsChanged"), r.get("occupiedCellsRezoned"), r.get("errors"), flush=True)
        if dry:
            run()
        else:
            with paused(WHO):
                run()
        print(total)
