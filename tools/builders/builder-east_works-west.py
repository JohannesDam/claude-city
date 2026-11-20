"""builder-east / b2-growth-eg-works-ore-logistics: East Gate Works west (Foundry quarter).

Tile (3117,-1870). From a new 'Foundry Gate' roundabout on the parkway node (2935.1,-1335.6):
- Foundry Road (Large Road, Trees+Grass) curving south-east to J_S (3470,-2130);
- Smelter Loop (Medium Road, Trees) from Foundry Road at J_N (3000,-1570) east and south round the quarter back to
  J_S, so the quarter is a ring with one gate on the parkway (trucks reach the SE highway in ~1.9 km).
- Industrial Manufacturing on both sides of both roads except Foundry Road's first ~180 m (green belt along the
  parkway). Heavy industry stays east of x 2800 (plume drifts NE onto the airport, away from the Commons).
- x 2181..2800 (tile (2493,-1870)) stays free for the Southern Rail cargo terminal and rail corridor.
Stages: tile | dry | roads | zone-dry | zone
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-east"
LOG = r"C:\soft\cityskyline\logs\council\builder-east_works-west.json"
TILE = (3117, -1870)
GATE = [2935.1, -1335.6]
J_N = [3000, -1570]
J_S = [3470, -2130]
ROADS = [
    ("foundry road 1", "Large Road", [GATE, [2955, -1460], J_N]),
    ("foundry road 2", "Large Road", [J_N, [3050, -1700], [3160, -1860], [3320, -2030], J_S]),
    ("smelter loop", "Medium Road", [J_N, [3150, -1530], [3370, -1560], [3515, -1700], [3555, -1900], [3530, -2060],
                                     J_S]),
]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def show(label, r):
    out = short(r, keys=("applied", "cost", "errors", "problems", "error", "wouldDemolish", "cellsChanged"))
    if r.get("conflicts"):
        out["conflicts"] = [{k: c.get(k) for k in ("error", "at", "prefab")} for c in r["conflicts"][:3]]
    g = r.get("geometry") or {}
    if g:
        out["geo"] = {k: g.get(k) for k in ("length", "minRadius") if g.get(k) is not None}
    snaps = [x.get("snappedTo") for x in r.get("snapping") or []]
    if any(snaps):
        out["snap"] = snaps
    print(f"{label:<16} {json.dumps(out)[:700]}", flush=True)
    return r


if __name__ == "__main__":
    MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
    if MODE == "tile":
        c = [k for k in get("/tiles")["candidates"] if tuple(k["centre"]) == TILE]
        if c:
            with paused(WHO):
                r = post("/buytile", {"x": TILE[0], "z": TILE[1]})
            print(c[0]["price"], short(r))
            log("tile", {"price": c[0]["price"], "upkeep": c[0]["upkeepIncrease"]})
    elif MODE in ("dry", "roads"):
        with paused(WHO):
            for label, prefab, pts in ROADS:
                spec = {"prefab": prefab, "points": pts, "curve": "smooth"}
                d = show(label + " dry", post("/road", {**spec, "dryRun": True}))
                if MODE == "roads":
                    if d.get("errors") != 0 or d.get("wouldDemolish"):
                        sys.exit(f"stopped at {label}")
                    r = show(label, post("/road", spec))
                    log(label, {"cost": r.get("cost"), "edges": r.get("newEdgeIds")})
                    time.sleep(1.0)
            if MODE == "roads":
                for at, size in ((GATE, "large"), (J_N, "medium")):
                    d = show(f"rb {at} dry", post("/roundabout", {"at": at, "size": size, "dryRun": True}))
                    if d.get("errors") == 0 and not d.get("wouldDemolish"):
                        log(f"roundabout {at}", short(show(f"rb {at}", post("/roundabout", {"at": at, "size": size}))))
                box = [2900, -2180, 3600, -1380]
                for prefab, ups in (("Large Road", ("Trees", "Grass")), ("Medium Road", ("Trees",))):
                    for up in ups:
                        u = post("/roadupgrade", {"box": box, "prefabFilter": prefab, "upgrade": up, "side": "both"})
                        print(prefab, up, u.get("applied"), u.get("cost"), u.get("errors"))
