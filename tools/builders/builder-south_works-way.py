"""builder-south / jobs (office demand ~100, 2033-01): 'Works Way' office quarter on the two retired OSW landfill
sites south of the z=-1330 street.

Works Way: curved Medium Road with trees+grass from the South gateway freight road (-2004,-1422) east to Quarry Lane
(-1692,-1405) (4-way junction), then on south-east over the old Landfill01 730257:3 site to the Southern leg Large Road
node (-1470,-1750) (builder-east's parkway). It gives the Gateway Business Park a second link to the freight road and
the parkway. Office High both sides (no homes: polluted ground, freight traffic) and on the south side of the z=-1330
street. Dry runs 2033-01-08: no demolition, no conflicts.
Stages: build | zone
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, short, zone

WHO = "builder-south"
WEST = [[-2004, -1422], [-1950, -1450], [-1850, -1454], [-1760, -1434], [-1695, -1405]]
EAST = [[-1695, -1405], [-1600, -1418], [-1510, -1462], [-1462, -1560], [-1470, -1750]]

stage = sys.argv[1] if len(sys.argv) > 1 else ""
if stage == "build":
    with paused(WHO):
        ids = []
        for name, pts in (("west", WEST), ("east", EAST)):
            d = post("/road", {"prefab": "Medium Road", "points": pts, "curve": "smooth", "dryRun": True})
            if d.get("errors") or d.get("wouldDemolish"):
                print(name, "ABORT", d.get("errors"), d.get("problems"), d.get("wouldDemolish"))
                sys.exit(1)
            r = road("Medium Road", pts, curve="smooth")
            ids += r.get("newEdgeIds") or []
            print(name, short(r), r.get("newEdgeIds"))
            time.sleep(1)
        for up in ("Trees", "Grass"):
            u = post("/roadupgrade", {"polyline": WEST + EAST[1:], "tolerance": 14, "prefabFilter": "Medium Road",
                                      "upgrade": up, "side": "both"})
            print(up, u.get("applied"), u.get("errors"), u.get("edgeIdsAfter"))

def edges_by_ends(pts, tol=6):
    """current Medium Road edges whose both ends lie within tol of the course (smooth curve passes through pts)"""
    import math
    segs = get("/nets", x=-1730, z=-1500, radius=360, filter="Medium Road", limit=500)["segments"]

    def near(p):
        return any(math.hypot(p[0] - a[0], p[1] - a[1]) <= tol for a in pts)
    return [s["id"] for s in segs if near(s["start"]) and near(s["end"])]


if stage == "zone":
    with paused(WHO):
        west, east = edges_by_ends(WEST), edges_by_ends(EAST)
        print("edges west", west, "east", east)
        for name, es, dep in (("west", west, 5), ("east", east, 6)):
            z = zone("Office High", mode="road", edges=es, depth=dep, side="both", overwrite=False)
            print(name, z.get("applied"), z.get("cellsChanged"), z.get("occupiedCellsRezoned"))
        z = zone("Office High", mode="rect", points=[[-1950, -1339], [-1420, -1396]], overwrite=False)
        print("z=-1330 south side", z.get("applied"), z.get("cellsChanged"), z.get("occupiedCellsRezoned"))
