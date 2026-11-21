"""builder-northeast - b3-health_safety-eastport-safety-station (police part; FireHouse01 1847096:61 done).

Survey 2033: Eastport has two crime clusters that the highway separates (no road link nearby):
- west/south-west: manufacturing on the x~2400/2580 streets, z 2550..2720 (8 buildings at 23-31%),
- east: petrochemical waterfront on the diagonal road from (2811,3088), worst building 33% at (2829,3039).
So: PoliceStation03 on the bridge road west of the highway (2630,3016), facing Medium Road 957715:167
(~420 m from the SW cluster), and a small PoliceStation02 on the east diagonal (2830,3116), edge 1113122:151
(~80 m from the worst building). Trees both sides on both frontages.

Usage: python -I builders/builder-northeast_eastport_police.py dry|build|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, access_check  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_eastport_police.json"
EP = [2181, 2181, 3428, 3428]
SITES = [
    ("west", "PoliceStation03", [2630, 3016], [2560, 2900, 2720, 3060]),
    ("east", "PoliceStation02", [2830, 3116], [2760, 3040, 2960, 3180]),
]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def place(prefab, near, box, dry):
    r = post("/place", {"prefab": prefab, "near": near, "within": box, "radius": 120, "maxHouseholds": 0,
                        "maxCompanies": 0, "dryRun": dry}, timeout=300)
    c = r.get("chosen") or {}
    out = {k: c.get(k) for k in ("x", "z", "angle", "edge", "road", "cost", "demolition", "roadConnected")}
    out["placedId"] = r.get("placedId")
    out["error"] = r.get("error")
    return out


def trees_near(x, z):
    segs = get("/nets", x=x, z=z, radius=60, filter="Medium Road").get("segments") or []
    ids = [s["id"] for s in segs[:2]]
    if not ids:
        return None
    r = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"})
    return {"edges": ids, "applied": r.get("applied"), "cost": r.get("cost"), "errors": r.get("errors")}


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
                t = trees_near(r["x"], r["z"])
                print("  trees", json.dumps(t))
                log(name + "_trees", t)
elif MODE == "check":
    d = json.load(open(LOG))
    ids = [d[k]["placedId"] for k in ("west", "east") if d.get(k, {}).get("placedId")]
    s = get("/services", kind="police", brief=1)
    for b in s.get("buildings", s.get("services", [])):
        if b.get("id") in ids:
            print(json.dumps(b)[:400])
    print("access", access_check(ids))
