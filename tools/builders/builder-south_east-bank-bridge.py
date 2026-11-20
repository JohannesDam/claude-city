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


WEST_TIE = [[-1140, -870], [-1070, -873]]  # Medium: a Large junction at the x=-1140 node would clip an office
WEST_APPROACH = [[-1070, -873], [-1000, -876], [-820, -885], [-640, -889]]
WEST_RAMP = deck_points([-640, -560, -450], [-889, -891, -897], grounded=(0,))
# span control points ~90 m apart (shorter bridge segments fail with 'Short Distance'); every point sits at the
# same absolute deck height and a segment is a 3D curve between its end nodes, so the deck stays level at ~520 m
SPAN_X = [-450, -360, -270, -180, -90, 0]
SPAN_Z = [-897, -903, -907, -909, -906, -902]
SPAN = deck_points(SPAN_X, SPAN_Z)
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


# bridgehead gateway: the boulevard continues east (town spine) and a shore drive crosses it -> roundabout
SPINE_STUB = [[300, -885], [360, -882], [420, -880]]
SHORE_NORTH = [[300, -885], [325, -820], [370, -765]]
SHORE_SOUTH = [[300, -885], [287, -945], [265, -1000]]
PARTS = [("west tie", "Medium Road", WEST_TIE), ("west approach", "Large Road", WEST_APPROACH), ("west ramp", "Large Road", WEST_RAMP),
         ("bridge span", BRIDGE, SPAN), ("east ramp", "Large Road", EAST_RAMP), ("east approach", "Large Road", EAST_APPROACH),
         ("town spine stub", "Large Road", SPINE_STUB), ("shore drive N", "Medium Road", SHORE_NORTH),
         ("shore drive S", "Medium Road", SHORE_SOUTH)]

stage = sys.argv[1] if len(sys.argv) > 1 else ""
if stage == "dry":
    # every part on its own (later parts snap to the earlier ones only once those exist), plus a clearance check
    t = call("POST", "/terrain", {"points": [[float(p[0]), float(p[1])] for p in SPAN]})["points"]
    for p, h in zip(SPAN, t):
        print(f"  span point {p}: terrain {h['height']:.1f}, water depth {h['waterDepth']:.1f}, deck {h['height'] + p[2]:.1f} m")
    ends = [(WEST_APPROACH[-1], WEST_RAMP[0]), (WEST_RAMP[-1], SPAN[0]), (SPAN[-1], EAST_RAMP[0]), (EAST_RAMP[-1], EAST_APPROACH[0])]
    print("  joints match:", all(a[:2] == b[:2] and (len(a) < 3 or len(b) < 3 or a[2] == b[2]) for a, b in ends))
    for label, prefab, pts in PARTS:
        d = post("/road", {"prefab": prefab, "points": pts, "curve": "smooth", "dryRun": True})
        g = d.get("geometry") or {}
        print(f"{label:<14} errors={d.get('errors')} problems={d.get('problems')} warnings={g.get('warnings')} "
              f"minR={g.get('minRadius')} demolish={len(d.get('wouldDemolish') or [])} cost={d.get('cost')}")
    d = post("/roundabout", {"at": EAST_APPROACH[-1], "size": "medium", "dryRun": True})
    print("roundabout (needs the approach first):", d.get("errors"), d.get("error"))

if stage == "all":
    with paused(WHO):
        for label, prefab, pts in PARTS:
            if build(label, prefab, pts) is None:
                sys.exit(f"stopped at {label}")
        d = post("/roundabout", {"at": EAST_APPROACH[-1], "size": "medium", "dryRun": True})
        print("roundabout dry", d.get("errors"), d.get("error"), d.get("wouldDemolish"))
        if d.get("errors") == 0 and not d.get("wouldDemolish"):
            print("roundabout", short(post("/roundabout", {"at": EAST_APPROACH[-1], "size": "medium"})))
        box = [-1145, -1010, 430, -760]
        for prefab, ups in (("Large Road", ("Trees", "Grass")), ("Medium Road", ("Trees",))):
            for up in ups:
                u = post("/roadupgrade", {"box": box, "prefabFilter": prefab, "upgrade": up, "side": "both"})
                print(prefab, up, u.get("applied"), u.get("cost"), u.get("errors"))
