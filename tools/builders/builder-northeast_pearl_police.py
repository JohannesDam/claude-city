"""builder-northeast - b3-health_safety-pearl-bay-police.

PoliceStation03 via /place near (1050,1850); the first clean candidate is (1235,2008) on Small Road
1616488:71 at the north-east edge of the bay quarter, inside the worst-coverage cells (1209..1509, 1750..2050),
off the promenade. Trees on the frontage. The care-post site (x 500-700, z 1700-1900) stays free.

Usage: python -I builders/builder-northeast_pearl_police.py dry|build|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, access_check  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_pearl_police.json"
BODY = {"prefab": "PoliceStation03", "near": [1235, 2008], "within": [1150, 1930, 1340, 2080], "radius": 120,
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
        log("police", r)
        if r.get("placedId"):
            segs = get("/nets", x=r["x"], z=r["z"], radius=60, filter="Road").get("segments") or []
            ids = [s["id"] for s in segs[:2] if not (s.get("upgrades") or {}).get("Trees")]
            if ids:
                t = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"})
                print("trees", ids, t.get("applied"), t.get("cost"), t.get("errors"))
                log("trees", {"edges": ids, "applied": t.get("applied")})
elif MODE == "check":
    d = json.load(open(LOG))
    print("access", access_check([d["police"]["placedId"]]))
