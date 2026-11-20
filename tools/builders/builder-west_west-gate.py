"""builder-west, item b2-renewal-westside-p1-west-gate - Westside renewal phase 1 "West Gate".

Survey 2032-01-07: the box x -2900..-2510, z 760..1240 holds 129 buildings and 922 households, but 824 of them live
in the 16 level-5 Mixed buildings on both sides of the x=-2569 street. Those stay, with their street. The renewal
core is the low-density grid west of them, x -2862..-2605: 99 single-family homes (1 household each) and 12 gas
stations. The z=900 arterial (Medium Road with Trees and Grass), the z=760 greenway and the z=1230 street stay.

North core (z 905..1225) - "West Gate Circus":
  - the inner grid (x=-2789 between z 900 and 1230, z=1010 and z=1120 west of x=-2642) is removed;
  - an oval ring (Medium Road, Trees and Grass, tram-ready), centre (-2731.8,1065), semi-axes 100 (E-W) and
    125 (N-S); it passes through the kept z=1010/z=1120 stub nodes at x=-2642;
  - two Medium links from the arterial nodes (-2789,900) and (-2684,900) to the ring form the tram turning loop;
    Small links west to the kept stubs at x=-2862 and north to the z=1230 street;
  - inside the ring: West Gate Green (CityPark11), Playground02, BicycleParkingHall01;
  - zoning: High on both sides of the ring, Mixed on the arterial frontage, Medium Row on the z=1230 frontage,
    LowRent on the west links.
South core (z 765..895) - "Gate Crescent": a horseshoe Small Road with Trees from the greenway, replacing the
x=-2789 street south of the arterial; Mixed on the arterial frontage, Medium/Medium Row on the crescent.
Every stage clears, rebuilds and rezones inside one paused block (freed lots regrow within seconds).
Usage: python -I builders/builder-west_west-gate.py plan|north|south [dry]
"""
import json
import math
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, demolition_cost, find_spot, get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
ITEM = "b2-renewal-westside-p1-west-gate"
DRY = "dry" in sys.argv[2:]
LOG = r"C:\soft\cityskyline\logs\council\demolitions.jsonl"
WS = [-4020, -300, -2510, 2180]

CX, CZ, A, B = -2731.8, 1065.0, 100.0, 125.0


def ring_pt(deg):
    t = math.radians(deg)
    return [round(CX + A * math.cos(t), 1), round(CZ + B * math.sin(t), 1)]


def t_at_z(z, west):
    s = math.degrees(math.asin((z - CZ) / B))
    return (180 - s) if west else (s if s >= 0 else 360 + s)


def t_at_x(x, north):
    c = math.degrees(math.acos((x - CX) / A))
    return c if north else 360 - c


# north arc W apex -> top -> E apex, south arc W apex -> bottom -> E apex (points become nodes)
NORTH_ARC = [ring_pt(t) for t in (180, t_at_z(1120, True), 125, 90, 55, t_at_z(1120, False), 0)]
SOUTH_ARC = [ring_pt(t) for t in (180, t_at_z(1010, True), t_at_x(-2789, False), 270, t_at_x(-2684, False),
                                   t_at_z(1010, False), 360)]
P_W1, P_W2 = ring_pt(t_at_z(1010, True)), ring_pt(t_at_z(1120, True))
P_S1, P_S2 = ring_pt(t_at_x(-2789, False)), ring_pt(t_at_x(-2684, False))
P_N = ring_pt(90)
LINKS = [
    ("Medium Road", "Gate Link West (tram)", [[-2789, 900], P_S1]),
    ("Medium Road", "Gate Link East (tram)", [[-2684, 900], P_S2]),
    ("Small Road", "West Gate Lane 1010", [[-2862, 1010], P_W1]),
    ("Small Road", "West Gate Lane 1120", [[-2862, 1120], P_W2]),
    ("Small Road", "Gate Walk North", [P_N, [P_N[0], 1230]]),
]
NORTH_BOX = [-2865, 905, -2606, 1225]          # bulldoze box (building positions / edge midpoints)

