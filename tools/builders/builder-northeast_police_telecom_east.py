"""builder-northeast - b2-health_safety-eastern-works-port-police-post + b2-utilities-b2-utilities-telecom-east.

- PoliceStation03 at the item's findspot lot (554,2761) on Medium Road 1747926:159 (Eastern Works south edge,
  covers Eastern Works south/Riverside industry and the first Pearl Bay homes north of z 2500).
- TelecomTower01 for Port Claude at (1976,3909) on Medium Road 1792570:15 (by J_ne, away from the cranes and the
  recovery park).
- TelecomTower01 for Eastern Works: the industrial grid near (600,3100) has no gap (every lot clears 4-6
  companies), so it goes on free land just north of the estate (NE utilities / wind-field edge), if a clean lot
  exists there.

Usage: python -I builders/builder-northeast_police_telecom_east.py dry|build|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, access_check  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_police_telecom_east.json"
SITES = [
    ("police", "PoliceStation03", [554, 2761], [-440, 2500, 1540, 3415], 300),
    ("tower_port", "TelecomTower01", [1900, 3800], [936, 3428, 2805, 4674], 400),
    ("tower_works", "TelecomTower01", [450, 3440], [-400, 3400, 935, 4060], 350),
]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def place(prefab, near, box, rad, dry):
    r = post("/place", {"prefab": prefab, "near": near, "within": box, "radius": rad, "maxHouseholds": 0,
                        "maxCompanies": 0, "dryRun": dry}, timeout=400)
    c = r.get("chosen") or {}
    out = {k: c.get(k) for k in ("x", "z", "angle", "edge", "road", "cost", "demolition", "roadConnected")}
    out["placedId"] = r.get("placedId")
    out["error"] = r.get("error")
    return out


if MODE == "dry":
    for name, prefab, near, box, rad in SITES:
        print(name, json.dumps(place(prefab, near, box, rad, True)))
elif MODE == "build":
    for name, prefab, near, box, rad in SITES:
        with paused(WHO):
            r = place(prefab, near, box, rad, False)
            print(name, json.dumps(r), flush=True)
            log(name, r)
            if r.get("placedId"):
                segs = get("/nets", x=r["x"], z=r["z"], radius=60, filter="Road").get("segments") or []
                ids = [s["id"] for s in segs[:2] if not (s.get("upgrades") or {}).get("Trees")]
                if ids:
                    t = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"})
                    print("  trees", ids, t.get("applied"))
elif MODE == "check":
    d = json.load(open(LOG))
    ids = [v["placedId"] for v in d.values() if v.get("placedId")]
    print("access", access_check(ids))
    for k in ("police", "telecom"):
        dd = get("/services", kind=k)["kinds"][k]
        for kk, v in dd.items():
            if isinstance(v, list):
                for b in v:
                    if isinstance(b, dict) and b.get("id") in ids:
                        print(k, b.get("id"), b.get("prefab"), b.get("status"))
