"""builder-west, item b1-housing-westbrook-west-garden-village.

Westbrook West garden village on the owned free land west of the x=-4600 lane (x -5000..-4656, z -250..430):
  - "Village Loop": tree-lined Medium Road, a D-shaped smooth loop leaving the x=-4600 lane at z=-250 and
    returning at the existing z=430 node (in line with the z=430 cross street east of the lane);
  - "Green Crescent": curved Small Road concentric inside the loop, both ends on the loop curve;
  - "Green Lane": the gently bowed central street from the south arm to the north arm, with the village
    green, the school and the post office on it.
Zoning: villas (Low) only on the loop's outer, woodland side; Medium Row on the crescents; Medium apartments
on the loop's inner side near the bus lane; Mixed Old Town around the green. Nothing west of x -5100.

The two lane junctions replace 4 single-family homes and 3 row houses (31 households, ~2.7k/month), well
inside the make-room limits; the village adds ~3k households.
Usage: python -I builders/builder-west_garden-village.py loop|inner|zone|services [dry]
"""
import json
import math
import os
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, demolition_cost, find_spot, get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[2:]
LOOP = [[-4600, -250], [-4740, -250], [-4905, -195], [-4985, -60], [-5000, 90], [-4985, 240], [-4905, 375],
        [-4740, 430], [-4600, 430]]
DEMOLOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "logs", "council", "demolitions.jsonl")


def log_demolition(what, would):
    cost = demolition_cost(would)
    with open(DEMOLOG, "a", encoding="utf-8") as f:
        f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": WHO, "prefab": what,
                            "ids": [w.get("id") for w in would or []], "cost": cost}) + "\n")


def trees(box, prefab):
    """Trees both sides on every edge of this prefab in the box (the village is the only such road there)."""
    r = post("/roadupgrade", {"box": box, "prefabFilter": prefab, "upgrade": "Trees", "side": "both"})
    print("  trees:", short(r), flush=True)


def stage_loop():
    d = post("/road", {"prefab": "Medium Road", "points": LOOP, "curve": "smooth", "dryRun": True, "allowDemolish": True})
    would = d.get("wouldDemolish") or []
    cost = demolition_cost(would)
    print("loop dry:", short(d, ("errors", "problems", "cost")), cost, flush=True)
    if d.get("errors") or cost["households"] > 40 or cost["companies"] or DRY:
        return
    with paused(WHO):
        r = post("/road", {"prefab": "Medium Road", "points": LOOP, "curve": "smooth", "allowDemolish": True})
        print("loop:", short(r, ("applied", "cost", "errors", "problems")), flush=True)
        if r.get("applied"):
            log_demolition("Medium Road (Westbrook Village Loop junctions)", would)
            trees([-5010, -265, -4605, 445], "Medium Road")


