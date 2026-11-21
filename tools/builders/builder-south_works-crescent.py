"""builder-south / jobs (office demand ~100, 2033-01): 'Works Crescent' office collector through the empty interior of
the Old Southern Works plume block (the retired landfill and recycling sites, z -1160..-1280).

Curved Medium Road with trees+grass from the x=-2129 street node (-2129,-1220) (continues the z=-1220 street) west to
the x=-2708 street. Office High on both sides (clean work; no homes on the polluted ground). No demolition: the
industry rows north and south stay until the phase-1 gate. The later phase-1 'Works Boulevard' (north-south, to the
freight road) can cross it with a roundabout. The empty strip z -1050..-940 facing Southgate is left for the woodland.
"""
import math
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, short, zone

WHO = "builder-south"
P = [[-2129, -1220], [-2250, -1196], [-2400, -1204], [-2550, -1238], [-2708, -1228]]


def edges_by_ends(pts, tol=10):
    segs = get("/nets", x=-2420, z=-1215, radius=330, filter="Medium Road", limit=500)["segments"]

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
