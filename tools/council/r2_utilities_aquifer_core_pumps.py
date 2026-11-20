"""Council round 2 - Utilities - aquifer-core-pumps.

Planned site (west side of the x=-900 road) is now lined with low-density homes: every dry run there
demolished 2-3 homes and /findspot found 0 valid spots (170 zoned buildings rejected). Adapted site on the
SAME clean core, just west of the highway: /groundwater rates cells (-1082,2046), (-1082,2110), (-1146,2174)
as full (10000) and (-1146,2110) as 8/9. That land is empty and owned; access via a short Small Road spur
from the x=-1249 road at z=2080 (dry run: 0 demolitions, cost 768).
  A: south side of the spur, lot centre ~(-1082,2048)
  B: north side of the spur, lot centre ~(-1146,2112)  (A-B ~90 m apart)
Never x < -1175 (depleted western edge, existing pumps at x=-1216) and never east of the highway.

Usage: python -I council/r2_utilities_aquifer_core_pumps.py dry|road|pumps|verify
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, building, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
PREFAB = "GroundwaterPumpingStation01"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "blocked",
        "wouldDemolish", "conflicts", "pos", "snapping", "newEdgeIds", "builtEdges")
LOG = r"C:\soft\cityskyline\logs\council\r2_utilities_aquifer_core_pumps.json"
SPUR = [[-1249, 2080], [-1050, 2080]]
# (label, target x, side of the east-running spur, acceptance box x0,x1,z0,z1)
PUMPS = [
    ("A south", -1082, "right", (-1110, -1050, 2030, 2070)),
    ("B north", -1146, "left", (-1175, -1115, 2090, 2135)),
]


def show(label, r):
    print(f"{label:<28} {json.dumps(short(r, keys=KEYS))[:900]}", flush=True)
    return r


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)
    return d


def load():
    try:
        return json.load(open(LOG))
    except Exception:
        return {}


def spur_edges():
    """Current Small Road segments on the z=2080 spur (ids change when lots attach)."""
    out = []
    for s in get("/nets", x=-1150, z=2080, radius=120, filter="Small").get("segments") or []:
        if abs(s["start"][1] - 2080) < 2 and abs(s["end"][1] - 2080) < 2 and min(s["start"][0], s["end"][0]) > -1250:
            out.append(s)
    return out


def edge_at(x):
    for s in spur_edges():
        x0, x1 = s["start"][0], s["end"][0]
        if min(x0, x1) <= x <= max(x0, x1) and abs(x1 - x0) > 1:
            return s["id"], round((x - x0) / (x1 - x0), 4), x1 > x0
    return None, None, None


def ok(d, box, placed):
    if d.get("error") or d.get("errors") or d.get("blocked") or d.get("wouldDemolish"):
        return False
    x, z = (d.get("pos") or [0, 0])[:2]
    x0, x1, z0, z1 = box
    if not (x0 <= x <= x1 and z0 <= z <= z1):
        return False
    return all(math.dist((x, z), p) >= 88 for p in placed)


if __name__ == "__main__":
    if MODE in ("dry", "road"):
        with paused("utilities"):
            d = show("spur z=2080 (dry)", road("Small Road", SPUR, dryRun=True))
            if MODE == "road" and d.get("errors") == 0 and not d.get("wouldDemolish") and not d.get("error"):
                r = show("spur z=2080", road("Small Road", SPUR))
                log("road", r)
    elif MODE == "pumps":
        d = load()
        placed = list(d.get("pumps", []))
        placed_pos = [tuple(p["pos"]) for p in placed]
        spent = 0
        with paused("utilities"):
            for label, tx, side_east, box in PUMPS:
                if any(box[0] <= p[0] <= box[1] and box[2] <= p[1] <= box[3] for p in placed_pos):
                    print(label, "already placed")
                    continue
                done = False
                for dx in (0, 8, -8, 16, -16, 24):
                    e, t, east = edge_at(tx + dx)
                    if not e:
                        print(label, "no spur edge at", tx + dx)
                        continue
                    side = side_east if east else ("left" if side_east == "right" else "right")
                    dd = show(f"{label} x={tx + dx} (dry)", building(PREFAB, edge=e, t=t, side=side, dryRun=True))
                    if ok(dd, box, placed_pos):
                        r = show(label, building(PREFAB, edge=e, t=t, side=side))
                        if r.get("applied"):
                            placed.append({"label": label, "id": r.get("placedId"), "pos": r.get("pos") or dd.get("pos"), "cost": r.get("cost")})
                            placed_pos.append(tuple((r.get("pos") or dd.get("pos"))[:2]))
                            spent += r.get("cost") or 0
                            done = True
                            break
                if not done:
                    print(label, "NOT placed")
        log("pumps", placed)
        print("SPENT (pumps this run)", spent)
    elif MODE == "verify":
        d = load()
        ids = {p["id"] for p in d.get("pumps", [])}
        w = get("/services", kind="water")["kinds"]["water"]
        print(json.dumps(w["demand"]))
        for b in w["buildings"]:
            if "Groundwater" in b["prefab"]:
                print(("NEW " if b["id"] in ids else "    ") + b["id"], [round(v) for v in b["pos"]], json.dumps({k: b.get(k) for k in ("processing", "efficiency", "status")}))
        print(json.dumps(get("/buildings", kind="service", x=-1110, z=2080, radius=120))[:1500])
        g = get("/groundwater", x=-1100, z=1900, radius=700)
        print(json.dumps(g.get("bestClean"))[:600])
        print(g.get("grid"))
