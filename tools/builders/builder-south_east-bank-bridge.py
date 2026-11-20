"""builder-south / b1-housing-east-bank-acquire-bridge: the Lake Bridge boulevard to the East Bank.

Tiles (0,-623), (0,-1247), (623,-623), (623,-1247) are bought (separate calls).
- West approach: Large Road (Trees+Grass) from the x=-1140 street node (-1140,-870) east over the empty owned
  land between the water treatment plants (north) and the turbine grid (south) to the west shore ramp.
- Bridge: 'Extradosed Bridge - Large Road Divided - 6 Lanes' across the narrows at z~-900 (water x -430..-10,
  surface 507 m, lakebed down to 445 m), deck ~520 m (13 m above the water), elevations per point = deck - terrain,
  slight S-curve; Large Road ramps on both shores (<= 8%).
- East bridgehead: Large Road on to a medium roundabout at (300,-885); a 100x100 m transit square is kept free
  north-east of it.
Stages: west | bridge | east | trees
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import call, post, paused, road, short

WHO = "builder-south"
DECK = 520.0


def deck_points(xs, zs, grounded=()):
    t = call("POST", "/terrain", {"points": [[float(x), float(z)] for x, z in zip(xs, zs)]})["points"]
    return [[x, z, 0 if i in grounded else round(max(0.0, DECK - t[i]["height"]), 1)] for i, (x, z) in enumerate(zip(xs, zs))]


WEST_APPROACH = [[-1140, -870], [-1000, -876], [-820, -885], [-640, -889]]
WEST_RAMP = deck_points([-640, -560, -450], [-889, -891, -897], grounded=(0,))
SPAN = deck_points([-450, -340, -260, -180, -100, 0], [-897, -904, -908, -909, -907, -902])
EAST_RAMP = deck_points([0, 90, 180], [-902, -898, -894], grounded=(2,))
EAST_APPROACH = [[180, -894], [240, -890], [300, -885]]
BRIDGE = "Extradosed Bridge - Large Road Divided - 6 Lanes"


def build(label, prefab, pts):
    d = post("/road", {"prefab": prefab, "points": pts, "curve": "smooth", "dryRun": True})
    print(f"{label:<16} dry errors={d.get('errors')} {d.get('problems')} demolish={len(d.get('wouldDemolish') or [])} "
          f"snap={[(s.get('snappedTo'), s.get('elevation')) for s in d.get('snapping') or []]}", flush=True)
    if d.get("errors") != 0 or d.get("wouldDemolish"):
        return None
    r = road(prefab, pts, curve="smooth")
    print(f"{label:<16} {short(r)} edges={r.get('newEdgeIds')}", flush=True)
    return r


stage = sys.argv[1] if len(sys.argv) > 1 else ""
if stage == "all":
    with paused(WHO):
        build("west approach", "Large Road", WEST_APPROACH)
        build("west ramp", "Large Road", WEST_RAMP)
        build("bridge span", BRIDGE, SPAN)
        build("east ramp", "Large Road", EAST_RAMP)
        build("east approach", "Large Road", EAST_APPROACH)
        d = post("/roundabout", {"at": EAST_APPROACH[-1], "size": "medium", "dryRun": True})
        print("roundabout dry", d.get("errors"), d.get("error"), (d.get("node") or {}).get("degree"))
