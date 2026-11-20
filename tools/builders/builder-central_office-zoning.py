"""builder-central: office zoning push (conductor 2032-01: office demand 50, companyDemand 990, 0 free office lots).

1. Depot Road east edge (x -1975..-1900, z=-420): Office High on both sides, 150 m from the T1 Office Park
   terminus stop (-1909,-300). Empty, unzoned cells only.
2. "Office Park Lane": the 210 x 175 m landlocked interior of the block x -1470..-1140, z -100..205 gets a
   quiet curved lane - Alley entries through the only gaps between existing office lots (west at z=138 off
   the x=-1470 road, east at z=9 off the x=-1140 Medium Road; any wider road demolishes offices), and a
   curved tree-lined Small Road between them. Office Low on both sides, a pocket green at the bend.
   No demolition (every piece dry-run clean).

Run: cd C:/soft/cityskyline/tools && python -I builders/builder-central_office-zoning.py depot|lane|lanezone [--dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short, road, find_spot, building  # noqa: E402

WHO = "builder-central"
DRY = "--dry" in sys.argv
STAGE = next((a for a in sys.argv[1:] if not a.startswith("--")), "depot")

LANE = [
    ("Alley", [[-1469, 138], [-1400, 138]], None),
    ("Alley", [[-1400, 138], [-1385, 114]], None),
    ("Small Road", [[-1385, 114], [-1340, 92], [-1300, 62], [-1275, 30], [-1262, 10]], "smooth"),
    ("Alley", [[-1262, 10], [-1200, 7.5], [-1140, 9.3]], "smooth"),
]
LANE_CURVE = LANE[2][1]


def edges_along(points, prefab, tol=10):
    """Edge ids of `prefab` whose midpoint lies within tol of the polyline through points."""
    xs = [p[0] for p in points]
    zs = [p[1] for p in points]
    cx, cz = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2
    r = max(max(xs) - min(xs), max(zs) - min(zs)) / 2 + 40
    out = []
    for s in get("/nets", x=cx, z=cz, radius=r, filter=prefab).get("segments") or []:
        if s["prefab"] != prefab:
            continue
        mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
        best = 1e9
        for (ax, az), (bx, bz) in zip(points, points[1:]):
            dx, dz = bx - ax, bz - az
            t = max(0, min(1, ((mx - ax) * dx + (mz - az) * dz) / (dx * dx + dz * dz)))
            best = min(best, ((ax + t * dx - mx) ** 2 + (az + t * dz - mz) ** 2) ** 0.5)
        if best <= tol:
            out.append(s["id"])
    return out


def stage_depot():
    ids = edges_along([[-1975, -420], [-1900, -420]], "Medium Road", tol=4)
    print("depot road edges", ids)
    with paused(WHO):
        z = post("/zone", {"zone": "Office High", "mode": "road", "edges": ids, "depth": 6, "side": "both",
                           "overwrite": False, "dryRun": DRY})
        print("Office High", short(z, keys=("applied", "errors", "cellsChanged", "cellsFromZone", "occupiedCellsRezoned", "error")))


def stage_lane():
    with paused(WHO):
        for prefab, pts, curve in LANE:
            body = {"prefab": prefab, "points": pts, "dryRun": True}
            if curve:
                body["curve"] = curve
            d = post("/road", body)
            if d.get("errors") or d.get("wouldDemolish"):
                print("STOP", prefab, pts, short(d))
                return
            if DRY:
                print("dry ok", prefab, d.get("cost"))
                continue
            body.pop("dryRun")
            r = post("/road", body)
            print(prefab, short(r, keys=("applied", "cost", "errors", "newEdgeIds")), flush=True)
        if not DRY:
            u = post("/roadupgrade", {"polyline": LANE_CURVE, "tolerance": 10, "prefabFilter": "Small Road", "upgrade": "Trees",
                                      "side": "both"})
            print("lane trees", short(u, keys=("applied", "cost", "errors")))


def stage_lanezone():
    ids = edges_along(LANE_CURVE, "Small Road", tol=12)
    print("lane edges", ids)
    with paused(WHO):
        z = post("/zone", {"zone": "Office Low", "mode": "road", "edges": ids, "depth": 6, "side": "both",
                           "overwrite": False, "dryRun": DRY})
        print("Office Low", short(z, keys=("applied", "errors", "cellsChanged", "cellsFromZone", "occupiedCellsRezoned", "error")))
        if DRY:
            return
        for c in find_spot("CityPark06", -1318, 92, radius=50, n=4):
            b = dict(c["buildingXZ"])
            b.pop("prefab", None)
            d = post("/building", {"prefab": "CityPark06", **b, "dryRun": True})
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                print("lane green", short(building("CityPark06", **b)))
                break


if __name__ == "__main__":
    {"depot": stage_depot, "lane": stage_lane, "lanezone": stage_lanezone}[STAGE]()
