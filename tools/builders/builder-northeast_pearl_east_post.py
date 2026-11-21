"""builder-northeast - b3-utilities-pearl-bay-east-post-telecom.

PostOffice01 via /place near (1150,1900): first clean site (1286,2136) on the north side of Small Road
2099027:13 (north-east edge of the bay quarter, next to the new police station). Two RadioMast01 ~1 km apart
on the inland edge: east (1320,2083) on the south side of the same stub, west (306,2201) on Small Road
1617583:83. Trees on the frontages.

Usage: python -I builders/builder-northeast_pearl_east_post.py dry|build|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, access_check  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_pearl_east_post.json"
PB = [-320, 1250, 1350, 2790]
SITES = [
    ("post", "PostOffice01", [1150, 1900], [1200, 2100, 1350, 2200]),
    ("mast_east", "RadioMast01", [1320, 2083], [1290, 2050, 1350, 2100]),
    ("mast_west", "RadioMast01", [306, 2201], [250, 2150, 360, 2250]),
]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def place(prefab, near, box, dry):
    r = post("/place", {"prefab": prefab, "near": near, "within": PB, "radius": 400 if prefab == "PostOffice01" else 40, "maxHouseholds": 0,
                        "maxCompanies": 0, "dryRun": dry}, timeout=300)
    c = r.get("chosen") or {}
    out = {k: c.get(k) for k in ("x", "z", "angle", "edge", "road", "cost", "demolition", "roadConnected")}
    out["placedId"] = r.get("placedId")
    out["error"] = r.get("error")
    return out


if MODE == "dry":
    for name, prefab, near, box in SITES:
        print(name, json.dumps(place(prefab, near, box, True)))
elif MODE == "build":
    for name, prefab, near, box in SITES:
        with paused(WHO):
            r = place(prefab, near, box, False)
            print(name, json.dumps(r), flush=True)
            log(name, r)
            if r.get("placedId"):
                segs = get("/nets", x=r["x"], z=r["z"], radius=50, filter="Road").get("segments") or []
                ids = [s["id"] for s in segs[:2] if not (s.get("upgrades") or {}).get("Trees")]
                if ids:
                    t = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"})
                    print("  trees", ids, t.get("applied"), t.get("errors"))
elif MODE == "check":
    d = json.load(open(LOG))
    ids = [v["placedId"] for v in d.values() if isinstance(v, dict) and v.get("placedId")]
    print("access", access_check(ids))

# Fix (same day): the post office's driveway (1258.7,2098.1) is at the west front corner, beyond the stub's
# dead end (1290,2095); extend the stub 64 m along the lot front (direction of the lot front line).
if MODE == "stub":
    with paused(WHO):
        r = post("/road", {"prefab": "Small Road", "points": [[1290, 2095], [1228, 2076]]})
        print("stub", json.dumps({k: r.get(k) for k in ("applied", "cost", "errors", "newEdgeIds")}))
        log("stub", {k: r.get(k) for k in ("applied", "cost", "newEdgeIds")})
        ids = r.get("newEdgeIds") or []
        if ids:
            t = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"})
            print("trees", t.get("applied"))
