"""builder-east / b2-growth-eg-commons-hotels-offices: East Gate Commons, phase 1 (lakefront hotel quarter).

Between East Bank and the parkway on tiles (1247,-623) (owned) and (1247,0) (south shore of the lake, facing the
Downtown skyline across the water):
- Commons Gate: roundabout on the parkway node (1271.5,-930.1).
- Commons Drive (Medium Road, Trees+Grass): curved, north from the gate to the lakefront.
- Lakefront Drive (Medium Road, Trees): east along the south shore ~80 m inland from a stub at (955,-326) facing
  East Bank's Shore Drive N end (790,-335) - hotels on the water side. Joining the two (East Bank's second entry for
  its northern half) would remove East Bank home 1902816:43, so that link is left to builder-south.
- Two Small Road crescents (Trees): east crescent round the central green, west crescent towards East Bank.
- Zoning: NA Commercial High (hotels, dining) on Lakefront Drive and the north half of Commons Drive; Office High on
  the east crescent and the south half of Commons Drive (office company demand 392). CityPark on the green.
Stages: tile | dry | roads | zone-dry | zone | park
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import find_spot, get, post, paused, short, building  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-east"
LOG = r"C:\soft\cityskyline\logs\council\builder-east_commons.json"
TILE = (1247, 0)
GATE = [1271.5, -930.1]
SHORE_END = [790, -335]
T_LAKE = [1300, -140]
ROADS = [
    ("commons drive", "Medium Road", [GATE, [1290, -780], [1340, -600], [1350, -420], [1325, -260], T_LAKE]),
    # west end stops at (955,-326): joining Shore Drive N's end (790,-335) would remove East Bank home 1902816:43
    # (24 households) - left to builder-south
    ("lakefront west", "Medium Road", [[955, -326], [1050, -268], [1150, -190], T_LAKE]),
    ("lakefront east", "Medium Road", [T_LAKE, [1420, -132], [1500, -140], [1538, -165]]),
    ("east crescent", "Small Road", [[1340, -600], [1440, -585], [1505, -480], [1475, -360], [1347, -380]]),
    ("west crescent", "Small Road", [[1305, -720], [1170, -720], [1060, -620], [1070, -470], [1180, -400],
                                     [1350, -420]]),
]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def show(label, r):
    out = short(r, keys=("applied", "cost", "errors", "problems", "error", "wouldDemolish", "placedId", "cellsChanged"))
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
                        print("  skipped", label)
                        continue
                    r = show(label, post("/road", spec))
                    log(label, {"cost": r.get("cost"), "edges": r.get("newEdgeIds")})
                    time.sleep(1.0)
            if MODE == "roads":
                d = show("gate rb dry", post("/roundabout", {"at": GATE, "size": "large", "dryRun": True}))
                if d.get("errors") == 0 and not d.get("wouldDemolish"):
                    log("gate roundabout", short(show("gate rb", post("/roundabout", {"at": GATE, "size": "large"}))))
                box = [940, -925, 1545, -60]
                for prefab, ups in (("Medium Road", ("Trees", "Grass")), ("Small Road", ("Trees",))):
                    for up in ups:
                        u = post("/roadupgrade", {"box": box, "prefabFilter": prefab, "upgrade": up, "side": "both"})
                        print(prefab, up, u.get("applied"), u.get("cost"), u.get("errors"))
                u = post("/roadupgrade", {"box": [780, -345, 950, -320], "prefabFilter": "Medium Road", "upgrade": "Trees",
                                          "side": "both"})
                print("lakefront west end Trees", u.get("applied"), u.get("errors"))


# zoning (edge ids read after building)
ZONING = [
    ("NA Commercial High", ["1960348:149", "1961436:77", "1962449:61", "1969138:87", "1691329:47", "1774798:131",  # lakefront
                            "1962957:59", "2061790:13", "1835773:11", "1835772:11"]),                          # Commons Drive north
    # west crescent (towards East Bank) left unzoned for now: Residential Mixed there if builder-south agrees
    ("Office High", ["2061784:13", "1835768:11", "1835767:11",                                    # Commons Drive south
                     "1794104:53", "1792039:31", "1792257:55", "1763889:31"]),                    # east crescent
]


def zone_all(dry):
    for zname, edges in ZONING:
        r = post("/zone", {"zone": zname, "mode": "road", "edges": edges, "depth": 6, "side": "both", "dryRun": dry})
        print(zname, "dry" if dry else "", json.dumps(short(r, keys=("applied", "errors", "cellsChanged", "occupiedCellsRezoned",
                                                                     "error"))))
        if not dry:
            log(f"zone {zname}", short(r, keys=("applied", "cellsChanged")))


if __name__ == "__main__" and len(sys.argv) > 1 and sys.argv[1] in ("zone-dry", "zone"):
    with paused(WHO):
        zone_all(sys.argv[1] == "zone-dry")
