"""Council round 1 - Mobility - bus-network.

Four two-way bus lines that meet line 1 (x=-1909): Westline (x=-3400 + z=900), Southline (z=-100 + x=-1140),
University line (x=-1470), Industry line (z=3040). Right-hand traffic: a stop sits on the right of the
direction the bus travels (northbound -> east side, eastbound -> south side). Every stop is checked against
/nets first (nearest road within 20 m, mid-block, right side for its travel direction); a stop that fails is
moved +-20/40 m along its road. Each line is dry-run first.

The planned West bus depot at (-3129,386) is NOT built: that /findspot spot (and every other BusDepot01
candidate within 1 km) lies on occupied home lots, which the game would demolish without an error.

Usage: python -I council/r1_mobility_bus_network.py check|line2|line3|line4|line5|verify
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "verify"
LOG = r"C:\soft\cityskyline\logs\council\r1_mobility_bus_network.json"
STOP = "NA_BusStop01"


def _x(xs, z):
    return [(x, z) for x in xs]


def _z(x, zs):
    return [(x, z) for z in zs]


LINES = {
    "line2": dict(name="Westline", color="#1E88E5", stops=
                  _z(-3406, [1885, 1530, 1065]) + _x([-3120, -2680, -2240, -1800, -1360], 891)
                  + _x([-1360, -1800, -2240, -2680, -3120], 909) + _z(-3394, [1065, 1530, 1885])),
    "line3": dict(name="Southline", color="#43A047", stops=
                  _x([-3730, -3120, -2680, -2240, -1800, -1360], -106) + _z(-1149, [-340, -850])
                  + _z(-1131, [-850, -340]) + _x([-1360, -1800, -2240, -2680, -3120, -3730], -94)),
    "line4": dict(name="University line", color="#8E24AA", stops=
                  _z(-1464, [375, 830, 1285, 1775, 2215, 2655, 3095, 3590, 3980, 4150])
                  + _z(-1476, [4150, 3980, 3590, 3095, 2655, 2215, 1775, 1285, 830, 375])),
    "line5": dict(name="Industry line", color="#FB8C00", stops=
                  [(-2460, 3034), (-2020, 3034), (-1580, 3034), (-1100, 3031), (-850, 3031), (-400, 3031), (-100, 3031),
                   (-100, 3049), (-400, 3049), (-850, 3049), (-1100, 3049), (-1580, 3046), (-2020, 3046), (-2460, 3046)]),
}


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def nearest_road(x, z, radius=22):
    best = None
    for s in get("/nets", x=x, z=z, radius=radius).get("segments") or []:
        if not s.get("road") or "Highway" in s["prefab"]:
            continue
        if best is None or s["distance"] < best["distance"]:
            best = s
    return best


def analyse(stops):
    """For each stop: road, distance from centre, which side of travel it is on, distance to the nearest node."""
    out = []
    n = len(stops)
    for i, (x, z) in enumerate(stops):
        s = nearest_road(x, z)
        r = {"i": i, "pos": [x, z]}
        if not s:
            r["problem"] = "no road within 22 m"
            out.append(r)
            continue
        (x0, z0), (x1, z1) = s["start"], s["end"]
        L = math.hypot(x1 - x0, z1 - z0) or 1
        ux, uz = (x1 - x0) / L, (z1 - z0) / L
        along = (x - x0) * ux + (z - z0) * uz
        cx, cz = x0 + along * ux, z0 + along * uz
        ox, oz = x - cx, z - cz
        # travel direction: towards the next stop, projected on the road; at a U-turn use the previous stop
        nx, nz = stops[(i + 1) % n]
        dt = (nx - x) * ux + (nz - z) * uz
        if abs(dt) < 25:
            px, pz = stops[(i - 1) % n]
            dt = (x - px) * ux + (z - pz) * uz
        tx, tz = (ux, uz) if dt >= 0 else (-ux, -uz)
        right = (ox * tz - oz * tx) > 0  # right of travel = (tz, -tx)
        r.update({"edge": s["id"], "prefab": s["prefab"], "off": round(math.hypot(ox, oz), 1),
                  "toNode": round(min(along, L - along), 1), "rightSide": right})
        if not right:
            r["problem"] = "stop on the left of travel"
        elif min(along, L - along) < 12:
            r["problem"] = "too close to a junction"
        out.append(r)
    return out


def fix_stops(stops, min_node=15):
    """Move a stop that fails the checks to mid-block: the midpoint of its road segment (same side offset),
    else +-20/40 m along the road."""
    res = list(stops)
    for a in analyse(stops):
        if "problem" not in a and a.get("toNode", 0) >= min_node:
            continue
        x, z = a["pos"]
        s = nearest_road(x, z, radius=30)
        if not s:
            continue
        (x0, z0), (x1, z1) = s["start"], s["end"]
        L = math.hypot(x1 - x0, z1 - z0) or 1
        ux, uz = (x1 - x0) / L, (z1 - z0) / L
        along = (x - x0) * ux + (z - z0) * uz
        ox, oz = x - (x0 + along * ux), z - (z0 + along * uz)
        mids = [(round(x0 + L / 2 * ux + ox, 1), round(z0 + L / 2 * uz + oz, 1))] if L >= 2 * min_node else []
        for cand in mids + [(round(x + d * ux, 1), round(z + d * uz, 1)) for d in (20, -20, 40, -40)]:
            trial = list(res)
            trial[a["i"]] = cand
            b = analyse(trial)[a["i"]]
            if "problem" not in b and b.get("toNode", 0) >= min_node:
                res[a["i"]] = cand
                break
    return res


def run_line(key, apply=True):
    L = LINES[key]
    stops = fix_stops(L["stops"])
    bad = [a for a in analyse(stops) if "problem" in a]
    for a in bad:
        print("  still bad:", a)
    body = {"stops": [{"x": x, "z": z} for x, z in stops], "color": L["color"], "stopPrefab": STOP}
    with paused("mobility"):
        d = post("/busline", {**body, "dryRun": True}, timeout=300)
    print(key, "dry:", json.dumps({k: d.get(k) for k in ("error", "line", "warning")})[:600])
    errs = [ns for ns in (d.get("newStops") or []) if (ns or {}).get("errors")]
    if d.get("error") or errs:
        print(key, "dry-run problems:", json.dumps(errs)[:800])
        log(key, {"dry": d, "stops": stops})
        return None
    if not apply:
        return d
    with paused("mobility"):
        r = post("/busline", body, timeout=300)
    cost = sum((ns or {}).get("cost") or 0 for ns in (r.get("newStops") or []))
    if isinstance(r.get("line"), dict):
        cost += r["line"].get("cost") or 0
    print(key, "applied:", json.dumps({k: r.get(k) for k in ("error", "lineId", "warning")}), "cost", cost)
    log(key, {"lineId": r.get("lineId"), "stops": stops, "cost": cost, "line": r.get("line"),
              "newStops": [short(ns or {}, keys=("applied", "cost", "errors", "placedId", "pos", "edge")) for ns in r.get("newStops") or []]})
    return r


if __name__ == "__main__":
    if MODE == "check":
        for key, L in LINES.items():
            print("==", key, L["name"])
            for a in analyse(L["stops"]):
                print("  ", json.dumps(a))
            fixed = fix_stops(L["stops"])
            moved = [(o, f) for o, f in zip(L["stops"], fixed) if o != f]
            print("   moved:", moved)
    elif MODE in LINES:
        run_line(MODE)
    elif MODE.startswith("dry"):
        run_line(MODE[3:], apply=False)
    elif MODE == "verify":
        d = get("/lines")
        for ln in d.get("lines") or []:
            print(ln["number"], ln["color"], "complete", ln["complete"], "stops", len(ln["stops"]), "vehicles", ln.get("vehicles"),
                  "waiting", ln.get("waitingPassengers"), "onBoard", ln.get("passengersOnBoard"), "flags", ln.get("flags"),
                  "len", ln.get("length"), "interval", ln.get("vehicleInterval"))
        print("depots:", json.dumps(d.get("busDepots")))
