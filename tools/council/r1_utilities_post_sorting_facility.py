"""Council round 1 - Utilities - post-sorting-facility.

PostSortingFacility01 at Site A (-919,-630), south part of SE lakeside (next to the WWTPs), fronting
the Small Road edge 1507093:15. Fallback Site B (-3137,-990) in Old industry south (edge 1294505:1).

Usage: python -I council/r1_utilities_post_sorting_facility.py dry|build|verify
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, paused, building, find_spot, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
PREFAB = "PostSortingFacility01"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "facing", "pos")
LOG = r"C:\soft\cityskyline\logs\council\r1_utilities_post_sorting_facility.json"

SITE_A = dict(x=-919, z=-630, edge="1507093:15", t=0.471, side="left")
SITE_B = dict(x=-3137, z=-990, edge="1294505:1", t=0.542, side="left")


def show(label, r):
    print(f"{label:<40} {json.dumps(short(r, keys=KEYS))}", flush=True)
    return r


def spots(site):
    """Re-check the site with /findspot right before building; fall back to the planned edge placement."""
    out = []
    for c in find_spot(PREFAB, site["x"], site["z"], radius=150, n=4):
        b = dict(c["building"])
        b.pop("prefab", None)
        out.append(b)
    planned = {k: site[k] for k in ("edge", "t", "side")}
    if planned not in out:
        out.append(planned)
    return out


def post_summary():
    s = get("/services", kind="post")["kinds"]["post"]
    rows = [(b["id"], b["prefab"], b.get("mail"), b.get("flags"), b.get("status")) for b in s["buildings"]]
    return s["totals"], s["demand"], rows


if __name__ == "__main__":
    if MODE == "dry":
        for name, site in (("A", SITE_A), ("B", SITE_B)):
            with paused("utilities"):
                for b in spots(site):
                    show(f"site {name} {b} (dry)", building(PREFAB, **b, dryRun=True))
    elif MODE == "build":
        res = None
        with paused("utilities"):
            for b in spots(SITE_A):
                d = show(f"site A {b} (dry)", building(PREFAB, **b, dryRun=True))
                if d.get("errors") == 0 and not d.get("error"):
                    res = show("PostSortingFacility01 site A", building(PREFAB, **b))
                    break
        if not (res or {}).get("applied"):
            print("Site A failed; Site B needs Economy's agreement - not built automatically.")
        print("SPENT", (res or {}).get("cost") if (res or {}).get("applied") else 0)
        json.dump(res, open(LOG, "w"), indent=1)
    elif MODE == "verify":
        tot, dem, rows = post_summary()
        print(json.dumps(tot))
        print(json.dumps(dem))
        for r in rows:
            print(r)
        print(json.dumps(get("/buildings", kind="service", x=-919, z=-630, radius=200))[:1500])
