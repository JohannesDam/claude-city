"""Council round 2 - Utilities - north-wind-gate.

16 x WindTurbine01 on the empty owned block x -1010..-442, z 3512..4040 (/freeland), wind 0.54 (>= 0.40 for
100 %). Access: the planned dead end (-800,3400) is now the merge node of Mobility's R3-north off-ramp
(1804002:7 -> node 1046067:7), so the spine leaves the z=3400 Small Road 50 m further west at x=-850 and
does not touch the ramp junction. Rows z = 3530/3655/3780/3905 from x=-925 (just east of the University
quarter, x -935) to x=-490 (54 m from the trunk). Turbines on the NORTH side of each row at
x = -905, -760, -635, -510 (125-145 m apart; rows 125 m apart).

Modes:
  roads-dry | roads            spine + 4 rows
  turbines [n_max]             place by edge + t on the rows (findspot is stale on fresh roads), dry run first
  verify                       new turbines: WindSpeed, capacity, producerEdge bottleneck; problems
  feed2-dry | feed2            second feed (-925,3530) -> (-960,3400) if the park bottlenecks
  retire-check                 is the park >= 850k? (step 5 precondition, needs 2 game days of proof)
  retire-dry | retire          bulldoze the 13 weak SW turbines (only after retire-check passed for 2 game days)
  battery                      backup check (dischargeEdge.disconnected of the two batteries)

Usage: python -I council/r2_utilities_north_wind_gate.py <mode>
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, building, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "verify"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "blocked",
        "wouldDemolish", "conflicts", "pos", "snapping", "newEdgeIds")
LOG = r"C:\soft\cityskyline\logs\council\r2_utilities_north_wind_gate.json"
SPINE_X = -850
ROWS = [3530, 3655, 3780, 3905]
COLS = [-905, -760, -635, -510]
ROW_W, ROW_E = -925, -490
RETIRE = ['254152:99', '495456:13', '249714:153', '294698:99', '177044:789', '243591:31', '177146:359', '246483:69',
          '326728:133', '252419:97', '425731:13', '229607:111', '452266:11']


def show(label, r):
    print(f"{label:<34} {json.dumps(short(r, keys=KEYS))[:700]}", flush=True)
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


def row_edge(Z, x):
    """Current Small Road segment of row Z that contains x: (id, t, runs_east)."""
    for s in get("/nets", x=x, z=Z, radius=90, filter="Small").get("segments") or []:
        (x0, z0), (x1, z1) = s["start"][:2], s["end"][:2]
        if abs(z0 - Z) > 1.5 or abs(z1 - Z) > 1.5 or abs(x1 - x0) < 1:
            continue
        if min(x0, x1) <= x <= max(x0, x1):
            return s["id"], round((x - x0) / (x1 - x0), 4), x1 > x0
    return None, None, None


def park_state(ids):
    k = get("/services", kind="electricity")["kinds"]["electricity"]
    out = {}
    for b in k["buildings"]:
        if b["id"] in ids:
            pe = (b.get("grid") or {}).get("producerEdge") or {}
            out[b["id"]] = {"pos": [round(v, 1) for v in b["pos"]], "capacity": b.get("capacity"), "production": b.get("production"),
                            "wind": (b.get("efficiencyFactors") or {}).get("WindSpeed", 1.0), "status": b.get("status"),
                            "bottleneck": pe.get("bottleneck"), "beyond": pe.get("beyondBottleneck"), "disconnected": pe.get("disconnected"),
                            "flow": pe.get("flow")}
    return out, k["demand"], k["totals"]


if __name__ == "__main__":
    if MODE in ("roads-dry", "roads"):
        plan = [("spine x=-850", [[SPINE_X, 3400], [SPINE_X, 3530], [SPINE_X, 3655], [SPINE_X, 3780], [SPINE_X, 3905]])]
        plan += [(f"row z={Z}", [[ROW_W, Z], [SPINE_X, Z], [ROW_E, Z]]) for Z in ROWS]
        res, spent = [], 0
        with paused("utilities"):
            for label, pts in plan:
                d = show(f"{label} (dry)", road("Small Road", pts, dryRun=True))
                if MODE == "roads" and d.get("errors") == 0 and not d.get("error") and not d.get("wouldDemolish"):
                    r = show(label, road("Small Road", pts))
                    res.append({"label": label, "applied": r.get("applied"), "cost": r.get("cost"), "edges": r.get("newEdgeIds")})
                    spent += r.get("cost") or 0 if r.get("applied") else 0
        if MODE == "roads":
            log("roads", res)
            print("SPENT roads", spent)
    elif MODE == "turbines":
        n_max = int(sys.argv[2]) if len(sys.argv) > 2 else 16
        d = load()
        placed = list(d.get("turbines", []))
        have = {(p["row"], p["col"]) for p in placed}
        todo = [(Z, X) for Z in ROWS for X in COLS if (Z, X) not in have]
        spent, n = 0, 0
        for i in range(0, len(todo), 6):  # short batches: other crews may wait for the build lock
            if n >= n_max:
                break
            with paused("utilities"):
                for Z, X in todo[i:i + 6]:
                    if n >= n_max:
                        break
                    done = False
                    for dx in (0, 6, -6, 12, -12):
                        e, t, east = row_edge(Z, X + dx)
                        if not e:
                            print(f"turbine ({X + dx},{Z}): no row edge", flush=True)
                            continue
                        side = "left" if east else "right"  # north side of the row
                        dd = post("/building", {"prefab": "WindTurbine01", "edge": e, "t": t, "side": side, "dryRun": True})
                        pz = (dd.get("pos") or [0, 0])[1]
                        if dd.get("errors") == 0 and not dd.get("error") and not dd.get("wouldDemolish") and Z + 10 <= pz <= Z + 35:
                            r = show(f"turbine ({X + dx},{Z}) {e}@{t}", building("WindTurbine01", edge=e, t=t, side=side))
                            if r.get("applied"):
                                placed.append({"row": Z, "col": X, "id": r.get("placedId"), "pos": r.get("pos") or dd.get("pos"), "cost": r.get("cost")})
                                spent += r.get("cost") or 0
                                n += 1
                                done = True
                                break
                        else:
                            show(f"turbine ({X + dx},{Z}) (dry)", dd)
                    if not done:
                        print(f"turbine ({X},{Z}) NOT placed", flush=True)
            log("turbines", placed)
        print(f"placed {n} this run, park total {len(placed)}, SPENT (this run) {spent}")
    elif MODE == "verify":
        d = load()
        ids = {p["id"] for p in d.get("turbines", [])}
        st, dem, tot = park_state(ids)
        for i, v in sorted(st.items(), key=lambda kv: (kv[1]["pos"][1], kv[1]["pos"][0])):
            print(i, json.dumps(v))
        cap = sum(v["capacity"] or 0 for v in st.values())
        prod = sum(v["production"] or 0 for v in st.values())
        print(f"park: {len(st)} turbines, capacity {cap}, production {prod}, bottlenecks {sum(1 for v in st.values() if v['bottleneck'] or v['beyond'])}")
        print("grid", json.dumps(dem))
        print(json.dumps(get("/problems", type="Electricity"))[:1200])
        c = get("/city")
        print(c["date"], json.dumps(c["electricity"]))
        print(json.dumps(get("/buildings", kind="service", x=-700, z=3720, radius=330))[:600])
    elif MODE in ("feed2-dry", "feed2"):
        with paused("utilities"):
            pts = [[ROW_W, 3530], [-960, 3400]]
            dd = show("feed2 (dry)", road("Small Road", pts, dryRun=True))
            if MODE == "feed2" and dd.get("errors") == 0 and not dd.get("wouldDemolish") and not dd.get("error"):
                log("feed2", show("feed2", road("Small Road", pts)))
    elif MODE == "retire-check":
        d = load()
        ids = {p["id"] for p in d.get("turbines", [])}
        st, dem, tot = park_state(ids)
        prod = sum(v["production"] or 0 for v in st.values())
        # production is demand-limited (the grid exports/curtails a surplus), so the proof is CAPACITY at full wind
        # with no bottleneck; production is logged alongside.
        cap = sum(v["capacity"] or 0 for v in st.values())
        wmin = min((v["wind"] for v in st.values()), default=0)
        bn = sum(1 for v in st.values() if v["bottleneck"] or v["beyond"] or v["disconnected"])
        c = get("/city")
        checks = list(d.get("retireChecks", []))
        checks.append({"date": c["date"], "parkCapacity": cap, "parkProduction": prod, "minWind": wmin, "bottlenecks": bn,
                       "turbines": len(st), "grid": c["electricity"], "trade": dem.get("trade")})
        log("retireChecks", checks)
        print(json.dumps(checks[-1]))
        print("park capacity >= 850k and no bottleneck now:", cap >= 850000 and bn == 0,
              "| checks so far:", [(x["date"], x.get("parkCapacity"), x["parkProduction"]) for x in checks])
    elif MODE in ("retire-dry", "retire"):
        r = show("bulldoze 13 SW turbines (dry)", post("/bulldoze", {"ids": RETIRE, "dryRun": True}))
        print(json.dumps(r)[:1500])
        if MODE == "retire":
            if not (r.get("errors") == 0 or r.get("ok")) or r.get("error"):
                print("dry run not clean - not retiring")
            else:
                with paused("utilities"):
                    rr = show("bulldoze 13 SW turbines", post("/bulldoze", {"ids": RETIRE}))
                log("retired", rr)
                print(json.dumps(rr)[:1500])
    elif MODE == "battery":
        k = get("/services", kind="electricity")["kinds"]["electricity"]
        for b in k["buildings"]:
            if b["id"] in ("512995:49", "1503856:33"):
                g = b.get("grid") or {}
                print(b["id"], json.dumps({"discharge": g.get("dischargeEdge"), "charge": g.get("chargeEdge"), "hv": g.get("highVoltageConnected"), "battery": b.get("battery")}))
