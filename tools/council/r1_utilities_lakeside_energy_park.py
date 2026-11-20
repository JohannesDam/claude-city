"""Council round 1 - Utilities - lakeside-energy-park.

Buy tiles (-623,-1247) and (-623,-1870), open them with Small Roads from SE utilities, then a row of
WindTurbine01 on the lake side of an x=-760 spine. Solar is dropped (its precondition, HV lines that snap,
was not met) -> wind-only variant + battery. Turbines are tested first: wind in the south is weak
(WindSpeed factor 0.26-0.6 on existing southern turbines), so the row is only filled if the test turbines
reach a useful factor.

Usage: python -I council/r1_utilities_lakeside_energy_park.py tiles|roads-dry|roads|test|fill|cols-roads-dry|cols-roads|column|battery|verify

As run on 2030-01-12: tiles, roads, 2 test turbines (WindSpeed 0.71 north / 0.42 south), fill left
(turbines need a ~115-120 m clear circle -> only 7 fit on the spine), cols-roads, column -885 east,
column -1010 east, then (inline) Small Roads (-760,-1000)->(-640,-1000)->(-640,-1520) and
column -640 east; battery placed by edge on the south side of the z=-1780 link (findspot was stale on
the new roads); the 4 hospital-campus turbines were bulldozed once production was 1.81x consumption.
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, building, road, find_spot, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "verify"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "pos", "snapping")
LOG = r"C:\soft\cityskyline\logs\council\r1_utilities_lakeside_energy_park.json"
TILES = [(-623, -1247), (-623, -1870)]
ROADS = [
    ("link z=-1665", [[-1156, -1665], [-760, -1665]]),
    ("spine north", [[-760, -1000], [-760, -1665]]),
    ("spine south", [[-760, -1665], [-760, -2000]]),
    ("link z=-1780", [[-1156, -1780], [-760, -1780]]),
]


def show(label, r):
    print(f"{label:<36} {json.dumps(short(r, keys=KEYS))}", flush=True)
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


def turbine(x, z, radius=40):
    """Best validated WindTurbine01 spot near (x, z) on our new roads; returns (result, candidate)."""
    for c in find_spot("WindTurbine01", x, z, radius=radius, n=6):
        b = dict(c["building"])
        b.pop("prefab", None)
        d = post("/building", {"prefab": "WindTurbine01", **b, "dryRun": True})
        if d.get("errors") == 0 and not d.get("error"):
            return building("WindTurbine01", **b), c
    return None, None


def wind_of(ids):
    k = get("/services", kind="electricity")["kinds"]["electricity"]
    return {b["id"]: (b["pos"], b.get("capacity"), (b.get("efficiencyFactors") or {}).get("WindSpeed", 1.0))
            for b in k["buildings"] if b["id"] in ids}


if __name__ == "__main__":
    if MODE == "tiles":
        out = []
        with paused("utilities"):
            for x, z in TILES:
                d = show(f"tile {x},{z} (dry)", post("/buytile", {"x": x, "z": z, "dryRun": True}))
                if d.get("status") == "None":
                    r = post("/buytile", {"x": x, "z": z})
                    print(f"tile {x},{z}", json.dumps(r))
                    out.append(r)
        log("tiles", out)
    elif MODE in ("roads-dry", "roads"):
        res = []
        with paused("utilities"):
            for label, pts in ROADS:
                d = show(f"{label} (dry)", road("Small Road", pts, dryRun=True))
                if MODE == "roads" and d.get("errors") == 0 and not d.get("error"):
                    res.append(show(label, road("Small Road", pts)))
        if MODE == "roads":
            log("roads", res)
    elif MODE == "test":
        # one turbine at the north end and one at the south end of the spine
        placed = []
        with paused("utilities"):
            for z in (-1060, -1940):
                r, c = turbine(-730, z)
                show(f"test turbine z={z}", r or {})
                if r and r.get("applied"):
                    placed.append(r["placedId"])
        log("testTurbines", placed)
    elif MODE == "wind":
        n_max = int(sys.argv[2]) if len(sys.argv) > 2 else 24
        d = load()
        placed = list(d.get("testTurbines", []))
        spent = 0
        zs = [z for z in range(-1980, -1000, 40)]
        for i in range(0, len(zs), 6):  # short batches: other crews may wait for the build lock
            with paused("utilities"):
                for z in zs[i:i + 6]:
                    if len(placed) >= n_max:
                        break
                    r, c = turbine(-730, z)
                    if r and r.get("applied"):
                        placed.append(r["placedId"])
                        spent += r.get("cost") or 0
                    show(f"turbine z={z}", r or {"error": "no spot"})
        log("turbines", placed)
        print("turbines", len(placed), "SPENT (this run)", spent)
    elif MODE == "fill":
        # /findspot is stale on the freshly built spine, so place by edge + t on a 40 m grid.
        # side "left" = lake (east) side of the southbound spine edges, "right" = west side.
        side = sys.argv[2] if len(sys.argv) > 2 else "left"
        z_from, z_to = (float(sys.argv[3]), float(sys.argv[4])) if len(sys.argv) > 4 else (-1007.5, -1990)
        n_max = int(sys.argv[5]) if len(sys.argv) > 5 else 99
        segs = json.load(open(r"C:\soft\cityskyline\logs\council\r1_utilities_spine_edges.json"))
        d = load()
        placed = list(d.get("turbines", d.get("testTurbines", [])))
        existing = [v[0] for v in wind_of(set(placed)).values()]
        xside = -739.5 if side == "left" else -780.5
        grid = []
        z = z_from
        while z >= z_to:
            grid.append(z)
            z -= 40
        todo = [z for z in grid if not any(abs(p[1] - z) < 30 and abs(p[0] - xside) < 5 for p in existing)]

        def spine_edge(z):
            """Current spine edge containing z (placing a turbine replaces edges, so look it up fresh)."""
            for s in get("/nets", x=-760, z=z, radius=80, filter="Small")["segments"]:
                if abs(s["start"][0] + 760) > 1 or abs(s["end"][0] + 760) > 1:
                    continue
                z0, z1 = s["start"][1], s["end"][1]
                lo, hi = min(z0, z1), max(z0, z1)
                if lo <= z <= hi and hi - lo > 1:
                    return s["id"], round((z0 - z) / (z0 - z1), 4), (z0 > z1)
            return None, None, None

        spent, n = 0, 0
        for i in range(0, len(todo), 6):  # short batches: other crews may wait for the build lock
            if n >= n_max:
                break
            with paused("utilities"):
                for z in todo[i:i + 6]:
                    if n >= n_max:
                        break
                    e, t, south = spine_edge(z)
                    if not e:
                        print(f"turbine z={z}: no spine edge", flush=True)
                        continue
                    s_side = side if south else ("right" if side == "left" else "left")
                    dd = post("/building", {"prefab": "WindTurbine01", "edge": e, "t": t, "side": s_side, "dryRun": True})
                    if dd.get("errors") == 0 and not dd.get("error") and abs((dd.get("pos") or [0, 0])[1] - z) < 6:
                        r = building("WindTurbine01", edge=e, t=t, side=s_side)
                        if r.get("applied"):
                            placed.append(r["placedId"])
                            spent += r.get("cost") or 0
                            n += 1
                        show(f"turbine {side} z={z} {e}@{t}", r)
                    else:
                        show(f"turbine {side} z={z} {e}@{t} (dry)", dd)
            log("turbines", placed)
        print(f"placed {n} ({side}), total park turbines {len(placed)}, SPENT (this run) {spent}")
    elif MODE in ("cols-roads-dry", "cols-roads"):
        # turbines need a ~115-120 m clear circle, so add N-S access roads 125 m apart (x=-885, x=-1010)
        # inside the approved (now unused) solar footprint, and close the loop along z=-1000
        COLS = [
            ("col B x=-885 north", [[-885, -1000], [-885, -1665]]),
            ("col B x=-885 south", [[-885, -1665], [-885, -1780]]),
            ("col D x=-1010", [[-1010, -1000], [-1010, -1665]]),
            ("loop z=-1000 west", [[-1010, -1000], [-885, -1000]]),
            ("loop z=-1000 east", [[-885, -1000], [-760, -1000]]),
        ]
        res = []
        with paused("utilities"):
            for label, pts in COLS:
                dd = show(f"{label} (dry)", road("Small Road", pts, dryRun=True))
                if MODE == "cols-roads" and dd.get("errors") == 0 and not dd.get("error"):
                    res.append(show(label, road("Small Road", pts)))
        if MODE == "cols-roads":
            log("colRoads", res)
    elif MODE == "column":
        # python ... column <road_x> <turbine side: east|west> <z_from> <z_to> <pitch> [n_max]
        rx, tside = float(sys.argv[2]), sys.argv[3]
        z_from, z_to, pitch = float(sys.argv[4]), float(sys.argv[5]), float(sys.argv[6])
        n_max = int(sys.argv[7]) if len(sys.argv) > 7 else 99
        d = load()
        placed = list(d.get("turbines", []))

        def col_edge(z):
            for s in get("/nets", x=rx, z=z, radius=80, filter="Small")["segments"]:
                if abs(s["start"][0] - rx) > 1 or abs(s["end"][0] - rx) > 1:
                    continue
                z0, z1 = s["start"][1], s["end"][1]
                if min(z0, z1) <= z <= max(z0, z1) and abs(z0 - z1) > 1:
                    return s["id"], round((z0 - z) / (z0 - z1), 4), z0 > z1
            return None, None, None

        spent, n, z = 0, 0, z_from
        zs = []
        while z >= z_to:
            zs.append(z)
            z -= pitch
        for i in range(0, len(zs), 6):
            if n >= n_max:
                break
            with paused("utilities"):
                for z in zs[i:i + 6]:
                    if n >= n_max:
                        break
                    e, t, south = col_edge(z)
                    if not e:
                        print(f"x={rx} z={z}: no edge", flush=True)
                        continue
                    # southbound edge: left = east; northbound: left = west
                    side = ("left" if tside == "east" else "right") if south else ("right" if tside == "east" else "left")
                    dd = post("/building", {"prefab": "WindTurbine01", "edge": e, "t": t, "side": side, "dryRun": True})
                    if dd.get("errors") == 0 and not dd.get("error") and abs((dd.get("pos") or [0, 0])[1] - z) < 6:
                        r = show(f"turbine x={rx} {tside} z={z}", building("WindTurbine01", edge=e, t=t, side=side))
                        if r.get("applied"):
                            placed.append(r["placedId"])
                            spent += r.get("cost") or 0
                            n += 1
                    else:
                        show(f"turbine x={rx} {tside} z={z} (dry)", dd)
            log("turbines", placed)
        print(f"placed {n} on x={rx}, total park turbines {len(placed)}, SPENT (this run) {spent}")
    elif MODE == "battery":
        with paused("utilities"):
            r = None
            for c in find_spot("EmergencyBatteryStation01", -900, -1900, radius=200, n=6):
                b = dict(c["building"])
                b.pop("prefab", None)
                dd = show(f"battery {b} (dry)", post("/building", {"prefab": "EmergencyBatteryStation01", **b, "dryRun": True}))
                if dd.get("errors") == 0 and not dd.get("error"):
                    r = show("battery energy park", building("EmergencyBatteryStation01", **b))
                    break
        log("battery", r)
    elif MODE == "verify":
        d = load()
        ids = set(d.get("turbines", d.get("testTurbines", [])))
        w = wind_of(ids)
        for i, v in sorted(w.items(), key=lambda kv: kv[1][0][1]):
            print(i, v)
        if w:
            print("turbines", len(w), "capacity", sum(v[1] or 0 for v in w.values()),
                  "avg WindSpeed", round(sum(v[2] for v in w.values()) / len(w), 2))
        print(json.dumps(get("/problems", type="Electricity")))
        print(json.dumps(get("/city").get("electricity")))