# south core: horseshoe from the greenway (z=760), apex below the arterial
SCX, SA, SB = -2735.0, 105.0, 88.0
GATE_CRESCENT = [[round(SCX + SA * math.cos(math.radians(t)), 1), round(760 + SB * math.sin(math.radians(t)), 1)]
                 for t in (180, 150, 120, 90, 60, 30, 0)]
SOUTH_BOX = [-2862, 765, -2606, 895]


def log(what, would):
    if not would:
        return
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": WHO, "prefab": what, "item": ITEM,
                            "ids": [w.get("id") for w in would], "cost": demolition_cost(would),
                            "note": "approved renewal box x -2900..-2510 z 760..1240; rebuilt and rezoned in the same lock"}) + "\n")


def build_road(prefab, name, pts, curve=True, dry=False):
    body = {"prefab": prefab, "points": pts, "allowDemolish": True}
    if curve:
        body["curve"] = "smooth"
    d = post("/road", {**body, "dryRun": True})
    cost = demolition_cost(d.get("wouldDemolish"))
    g = d.get("geometry") or {}
    print(f"{name} dry: {short(d, ('errors', 'cost', 'problems'))} len {g.get('length')} minR {g.get('minRadius')} "
          f"warn {g.get('warnings')} hh {cost['households']} co {cost['companies']}", flush=True)
    if dry or d.get("errors"):
        return None
    r = post("/road", body)
    print(f"{name}: {short(r, ('applied', 'cost', 'errors', 'problems'))}", flush=True)
    log(f"{prefab} ({name})", d.get("wouldDemolish"))
    return r


def beautify(pts, prefab, upgrades=("Trees",), tol=14):
    for upg in upgrades:
        t = post("/roadupgrade", {"polyline": pts, "tolerance": tol, "prefabFilter": prefab, "upgrade": upg, "side": "both"})
        print(f"  {upg} {prefab}: {short(t, ('applied', 'cost', 'errors'))}", flush=True)


def place(prefab, box, near, radius=200):
    for c in find_spot(prefab, near[0], near[1], radius=radius, n=40, within=WS):
        if box[0] <= c["x"] <= box[1] and box[2] <= c["z"] <= box[3]:
            b = dict(c["building"])
            b.pop("prefab", None)
            d = post("/building", {"prefab": prefab, **b, "dryRun": True})
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                r = building(prefab, **b)
                print(f"  {prefab}: {short(r, ('applied', 'cost', 'placedId', 'roadConnected'))} at ({c['x']}, {c['z']})", flush=True)
                return r.get("placedId")
    print(f"  {prefab}: no clean spot in {box}", flush=True)
    return None


def segs_near(x, z, radius, prefabs=("Medium Road", "Small Road")):
    out = {}
    off = 0
    while True:
        r = get("/nets", x=x, z=z, radius=radius, offset=off)
        for s in r.get("segments") or []:
            if s.get("prefab") in prefabs:
                out[s["id"]] = s
        if not r.get("truncated") or not r.get("returned"):
            break
        off += r["returned"]
    return list(out.values())


def inside_ring(x, z, margin=0.0):
    return ((x - CX) / (A + margin)) ** 2 + ((z - CZ) / (B + margin)) ** 2 <= 1.0


def on_ring(s):
    mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
    q = ((mx - CX) / A) ** 2 + ((mz - CZ) / B) ** 2
    return s.get("prefab") == "Medium Road" and 0.8 < q < 1.15


def ring_side(s, inner):
    """'left'/'right' side of the edge that faces the ring centre (inner) or away from it."""
    (sx, sz), (ex, ez) = s["start"], s["end"]
    dx, dz = ex - sx, ez - sz
    mx, mz = (sx + ex) / 2, (sz + ez) / 2
    right_is_centre = dz * (CX - mx) - dx * (CZ - mz) > 0
    if inner:
        return "right" if right_is_centre else "left"
    return "left" if right_is_centre else "right"


