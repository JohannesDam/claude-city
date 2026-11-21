"""builder-central: Office High for the 2033 jobs push (office demand ~100, unemployment the top penalty).

Stage depot-road: the north side of the Depot Road (z=-420) x -1900..-1362 was unzoned free land between the
Office Park blocks and the road (opposite the third tram depot, 300-500 m from the T1 and M1 Office Park stops).
Zone Office High, depth 6, no overwrite (only unzoned cells change).

Stage lane: "Highway Lane", a curved Small Road cul-de-sac (Trees) from the x=-900 street node (-900,1665) west
into the free land between the highway and the Lakeshore homes (x -1080..-935, z 1620..1775), Office High on both
sides (highway noise: offices, not homes). Removes 2 low homes at the junction.

Stage waterfront: the two unzoned blocks between the z=900 and z=1010 streets in Waterfront: Office High west of
x=-900 (next to the highway gate), Residential Medium east of it (lake side, Waterfront homes); Trees on the
frontage streets. No overwrite.

usage: python -I builders/builder-central_office-high-2033.py depot-road|lane|waterfront [dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused  # noqa: E402

WHO = "builder-central"
STAGE = sys.argv[1] if len(sys.argv) > 1 else "depot-road"
DRY = "dry" in sys.argv[2:]


def depot_road_edges():
    r = get("/nets", x=-1630, z=-420, radius=280, limit=200)
    return [s["id"] for s in r["segments"]
            if abs(s["start"][1] + 420) < 3 and abs(s["end"][1] + 420) < 3 and min(s["start"][0], s["end"][0]) >= -1905
            and max(s["start"][0], s["end"][0]) <= -1360 and s["start"][0] > s["end"][0]]  # all run west: right = north


if STAGE == "depot-road":
    ids = depot_road_edges()
    print("edges", ids)
    with paused(WHO):
        z = post("/zone", {"zone": "Office High", "mode": "road", "edges": ids, "depth": 6, "side": "right", "dryRun": DRY})
    print(json.dumps(z)[:700])

LANE = [[-900, 1665], [-940, 1665], [-990, 1690], [-1060, 1690]]
if STAGE == "lane":
    with paused(WHO):
        d = post("/road", {"prefab": "Small Road", "points": LANE, "curve": "smooth", "dryRun": True, "allowDemolish": True})
        wd = d.get("wouldDemolish") or []
        print("lane dry", d.get("errors"), [(w["prefab"], w["center"]) for w in wd])
        if d.get("errors") or len(wd) > 2 or any("Residential" not in w["prefab"] for w in wd) or DRY:
            sys.exit(0)
        r = post("/road", {"prefab": "Small Road", "points": LANE, "curve": "smooth", "allowDemolish": True})
        ids = r.get("newEdgeIds") or []
        print("lane", r.get("applied"), r.get("cost"), ids)
        u = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"})
        ids = u.get("edgeIdsAfter") or ids
        print("trees", u.get("applied"), u.get("cost"))
        z = post("/zone", {"zone": "Office High", "mode": "road", "edges": ids, "depth": 6, "side": "both"})
        print("zone", json.dumps({k: z.get(k) for k in ("applied", "cellsChanged", "cellsFromZone", "occupiedCellsRezoned")}))

if STAGE == "waterfront":
    with paused(WHO):
        for zn, rect in (("Office High", [[-1056, 904], [-904, 1006]]), ("NA Residential Medium", [[-896, 904], [-722, 1006]])):
            z = post("/zone", {"zone": zn, "mode": "rect", "points": rect, "dryRun": DRY})
            print(zn, json.dumps({k: z.get(k) for k in ("applied", "cellsChanged", "cellsFromZone", "occupiedCellsRezoned")}))
        for line in ([[-1054, 900], [-720, 900]], [[-1070, 1010], [-720, 1010]], [[-900, 900], [-900, 1010]]):
            u = post("/roadupgrade", {"polyline": line, "tolerance": 6, "prefabFilter": "Small Road", "upgrade": "Trees",
                                      "side": "both", "dryRun": DRY})
            print("trees", line, u.get("applied"), u.get("cost"), u.get("errors"))
