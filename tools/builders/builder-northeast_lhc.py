"""builder-northeast - b3-growth-lhc-collider-tile: Large Hadron Collider east of Science Ridge.

LargeHadronCollider01 has a 54x64-cell lot (432 x 512 m), so it needs most of tile (-623,4363)
(x -935..-312, z 4052..4674; keep z > 4600 free for Harvest Valley).
- Buy tile (-623,4363).
- Collider Drive: a straight Medium Road (Trees+Grass) along x=-905, z 4110..4590, inside the tile's west
  edge, linked by 'Collider Gate' (Medium) from Medical Drive node (-975,4390) - the frontage for the lot.
- /place LargeHadronCollider01 east of it, within the tile, no demolition.

Usage: python -I builders/builder-northeast_lhc.py dry|tile|roads|place-dry|place|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short, access_check  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_lhc.json"
TILE = [-935, 4051, -312, 4675]
ROADS = [
    ("collider_drive", [[-905, 4110], [-905, 4590]]),
    ("collider_gate", [[-975, 4390], [-905, 4390]]),
]
PLACE = {"prefab": "LargeHadronCollider01", "near": [-640, 4350], "within": TILE, "radius": 400,
         "maxHouseholds": 0, "maxCompanies": 0}


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def show_place(r):
    c = r.get("chosen") or {}
    out = {k: c.get(k) for k in ("x", "z", "angle", "edge", "road", "cost", "demolition", "roadConnected", "corners")}
    out["placedId"] = r.get("placedId")
    out["error"] = r.get("error")
    return out


if MODE == "dry":
    for name, pts in ROADS:
        r = post("/road", {"prefab": "Medium Road", "points": pts, "dryRun": True})
        print(name, json.dumps(short(r))[:500], [s.get("snappedTo") for s in r.get("snapping") or []])
elif MODE == "tile":
    with paused(WHO):
        r = post("/buytile", {"x": -623, "z": 4363})
        print(json.dumps(r)[:400])
        log("tile", {k: r.get(k) for k in ("applied", "cost", "price", "error")})
elif MODE == "roads":
    with paused(WHO):
        for name, pts in ROADS:
            r = post("/road", {"prefab": "Medium Road", "points": pts})
            o = short(r)
            o["newEdgeIds"] = r.get("newEdgeIds")
            print(name, json.dumps(o)[:500], flush=True)
            log(name, o)
            ids = r.get("newEdgeIds") or []
            for i in range(0, len(ids), 4):
                for up in ("Trees", "Grass"):
                    post("/roadupgrade", {"edges": ids[i:i + 4], "upgrade": up, "side": "both"})
elif MODE == "place-dry":
    r = post("/place", {**PLACE, "dryRun": True, "all": True}, timeout=600)
    print(json.dumps(show_place(r)))
    for c in (r.get("candidates") or [])[:8]:
        print("  ", {k: c.get(k) for k in ("x", "z", "angle", "edge", "pass", "errors")}, str(c.get("problems") or "")[:120])
elif MODE == "place":
    with paused(WHO):
        r = show_place(post("/place", PLACE, timeout=600))
        print(json.dumps(r), flush=True)
        log("lhc", r)
elif MODE == "check":
    pid = json.load(open(LOG))["lhc"]["placedId"]
    print("access", access_check([pid]))
    for k in ("Electricity Notification", "Water Notification", "Sewage Notification"):
        p = (get("/problems", type=k).get("problems") or [{}])[0]
        print(k, [e for e in p.get("examples", []) if e.get("owner") == pid])
    print("modifiers", [m for m in json.dumps(get("/modifiers")).split('"') if "Electronics" in m][:5])

# Gate square: PocketPark01 on the north side of Collider Gate (-940,4406).
if MODE == "gate-park":
    with paused(WHO):
        r = post("/place", {"prefab": "PocketPark01", "near": [-940, 4410], "within": [-1225, 4051, -312, 4675],
                            "radius": 80, "maxHouseholds": 0, "maxCompanies": 0}, timeout=300)
        print(json.dumps({"placedId": r.get("placedId"), "error": r.get("error")}))
        log("gate_park", {"placedId": r.get("placedId")})
