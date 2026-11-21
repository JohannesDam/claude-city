"""builder-northeast - b2-utilities-b2-utilities-sea-wwtp: Clean Lake, third wastewater plant.

WastewaterTreatmentPlant01 on the Riverside channel bank (2173,3121), facing the Riverside Drive east leg
(Medium Road 1834540:47), north of the Eastport bridge: downstream of the lake (lake -> channel -> sea). In
this game the WWTP needs no shoreline; it purifies and returns water to the network (domains.md).
Extensions: Extra Processing Unit + Advanced Filtering; Advanced Filtering on both lake plants
1523202:9 and 211058:141. Trees on the frontage.

Usage: python -I builders/builder-northeast_sea_wwtp.py dry|build|ext|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, access_check  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_sea_wwtp.json"
BODY = {"prefab": "WastewaterTreatmentPlant01", "near": [2173, 3121], "within": [1540, 2810, 2400, 3415],
        "radius": 60, "maxHouseholds": 0, "maxCompanies": 0}
LAKE_PLANTS = ["1523202:9", "211058:141"]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def show(r):
    c = r.get("chosen") or {}
    out = {k: c.get(k) for k in ("x", "z", "angle", "edge", "road", "cost", "demolition", "roadConnected")}
    out["placedId"] = r.get("placedId")
    out["error"] = r.get("error")
    return out


def ext(owner, prefab):
    r = post("/extension", {"owner": owner, "prefab": prefab, "side": "auto"}, timeout=120)
    o = {k: r.get(k) for k in ("applied", "cost", "errors", "error", "placedId", "blocked")}
    print("  ", owner, prefab, json.dumps(o), flush=True)
    return o


if MODE == "dry":
    print(json.dumps(show(post("/place", {**BODY, "dryRun": True}, timeout=300))))
    for o in LAKE_PLANTS:
        ex = get("/extensions", owner=o)
        print(o, ex.get("prefab"), [e.get("name") for e in ex.get("installed") or []],
              [(e.get("name"), e.get("canBuild")) for e in ex.get("offered") or []])
elif MODE == "build":
    with paused(WHO):
        r = show(post("/place", BODY, timeout=300))
        print(json.dumps(r), flush=True)
        log("plant", r)
elif MODE == "ext":
    pid = json.load(open(LOG))["plant"]["placedId"]
    with paused(WHO):
        log("ext_epu", ext(pid, "WastewaterTreatmentPlant01 Extra Processing Unit"))
        log("ext_af", ext(pid, "WastewaterTreatmentPlant01 Advanced Filtering System"))
    for o in LAKE_PLANTS:
        with paused(WHO):
            log("af_" + o, ext(o, "WastewaterTreatmentPlant01 Advanced Filtering System"))
    with paused(WHO):
        segs = get("/nets", x=2140, z=3121, radius=70, filter="Medium Road").get("segments") or []
        ids = [s["id"] for s in segs[:3] if not (s.get("upgrades") or {}).get("Trees")]
        if ids:
            t = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"})
            print("trees", ids, t.get("applied"))
elif MODE == "check":
    pid = json.load(open(LOG))["plant"]["placedId"]
    print("access", access_check([pid]))
    c = get("/city")
    print({k: v for k, v in c.items() if "sewage" in k.lower() or "water" in k.lower()})
