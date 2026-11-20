"""builder-south / b1-housing-east-bank-town-p1: East Bank new town, phase 1.

Built around the bridgehead gateway roundabout G = (300,-885) (Lake Bridge boulevard from Downtown/Health Campus).
- Spine (Large Road, Trees+Grass): G -> east to the tile edge (925,-870); medium roundabout where the ring crosses.
- Shore Drive (Medium, Trees): north-east along the lake ~70-90 m inland to (790,-330); south from G along the
  shore towards the river mouth, staying west of the reserved water field (x 300..700, z -1450..-1150).
- Ring Road (Medium, Trees): an arc of radius ~270 m around G from Shore Drive north to Shore Drive south,
  crossing the spine at (570,-885): the town core (High/Mixed, transit square, civic campus) lies inside it.
- Outer Crescent (Small Road, Trees): radius ~480 m from Shore Drive north to the spine at (780,-885), with spokes.
Reserved and kept free: transit square x 320..420, z -870..-770; water field x 300..700, z -1450..-1150;
south edge z < -1450 (Southern Parkway corridor, builder-east).
Stages: dry | roads | trees
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, short

WHO = "builder-south"
G = (300.0, -885.0)


def at(a, r):
    return [round(G[0] + r * math.cos(math.radians(a)), 1), round(G[1] + r * math.sin(math.radians(a)), 1)]


SHORE_N = [[370, -765], [440, -680], [520, -600], [610, -512], [700, -425], [790, -335]]
SHORE_S = [[265, -1000], [215, -1085], [150, -1180], [85, -1290], [30, -1400]]
SPINE_E = [[420, -880], [560, -893], [700, -900], [830, -888], [905, -876]]
RING_N = [[455.6, -664.4], at(45, 270), at(25, 270), [570, -893]]        # from Shore Drive N down to the spine
RING_S = [[570, -893], at(-25, 270), at(-50, 270), at(-75, 270), at(-100, 270), [184.8, -1129.2]]  # to Shore Drive S
OUTER = [[606.4, -515.5], at(35, 480), at(18, 480), [780, -895]]           # outer crescent: Shore Drive N -> spine
SPOKES = [[at(35, 270), at(35, 480)]]
# order matters: the ring and crescent ends are computed on the built shore drives and spine
PARTS = [("spine east", "Large Road", SPINE_E), ("shore drive N", "Medium Road", SHORE_N),
         ("shore drive S", "Medium Road", SHORE_S), ("ring N", "Medium Road", RING_N), ("ring S", "Medium Road", RING_S),
         ("outer crescent", "Small Road", OUTER)] + [("spoke", "Small Road", s) for s in SPOKES]


def dry(label, prefab, pts):
    d = post("/road", {"prefab": prefab, "points": pts, "curve": "smooth", "dryRun": True})
    g = d.get("geometry") or {}
    print(f"{label:<16} errors={d.get('errors')} {d.get('problems')} minR={g.get('minRadius')} len={g.get('length')} "
          f"snap={[s.get('snappedTo') for s in d.get('snapping') or []]} demolish={len(d.get('wouldDemolish') or [])}", flush=True)
    return d


stage = sys.argv[1] if len(sys.argv) > 1 else ""
if stage == "dry":
    for p in PARTS:
        dry(*p)

if stage == "roads":
    with paused(WHO):
        for label, prefab, pts in PARTS:
            d = dry(label, prefab, pts)
            if d.get("errors") != 0 or d.get("wouldDemolish"):
                print("  skipped", label)
                continue
            r = road(prefab, pts, curve="smooth")
            print(f"{label:<16} {short(r)}", flush=True)
        for at_ in ([570, -893],):
            d = post("/roundabout", {"at": at_, "size": "medium", "dryRun": True})
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                print("roundabout", at_, short(post("/roundabout", {"at": at_, "size": "medium"})))
            else:
                print("roundabout", at_, d.get("errors"), d.get("error"))
        box = [-60, -1460, 935, -320]
        for prefab, ups in (("Large Road", ("Trees", "Grass")), ("Medium Road", ("Trees",)), ("Small Road", ("Trees",))):
            for up in ups:
                u = post("/roadupgrade", {"box": box, "prefabFilter": prefab, "upgrade": up, "side": "both"})
                print(prefab, up, u.get("applied"), u.get("cost"), u.get("errors"))

# ---- civic buildings and parks (before zoning) ----
from cc import find_spot, building  # noqa: E402

FIELD = (300, -1450, 700, -1150)  # reserved water field


def place_near(label, prefab, x, z, radius=180, box=(-60, -1450, 925, -320), keep_free=(FIELD,)):
    for c in find_spot(prefab, x, z, radius=radius, n=10):
        cs = c["corners"]
        if not all(box[0] <= p[0] <= box[2] and box[1] <= p[1] <= box[3] for p in cs):
            continue
        if any(any(k[0] <= p[0] <= k[2] and k[1] <= p[1] <= k[3] for p in cs) for k in keep_free):
            continue
        b = dict(c["building"]); b.pop("prefab", None)
        d = post("/building", {"prefab": prefab, **b, "dryRun": True})
        if d.get("errors") == 0 and not d.get("wouldDemolish"):
            r = building(prefab, **b)
            print(f"{label:<20} {short(r)} at ({c['x']},{c['z']})", flush=True)
            return r
    print(f"{label:<20} no spot", flush=True)


if stage == "civic":
    with paused(WHO):
        place_near("bus station", "BusStation02", 360, -840, radius=120, box=(305, -880, 435, -755))
        place_near("clinic", "MedicalClinic01", 400, -740, radius=150)
        place_near("central green", "CityPark04", 420, -1000, radius=160)
        place_near("elementary school", "ElementarySchool03", 330, -1060, radius=160)
        place_near("high school", "HighSchool03", 500, -990, radius=180)
        place_near("police", "PoliceStation02", 650, -860, radius=150)
        place_near("fire house", "FireHouse01", 650, -920, radius=150)
        place_near("post office", "PostOffice02", 480, -860, radius=150)
        place_near("lakefront park N", "CityPark03", 330, -700, radius=150)
        place_near("lakefront park NE", "CityPark02", 560, -520, radius=150)
        place_near("playground ring", "Playground02", 640, -680, radius=150)
        place_near("playground south", "Playground02", 200, -1050, radius=150)

# ---- zoning (EU theme); /zone no longer overwrites, so the most specific bands go first ----
from cc import zone  # noqa: E402


def dist_to_poly(p, poly):
    best = 1e9
    for a, b in zip(poly, poly[1:]):
        ax, az, bx, bz = a[0], a[1], b[0], b[1]
        vx, vz = bx - ax, bz - az
        t = max(0.0, min(1.0, ((p[0] - ax) * vx + (p[1] - az) * vz) / (vx * vx + vz * vz or 1)))
        best = min(best, math.hypot(p[0] - ax - t * vx, p[1] - az - t * vz))
    return best


def edges_on(poly, prefab, tol=25):
    out = []
    cx = sum(p[0] for p in poly) / len(poly); cz = sum(p[1] for p in poly) / len(poly)
    off = 0
    while True:
        d = get("/nets", x=cx, z=cz, radius=700, filter=prefab, offset=off)
        for s in d.get("segments") or []:
            if s.get("prefab") != prefab:
                continue
            m = ((s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2)
            if dist_to_poly(m, poly) <= tol:
                out.append(s)
        if not d.get("nextOffset"):
            break
        off = d["nextOffset"]
    return out


def side_to(s, tx, tz):
    nx, nz = -(s["end"][1] - s["start"][1]), s["end"][0] - s["start"][0]
    return "left" if nx * tx + nz * tz > 0 else "right"


def Z(label, zname, edges, depth, side="both"):
    if not edges:
        print(f"{label:<26} no edges"); return
    r = zone(zname, mode="road", edges=edges, depth=depth, side=side)
    print(f"{label:<26} {zname:<26} {len(edges)} edges {short(r, ('applied', 'errors', 'quads', 'error'))}", flush=True)


if stage == "zone":
    GATE_SPINE = [[300, -885], [420, -880], [560, -893]]
    shoreN = edges_on([[300, -885]] + SHORE_N, "Medium Road")
    shoreS = edges_on([[300, -885]] + SHORE_S, "Medium Road")
    spine_core = edges_on(GATE_SPINE, "Large Road")
    spine_east = [s for s in edges_on(SPINE_E, "Large Road") if s not in spine_core and (s["start"][0] + s["end"][0]) / 2 > 580]
    ringN = edges_on(RING_N, "Medium Road", tol=20)
    ringS = edges_on(RING_S, "Medium Road", tol=20)
    shoreN = [s for s in shoreN if s["id"] not in {e["id"] for e in ringN}]
    shoreS = [s for s in shoreS if s["id"] not in {e["id"] for e in ringS}]
    outer = edges_on(OUTER, "Small Road") + [e for sp in SPOKES for e in edges_on(sp, "Small Road")]
    inside = lambda s: math.hypot((s["start"][0] + s["end"][0]) / 2 - G[0], (s["start"][1] + s["end"][1]) / 2 - G[1]) < 260
    with paused(WHO):
        # lakefront: villas only on the water side of the shore drives, outside the core
        for s in shoreN:
            if not inside(s):
                Z("shore N lakeside " + s["id"], "EU Residential Low Waterfront", [s["id"]], 4, side_to(s, -0.7, 0.7))
        for s in shoreS:
            if not inside(s):
                Z("shore S lakeside " + s["id"], "EU Residential Low Waterfront", [s["id"]], 4, side_to(s, -1, 0))
        # core around the transit square: shops on the gateway spine, high and mixed homes, ~10% low rent
        Z("gateway spine", "EU Commercial High", [s["id"] for s in spine_core], 2)
        Z("gateway spine homes", "EU Residential Mixed", [s["id"] for s in spine_core], 5)
        Z("shore drives in core", "EU Residential Mixed", [s["id"] for s in shoreN + shoreS if inside(s)], 4)
        for s in ringN + ringS:
            Z("ring inner " + s["id"], "EU Residential High", [s["id"]], 4, side_to(s, G[0] - s["start"][0], G[1] - s["start"][1]))
        Z("ring S low rent", "Residential LowRent", [s["id"] for s in ringS][-1:], 4)
        # outside the ring: apartments, then row houses on the crescent and spoke
        Z("ring outer", "EU Residential Medium", [s["id"] for s in ringN + ringS], 4)
        Z("shore drives land side", "EU Residential Medium", [s["id"] for s in shoreN + shoreS if not inside(s)], 4)
        Z("spine east", "EU Residential Mixed", [s["id"] for s in spine_east], 4)
        Z("outer crescent + spoke", "EU Residential Medium Row", [s["id"] for s in outer], 4)

# ---- bus line 12 'Lake Bridge': East Bank square -> bridge -> x=-1140 north to the T2 tram roundabout (-1140,900) ----
LINE12 = ["283568:33",                 # BusStation02 on the East Bank transit square
          "268949:121",                # x=-1140 northbound at z=-300 (shared with line 3)
          {"x": -1133, "z": 380},      # x=-1140 northbound
          {"x": -1132, "z": 800},      # x=-1140 northbound, T2 tram interchange
          {"x": -1145, "z": 800},      # southbound
          {"x": -1146, "z": 380},      # southbound
          "162415:29"]                 # x=-1140 southbound at z=-300 (shared with line 3)
if stage == "line":
    with paused(WHO):
        r = post("/busline", {"stops": LINE12, "color": "#F4511E", "stopPrefab": "NA_BusStop01"}, timeout=300)
    print(json.dumps(r.get("stops"))[:800], (r.get("line") or {}).get("lineId"), r.get("error"))
