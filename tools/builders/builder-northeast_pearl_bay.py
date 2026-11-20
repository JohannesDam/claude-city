"""builder-northeast - b1-parks-pearl-bay: Pearl Bay lakeside resort quarter.

Tiles (0,2493), (0,1870), (623,1870), (1247,1870) bought. The north shore runs from (-130,2062) south-east to
(580,1570). Layout:
- Bay Boulevard: tree-lined Medium Road ~190 m inland, parallel to the shore, from the Lakeshore dead end
  (-330,2270) east to (1450,1880); Farm Link (Medium) north from the boulevard to the farm lane end (850,2205).
- Shore Promenade: Small Road with trees and wide sidewalks 35-80 m from the water, (-200,2150) -> (660,1610),
  linked to the boulevard at its ends and twice in between (hotel blocks between promenade and boulevard).
- Beach parks on the water side of the promenade; Commercial High (hotels, dining) between promenade and
  boulevard; Residential Medium/Row on curved crescents north of the boulevard (>= 150 m from the farms north
  of z 2190 at x > 580).

Usage: python -I builders/builder-northeast_pearl_bay.py dry|<piece>|...
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_pearl_bay.json"
KEYS = ("applied", "cost", "errors", "problems", "wouldDemolish", "conflicts")

ROADS = {
    # Small Road stub from the Lakeshore dead end: a Medium Road junction there would clip two homes
    "stub": {"prefab": "Small Road", "points": [[-330, 2270], [-265, 2266]]},
    "boulevard": {"prefab": "Medium Road", "curve": "smooth",
                  "points": [[-265, 2266], [-180, 2255], [-40, 2200], [90, 2110], [200, 1990], [330, 1910],
                             [480, 1870], [640, 1820], [800, 1790], [1000, 1790], [1200, 1820], [1450, 1880]]},
    "farm link": {"prefab": "Medium Road", "curve": "smooth", "points": [[850, 1793], [862, 1950], [850, 2205]]},
    "promenade": {"prefab": "Small Road", "curve": "smooth",
                  "points": [[-200, 2150], [-90, 2092], [-5, 2030], [62, 1935], [120, 1842], [230, 1797],
                             [400, 1752], [520, 1702], [600, 1652], [660, 1612]]},
    "link west": {"prefab": "Small Road", "curve": "smooth", "points": [[-200, 2150], [-195, 2200], [-180, 2255]]},
    "link mid": {"prefab": "Small Road", "curve": "smooth", "points": [[62, 1935], [110, 1990], [150, 2047]]},
    "link east": {"prefab": "Small Road", "curve": "smooth", "points": [[400, 1752], [415, 1820], [430, 1886]]},
    "link head": {"prefab": "Small Road", "curve": "smooth", "points": [[660, 1612], [690, 1700], [705, 1808]]},
}


def show(label, r):
    out = short(r, keys=KEYS)
    g = r.get("geometry") or {}
    if g:
        out["geo"] = {k: g.get(k) for k in ("length", "minRadius", "warnings") if g.get(k) is not None}
    out["snap"] = [s.get("snappedTo") for s in r.get("snapping") or []] or None
    print(f"{label:<12} {json.dumps(out)[:600]}", flush=True)
    return r


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


CRESCENTS = {
    # west: crescent from the boulevard round the north (homes stay south of z 2450)
    "pearl crescent": {"prefab": "Small Road", "curve": "smooth",
                       "points": [[-120, 2234], [-75, 2345], [40, 2405], [180, 2375], [270, 2250], [300, 2110],
                                  [330, 1910]]},
    "shell close": {"prefab": "Small Road", "curve": "smooth", "points": [[90, 2110], [105, 2200], [95, 2290]]},
    # middle: between the boulevard and the farm buffer (z < 2040 for x > 580)
    "coral crescent": {"prefab": "Small Road", "curve": "smooth",
                       "points": [[520, 1862], [535, 1960], [650, 2025], [760, 1990], [800, 1790]]},
    # east of the Farm Link
    "dune crescent": {"prefab": "Small Road", "curve": "smooth",
                      "points": [[862, 1950], [990, 2020], [1150, 2015], [1270, 1935], [1325, 1850]]},
    # south of the boulevard on the east headland
    "headland loop": {"prefab": "Small Road", "curve": "smooth",
                      "points": [[920, 1790], [935, 1680], [1080, 1625], [1250, 1660], [1320, 1745], [1325, 1850]]},
}
ROADS.update(CRESCENTS)


def edges_along(points, prefab, tol=20):
    """Current edges of `prefab` whose midpoint lies within tol m of the polyline."""
    import math
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


def zone_side(zone_name, points, prefab, ref, towards=True, depth=6, dry=False):
    """Zone one side of a road (the side towards `ref`, or away from it) with `zone_name`."""
    groups = {"left": [], "right": []}
    for sg in edges_along(points, prefab):
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
        for k, b in ROADS.items():
            show(k, post("/road", {**b, "dryRun": True}))
    elif MODE in ROADS:
        with paused(WHO):
            log(MODE, show(MODE, post("/road", ROADS[MODE])))
