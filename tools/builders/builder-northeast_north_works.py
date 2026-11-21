"""builder-northeast - b3-growth-port-claude-north-works: Port Claude North Works.

The airport reserve (z > 4250) is void: b1-growth-port-claude-airport was superseded by the East Gate
airport. The owned coastal plain x 1540..2560, z 4205..4660 (tile (1870,4363) and east) is empty and
>300 m from the highway, so tile (1247,4363) is not needed now.
- Coast Works Road: curved Medium Road with Trees from Recovery Lane node (1710,4200) north and east over the
  plain to the dead end of the harbour road (2463.5,4495): a second way in, ~1,070 m.
- Works Lane: Small Road with Trees from Recovery Lane node (1940,4200) north to the collector.
- Industrial Manufacturing: /zone rect over the plain (only road-adjacent blocks get zoned); nothing east of
  the harbour road (shore strip).
Roads carry water/sewage/power from the Recovery Lane / Harbour Road network.

Usage: python -I builders/builder-northeast_north_works.py dry|roads|zone-dry|zone|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_north_works.json"
ROADS = [
    ("coast_works_road", "Medium Road", [[1710, 4200], [1690, 4390], [1790, 4535], [2000, 4585], [2250, 4565], [2463.5, 4495]]),
    ("works_lane", "Small Road", [[1940, 4200], [1955, 4390], [2060, 4582]]),
]
ZONE_RECT = [[1545, 4206], [2455, 4655]]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def show(label, r):
    out = short(r)
    g = r.get("geometry") or {}
    out["geo"] = {k: g.get(k) for k in ("length", "minRadius", "warnings")}
    out["snap"] = [s.get("snappedTo") for s in r.get("snapping") or []]
    out["newEdgeIds"] = r.get("newEdgeIds")
    print(f"{label:<18} {json.dumps(out)[:700]}", flush=True)
    return out


if MODE in ("dry", "roads"):
    for name, prefab, pts in ROADS:
        body = {"prefab": prefab, "points": pts, "curve": "smooth"}
        if MODE == "dry":
            show(name, post("/road", {**body, "dryRun": True}))
            continue
        with paused(WHO):
            r = post("/road", body)
            o = show(name, r)
            log(name, o)
            ids = r.get("newEdgeIds") or []
            for i in range(0, len(ids), 4):
                for up in ("Trees", "Grass"):
                    t = post("/roadupgrade", {"edges": ids[i:i + 4], "upgrade": up, "side": "both"})
                    print("   ", up, t.get("applied"), t.get("errors"))
elif MODE in ("zone-dry", "zone"):
    body = {"zone": "Industrial Manufacturing", "mode": "rect", "points": ZONE_RECT, "dryRun": MODE == "zone-dry"}
    if MODE == "zone-dry":
        print(json.dumps(post("/zone", body))[:800])
    else:
        with paused(WHO):
            r = post("/zone", body)
            print(json.dumps(r)[:800])
            log("zone", {k: r.get(k) for k in ("applied", "cellsChanged", "cost", "errors")})
elif MODE == "check":
    z = get("/zonestats") if False else None
    b = get("/buildings", kind="all", x=2000, z=4430, radius=520)
    blds = [x for x in b.get("buildings", []) if x["pos"][1] > 4205]
    print("buildings on the plain", len(blds), sorted({x["prefab"].split("_")[0] for x in blds})[:10])
    print("demand", json.dumps(get("/demand"))[:400])

# Bus 10 (748116:115) for the workers: instead of turning at the harbour road end, run north onto Coast Works
# Road, west and south round the plain, east on Recovery Lane and back to Harbour Road.
# Stops 316420:109 (harbour road southbound) and 312326:85 (Harbour Road westbound) lie off the new path.
if MODE in ("bus-dry", "bus"):
    body = {"line": "748116:115", "remove": ["316420:109", "312326:85"],
            "add": [{"stop": {"x": 1880, "z": 4193}, "after": "292393:61"},
                    {"stop": {"x": 1681, "z": 4383}, "after": "292393:61"},
                    {"stop": {"x": 2180, "z": 4584.5}, "after": "292393:61"}],
            "dryRun": MODE == "bus-dry"}
    if MODE == "bus":
        with paused(WHO):
            r = post("/line/edit", body, timeout=300)
    else:
        r = post("/line/edit", body, timeout=300)
    print(json.dumps({k: r.get(k) for k in ("applied", "error", "warnings")}), json.dumps(r.get("pathCheck"))[:400])
    print([(s.get("index"), s.get("pos")) for s in r.get("stops") or []])
