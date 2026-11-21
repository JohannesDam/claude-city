"""builder-south / jobs (office demand ~100, 2033-01): office collector through the empty Health Campus block
x -1600..-1150, z -630..-430 (between the z=-420 Depot Road street and the z=-700 street).

Curved Medium Road with trees+grass from the x=-1140 street (-1140,-545) west and then north to the z=-420 node
(-1469,-420), continuing the x=-1469 street from the Office Park into the campus (a 4-way there). An end at (-1580,-420)
was refused: WindTurbine01 617147:1 (-1600,-440) stands in the way. Office High both sides and on the south side of the
z=-420 street. No demolition.
"""
import math
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, short, zone

WHO = "builder-south"
P = [[-1140, -545], [-1290, -556], [-1400, -542], [-1456, -492], [-1469, -420]]


def edges_by_ends(pts, tol=10):
    segs = get("/nets", x=-1300, z=-500, radius=260, filter="Medium Road", limit=500)["segments"]

    def near(p):
        return any(math.hypot(p[0] - a[0], p[1] - a[1]) <= tol for a in pts)
    return [s["id"] for s in segs if near(s["start"]) and near(s["end"])]


with paused(WHO):
    d = post("/road", {"prefab": "Medium Road", "points": P, "curve": "smooth", "dryRun": True})
    if d.get("errors") or d.get("wouldDemolish"):
        print("ABORT", d.get("errors"), d.get("problems"), d.get("wouldDemolish"))
        sys.exit(1)
    r = road("Medium Road", P, curve="smooth")
    print("road", short(r), r.get("newEdgeIds"))
    time.sleep(1)
    for up in ("Trees", "Grass"):
        u = post("/roadupgrade", {"polyline": P, "tolerance": 32, "prefabFilter": "Medium Road", "upgrade": up, "side": "both"})
        print(up, u.get("applied"), u.get("edgeIdsAfter"))
    es = edges_by_ends(P)
    print("edges", es)
    z = zone("Office High", mode="road", edges=es, depth=6, side="both", overwrite=False)
    print("office high", z.get("applied"), z.get("cellsChanged"), z.get("occupiedCellsRezoned"))
    z = zone("Office High", mode="rect", points=[[-1595, -429], [-1150, -472]], overwrite=False)
    print("z=-420 south side", z.get("applied"), z.get("cellsChanged"), z.get("occupiedCellsRezoned"))
