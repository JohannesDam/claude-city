"""builder-northeast - b1-education-edu-campus-village: University Hill Campus Village.

The NW lakeside corner of University Hill is now taken by TechnicalUniversity01 1482518:7 (x -2178..-1722,
z 4260..4568) and MedicalUniversity01 361348:5 reaches west to x -1713, so the village goes on the lake tile north of
the campus, tile (-1870,4986), with the free owned strip z 4568..4675 as its southern edge. Access comes from the
west: tile (-2493,4363) holds the lake shore west of the Technical University, where Lakeside Drive runs from the
z=4250 campus road end (-2150,4250) round the university to the village.

Layout (x east, z north):
- Lakeside Drive (Medium, trees): shore section to (-2165,4700), then into the village loop at (-2045,4845).
- Campus Loop (Medium, trees): a loop along the lake shore (west arm, lakeside green and parks on the water side),
  north arm z ~5240, east arm x ~-1600, south arm z ~4840.
- Inside the loop two gently curved Small streets (Elm Walk x ~-1830, Oak Walk x ~-1715); the campus green with the
  school and the shops of the square sits between them at z 5000..5100.
- South of the loop: College Row (z ~4625) and Scholars Lane (z ~4735), Small, tied together on the east by
  Fellows Way (x ~-1730), which runs north into the loop.

Usage: python -I builders/builder-northeast_campus_village.py dry|tiles|<road>|trees|zone-dry|zone|...
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_campus_village.json"
KEYS = ("applied", "cost", "errors", "problems", "wouldDemolish")

TILES = [(-2493, 4363), (-1870, 4986)]

ROADS = {
    "lakeside drive": {"prefab": "Medium Road", "curve": "smooth",
                       "points": [[-2150, 4250], [-2190, 4253], [-2220, 4290], [-2228, 4380], [-2222, 4470],
                                  [-2205, 4560], [-2180, 4640], [-2155, 4700]]},
    "lakeside entry": {"prefab": "Medium Road", "curve": "smooth",
                       "points": [[-2155, 4700], [-2105, 4775], [-2045, 4845]]},
    "loop west": {"prefab": "Medium Road", "curve": "smooth",
                  "points": [[-2045, 4845], [-1985, 4920], [-1950, 5010], [-1938, 5110], [-1930, 5190]]},
    "loop north": {"prefab": "Medium Road", "curve": "smooth",
                   "points": [[-1930, 5190], [-1870, 5238], [-1760, 5250], [-1660, 5215], [-1600, 5130]]},
    "loop east": {"prefab": "Medium Road", "curve": "smooth",
                  "points": [[-1600, 5130], [-1590, 5030], [-1610, 4935], [-1680, 4870]]},
    "loop south": {"prefab": "Medium Road", "curve": "smooth",
                   "points": [[-1680, 4870], [-1790, 4838], [-1920, 4830], [-2045, 4845]]},
    "college row": {"prefab": "Small Road", "curve": "smooth",
                    "points": [[-2184, 4628], [-2060, 4624], [-1910, 4620], [-1790, 4628], [-1745, 4660]]},
    "fellows way": {"prefab": "Small Road", "curve": "smooth",
                    "points": [[-1745, 4660], [-1732, 4740], [-1712, 4810], [-1680, 4870]]},
    "scholars lane": {"prefab": "Small Road", "curve": "smooth",
                      "points": [[-2120, 4752], [-2010, 4736], [-1880, 4732], [-1730, 4740]]},
    "elm walk": {"prefab": "Small Road", "curve": "smooth",
                 "points": [[-1835, 4836], [-1845, 4950], [-1828, 5070], [-1835, 5160], [-1830, 5246]]},
    "oak walk": {"prefab": "Small Road", "curve": "smooth",
                 "points": [[-1722, 4855], [-1712, 4960], [-1722, 5070], [-1712, 5170], [-1720, 5242]]},
}
ORDER = ["lakeside drive", "lakeside entry", "loop west", "loop north", "loop east", "loop south", "college row",
         "fellows way", "scholars lane", "elm walk", "oak walk"]


def show(label, r):
    out = short(r, keys=KEYS)
    g = r.get("geometry") or {}
    if g:
        out["geo"] = {k: g.get(k) for k in ("length", "minRadius", "warnings") if g.get(k) is not None}
    out["snap"] = [s.get("snappedTo") for s in r.get("snapping") or []] or None
    other = [(c.get("error"), c.get("at"), c.get("prefab"), (c.get("owner") or {}).get("prefab"))
             for c in r.get("conflicts") or [] if c.get("error") != "Exceeds City Limits"]
    if other:
        out["other"] = other[:6]
    print(f"{label:<15} {json.dumps(out)[:700]}", flush=True)
    return r


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def edges_along(points, prefab, tol=20):
    """Current edges of `prefab` whose midpoint lies within tol m of the polyline."""
    xs, zs = [p[0] for p in points], [p[1] for p in points]
    cx, cz = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2
    r = max(max(xs) - min(xs), max(zs) - min(zs)) / 2 + 80
    segs = get("/nets", x=cx, z=cz, radius=r, filter=prefab).get("segments") or []
    out = []
    for sg in segs:
        if sg.get("prefab") != prefab:
            continue
        mx, mz = (sg["start"][0] + sg["end"][0]) / 2, (sg["start"][1] + sg["end"][1]) / 2
        best = 1e9
        for (ax, az), (bx, bz) in zip(points, points[1:]):
            dx, dz = bx - ax, bz - az
            L2 = dx * dx + dz * dz or 1
            t = max(0, min(1, ((mx - ax) * dx + (mz - az) * dz) / L2))
            best = min(best, math.hypot(ax + t * dx - mx, az + t * dz - mz))
        if best <= tol:
            out.append(sg)
    return out


def side_towards(sg, ref):
    """'left' if ref lies left of the segment direction, else 'right'."""
    (sx, sz), (ex, ez) = sg["start"], sg["end"]
    return "left" if (ex - sx) * (ref[1] - sz) - (ez - sz) * (ref[0] - sx) > 0 else "right"


def zone_side(zone_name, points, prefab, ref, towards=True, depth=6, dry=False, zrange=None):
    """Zone one side of a road (the side towards `ref`, or away from it). zrange=(z0, z1) keeps only edges whose
    midpoint z lies in that range."""
    groups = {"left": [], "right": []}
    for sg in edges_along(points, prefab):
        mz = (sg["start"][1] + sg["end"][1]) / 2
        if zrange and not (zrange[0] <= mz <= zrange[1]):
            continue
        sd = side_towards(sg, ref)
        if not towards:
            sd = "left" if sd == "right" else "right"
        groups[sd].append(sg["id"])
    res = []
    for sd, ids in groups.items():
        if ids:
            r = post("/zone", {"zone": zone_name, "mode": "road", "edges": ids, "depth": depth, "side": sd,
                               "dryRun": dry})
            res.append((sd, len(ids), r.get("cellsChanged"), r.get("errors"), r.get("error")))
    return res


if __name__ == "__main__":
    if MODE == "dry":
        for k in ORDER:
            show(k, post("/road", {**ROADS[k], "dryRun": True}))
    elif MODE == "tiles":
        with paused(WHO):
            for x, z in TILES:
                r = post("/buytile", {"x": x, "z": z})
                print("buytile", x, z, json.dumps(r)[:300])
                log(f"tile {x},{z}", r)
    elif MODE == "roads":
        with paused(WHO):
            for k in ORDER:
                log(k, show(k, post("/road", ROADS[k])))
    elif MODE in ROADS:
        with paused(WHO):
            log(MODE, show(MODE, post("/road", ROADS[MODE])))


# zoning: (zone, road, ref point, towards?) - side=None zones both sides
ZONING = [
    ("NA Commercial Low", "rect", [[-1840, 4935], [-1708, 4990]], None),   # the square south of the school
    ("NA Commercial Low", "rect", [[-1840, 5145], [-1708, 5190]], None),   # the square north of the green
    ("NA Residential High", "loop west", (-1850, 5050), True),
    ("NA Residential High", "loop south", None, None),
    ("NA Residential High", "loop north", (-1780, 5100), True),
    ("NA Residential Medium", "loop north", (-1780, 5100), False),
    ("NA Residential High", "loop east", (-1780, 5050), True),
    ("NA Residential Medium", "loop east", (-1780, 5050), False),
    ("NA Residential Mixed", "elm walk", None, None),
    ("NA Residential Mixed", "oak walk", None, None),
    ("NA Residential Mixed", "college row", None, None),
    ("NA Residential Medium", "scholars lane", None, None),
    ("NA Residential Medium", "fellows way", None, None),
    ("NA Residential Medium", "lakeside entry", (-1950, 4700), True),
]


def run_zoning(dry):
    for zname, key, ref, towards in ZONING:
        if key == "rect":
            r = post("/zone", {"zone": zname, "mode": "rect", "points": ref, "dryRun": dry})
            print(f"{zname:<24} rect {ref} cells {r.get('cellsChanged')} err {r.get('errors')}", flush=True)
            continue
        b = ROADS[key]
        if ref is None:
            ids = [sg["id"] for sg in edges_along(b["points"], b["prefab"])]
            r = post("/zone", {"zone": zname, "mode": "road", "edges": ids, "depth": 6, "side": "both", "dryRun": dry})
            res = [("both", len(ids), r.get("cellsChanged"), r.get("errors"), r.get("error"))]
        else:
            res = zone_side(zname, b["points"], b["prefab"], ref, towards=towards, dry=dry)
        print(f"{zname:<24} {key:<15} {res}", flush=True)


if __name__ == "__main__" and MODE in ("zone-dry", "zone"):
    if MODE == "zone-dry":
        run_zoning(True)
    else:
        with paused(WHO):
            run_zoning(False)
