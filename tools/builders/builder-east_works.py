"""builder-east / b2-economy-econ-eastgate-logistics: East Gate Works, phase 1 (ore + logistics park).

South of the Southern Parkway, entered from the Airport Gate roundabout (3900,-1360) (its free south leg), so trucks
reach it from the SE highway diamond in ~1 km without touching homes. Downwind (NE) of every home.
- Tiles (3740,-1870) (richest ore) and (4363,-1870).
- Works Way: tree-lined Large Road spine, a gentle S from the gate south-east to (4500,-2040).
- Foundry Loop: Medium Road (Trees) from the spine end north round the east side back to the spine at Works Cross
  (3905,-1500); roundabouts at Works Cross and Ore Cross (3960,-1680).
- Ore Lane: Medium Road (Trees) west from Ore Cross curving south into the ore field, cul-de-sac; the Ore extractor
  hub sits on it with its work area over the rich cells x 3450..3950, z -1800..-2170 (kept unzoned).
- Industrial Manufacturing on the spine, the loop and the north side of Ore Lane, 6 cells deep; a ~60-100 m tree
  belt is left along the parkway.
Stages: tiles | dry | roads | hub-dry | hub | zone-dry | zone
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import find_spot, get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-east"
LOG = r"C:\soft\cityskyline\logs\council\builder-east_works.json"
TILES = [(3740, -1870), (4363, -1870)]

GATE = [3900, -1360]
WORKS_X = [3905, -1500]
ORE_X = [3960, -1680]
SPINE_END = [4500, -2040]
ROADS = [
    ("works way 1", "Large Road", [GATE, WORKS_X]),
    ("works way 2", "Large Road", [WORKS_X, [3925, -1590], ORE_X]),
    ("works way 3", "Large Road", [ORE_X, [4080, -1840], [4260, -1960], SPINE_END]),
    ("foundry loop", "Medium Road", [SPINE_END, [4615, -1915], [4640, -1720], [4570, -1570], [4390, -1480],
                                     [4150, -1462], WORKS_X]),
    ("ore lane", "Medium Road", [ORE_X, [3810, -1725], [3700, -1830], [3680, -1980], [3690, -2120]]),
]
ORE_FIELD = [[3450, -1800], [3950, -1800], [3950, -2170], [3450, -2170]]
TREES = [("Large Road", ("Trees", "Grass")), ("Medium Road", ("Trees",))]
BOX = [3440, -2180, 4670, -1370]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def show(label, r):
    out = short(r, keys=("applied", "cost", "errors", "problems", "error", "placedId", "roadConnected", "wouldDemolish",
                         "conflicts"))
    g = r.get("geometry") or {}
    if g:
        out["geo"] = {k: g.get(k) for k in ("length", "minRadius", "warnings") if g.get(k) is not None}
    snaps = [x.get("snappedTo") for x in r.get("snapping") or []]
    if any(snaps):
        out["snap"] = snaps
    print(f"{label:<14} {json.dumps(out)[:700]}", flush=True)
    return r


def tiles():
    for t in TILES:
        tl = get("/tiles")
        if t in {tuple(c) for c in tl["ownedCentres"]}:
            print("owned", t)
            continue
        cand = [c for c in tl["candidates"] if tuple(c["centre"]) == t]
        if not cand:
            print("not buyable", t)
            break
        with paused(WHO):
            r = post("/buytile", {"x": t[0], "z": t[1]})
        print("buy", t, cand[0]["price"], cand[0]["upkeepIncrease"], short(r))
        log(f"tile {t}", {"price": cand[0]["price"], "upkeep": cand[0]["upkeepIncrease"]})


def roads(dry):
    for label, prefab, pts in ROADS:
        spec = {"prefab": prefab, "points": pts, "curve": "smooth"}
        r = show(label + (" dry" if dry else ""), post("/road", {**spec, "dryRun": True}))
        if dry:
            continue
        if r.get("errors") != 0 or r.get("wouldDemolish"):
            sys.exit(f"stopped at {label}")
        r = show(label, post("/road", spec))
        log(label, {"cost": r.get("cost"), "edges": r.get("newEdgeIds")})
        time.sleep(1.0)


if __name__ == "__main__":
    MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
    if MODE == "tiles":
        tiles()
    elif MODE == "dry":
        with paused(WHO):
            roads(dry=True)
    elif MODE == "roads":
        with paused(WHO):
            roads(dry=False)
            for at in (WORKS_X, ORE_X):
                d = show(f"rb {at} dry", post("/roundabout", {"at": at, "size": "medium", "dryRun": True}))
                if d.get("errors") == 0 and not d.get("wouldDemolish"):
                    log(f"roundabout {at}", short(show(f"rb {at}", post("/roundabout", {"at": at, "size": "medium"}))))
            for prefab, ups in TREES:
                for up in ups:
                    u = post("/roadupgrade", {"box": BOX, "prefabFilter": prefab, "upgrade": up, "side": "both"})
                    print(prefab, up, u.get("applied"), u.get("cost"), u.get("errors"))
