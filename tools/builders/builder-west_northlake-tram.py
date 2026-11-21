"""builder-west, conductor order 2033-01-10: Northlake tram link (new tram line, Northlake <-> T1 north loop).

  track - Double Tram Track on the z=2820 Small Road from Northlake Parkway junction (-2789,2820) east to the T1 avenue
          node (-1909,2820); turning loop in Northlake: Northlake Parkway (-2789,2820) -> Lakeview Crescent junction
          (~(-3062,2875)), Lakeview Crescent back to (-2789,2710), x=-2789 north to (-2789,2820).
          At the east end the line uses T1's north loop (x=-1909 z 2820..2930, z=2930, x=-1689, z=2820), which carries
          one-way track on both sides (builder-central's network; joined, not changed).
  line  - new tram line: Northlake loop stops, z=2820 stops both ways, a stop on x=-1909 for the T1 transfer.
Usage: python -I builders/builder-west_northlake-tram.py track|check|line [dry]
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, paused, post, short  # noqa: E402

WHO = "builder-west"
STAGE = sys.argv[1]
DRY = "dry" in sys.argv[2:]
LAKEVIEW = [[-2789, 2710], [-2900, 2702], [-3000, 2700], [-3065, 2745], [-3060, 2870]]
PARKWAY_S = [[-2789, 2820], [-2900, 2824], [-3020, 2852], [-3070, 2885]]


def dist_poly(p, poly):
    best = 1e9
    for (ax, az), (bx, bz) in zip(poly, poly[1:]):
        dx, dz = bx - ax, bz - az
        t = max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - az) * dz) / (dx * dx + dz * dz)))
        best = min(best, math.hypot(p[0] - ax - t * dx, p[1] - az - t * dz))
    return best


def near_edges(poly, prefab, tol, cx, cz, r):
    out = []
    for s in get("/nets", x=cx, z=cz, radius=r, filter=prefab).get("segments") or []:
        (sx, sz), (ex, ez) = s["start"], s["end"]
        m = ((sx + ex) / 2, (sz + ez) / 2)
        if dist_poly(m, poly) < tol and dist_poly((sx, sz), poly) < tol + 10 and dist_poly((ex, ez), poly) < tol + 10:
            out.append(s)
    return out


def loop_edges():
    pk = near_edges(PARKWAY_S, "Medium Road", 25, -2930, 2850, 170)
    lv = [s for s in near_edges(LAKEVIEW, "Small Road", 22, -2930, 2780, 200)
          if not (abs(s["start"][0] + 2789) < 3 and abs(s["end"][0] + 2789) < 3)]
    x2789 = [s for s in get("/nets", x=-2789, z=2765, radius=60, filter="Small Road").get("segments") or []
             if abs(s["start"][0] + 2789) < 3 and abs(s["end"][0] + 2789) < 3 and 2700 < min(s["start"][1], s["end"][1]) and max(s["start"][1], s["end"][1]) < 2830]
    return pk, lv, x2789


def track():
    pk, lv, xs = loop_edges()
    for name, lst in (("parkway", pk), ("lakeview", lv), ("x=-2789", xs)):
        print(name, [(s["id"], [round(v) for v in s["start"]], [round(v) for v in s["end"]]) for s in lst], flush=True)
    if DRY:
        r = post("/roadupgrade", {"polyline": [[-2789, 2820], [-1909, 2820]], "tolerance": 6, "prefabFilter": "Small Road",
                                  "upgrade": "Double Tram Track", "side": "both", "dryRun": True})
        print("z=2820 dry:", short(r, ("applied", "cost", "errors")), flush=True)
        return
    with paused(WHO):
        # loop first (one-way track, counter-clockwise: west on the parkway, south/east on Lakeview, north on x=-2789);
        # side "right" = the right-hand lane = running from the edge's start to its end
        cx, cz = -2920, 2780
        for name, lst in (("parkway", pk), ("lakeview", lv), ("x=-2789", xs)):
            for e in lst:
                (sx, sz), (ex, ez) = e["start"], e["end"]
                mx, mz = (sx + ex) / 2, (sz + ez) / 2
                ccw = (mx - cx) * (ez - sz) - (mz - cz) * (ex - sx) > 0
                r = post("/roadupgrade", {"edges": [e["id"]], "upgrade": "Oneway Tram Track", "side": "right" if ccw else "left"})
                print(f"  {name} {e['id']} {'right' if ccw else 'left'}:", short(r, ("applied", "cost", "errors")), flush=True)
        r = post("/roadupgrade", {"polyline": [[-2789, 2820], [-1909, 2820]], "tolerance": 6, "prefabFilter": "Small Road",
                                  "upgrade": "Double Tram Track", "side": "both"})
        print("z=2820:", short(r, ("applied", "cost", "errors")), flush=True)


def check():
    pr = get("/problems")["problems"]
    for x in pr:
        if x["type"] in ("Track Not Connected", "Pathfind Failed"):
            print(x["type"], x["count"], json.dumps(x["examples"])[:600])
    print("checked", flush=True)


STOPS = [  # running order: west on z=2820 into the Northlake loop, then east to the T1 loop and back
    {"x": -2160, "z": 2826.8}, {"x": -2460, "z": 2826.8},            # z=2820 westbound (north side)
    {"x": -2938, "z": 2850.0},                                        # parkway westbound (north side)
    {"x": -2940, "z": 2706.0},                                        # Lakeview eastbound (south side)
    {"x": -2460, "z": 2813.2}, {"x": -2160, "z": 2813.2},            # z=2820 eastbound (south side)
    {"x": -1902.2, "z": 2875},                                        # x=-1909 northbound (T1 transfer)
]


def line():
    body = {"type": "tram", "stops": STOPS, "color": "#00897B", "dryRun": DRY}
    with paused(WHO):
        r = post("/line", body, timeout=300)
    pc = r.get("pathCheck") or {}
    print("applied", r.get("applied"), r.get("lineId") or r.get("id"), r.get("error"), r.get("blocked"))
    print("pathCheck ok", pc.get("ok"), "failed", pc.get("failedSegments"))
    for s in pc.get("segments") or []:
        if s.get("status") != "ok":
            print("  ", json.dumps(s)[:300])
    print(json.dumps({k: v for k, v in r.items() if k not in ("pathCheck",)})[:1200])


if __name__ == "__main__":
    {"track": track, "check": check, "line": line}[STAGE]()
