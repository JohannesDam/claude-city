"""HV backbone: link the north-east high-voltage island (incinerator 379435:55, GeothermalPowerPlant01
2103793:213, transformers 419438:257 and 672799:209) to the main grid, so the geothermal plant can replace
imported power.

Findings (2026-11-21):
- A "High-voltage Line" pylon may only stand on free land (not on roads, service lots or homes), the API splits
  a leg into spans of at most ~96 m, and the 30 m wide line corridor demolishes homes it overlaps.
- Downtown between x -1250 and the main transformer 174144:263 (-2028.7,1474.5) is wall-to-wall housing:
  every route to that transformer demolishes 9+ high-density towers (~1,800 renters). Not done.
- So the line ends at the east edge of Downtown in a NEW TransformerStation02 on the main-grid low-voltage
  network (free strip between Downtown and the highway, x -1240..-1135, z 1452..1490).

Route: transformer 419438:257 (-249,3447) -> south along the east side of the highway, outside protected
Claude Park (bbox x -1258..-602, z 2762..3418) -> across the highway south of the park -> south-west along the
free strip between the highway and Northfield/Downtown -> new transformer at about (-1194,1476).

The planner picks pylon spots on free map cells (15 m grid from GET /map, spans <= 90 m, cells next to roads
excluded, pylons in Northfield penalised), then dry-runs the line and greedily nudges pylons next to each
conflict or demolition until the game accepts it with 0 errors and 0 demolitions. The line is built in ONE /road
call (atomic) inside a short paused block.

Built 2026-11-21 (game 2033-01-11): TransformerStation02 814579:123 at (-1193.6,1475.5) (HV marker
(-1193.6,1459.5), $30k) and a 31-span line ($6.2k) from 419438:257; pylons in builders/hv_backbone_route.json.
The highway crossing south of the park (pylons (-878,2418)..(-1070,2140)) and the park-corner pylon (-617,2688)
were placed by hand after the automatic fixer got stuck there.

Run: cd C:/soft/cityskyline/tools && python -I builders/hv_backbone.py            (plan + dry run)
                                     python -I builders/hv_backbone.py --build    (transformer + line)
     python -I builders/hv_backbone.py --saved --tf=<transformer id> [--build]    (re-check / rebuild the saved route)
"""
import heapq
import json
import math
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import find_spot, get, paused, post  # noqa: E402

BUILD = "--build" in sys.argv
PREFAB = "High-voltage Line"
START = "419438:257"                 # NE island transformer, HV marker at (-249.2,3447.2)
S = (-249.2, 3447.2)
TF_PREFAB = "TransformerStation02"
TF_NEAR = (-1194, 1476)              # free strip at the east edge of Downtown
PARK = (-1258, -602, 2762, 3418)     # Claude Park bbox x0, x1, z0, z1 (protected)
NORTHFIELD = (-2298, -1262, 2202, 3408)
SPAN = 90.0
GEO = "2103793:213"


def in_park(x, z, m=10):
    return PARK[0] - m <= x <= PARK[1] + m and PARK[2] - m <= z <= PARK[3] + m


def legal(pts):
    for (ax, az), (bx, bz) in zip(pts, pts[1:]):
        if math.hypot(bx - ax, bz - az) > 96:
            return False
        for k in range(11):
            x, z = ax + (bx - ax) * k / 10, az + (bz - az) * k / 10
            if in_park(x, z, 5) or (x < 312 and z > 4035):
                return False
    return True