def zone_rect(name, box, overwrite):
    z = zone(name, mode="rect", points=[[box[0], box[1]], [box[2], box[3]]], overwrite=overwrite)
    print(f"  rect {name:24} {box} {short(z, ('applied', 'cellsChanged', 'occupiedCellsRezoned', 'errors'))}", flush=True)
    return z.get("cellsChanged") or 0


PER_CELL = {"NA Residential High": 4.0, "NA Residential Mixed": 2.5, "NA Residential Medium": 2.0,
            "NA Residential Medium Row": 1.3, "Residential LowRent": 3.0}


def stage_plan():
    print("north arc", NORTH_ARC)
    print("south arc", SOUTH_ARC)
    print("links", LINKS)
    print("gate crescent", GATE_CRESCENT)
    for box in (NORTH_BOX, SOUTH_BOX):
        r = post("/bulldoze", {"box": box, "buildings": True, "dryRun": True})
        b = r.get("buildings") or {}
        print("buildings", box, {k: b.get(k) for k in ("count", "byKind", "households", "companies", "workplaces")})
        svc = [x for x in b.get("list") or [] if x.get("kind", "").startswith("service")]
        print("  services:", [(x["id"], x["prefab"], x["pos"]) for x in svc])
        r = post("/bulldoze", {"box": box, "nets": "Small Road", "dryRun": True})
        print("edges", box, [(e["id"], e["start"], e["end"]) for e in r.get("edges") or []])
    build_road("Medium Road", "ring north arc", NORTH_ARC, dry=True)
    build_road("Medium Road", "ring south arc", SOUTH_ARC, dry=True)
    for prefab, name, pts in LINKS:
        build_road(prefab, name, pts, curve=False, dry=True)
    build_road("Small Road", "Gate Crescent", GATE_CRESCENT, dry=True)


def stage_north():
    cap = 0.0
    out = {}
    with paused(WHO):
        bd = post("/bulldoze", {"box": NORTH_BOX, "buildings": True, "dryRun": True})
        b = bd.get("buildings") or {}
        print("north core buildings:", b.get("count"), "hh", b.get("households"), "companies", b.get("companies"), flush=True)
        if DRY or (b.get("households") or 0) > 300 or any(x.get("kind", "").startswith("service") for x in b.get("list") or []):
            print("STOP (dry run or outside limits)", flush=True)
            return
        r = post("/bulldoze", {"box": NORTH_BOX, "buildings": True})
        print("buildings removed:", short(r, ("applied", "errors")), flush=True)
        log("West Gate north core clearance (box)", b.get("list"))
        r = post("/bulldoze", {"box": NORTH_BOX, "nets": "Small Road"})
        print("old streets removed:", short(r, ("applied", "errors", "edgeCount")), flush=True)
        build_road("Medium Road", "ring north arc", NORTH_ARC)
        build_road("Medium Road", "ring south arc", SOUTH_ARC)
        for prefab, name, pts in LINKS:
            build_road(prefab, name, pts, curve=False)
        time.sleep(1)
        ring_pts = NORTH_ARC + SOUTH_ARC[::-1]
        beautify(ring_pts, "Medium Road", ("Trees", "Grass"))
        for prefab, name, pts in LINKS:
            beautify(pts, prefab, ("Trees", "Grass") if prefab == "Medium Road" else ("Trees",), tol=8)
        # services inside the ring before any zoning
        inner = (CX - A + 15, CX + A - 15, CZ - B + 15, CZ + B - 15)
        out["green"] = place("CityPark11", inner, (CX, CZ + 30))
        out["playground"] = place("Playground02", inner, (CX, CZ - 70))
        out["bikehall"] = place("BicycleParkingHall01", inner, (CX - 60, CZ - 40))
        # zoning: old blocks of the kept roads inside the core first (their buildings are gone), then the new roads
        cells = {}
        for name, box in (("NA Residential Mixed", [-2860, 906, -2606, 952]),
                          ("NA Residential Medium Row", [-2860, 1182, -2606, 1222]),
                          ("NA Residential High", [-2660, 952, -2606, 1182])):
            cells[name] = cells.get(name, 0) + zone_rect(name, box, overwrite=True)
        for s in segs_near(CX, CZ, 200):
            mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
            jobs = []
            if on_ring(s):
                jobs = [("NA Residential High", ring_side(s, True), 4), ("NA Residential High", ring_side(s, False), 4)]
            elif s["prefab"] == "Medium Road" and 905 < mz < 965 and -2795 < mx < -2678:
                jobs = [("NA Residential Mixed", "both", 3)]                     # tram links
            elif s["prefab"] == "Small Road" and -2866 < mx < -2815 and mz in (1010, 1120):
                jobs = [("Residential LowRent", "both", 3)]                      # west links
            elif s["prefab"] == "Small Road" and abs(mx - CX) < 3 and 1188 < mz < 1232:
                jobs = [("NA Residential Medium Row", "both", 3)]                # north walk
            for name, side, depth in jobs:
                z = zone(name, mode="road", edges=[s["id"]], depth=depth, side=side, overwrite=False)
                cells[name] = cells.get(name, 0) + (z.get("cellsChanged") or 0)
        cap = sum(n * PER_CELL[k] for k, n in cells.items())
        print("zoned cells:", cells, flush=True)
    print(json.dumps({"services": out, "capacityHouseholds": round(cap)}))


