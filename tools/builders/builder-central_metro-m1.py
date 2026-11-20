"""builder-central: Metro M1 (worklist b1-mobility-metro-m1), phase A: Southgate - Office Park - Downtown.

Station mechanics (probed 2032-01-07): SubwayStation01 (24x16 lot, 200k) has an underground Double Subway
Track platform centred under the building, parallel to its frontage road, with connection nodes ~100 m
either side. Tunnels are laid with POST /road {prefab:"Double Subway Track", points:[...,[x,z,elev]],
connectTo:{start|end: buildingId}} - connectTo snaps an endpoint to the building's connection node.
SubwayYard02 1642755:3 (Southgate reserve) has its usable connection at the back (-2499,-852).

Phase A stations:
  D-hub  627386:205 at (-1452.5,963)  platform x=-1452.5, z 863..1063 (T2 circle corner, bus 4)
  D-north          at (-1452.5,1763) platform x=-1452.5, z 1663..1863
  Office Park      at (-1708.7,-116.5) platform z=-116.5, x -1809..-1609 (z=-100 road)
  Southgate        at (-2532.7,-604)  platform along the South Ring (~29 deg)

Run: cd C:/soft/cityskyline/tools && python -I builders/builder-central_metro-m1.py <stage> [--dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short, building  # noqa: E402

WHO = "builder-central"
DRY = "--dry" in sys.argv
STAGE = next((a for a in sys.argv[1:] if not a.startswith("--")), "")
PF = "Double Subway Track"
HUB = "627386:205"
STATIONS = {
    "dnorth": ("SubwayStation01", {"x": -1452.5, "z": 1763.21, "angle": -90}),
    "office": ("SubwayStation01", {"x": -1708.7, "z": -116.5, "angle": 0}),
    "southgate": ("SubwayStation01", {"x": -2532.72, "z": -604.05, "angle": 150.6}),
}


def place(key):
    prefab, b = STATIONS[key]
    with paused(WHO):
        d = post("/building", {"prefab": prefab, **b, "dryRun": True})
        print(key, "dry", short(d))
        if DRY or d.get("errors") or d.get("wouldDemolish"):
            return None
        r = building(prefab, **b)
        print(key, short(r))
        return r.get("placedId")


def tunnel(label, points, connect):
    with paused(WHO):
        body = {"prefab": PF, "points": points, "curve": "smooth", "connectTo": connect}
        d = post("/road", {**body, "dryRun": True})
        g = d.get("geometry") or {}
        print(label, "dry", short(d, keys=("errors", "problems", "cost", "wouldDemolish", "connected")),
              "len", g.get("length"), "minR", g.get("minRadius"))
        if DRY or d.get("errors") or d.get("wouldDemolish"):
            return d
        r = post("/road", body)
        print(label, short(r, keys=("applied", "cost", "errors", "connected", "newEdgeIds")))
        return r


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if STAGE == "place":
        place(args[1])
    elif STAGE == "tunnel":
        spec = json.loads(args[1])
        tunnel(spec["label"], spec["points"], spec["connectTo"])