def plan(end, strict=True):
    """Dijkstra over free 15 m map cells from S to `end` (spans <= SPAN); strict also skips cells next to
    roads, rail and service lots."""
    r = get("/map", x=-1150, z=2450, size=2200, cell=15)
    x0, cell, cols = r["x0"], r["cell"], r["cols"]
    rows = [ln for ln in r["grid"].split("\n")[1:] if ln.strip()]
    Z = [float(ln.split()[0]) for ln in rows]
    G = [ln[-cols:] for ln in rows]
    blocked_nb = set("#T:SB") if strict else set()
    free = {}
    for i, row in enumerate(G):
        for j, ch in enumerate(row):
            if ch != ".":
                continue
            if any(0 <= i + a < len(G) and 0 <= j + b < cols and G[i + a][j + b] in blocked_nb
                   for a in (-1, 0, 1) for b in (-1, 0, 1)):
                continue
            x, z = x0 + cell * j + cell / 2, Z[i]
            if not in_park(x, z):
                free[(i, j)] = (x, z)
    pos = dict(free)
    pos["S"], pos["E"] = S, end
    n = int(SPAN // cell) + 1
    offs = [(a, b) for a in range(-n, n + 1) for b in range(-n, n + 1) if (a or b) and math.hypot(a, b) * cell <= SPAN]

    def nbrs(k):
        if k == "S":
            yield from (q for q, p in free.items() if math.hypot(p[0] - S[0], p[1] - S[1]) <= SPAN)
            return
        i, j = k
        for a, b in offs:
            if (i + a, j + b) in free:
                yield (i + a, j + b)
        p = pos[k]
        if math.hypot(p[0] - end[0], p[1] - end[1]) <= SPAN:
            yield "E"

    dist, prev, pq = {"S": 0}, {}, [(0, "S")]
    while pq:
        d, k = heapq.heappop(pq)
        if k == "E":
            break
        if d > dist.get(k, 1e18):
            continue
        for q in nbrs(k):
            a, b = pos[k], pos[q]
            if not legal([a, b]):
                continue
            nf = q != "E" and NORTHFIELD[0] <= b[0] <= NORTHFIELD[1] and NORTHFIELD[2] <= b[1] <= NORTHFIELD[3]
            c = d + 60 + math.hypot(a[0] - b[0], a[1] - b[1]) + (200 if nf else 0)
            if c < dist.get(q, 1e18):
                dist[q], prev[q] = c, k
                heapq.heappush(pq, (c, q))
    if "E" not in dist:
        return plan(end, False) if strict else None
    path = ["E"]
    while path[-1] != "S":
        path.append(prev[path[-1]])
    return [list(pos[k]) for k in reversed(path)]


def body(pts, end_id=None, dry=True):
    ct = {"start": START}
    if end_id:
        ct["end"] = end_id
    b = {"prefab": PREFAB, "points": [[round(x, 1), round(z, 1)] for x, z in pts], "connectTo": ct}
    if dry:
        b["dryRun"] = True
    return b


def score(d, want_conn):
    bad = (d.get("errors") or 0) + 3 * len(d.get("wouldDemolish") or [])
    conns = d.get("connections") or []
    ok = len(conns) == want_conn and all((c.get("preview") or {}).get("attached") for c in conns)
    return bad + (0 if ok else 50)


def fix(pts, end_id=None, max_rounds=12):
    """Move every pylon that sits on a conflict or causes a demolition to the best spot on rings of 15/30/45 m
    around it (all dry runs); repeat while the score improves."""
    want = 2 if end_id else 1
    d = post("/road", body(pts, end_id))
    s = score(d, want)
    print("dry run: errors", d.get("errors"), "demolish", len(d.get("wouldDemolish") or []), "score", s)
    ring = [(r * math.cos(a * math.pi / 8), r * math.sin(a * math.pi / 8)) for r in (15, 30, 45) for a in range(16)]
    for rnd in range(max_rounds):
        if s == 0:
            break
        spots = [c["at"] for c in d.get("conflicts") or []] + [w["center"] for w in d.get("wouldDemolish") or []]
        bad = []
        for p in spots:
            j = min(range(1, len(pts) - 1), key=lambda k: math.hypot(pts[k][0] - p[0], pts[k][1] - p[1]))
            if j not in bad:
                bad.append(j)
        s0 = s
        for j in bad:
            for dx, dz in ring:
                cp = pts[:j] + [[pts[j][0] + dx, pts[j][1] + dz]] + pts[j + 1:]
                if not legal(cp):
                    continue
                dd = post("/road", body(cp, end_id))
                ss = score(dd, want)
                if ss < s:
                    s, pts, d = ss, cp, dd
                    print(f"  round {rnd}: pylon {j} moved by ({dx:.0f},{dz:.0f}) -> score {s}")
                    break
        if s >= s0:
            print("  stuck at score", s)
            break
    return pts, d, s


def show(d):
    print("  errors", d.get("errors"), "cost", d.get("cost"), "segments", d.get("segments"),
          "connections", [(c["building"], (c.get("preview") or {}).get("attached")) for c in d.get("connections") or []])
    for c in d.get("conflicts") or []:
        print("  conflict", c["at"], c["prefab"], c["kind"])
    for w in d.get("wouldDemolish") or []:
        print("  demolish", w["id"], w["prefab"], w["center"], w.get("renters"))


def state():
    el = get("/services", kind="electricity")["kinds"]["electricity"]
    geo = next(b for b in el["buildings"] if b["id"] == GEO)
    return {"geoProduction": geo.get("production"), "import": el["demand"]["trade"]["import"],
            "export": el["demand"]["trade"]["export"], "consumption": el["demand"]["consumption"],
            "fulfilled": el["demand"]["fulfilledConsumption"]}


def hv_marker(bid):
    d = post("/road", {"prefab": PREFAB, "points": [list(S), list(S)], "connectTo": {"end": bid}, "dryRun": True})
    sn = d.get("snapping") or []
    return sn[-1]["pos"] if sn and sn[-1].get("snappedTo") == "connectionPoint" else None


if __name__ == "__main__":
    print("before:", json.dumps(state()))
    if "--saved" in sys.argv:
        tf_id = next(a.split("=", 1)[1] for a in sys.argv if a.startswith("--tf="))
        pts = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "hv_backbone_route.json")))
        d = post("/road", body(pts, tf_id))
        show(d)
        if BUILD and score(d, 2) == 0:
            with paused("hv-backbone"):
                b = post("/road", body(pts, tf_id, dry=False))
            print("line built:", {k: b.get(k) for k in ("applied", "errors", "cost", "segments", "newEdgeIds")})
        sys.exit(0)
    tf_id = next((a.split("=", 1)[1] for a in sys.argv if a.startswith("--tf=")), None)
    if not tf_id:
        cands = [c for c in find_spot(TF_PREFAB, *TF_NEAR, radius=120, n=6)
                 if abs(c["x"] - TF_NEAR[0]) < 60 and abs(c["z"] - TF_NEAR[1]) < 40]
        tf = None
        for c in cands:
            b = dict(c["building"])
            b.pop("prefab", None)
            r = post("/building", {"prefab": TF_PREFAB, **b, "dryRun": True})
            if r.get("errors") == 0 and not r.get("wouldDemolish") and r.get("roadConnected"):
                tf = b
                print("transformer spot", c["x"], c["z"], b)
                break
        if not tf:
            sys.exit("no transformer spot")
        # check the line can reach the spot before building anything
        end_guess = (c["x"], c["z"] + 45)
        pts = plan(end_guess)
        if not pts:
            sys.exit("no pylon chain to the transformer spot")
        pts, d, s = fix(pts[:-1])  # dangling dry run up to the last free pylon
        show(d)
        json.dump(pts, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "hv_backbone_route.json"), "w"))
        if s:
            sys.exit("line route not clean; nothing built")
        if not BUILD:
            sys.exit("dry run only")
        with paused("hv-backbone"):
            r = post("/building", {"prefab": TF_PREFAB, **tf})
        tf_id = r.get("placedId")
        print("transformer built:", tf_id, r.get("errors"), r.get("cost"))
        time.sleep(1.0)
    end = hv_marker(tf_id)
    print("transformer", tf_id, "HV marker", end)
    pts = plan(tuple(end))
    print("planned pylons:", len(pts))
    pts, d, s = fix(pts, end_id=tf_id)
    print("route:", json.dumps([[round(x, 1), round(z, 1)] for x, z in pts]))
    show(d)
    if BUILD and s == 0:
        with paused("hv-backbone"):
            b = post("/road", body(pts, tf_id, dry=False))
        print("line built:", {k: b.get(k) for k in ("applied", "errors", "cost", "segments", "problems")})
        print("new edges:", b.get("newEdgeIds"))
        print("connections:", [(c["building"], c.get("externalEdgesBefore")) for c in b.get("connections") or []])
        time.sleep(5.0)
        print("after:", json.dumps(state()))
    elif BUILD:
        print("line NOT built (route not clean); transformer", tf_id, "stands unconnected")
