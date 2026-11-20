"""Council round 1 - Economic Affairs - south-freight-gateway.

Cargo rail terminal on the unused Double Train Track stub that ends at (-2212.6,-1718.8) (node 367288:1).
Axis P(s) = (-2212.6,-1718.8) + s*(0.741,0.672); NW normal n = (-0.672,0.741).
Freight Road (Medium Road) 80 m NW of the axis, joining the old-industry south road at (-1948,-1332).
RailYard02 on the NW side of the Freight Road (unlocks the cargo terminals), CargoTrainTerminal01 between the
Freight Road and the axis, track from the stub end to the terminal.

Usage: python -I council/r1_economy_south_freight_gateway.py unlock|tiles|road|yard|terminal|tracks|ends|verify
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, building, place_smart, find_spot, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "verify"
DEPT = "economy"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "pos", "facing", "bought", "snapping")
LOG = r"C:\soft\cityskyline\logs\council\r1_economy_south_freight_gateway.json"
P0 = (-2212.6, -1718.8)
AX = (0.741, 0.672)
NW = (-0.672, 0.741)
STUB = [-2212.6, -1718.8]
FREIGHT = [[-2444, -1821], [-2207, -1606], [-1955, -1377.1], [-1955, -1330]]  # x=-1948 overlapped Landfill01; -1955 passes between WindTurbine01 and the landfill
TILES = [{"x": -1870, "z": -1870}, {"x": -2493, "z": -1870}]


def P(s, n=0.0):
    return [round(P0[0] + s * AX[0] + n * NW[0], 1), round(P0[1] + s * AX[1] + n * NW[1], 1)]


def sn(p):
    dx, dz = p[0] - P0[0], p[1] - P0[1]
    return round(dx * AX[0] + dz * AX[1], 1), round(dx * NW[0] + dz * NW[1], 1)


def show(label, r):
    print(f"{label:<44} {json.dumps(short(r or {}, keys=KEYS))}", flush=True)
    return r


def logged():
    try:
        return json.load(open(LOG))
    except Exception:
        return {}


def log(key, val):
    d = logged()
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def spent_add(amount, what):
    d = logged()
    d.setdefault("spent", []).append([what, amount])
    json.dump(d, open(LOG, "w"), indent=1)


def seg(prefab, pts, label=None):
    label = label or f"{prefab[:18]} {pts[0]}->{pts[-1]}"
    d = post("/road", {"prefab": prefab, "points": pts, "dryRun": True})
    if d.get("errors") or d.get("error"):
        show(label + " DRY-FAIL", d)
        return None
    r = road(prefab, pts)
    show(label, r)
    if r.get("applied"):
        spent_add(r.get("cost") or 0, label)
    return r


def freight_edges():
    out = []
    for s in get("/nets", x=-2196, z=-1576, radius=420, filter="Medium").get("segments") or []:
        a, b = sn(s["start"]), sn(s["end"])
        if abs(a[1] - 80) < 6 and abs(b[1] - 80) < 6:
            out.append(s)
    return out


def train_ends(x, z, radius=260):
    """Degree-1 train-track nodes near (x, z)."""
    d = get("/nets", x=x, z=z, radius=radius, filter="Train")
    ends = []
    for s in d.get("segments") or []:
        for p, dg, nid in ((s["start"], s["startDegree"], s["startNode"]), (s["end"], s["endDegree"], s["endNode"])):
            if dg == 1:
                ends.append({"node": nid, "pos": p, "sn": sn(p), "seg": s["id"], "prefab": s["prefab"]})
    return ends, d.get("segments") or []


def try_place(prefab, opts, label):
    for o in opts:
        d = post("/building", {"prefab": prefab, **o, "dryRun": True})
        show(f"{label} {json.dumps(o)} dry", d)
        if d.get("errors") == 0 and not d.get("error"):
            r = show(label, building(prefab, **o))
            if r.get("applied"):
                spent_add(r.get("cost") or 0, label)
                log(label, r)
                return r
    return None


if __name__ == "__main__":
    if MODE == "unlock":
        n = [x for x in get("/devtree")["nodes"] if x["name"] == "TrainNode"][0]
        print("TrainNode:", n)
        if n["locked"]:
            r = post("/devtree", {"node": "TrainNode"})
            print("devtree:", r)
            log("devtree", r)
        print("RailYard02:", json.dumps(get("/unlockinfo", name="RailYard02"))[:300])
    elif MODE == "tiles":
        with paused(DEPT):
            for t in TILES:
                r = show(f"buytile {t}", post("/buytile", t))
                if r.get("bought"):
                    spent_add(r.get("cost") or 0, f"tile {t}")
                log(f"tile {t['x']},{t['z']}", r)
    elif MODE == "road":
        print("freight points s/n:", [sn(p) for p in FREIGHT])
        with paused(DEPT):
            for a, b in zip(FREIGHT, FREIGHT[1:]):
                seg("Medium Road", [a, b])
        print("freight edges:", [(s["id"], s["start"], s["end"]) for s in freight_edges()])
    elif MODE == "yard":
        segs = freight_edges()
        first = [s for s in segs if sn(s["start"])[0] < 0 or sn(s["end"])[0] < 0]
        opts = []
        for s in first:
            fwd = sn(s["end"])[0] > sn(s["start"])[0]
            side = "left" if fwd else "right"  # NW of a road heading NE is its left
            for t in (0.7, 0.5, 0.4, 0.6, 0.3):  # as built: 271106:127 t=0.7 left (centre s=-104, n=152)
                opts.append({"edge": s["id"], "t": t, "side": side})
        c = P(-105, 152)
        opts.append({"x": c[0], "z": c[1], "angle": 137.8})
        with paused(DEPT):
            r = try_place("RailYard02", opts, "RailYard02")
            if r is None:
                r, c = place_smart("RailYard02", c[0], c[1], radius=200)
                show("RailYard02 place_smart", r or c)
                if r and r.get("applied"):
                    spent_add(r.get("cost") or 0, "RailYard02")
                    log("RailYard02", r)
        print("CargoTrainTerminal01:", json.dumps(get("/unlockinfo", name="CargoTrainTerminal01"))[:900])
    elif MODE == "terminal":
        # as built: centre s=190 on the axis, fronting Freight Road edge 1194062:37 (SE side = right)
        with paused(DEPT):
            try_place("CargoTrainTerminal01", [{"edge": "1194062:37", "t": 0.405, "side": "right"}], "CargoTrainTerminal01")
    elif MODE == "tracks":
        # Track ends are not listed by /nets (building sub-nets); they were read from the 'No Train Access'
        # icons. /bulldoze cannot remove net edges, so the stub 523893:1 stays and the terminal gets its own
        # branch from mainline node 367289:1 (bends <= 15.6 deg), 24 m SE of the stub.
        term_end = [-2173.5, -1711.2]                      # terminal SW connection (s 34.1, n -20.6)
        yard_end = [-2486.4, -1786.2]                      # rail yard SW connection, track n=134.1
        branch = [[-2387.5, -1877.3], P(-150, -24), P(-60, -24), term_end]
        yard = [yard_end, [-2516.3, -1813.1], [-2548.8, -1863.6], [-2611.8, -2059.8], [-2644.3, -2110.3]]
        with paused(DEPT):
            log("track_f2", seg("Double Train Track", branch, "track mainline->terminal SW end"))
            log("track_yard", seg("Twoway Train Track", yard, "yard SW end -> mainline"))
        print("No Train Access:", json.dumps(get("/problems", type="No Train Access"))[:600])
    elif MODE == "ends":
        y = logged().get("RailYard02") or {}
        if y.get("pos"):
            e, segs = train_ends(y["pos"][0], y["pos"][1])
            print("yard train segs:", [(s["id"], s["prefab"], s["start"], s["end"], s["startDegree"], s["endDegree"]) for s in segs])
            print("yard ends:", e)
        t = logged().get("CargoTrainTerminal01") or {}
        if t.get("pos"):
            e, segs = train_ends(t["pos"][0], t["pos"][1])
            print("terminal train segs:", [(s["id"], s["prefab"], s["start"], s["end"], s["startDegree"], s["endDegree"]) for s in segs])
            print("terminal ends:", e)
    elif MODE == "verify":
        for k in ("RailYard02", "CargoTrainTerminal01"):
            v = logged().get(k)
            if v and v.get("placedId"):
                b = get("/buildings", kind="service", x=v["pos"][0], z=v["pos"][1], radius=30)
                print(k, [(e["id"], e["prefab"], e["roadConnected"]) for e in b.get("buildings", []) if e["id"] == v["placedId"]])
        print("No Inputs:", json.dumps(get("/problems", type="No Inputs"))[:600])
        print("Not connected problems:", json.dumps(get("/problems", type="Not Connected"))[:300])
        print("lines:", json.dumps(get("/lines"))[:400])
        print("spent:", sum(a for _, a in logged().get("spent", [])), logged().get("spent"))