def densify(points, step=40):
    """Sample the loop polyline more finely so the polyline selection follows the smooth curve."""
    out = []
    for (x0, z0), (x1, z1) in zip(points, points[1:]):
        n = max(1, int(math.hypot(x1 - x0, z1 - z0) // step))
        out += [[x0 + (x1 - x0) * i / n, z0 + (z1 - z0) * i / n] for i in range(n)]
    return out + [points[-1]]


def loop_nodes():
    """Nodes of the built loop (Medium Road) as {node_id: (x, z)}."""
    nodes = {}
    for cx, cz in ((-4800, -200), (-4980, 90), (-4800, 400), (-4660, 90)):
        for s in get("/nets", x=cx, z=cz, radius=260, filter="Medium Road").get("segments") or []:
            nodes[s["startNode"]] = tuple(s["start"])
            nodes[s["endNode"]] = tuple(s["end"])
    return nodes


def nearest(nodes, x, z):
    return min(nodes.values(), key=lambda p: (p[0] - x) ** 2 + (p[1] - z) ** 2)


if __name__ == "__main__":
    stage = sys.argv[1] if len(sys.argv) > 1 else "loop"
    if stage == "loop":
        stage_loop()
    elif stage == "nodes":
        for k, v in sorted(loop_nodes().items(), key=lambda kv: kv[1][1]):
            print(k, v)


CRES = [[-4740, -250], [-4760, -180], [-4830, -80], [-4875, 30], [-4880, 90], [-4875, 150], [-4830, 260],
        [-4760, 360], [-4740, 430]]
GREEN_LANE = [[-4680, -250], [-4705, -80], [-4715, 90], [-4705, 260], [-4680, 430]]


def stage_inner():
    with paused(WHO):
        for name, pts in (("Green Crescent", CRES), ("Green Lane", GREEN_LANE)):
            d = post("/road", {"prefab": "Small Road", "points": pts, "curve": "smooth", "dryRun": True})
            if d.get("errors") or d.get("wouldDemolish") or DRY:
                print(name, "dry:", short(d), flush=True)
                continue
            r = post("/road", {"prefab": "Small Road", "points": pts, "curve": "smooth"})
            print(name, short(r, ("applied", "cost", "errors", "problems")), flush=True)
            time.sleep(1)
        if not DRY:
            # Small Roads inside the loop are all new (the lane x=-4600 strip lies east of x -4655)
            trees([-4890, -245, -4662, 425], "Small Road")


if __name__ == "__main__" and len(sys.argv) > 1 and sys.argv[1] == "inner":
    stage_inner()


CENTRE = (-4800, 90)
SERVICES = [  # (prefab, findspot target, acceptance box x0, x1, z0, z1)
    ("CityPark02", (-4750, 90), (-4790, -4720, 40, 140)),          # village green at the crescent's apex
    ("ElementarySchool02", (-4755, 190), (-4800, -4720, 130, 260)),  # village school next to the green
    ("PostOffice01", (-4700, -300), (-4745, -4655, -340, -270)),    # post office on the Village Loop (post item)
]


def side_of(seg, point):
    """'right' or 'left': the side of the edge (start -> end) that faces point."""
    (sx, sz), (ex, ez) = seg["start"], seg["end"]
    dx, dz = ex - sx, ez - sz
    mx, mz = (sx + ex) / 2, (sz + ez) / 2
    return "right" if dz * (point[0] - mx) - dx * (point[1] - mz) > 0 else "left"


def other(side):
    return "left" if side == "right" else "right"


def village_edges():
    segs = {}
    for cx, cz in ((-4800, -150), (-4850, 90), (-4800, 330)):
        for s in get("/nets", x=cx, z=cz, radius=260).get("segments") or []:
            mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
            if -5010 < mx < -4605 and -262 < mz < 442 and s.get("prefab") in ("Medium Road", "Small Road"):
                if s["prefab"] == "Small Road" and mx > -4662:
                    continue  # the old lane strip
                segs[s["id"]] = s
    loop = [s for s in segs.values() if s["prefab"] == "Medium Road"]
    small = [s for s in segs.values() if s["prefab"] == "Small Road"]
    lane = [s for s in small if max(s["start"][0], s["end"][0]) > -4725 and min(s["start"][0], s["end"][0]) > -4730]
    cres = [s for s in small if s not in lane]
    return loop, cres, lane


def stage_services():
    with paused(WHO):
        for prefab, (x, z), box in SERVICES:
            c = next((k for k in find_spot(prefab, x, z, radius=150, n=40)
                      if box[0] <= k["x"] <= box[1] and box[2] <= k["z"] <= box[3]), None)
            if not c:
                print(prefab, "no spot", flush=True)
                continue
            b = dict(c["building"])
            b.pop("prefab", None)
            d = post("/building", {"prefab": prefab, **b, "dryRun": True})
            if d.get("errors") or d.get("wouldDemolish") or DRY:
                print(prefab, "dry:", short(d), flush=True)
                continue
            r = building(prefab, **b)
            print(prefab, short(r), (c["x"], c["z"]), flush=True)


def stage_zone():
    loop, cres, lane = village_edges()
    print(f"edges: loop {len(loop)}, crescent {len(cres)}, green lane {len(lane)}", flush=True)
    jobs = []  # (zone, edge ids, side, depth)
    for s in loop:
        arm = min(s["start"][0], s["end"][0]) > -4745
        inner = side_of(s, CENTRE)
        if arm:
            jobs.append(("NA Residential Medium", [s["id"]], "both", 4))
        else:
            jobs.append(("NA Residential Medium Row", [s["id"]], inner, 5))
            jobs.append(("NA Residential Low", [s["id"]], other(inner), 4))   # villas at the woodland edge
    for s in cres:
        jobs.append(("NA Residential Medium Row", [s["id"]], "both", 4))
    for s in lane:
        mz = (s["start"][1] + s["end"][1]) / 2
        name = "NA Residential Mixed Old Town" if -30 < mz < 260 else "NA Residential Medium Row"
        jobs.append((name, [s["id"]], "both", 4))
    with paused(WHO):
        for name, ids, side, depth in jobs:
            r = zone(name, mode="road", edges=ids, depth=depth, side=side, **({"dryRun": True} if DRY else {}))
            print(f"  {name:32} {ids[0]:>12} {side:5} d{depth} {short(r, ('applied', 'errors', 'cells', 'error'))}", flush=True)


if __name__ == "__main__" and len(sys.argv) > 1 and sys.argv[1] in ("services", "zone"):
    {"services": stage_services, "zone": stage_zone}[sys.argv[1]]()
