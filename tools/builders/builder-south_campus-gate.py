"""builder-south / b2-renewal-osw-p2-campus-gate: Old Southern Works phase 2 'Campus Gate'.

Gateway Boulevard: a curved, tree-lined Medium Road that continues Freight Avenue (x=-1814, ends at z=-945) south
through the OSW factory grid to Quarry Lane / Gateway Business Park at (-1689,-1330). It is a gentle S-curve of two
r~249 m arcs: arc 1 centred (-1565,-1000) from (-1814,-1000) to the inflection (-1751.5,-1165), arc 2 centred
(-1938,-1330) down to (-1689,-1330). It crosses the z=-1000, -1110 and -1220 factory streets with 4-way junctions;
the x=-1689 Small Road between z -1110 and -1330 is removed (it would run parallel 0-25 m from the boulevard and
put junctions too close together; the lots along it face the east-west streets).

Stages (argument):
  plan      dry runs only: removal, stub upgrade, boulevard (lists the factories in the way)
  boulevard build it (allowDemolish inside the approved renewal phase), trees+grass, log the demolitions
  zone      Office High along the boulevard, NA Commercial High at its north end (Freight Avenue)
  green     Foundry Green: east part of the park buffer facing the Health Campus (z -1000..-945)
"""
import json
import math
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, short, zone, upgrade, find_spot, building

WHO = "builder-south"
ITEM = "b2-renewal-osw-p2-campus-gate"


def arc(cx, cz, r, a0, a1, sx, sz, n):
    """points on a circle; sx/sz = +-1 to pick the quadrant"""
    out = []
    for i in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        out.append([round(cx + sx * r * math.cos(a), 1), round(cz + sz * r * math.sin(a), 1)])
    return out


R = 249.0
A = math.degrees(math.asin(165 / R))  # 41.5 deg: turning angle of each arc


def a1(z):  # arc 1 point at depth z
    return [round(-1565 - R * math.cos(math.asin((-1000 - z) / R)), 1), z]


def a2(z):  # arc 2 point at depth z
    return [round(-1938 + R * math.cos(math.asin((z + 1330) / R)), 1), z]


# points exactly on the street crossings (z -1110, -1220) and halfway between streets (never within snap range)
BOULEVARD = [[-1814, -1000], a1(-1055), a1(-1110), [-1751.5, -1165], a2(-1220), a2(-1275), [-1689, -1330]]
# factories in the way (dry run 2032-01-07 21:40; all in the approved Campus Gate phase box)
EXPECTED = {"726816:7", "250478:77", "179386:41", "265210:33", "753094:1", "734272:5", "537517:21", "237907:117",
            "226743:73", "406779:49", "735114:9", "424637:17", "723527:13", "732723:1"}
STUB = "310983:13"                       # Small Road (-1814,-945)->(-1814,-1000): becomes Medium (Freight Avenue end)
X1689 = ["444991:19", "445009:7", "452265:11"]  # x=-1689 Small Road, z -1110..-1330


def dist_to_poly(p, poly):
    best = 1e9
    for a, b in zip(poly, poly[1:]):
        vx, vz = b[0] - a[0], b[1] - a[1]
        t = max(0.0, min(1.0, ((p[0] - a[0]) * vx + (p[1] - a[1]) * vz) / (vx * vx + vz * vz or 1)))
        best = min(best, math.hypot(p[0] - a[0] - t * vx, p[1] - a[1] - t * vz))
    return best


