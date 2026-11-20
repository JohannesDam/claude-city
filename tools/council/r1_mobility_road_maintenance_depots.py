"""Council round 1 - Mobility - road-maintenance-depots.

RoadMaintenanceDepot01 depots at the nearest CLEAN sites to the planned West / North / SW centres (see SITES:
the planned /findspot spots would have demolished homes and factories), never inside a PROTECTED zone, plus a 50 m Small Road that joins the orphaned street at
x=-2150 (z -870..-700) to the dead end of the z=-700 street.

Usage: python -I council/r1_mobility_road_maintenance_depots.py depots|orphan|verify
"""
import json
import math
import re
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, paused, building, road, find_spot, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "verify"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "pos")
LOG = r"C:\soft\cityskyline\logs\council\r1_mobility_road_maintenance_depots.json"
PREFAB = "RoadMaintenanceDepot01"
# The planned /findspot spots all sit on occupied home/industry lots: /findspot (and /freeland) treat zoned
# buildings as overridable, and the game removes them silently (dry run: 0 errors). Every /findspot
# candidate within 1 km of the West, North-residential and East-lakeside centres overlapped zoned lots or lay
# in a PROTECTED park, so the depots go to the nearest CLEAN sites (no zoned lot under the footprint):
SITES = [
    # label, findspot centre, preferred edge, planned centre (for the record)
    ("West (Office park west edge)", (-2432, -156), "354372:5", (-3295, 817)),
    ("North gate (north of Claude Park)", (-940, 3456), "598507:43", (-1746, 2989)),
    ("SW (freight road by the rail yard)", (-2211, -1528), "1193998:27", (-2490, -1167)),
]
MP = json.load(open(r"C:\soft\cityskyline\docs\council\masterplan.json", encoding="utf-8"))
PROTECTED = [z for z in MP["zones"] if "PROTECTED" in z["use"]]


def show(label, r):
    print(f"{label:<44} {json.dumps(short(r or {}, keys=KEYS))}", flush=True)
    return r


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def protected_at(x, z):
    for zn in PROTECTED:
        x0, z0, x1, z1 = zn["bbox"]
        if x0 <= x <= x1 and z0 <= z <= z1:
            return zn["name"]
    return None


def lot_radius(prefab):
    m = re.search(r"_(\d+)x(\d+)$", prefab or "")
    if not m:
        return 20.0
    return math.hypot(int(m.group(1)) * 8, int(m.group(2)) * 8) / 2 - 0.5


def rect_circle_overlap(corners, c, r):
    p0, p1, _, p3 = corners
    ux, uz, vx, vz = p1[0] - p0[0], p1[1] - p0[1], p3[0] - p0[0], p3[1] - p0[1]
    lu, lv = math.hypot(ux, uz), math.hypot(vx, vz)
    ux, uz, vx, vz = ux / lu, uz / lu, vx / lv, vz / lv
    dx, dz = c[0] - p0[0], c[1] - p0[1]
    a = min(max(dx * ux + dz * uz, 0), lu)
    b = min(max(dx * vx + dz * vz, 0), lv)
    return math.hypot(dx - a * ux - b * vx, dz - a * uz - b * vz) < r


def zoned_hits(corners):
    """Zoned (growable) buildings whose lot circle touches the footprint (conservative)."""
    xs, zs = [p[0] for p in corners], [p[1] for p in corners]
    cx, cz = sum(xs) / 4, sum(zs) / 4
    R = math.hypot(max(xs) - min(xs), max(zs) - min(zs)) / 2 + 60
    return [b["prefab"] for b in get("/buildings", kind="all", x=cx, z=cz, radius=R).get("buildings") or []
            if not b.get("service") and rect_circle_overlap(corners, b["pos"], lot_radius(b["prefab"]))]


def place(label, centre, edge):
    cands = find_spot(PREFAB, centre[0], centre[1], radius=250, n=30)
    cands.sort(key=lambda c: (c["edge"] != edge, -c["score"]))
    for c in cands:
        if protected_at(c["x"], c["z"]):
            continue
        hits = zoned_hits(c["corners"])
        if hits:
            continue
        b = dict(c["building"])
        b.pop("prefab", None)
        d = building(PREFAB, **b, dryRun=True)
        if d.get("errors") == 0 and not d.get("error"):
            show(f"{label} {b} (dry)", d)
            return show(label, building(PREFAB, **b))
    print(f"{label}: no clean valid spot")
    return None


if __name__ == "__main__":
    if MODE == "depots":
        out, spent = {}, 0
        for label, centre, edge, _planned in SITES:
            with paused("mobility"):
                r = place(label, centre, edge)
            out[label] = short(r or {}, keys=KEYS)
            if r and r.get("applied"):
                spent += r.get("cost") or 0
        log("depots", out)
        print("SPENT", spent)
    elif MODE == "orphan":
        pts = [[-2150, -700], [-2100, -700]]
        with paused("mobility"):
            d = show("orphan link (dry)", road("Small Road", pts, dryRun=True))
            r = show("orphan link", road("Small Road", pts)) if d.get("errors") == 0 and not d.get("error") else d
        log("orphan", short(r, keys=KEYS))
        print("SPENT", r.get("cost") if r.get("applied") else 0)
    elif MODE == "verify":
        deps = [b for b in get("/buildings", kind="service").get("buildings") or [] if b["prefab"] == PREFAB]
        print("road depots:", json.dumps(deps))
        for s in get("/nets", x=-2125, z=-720, radius=80, filter="Small").get("segments") or []:
            print("  orphan area", s["id"], s["start"], s["end"], s["startDegree"], s["endDegree"])
        k = get("/services", kind="park", brief=1)["kinds"]["park"]
        print("maintenance:", json.dumps(k["demand"]), json.dumps(k["totals"]["vehicles"]))
