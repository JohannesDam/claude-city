"""builder-south: Tram T4 "South Ring" (worklist b2-mobility-t3-south-ring-tram; number 3 is taken).

Southgate -> South Ring -> Depot Road -> avenue x=-1909 -> Office Park, with a turning loop at each end:
  - Southgate loop (clockwise): South Ring west (-2240,-436) -> (-2314,-470), Ring 1 arc NW to (-2381,-415),
    radial NNE to (-2350,-345), Ring 2 SE back to the South Ring at (-2240,-436).
  - Office Park loop (counter-clockwise): South Ring east (-2215,-428) -> (-2165,-420) -> Depot Road east ->
    (-1900,-420) -> avenue north (T1 track) -> z=-260 street west -> x=-2129 north -> North Street SW ->
    South Ring (-2215,-428).
  Roundabouts carry no track, so the R(-2480,-600) square itself is not on the line (phase 2: terminus/bypass).
Depots with room near the south end: TramDepot01 1421663:33 (-2068,-500) and 624049:39 (-1692,-497).

usage: python -I builders/builder-south_t4-south-ring-tram.py dry|track|line-dry|line|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

WHO = "builder-south"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"

# (start, end, prefab) by node coordinates; ids are looked up fresh right before each call
EDGES = [
    # South Ring stem + Southgate loop entry
    ((-2165, -420), (-2215, -428), "Large Road"),
    ((-2215, -428), (-2240, -436), "Large Road"),
    ((-2240, -436), (-2314, -470), "Large Road"),
    # Southgate loop
    ((-2314, -470), (-2381, -415), "Small Road"),
    ((-2381, -415), (-2350, -345), "Small Road"),
    ((-2350, -345), (-2289, -384), "Small Road"),
    ((-2289, -384), (-2240, -436), "Small Road"),
    # Office Park loop: North Street, x=-2129, z=-260
    ((-2215, -428), (-2214, -340), "Small Road"),
    ((-2214, -340), (-2205, -268), "Small Road"),
    ((-2205, -268), (-2180, -232), "Small Road"),
    ((-2180, -232), (-2150, -221), "Small Road"),
    ((-2150, -221), (-2129, -220), "Small Road"),
    ((-2129, -220), (-2129, -260), "Small Road"),
    ((-2129, -260), (-2056, -260), "Small Road"),
    ((-2056, -260), (-1982, -260), "Small Road"),
    ((-1982, -260), (-1909, -260), "Small Road"),
]

# line stops in travel order (point on the right-hand side of the travel direction)
STOPS = [
    {"x": -2280.0, "z": -443.0},   # A South Ring westbound (north side), Southgate
    {"x": -2340.5, "z": -433.8},   # B Ring 1 arc (outer side), nearest the station square
    {"x": -2100.0, "z": -427.0},   # C Depot Road eastbound (south side), Health Campus offices
    {"x": -1899.0, "z": -300.0},   # D T1 avenue stop northbound (shared)
    {"x": -2020.0, "z": -254.0},   # E z=-260 westbound (north side), Office Park
    {"x": -2216.0, "z": -303.0},   # F North Street southbound (west side), Office Park / Southgate
]


def find_edge(a, b, prefab):
    mx, mz = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
    rad = ((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2) ** 0.5 / 2 + 25
    r = get("/nets", x=mx, z=mz, radius=rad, limit=200)
    best = None
    for e in r.get("segments", []):
        if e["prefab"] != prefab:
            continue
        s, t = e["start"], e["end"]
        d1 = max(abs(s[0] - a[0]) + abs(s[1] - a[1]), abs(t[0] - b[0]) + abs(t[1] - b[1]))
        d2 = max(abs(s[0] - b[0]) + abs(s[1] - b[1]), abs(t[0] - a[0]) + abs(t[1] - a[1]))
        d = min(d1, d2)
        if d < 12 and (best is None or d < best[0]):
            best = (d, e)
    return best[1] if best else None


def edge_ids():
    ids, missing = [], []
    for a, b, pf in EDGES:
        e = find_edge(a, b, pf)
        if e is None:
            missing.append((a, b, pf))
        else:
            ups = e.get("upgrades") or {}
            has = [k for k, v in ups.items() if v] if isinstance(ups, dict) else ups
            ids.append((e["id"], pf, has))
    return ids, missing


def tracks(dry):
    ids, missing = edge_ids()
    if missing:
        print("MISSING edges", missing)
        return False
    need = [i for i, pf, has in ids if "Oneway Tram Track" not in str(has) and "Tram" not in str(has)]
    print("edges", len(ids), "need track", len(need))
    ok = True
    for k in range(0, len(need), 4):
        batch = need[k:k + 4]
        r = post("/roadupgrade", {"edges": batch, "upgrade": "Double Tram Track", "side": "both", "dryRun": dry})
        print(f"track {batch} applied={r.get('applied')} cost={r.get('cost')} err={r.get('errors')} "
              f"demolish={r.get('wouldDemolish')} {r.get('error') or ''}", flush=True)
        if r.get("error") or r.get("errors"):
            ok = False
    return ok


def line_body(dry):
    return {"type": "tram", "stopPrefab": "NA_TramStop01", "stops": STOPS, "color": "#C8102E", "dryRun": dry}


if MODE == "dry":
    for i, pf, has in edge_ids()[0]:
        print(i, pf, has)
    print("missing", edge_ids()[1])
    tracks(True)
elif MODE == "track":
    with paused(WHO):
        tracks(False)
elif MODE in ("line-dry", "line"):
    dry = MODE == "line-dry"
    if dry:
        r = post("/line", line_body(True), timeout=300)
    else:
        with paused(WHO):
            r = post("/line", line_body(False), timeout=300)
    print(json.dumps(r)[:4000])
elif MODE == "check":
    r = get("/lines")
    for l in r["lines"]:
        if l["type"] == "Tram":
            print(l["id"], l["number"], "veh", l["vehicles"], "wait", l["waitingPassengers"], "len", l["length"],
                  "broken", l["broken"], l.get("failedSegments"))
    for t in ("Track Not Connected", "Pathfind Failed"):
        p = get("/problems", type=t)
        print(t, json.dumps(p)[:800])
