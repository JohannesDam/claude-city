"""builder-northeast - b3-education-b3-edu-pearl-bay-high.

HighSchool03 via /place at (887,2201) on the east side of the x~855 Medium Road 1297563:7 (Bay Line stops
at (848/862, 2142), ~60 m), then its Extension Floors, then Trees on the frontage.

Usage: python -I builders/builder-northeast_pearl_high.py dry|build|ext|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, access_check  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_pearl_high.json"
BODY = {"prefab": "HighSchool03", "near": [887, 2201], "within": [-320, 1250, 1350, 2790], "radius": 60,
        "maxHouseholds": 0, "maxCompanies": 0}


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


if MODE == "dry":
    print(json.dumps(show(post("/place", {**BODY, "dryRun": True}, timeout=300))))
elif MODE == "build":
    with paused(WHO):
        r = show(post("/place", BODY, timeout=300))
        print(json.dumps(r), flush=True)
        log("school", r)
elif MODE == "ext":
    sid = json.load(open(LOG))["school"]["placedId"]
    ex = get("/extensions", owner=sid)
    names = [e.get("name") or e.get("prefab") for e in (ex.get("buildable") or ex.get("extensions") or ex.get("available") or [])]
    print("extensions", json.dumps(ex)[:600])
    with paused(WHO):
        r = post("/extension", {"owner": sid, "prefab": "HighSchool03 Extension Floors", "side": "auto"}, timeout=120)
        print("floors", json.dumps({k: r.get(k) for k in ("applied", "cost", "errors", "error", "placedId", "wouldDemolish", "blocked")}))
        log("floors", {k: r.get(k) for k in ("applied", "cost", "placedId", "error")})
        segs = get("/nets", x=887, z=2201, radius=70, filter="Medium Road").get("segments") or []
        ids = [s["id"] for s in segs[:2] if not (s.get("upgrades") or {}).get("Trees")]
        if ids:
            t = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"})
            print("trees", ids, t.get("applied"), t.get("errors"))
elif MODE == "check":
    sid = json.load(open(LOG))["school"]["placedId"]
    print("access", access_check([sid]))