def stage_south():
    cap = 0.0
    out = {}
    with paused(WHO):
        bd = post("/bulldoze", {"box": SOUTH_BOX, "buildings": True, "dryRun": True})
        b = bd.get("buildings") or {}
        lst = [x for x in b.get("list") or [] if not x.get("kind", "").startswith("service")]
        svc = [x for x in b.get("list") or [] if x.get("kind", "").startswith("service")]
        print("south core buildings:", b.get("count"), "hh", b.get("households"), "companies", b.get("companies"),
              "services", [(x["id"], x["prefab"]) for x in svc], flush=True)
        if DRY or (b.get("households") or 0) > 300:
            print("STOP (dry run or outside limits)", flush=True)
            return
        shelters = [x["id"] for x in svc if x["prefab"] == "EmergencyShelter01"]
        r = post("/bulldoze", {"ids": [x["id"] for x in lst] + shelters})
        print("buildings removed:", short(r, ("applied", "errors")), flush=True)
        log("West Gate south core clearance", lst)
        r = post("/bulldoze", {"box": SOUTH_BOX, "nets": "Small Road"})
        print("old streets removed:", short(r, ("applied", "errors", "edgeCount")), flush=True)
        build_road("Small Road", "Gate Crescent", GATE_CRESCENT)
        time.sleep(1)
        beautify(GATE_CRESCENT, "Small Road", ("Trees",))
        inner = (SCX - SA + 10, SCX + SA - 10, 765, 760 + SB - 10)
        if shelters:
            out["shelter"] = place("EmergencyShelter01", (SCX - SA - 30, SCX + SA + 30, 765, 895), (SCX, 800))
        out["pocket"] = place("PocketPark08", inner, (SCX, 790))
        cells = {}
        cells["NA Residential Mixed"] = zone_rect("NA Residential Mixed", [-2860, 848, -2606, 892], overwrite=True)
        cells["NA Residential Medium Row"] = zone_rect("NA Residential Medium Row", [-2860, 766, -2606, 848], overwrite=True)
        for s in segs_near(SCX, 810, 140, prefabs=("Small Road",)):
            mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
            if ((mx - SCX) / SA) ** 2 + ((mz - 760) / SB) ** 2 > 0.75 and 765 < mz < 860:
                z = zone("NA Residential Medium", mode="road", edges=[s["id"]], depth=4, side="both", overwrite=False)
                cells["NA Residential Medium"] = cells.get("NA Residential Medium", 0) + (z.get("cellsChanged") or 0)
        cap = sum(n * PER_CELL[k] for k, n in cells.items())
        print("zoned cells:", cells, flush=True)
    print(json.dumps({"services": out, "capacityHouseholds": round(cap)}))


if __name__ == "__main__":
    {"plan": stage_plan, "north": stage_north, "south": stage_south}[sys.argv[1]]()
