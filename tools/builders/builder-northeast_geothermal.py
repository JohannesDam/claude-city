"""builder-northeast - conductor 2033-01-10: clean base-load power (the grid leans on wind + 640k import).

GeothermalPowerPlant01 (lot 44x35 = 352 x 280 m, $750k) has no free site in NE utilities / Wind Fields /
North Works, so: buy tile (623,4363) (x 312..935, z 4052..4674, only its NW corner touches the highway),
lay 'Geo Lane' (Small Road, Trees) north from the wind-field loop lane node (590,3990), place the plant on
its east side, then a High-voltage Line from the plant's HV point to a TransformerStation02 (grid tie-in).

Usage: python -I builders/builder-northeast_geothermal.py tile|road-dry|road|place-dry|place|hv-dry|hv|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short, access_check  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "road-dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_geothermal.json"
TILE = [312, 4052, 935, 4674]
ROAD = [[590, 3990], [590, 4460]]
PLACE = {"prefab": "GeothermalPowerPlant01", "near": [740, 4260], "within": [312, 3428, 935, 4674], "radius": 400,
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
    out = {k: c.get(k) for k in ("x", "z", "angle", "edge", "cost", "demolition", "roadConnected", "corners")}
    out["placedId"] = r.get("placedId")
    out["error"] = r.get("error")
    return out


if MODE == "tile":
    with paused(WHO):
        r = post("/buytile", {"x": 623, "z": 4363})
        print(json.dumps(r)[:300])
        log("tile", r)
elif MODE in ("road-dry", "road"):
    body = {"prefab": "Small Road", "points": ROAD, "dryRun": MODE == "road-dry"}
    if MODE == "road-dry":
        print(json.dumps(short(post("/road", body))))
    else:
        with paused(WHO):
            r = post("/road", body)
            ids = r.get("newEdgeIds") or []
            print(json.dumps(short(r)), ids)
            log("road", ids)
            for i in range(0, len(ids), 4):
                post("/roadupgrade", {"edges": ids[i:i + 4], "upgrade": "Trees", "side": "both"})
elif MODE == "place-dry":
    r = post("/place", {**PLACE, "dryRun": True, "all": True}, timeout=600)
    print(json.dumps(show_place(r)))
    for c in (r.get("candidates") or [])[:6]:
        print("  ", {k: c.get(k) for k in ("x", "z", "angle", "pass", "errors")}, str(c.get("problems") or c.get("reason") or "")[:150])
elif MODE == "place":
    with paused(WHO):
        r = show_place(post("/place", PLACE, timeout=600))
        print(json.dumps(r), flush=True)
        log("plant", r)
elif MODE == "check":
    d = json.load(open(LOG))
    pid = d["plant"]["placedId"]
    print("access", access_check([pid]))
    print("city", get("/city").get("electricity"))
    s = get("/services", kind="electricity")["kinds"]["electricity"]
    print("trade", json.dumps(s["demand"].get("trade"))[:200])
    for b in s.get("buildings", []):
        if b.get("id") in (pid, d.get("transformer")):
            print(json.dumps(b)[:500])
