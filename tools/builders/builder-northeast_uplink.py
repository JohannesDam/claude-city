"""builder-northeast - b2-utilities-b2-utilities-uh-satellite-uplink.

SatelliteUplink01 via /place near (-1050,3800): first clean lot (-1031,3847) on the east side of Research Way
(Medium, x=-1060), the east edge of University Hill facing Science Ridge; no zoning around it. A PocketPark
viewing plaza beside it and Trees on the frontage.

Usage: python -I builders/builder-northeast_uplink.py dry|build|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, access_check  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_uplink.json"
UH = [-2178, 3542, -937, 4673]
BODY = {"prefab": "SatelliteUplink01", "near": [-1050, 3800], "within": UH, "radius": 400, "maxHouseholds": 0,
        "maxCompanies": 0}


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def show(r):
    c = r.get("chosen") or {}
    out = {k: c.get(k) for k in ("x", "z", "angle", "edge", "cost", "demolition", "corners")}
    out["placedId"] = r.get("placedId")
    out["error"] = r.get("error")
    return out


if MODE == "dry":
    print(json.dumps(show(post("/place", {**BODY, "dryRun": True}, timeout=400))))
elif MODE == "build":
    with paused(WHO):
        r = show(post("/place", BODY, timeout=400))
        print(json.dumps(r), flush=True)
        log("uplink", r)
    if r.get("placedId"):
        with paused(WHO):
            p = post("/place", {"prefab": "PocketPark02", "near": [r["x"], r["z"] - 45], "within": UH, "radius": 120,
                                "maxHouseholds": 0, "maxCompanies": 0}, timeout=300)
            c = p.get("chosen") or {}
            print("plaza", p.get("placedId"), c.get("x"), c.get("z"), p.get("error"))
            log("plaza", {"placedId": p.get("placedId"), "pos": [c.get("x"), c.get("z")]})
            segs = get("/nets", x=-1060, z=r["z"], radius=60, filter="Medium Road").get("segments") or []
            ids = [s["id"] for s in segs[:2] if not (s.get("upgrades") or {}).get("Trees")]
            if ids:
                print("trees", post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"}).get("applied"))
elif MODE == "check":
    d = json.load(open(LOG))
    print("access", access_check([d["uplink"]["placedId"]]))
