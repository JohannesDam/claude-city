"""builder-south / b1-mobility-southgate-transit-quarter: Southgate road network.

Layout (radial-concentric, around the station square R = (-2480,-600)):
- South Ring spine: Large Road (Trees + Grass), (-2800,-664) -> R -> the T1 tram stub at (-2165,-420). Built first.
  Its west end stops at the tile edge: the SW Quarter street x=-2900 is lined with SWQ homes (x -2895..-2870)
  and the SW school; the last ~95 m to the z=-650 street need builder-west / the conductor.
- Station Avenue: Medium Road (Trees), R north to Ring 2 at the tile's north edge (-2490,-318). A link on to
  the Office Park street z=-100 (or a 'North Lane' along the empty strip z~-240 from the SWQ stub (-2900,-210)
  to the x=-2129 street) is blocked: every tie-in node demolishes SWQ homes or Office Park offices.
- Health Campus link: Medium Road (Trees), R south-east to the z=-700 street at (-2150,-700).
- Medium roundabout on R.
- Ring 1 (r~210) and Ring 2 (r~290-305) around R: tree-lined Small Road arcs from the spine to the avenue,
  north-west and north-east of the square, with short spokes between them.
Run with a stage name: avenue | roundabout | rings | trees
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, short

WHO = "builder-south"
R = (-2480.0, -600.0)


def arc(r_start, r_end, a0, a1, n):
    """Points on a (spiral) arc around R from angle a0 to a1 (degrees, 0 = east, 90 = north)."""
    pts = []
    for i in range(n + 1):
        f = i / n
        a = math.radians(a0 + (a1 - a0) * f)
        r = r_start + (r_end - r_start) * f
        pts.append([round(R[0] + r * math.cos(a), 1), round(R[1] + r * math.sin(a), 1)])
    return pts


def build(label, prefab, pts, dry_first=True, **kw):
    body = {"prefab": prefab, "points": pts, "curve": "smooth", **kw}
    d = post("/road", {**body, "dryRun": True})
    g = d.get("geometry") or {}
    print(f"{label:<22} dry errors={d.get('errors')} problems={d.get('problems')} minR={g.get('minRadius')} "
          f"demolish={[w.get('prefab') for w in d.get('wouldDemolish') or []]}", flush=True)
    if d.get("errors") != 0 or d.get("wouldDemolish"):
        print("   conflicts", json.dumps(d.get("conflicts"))[:600])
        return None
    r = road(prefab, pts, curve="smooth", **kw)
    print(f"{label:<22} {short(r)} edges={r.get('newEdgeIds')}", flush=True)
    return r


def edges_along(pts, prefab, tol=12):
    d = get("/nets", x=sum(p[0] for p in pts) / len(pts), z=sum(p[1] for p in pts) / len(pts), radius=600, filter=prefab)
    out = []
    for s in d.get("segments") or []:
        mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
        if min(math.hypot(mx - p[0], mz - p[1]) for p in pts) < 60:
            out.append(s["id"])
    return out


stage = sys.argv[1] if len(sys.argv) > 1 else ""

AVENUE = [[-2480, -600], [-2489, -505], [-2491, -400], [-2490, -318]]  # ends on Ring 2; no clean link north (see below)
HC_LINK = [[-2480, -600], [-2425, -660], [-2335, -702], [-2240, -712], [-2150, -700]]

if stage == "avenue":
    with paused(WHO):
        build("Station Avenue", "Medium Road", AVENUE)

if stage == "roundabout":
    with paused(WHO):
        d = post("/roundabout", {"at": list(R), "size": "medium", "dryRun": True})
        print("dry", json.dumps(d)[:800])
        if d.get("errors") == 0 and not d.get("wouldDemolish"):
            r = post("/roundabout", {"at": list(R), "size": "medium"})
            print("roundabout", json.dumps(r)[:800])


def ring_pts(a0, r0, a1, r1, step=15, start=None, end=None):
    """Arc points around R from (a0, r0) to (a1, r1); exact start/end points replace the first/last."""
    n = max(2, int(abs(a1 - a0) / step))
    pts = arc(r0, r1, a0, a1, n)
    if start:
        pts[0] = list(start)
    if end:
        pts[-1] = list(end)
    return pts


def at(a, r):
    return [round(R[0] + r * math.cos(math.radians(a)), 1), round(R[1] + r * math.sin(math.radians(a)), 1)]


def r2(a):
    """Ring 2 radius: 307 m at the spine (192.5 deg) -> 282 m at the avenue (92 deg) -> 290 m at the spine (34 deg)."""
    if a >= 92:
        return 307 - (192.5 - a) / 100.5 * 25
    return 282 + (92 - a) / 58 * 8


RINGS = [
    ("Ring 1 north-west", ring_pts(200, 210, 93, 210, start=(-2678, -672), end=(-2491, -390))),
    ("Ring 1 north-east", ring_pts(93, 210, 37.6, 210, start=(-2491, -390), end=(-2313.6, -471.9))),
    ("Ring 2 north-west", ring_pts(192.5, 307, 92, 282, step=12, start=(-2780, -666.5), end=(-2490, -318))),
    ("Ring 2 north-east", ring_pts(92, 282, 34, 290, step=12, start=(-2490, -318), end=(-2239.6, -437.8))),
]
SPOKES = [
    ("Spoke 160", [at(160, 210), at(160, r2(160))]),
    ("Spoke 125", [at(125, 210), at(125, r2(125))]),
    ("Spoke 62", [at(62, 210), at(62, r2(62))]),
]

if stage in ("rings-dry", "rings"):
    for label, pts in RINGS + SPOKES:
        d = post("/road", {"prefab": "Small Road", "points": pts, "curve": "smooth", "dryRun": True})
        g = d.get("geometry") or {}
        print(f"{label:<20} errors={d.get('errors')} {d.get('problems')} minR={g.get('minRadius')} len={g.get('length')} "
              f"snap={[s.get('snappedTo') for s in d.get('snapping') or []]} demolish={len(d.get('wouldDemolish') or [])}")
    if stage == "rings":
        with paused(WHO):
            for label, pts in RINGS + SPOKES:
                build(label, "Small Road", pts)

# ---- services and parks (placed before zoning so zone cells do not take their frontage) ----
from cc import find_spot, building  # noqa: E402


def place_near(label, prefab, x, z, radius=200, rank=0, bbox=(-2830, -940, -2150, -295)):
    """Best /findspot candidate inside the Southgate area (bbox x0,z0,x1,z1 checked on all lot corners)."""
    cands = find_spot(prefab, x, z, radius=radius, n=8)
    ok = [c for c in cands if all(bbox[0] <= p[0] <= bbox[2] and bbox[1] <= p[1] <= bbox[3] for p in c["corners"])]
    for c in ok[rank:]:
        b = dict(c["building"])
        b.pop("prefab", None)
        d = post("/building", {"prefab": prefab, **b, "dryRun": True})
        if d.get("errors") == 0 and not d.get("wouldDemolish"):
            r = building(prefab, **b)
            print(f"{label:<22} {short(r)} at ({c['x']},{c['z']})", flush=True)
            return r
    print(f"{label:<22} no clean spot", flush=True)
    return None


if stage == "services":
    with paused(WHO):
        place_near("Commons green", "CityPark04", -2580, -520)
        place_near("Southgate school", "ElementarySchool01", -2729, -377, bbox=(-2810, -940, -2150, -300))
        place_near("Southgate clinic", "MedicalClinic01", -2452, -476)
        place_near("Station bus station", "BusStation03", -2557, -705)

if stage == "parks":
    with paused(WHO):
        place_near("Landmark tower", "NA_ResidentialHighSignature01", -2525, -440, radius=120)
        place_near("Playground ring 1 NW", "Playground02", -2660, -560, radius=120)
        place_near("Playground ring 2 NE", "Playground02", -2400, -340, radius=120)
        place_near("South belt park A", "CityPark03", -2340, -760, radius=200)
        place_near("South belt park B", "CityPark02", -2230, -760, radius=200)

# ---- zoning ----
from cc import zone  # noqa: E402


def southgate_edges():
    out = []
    for pf in ("Large Road", "Medium Road", "Small Road"):
        off = 0
        while True:
            d = get("/nets", x=-2480, z=-600, radius=420, filter=pf, offset=off)
            for s in d.get("segments") or []:
                mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
                if -2805 <= mx <= -2175 and -935 <= mz <= -305 and s.get("prefab") == pf:
                    s["mid"] = (mx, mz)
                    s["r"] = math.hypot(mx - R[0], mz - R[1])
                    s["a"] = math.degrees(math.atan2(mz - R[1], mx - R[0])) % 360
                    out.append(s)
            if not d.get("nextOffset"):
                break
            off = d["nextOffset"]
    return out


def side_towards(s, wantNorth):
    dx, dz = s["end"][0] - s["start"][0], s["end"][1] - s["start"][1]
    leftNorth = dx > 0  # left normal (-dz, dx): its z sign is the sign of dx
    if abs(dx) < 1e-6:
        leftNorth = dz < 0  # vertical edge: left points west; treat west as 'north' for the avenue
    return "left" if leftNorth == wantNorth else "right"


def Z(label, zname, edges, depth, side="both"):
    if not edges:
        print(f"{label:<28} no edges")
        return
    r = zone(zname, mode="road", edges=edges, depth=depth, side=side)
    print(f"{label:<28} {zname:<22} edges={len(edges)} {short(r, ('applied', 'cells', 'zoned', 'errors', 'error', 'quads'))}", flush=True)


if stage in ("zone-dry", "zone"):
    E = southgate_edges()
    spine = [s for s in E if s["prefab"] == "Large Road"]
    avenue = [s for s in E if s["prefab"] == "Medium Road" and s["mid"][1] > -600]
    hclink = [s for s in E if s["prefab"] == "Medium Road" and s["mid"][1] <= -600]
    small = [s for s in E if s["prefab"] == "Small Road"]
    ring1 = [s for s in small if 185 <= s["r"] <= 225]
    ring2 = [s for s in small if 265 <= s["r"] <= 320]
    spokes = [s for s in small if s not in ring1 and s not in ring2]
    for name, g in (("spine", spine), ("avenue", avenue), ("hclink", hclink), ("ring1", ring1), ("ring2", ring2), ("spokes", spokes)):
        print(name, [(s["id"], round(s["r"]), round(s["a"])) for s in g])
    if stage == "zone":
        ids = lambda g: [s["id"] for s in g]
        with paused(WHO):
            # quiet residential rings first, then the denser bands along the boulevard and avenue override them
            Z("ring 2 north-west", "NA Residential Medium", ids([s for s in ring2 if s["a"] > 92]), 4)
            Z("ring 2 north-east", "NA Residential High", ids([s for s in ring2 if s["a"] <= 92 and s["mid"][0] < -2300]), 4)
            Z("ring 2 east (low rent)", "Residential LowRent", ids([s for s in ring2 if s["mid"][0] >= -2300]), 4)
            Z("ring 1 north-west", "NA Residential Medium", ids([s for s in ring1 if s["a"] > 93]), 4)
            Z("ring 1 north-east", "NA Residential High", ids([s for s in ring1 if s["a"] <= 93]), 4)
            Z("spokes", "NA Residential Medium Row", ids(spokes), 3)
            for s in spine:
                Z("spine north " + s["id"], "NA Residential Mixed", [s["id"]], 4, side_towards(s, True))
                if s["mid"][0] > -2440:
                    Z("spine south " + s["id"], "NA Commercial High", [s["id"]], 4, side_towards(s, False))
            Z("avenue", "NA Residential Mixed", ids(avenue), 4)
            for s in hclink:
                Z("hc link north " + s["id"], "Office High", [s["id"]], 4, side_towards(s, True))
                if s["mid"][0] < -2390:
                    Z("hc link south " + s["id"], "NA Commercial High", [s["id"]], 3, side_towards(s, False))

# ---- bus line 8 Southgate: station square -> Station Avenue -> Ring 2 -> South Ring -> z=-420 -> x=-1909 (T1 tram) ----
LINE8 = [
    "1907353:7",            # BusStation03 integrated stop at the square
    {"x": -2484, "z": -450},  # Station Avenue northbound (east side), clinic / Commons
    {"x": -2385, "z": -338.7},  # Ring 2 north-east, eastbound
    {"x": -2050, "z": -426},  # z=-420 eastbound (tram depot)
    {"x": -1903, "z": -200},  # x=-1909 northbound (T1 tram)
    {"x": -1915, "z": -200},  # x=-1909 southbound
    {"x": -2050, "z": -414},  # z=-420 westbound
    {"x": -2355, "z": -491},  # South Ring westbound
]
if stage in ("line-dry", "line"):
    d = post("/busline", {"stops": LINE8, "color": "#00897B", "stopPrefab": "NA_BusStop01", "dryRun": True}, timeout=300)
    print("dry", json.dumps(d.get("stops"))[:1500], d.get("error"))
    if stage == "line" and not d.get("error"):
        with paused(WHO):
            r = post("/busline", {"stops": LINE8, "color": "#00897B", "stopPrefab": "NA_BusStop01"}, timeout=300)
        print("line", r.get("lineId") or r.get("line"), json.dumps(r)[-800:])

if stage == "post":
    with paused(WHO):
        place_near("Southgate post office", "PostOffice01", -2660, -470, radius=220)
