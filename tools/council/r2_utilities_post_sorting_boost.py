"""Council round 2 - Utilities - post-sorting-boost.

Upgrades on the existing post buildings (no new land):
  1) Automated Sorting on PostSortingFacility01 1456330:21 (fixed, +25k sorting)
  2) Extra Loading Ramps on 1456330:21 (fixed, +5 post trucks)
  3) Post Van Garage on PostOffice01 175520:119 (placeable, +10 vans)
  4) Loading Dock on PostOffice02 180424:247 (fixed, +10k mail storage)
The request reset (step 5) is a mayor-only action and is NOT done here.

Usage: python -I council/r2_utilities_post_sorting_boost.py dry|build|verify
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "blocked",
        "wouldDemolish", "conflicts", "side", "pos", "upgrade", "installed")
LOG = r"C:\soft\cityskyline\logs\council\r2_utilities_post_sorting_boost.json"

STEPS = [
    ("Automated Sorting", "1456330:21", "Automated Sorting"),
    ("Extra Loading Ramps", "1456330:21", "Extra Loading Ramps"),
    ("Post Van Garage", "175520:119", "Post Van Garage"),
    ("Loading Dock", "180424:247", "Loading Dock"),
]


def show(label, r):
    print(f"{label:<32} {json.dumps(short(r, keys=KEYS))[:900]}", flush=True)
    return r


def post_state():
    p = get("/services", kind="post")["kinds"]["post"]
    out = {"totals": p["totals"], "demand": p.get("demand")}
    for b in p["buildings"]:
        if b["id"] in ("1456330:21", "175520:119", "180424:247", "370375:209"):
            out[b["id"]] = {k: b.get(k) for k in ("prefab", "status", "vehicles", "storage", "processing", "postTrucks", "mail", "workers")}
    return out


if __name__ == "__main__":
    if MODE == "dry":
        with paused("utilities"):
            for label, owner, prefab in STEPS:
                show(f"{label} (dry)", post("/extension", {"owner": owner, "prefab": prefab, "side": "auto", "dryRun": True}, timeout=120))
    elif MODE == "build":
        res, spent = {}, 0
        with paused("utilities"):
            for label, owner, prefab in STEPS:
                d = show(f"{label} (dry)", post("/extension", {"owner": owner, "prefab": prefab, "side": "auto", "dryRun": True}, timeout=120))
                if d.get("error") or d.get("errors") or d.get("blocked") or d.get("wouldDemolish"):
                    res[label] = {"skipped": True, "dry": d}
                    continue
                r = show(label, post("/extension", {"owner": owner, "prefab": prefab, "side": "auto"}, timeout=120))
                res[label] = r
                if r.get("applied"):
                    spent += r.get("cost") or 0
        res["spent"] = spent
        json.dump(res, open(LOG, "w"), indent=1)
        print("SPENT", spent)
    elif MODE == "verify":
        print(json.dumps(post_state(), indent=1))
        for o in ("1456330:21", "175520:119", "180424:247"):
            x = get("/extensions", owner=o)
            print(o, "installed:", json.dumps(x.get("installed")))
        r = get("/requests", kind="post", onlyFailed=1)
        print(json.dumps({k: v for k, v in r.items() if k not in ("targets", "items", "examples")})[:1500])