def edges_on(poly, prefab, tol=14):
    d = get("/nets", x=-1760, z=-1140, radius=320, filter=prefab, limit=500)
    out = []
    for s in d.get("segments") or []:
        if s.get("prefab") != prefab:
            continue
        mid = ((s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2)
        if dist_to_poly(mid, poly) <= tol and dist_to_poly(s["start"], poly) <= tol and dist_to_poly(s["end"], poly) <= tol:
            out.append(s["id"])
    return out


def log_demolitions(what, would):
    with open(r"C:\soft\cityskyline\logs\council\demolitions.jsonl", "a", encoding="utf-8") as f:
        f.write(json.dumps({"date": get("/city")["date"], "builder": WHO, "item": ITEM, "what": what,
                            "wouldDemolish": would}) + "\n")


def brief(would):
    if not would:
        return would
    if isinstance(would, dict):
        return {k: v for k, v in would.items() if k != "list"} | {"n": len(would.get("list") or [])}
    return [(w.get("id"), w.get("prefab"), w.get("pos"), w.get("workers")) for w in would]


stage = sys.argv[1] if len(sys.argv) > 1 else "plan"
print("boulevard points", BOULEVARD)

if stage == "plan":
    d = post("/bulldoze", {"edges": X1689, "dryRun": True})
    print("remove x=-1689:", d.get("edgeCount"), json.dumps(d.get("impact"))[:600], d.get("selection"))
    d = post("/upgrade", {"prefab": "Medium Road", "edges": [STUB], "dryRun": True, "allowDemolish": True})
    print("stub upgrade:", json.dumps(short(d))[:800])
    d = post("/road", {"prefab": "Medium Road", "points": BOULEVARD, "curve": "smooth", "dryRun": True, "allowDemolish": True})
    print("boulevard:", json.dumps({k: d.get(k) for k in ("applied", "cost", "errors", "problems", "conflicts", "geometry")})[:2500])
    w = d.get("wouldDemolish")
    print("wouldDemolish:", json.dumps(w)[:3000])

if stage == "boulevard":
    with paused(WHO):
        d = post("/bulldoze", {"edges": X1689})
        print("remove x=-1689:", d.get("applied"), d.get("deletedEdgeIds"), d.get("error"))
        time.sleep(1)
        d = post("/upgrade", {"prefab": "Medium Road", "edges": [STUB], "dryRun": True, "allowDemolish": True})
        if d.get("wouldDemolish"):
            log_demolitions("Freight Avenue end upgraded to Medium", d.get("wouldDemolish"))
        extra = [w["id"] for w in d.get("wouldDemolish") or [] if w["id"] not in EXPECTED]
        if extra:
            print("ABORT: unexpected demolitions", extra)
            sys.exit(1)
        r = upgrade("Medium Road", [STUB], allowDemolish=True)
        print("stub upgrade:", json.dumps(short(r))[:600], r.get("edgeIdsAfter"))
        d = post("/road", {"prefab": "Medium Road", "points": BOULEVARD, "curve": "smooth", "dryRun": True, "allowDemolish": True})
        print("boulevard dry:", d.get("errors"), d.get("problems"), d.get("conflicts"))
        extra = [w["id"] for w in d.get("wouldDemolish") or [] if w["id"] not in EXPECTED]
        if extra:
            print("ABORT: unexpected demolitions", extra)
            sys.exit(1)
        if d.get("wouldDemolish"):
            log_demolitions("Gateway Boulevard (Medium, trees+grass) Freight Avenue -> Quarry Lane", d.get("wouldDemolish"))
        if d.get("errors") == 0:
            r = road("Medium Road", BOULEVARD, curve="smooth", allowDemolish=True)
            print("boulevard:", json.dumps(short(r))[:1500])
            print("new edges:", r.get("newEdgeIds") or r.get("builtEdges"))
            time.sleep(1)
        poly = [[-1814, -945]] + BOULEVARD
        for up in ("Trees", "Grass"):
            u = post("/roadupgrade", {"polyline": poly, "tolerance": 14, "prefabFilter": "Medium Road", "upgrade": up, "side": "both"})
            print(up, u.get("applied"), u.get("errors"), u.get("edgeIdsAfter"))

if stage == "zone":
    with paused(WHO):
        e = edges_on(BOULEVARD, "Medium Road")
        print("boulevard edges", e)
        north = [i for i in e if i in edges_on(BOULEVARD[:2], "Medium Road", tol=14)]
        rest = [i for i in e if i not in north]
        z = zone("NA Commercial High", mode="road", edges=north, depth=3, side="both")
        print("commercial", north, short(z, ("applied", "errors", "cells", "quads", "occupiedCellsRezoned")))
        z = zone("Office High", mode="road", edges=rest, depth=4, side="both")
        print("office high", rest, short(z, ("applied", "errors", "cells", "quads", "occupiedCellsRezoned")))
