"""builder-east / b2-growth-eg-international-airport-parkway: Claude International Airport.

InternationalAirport01 (lot 1360 x 1264 m) north of the Southern Parkway between x~3440 and ~4800, terminal side
facing the parkway; access from an 'Airport Gate' roundabout on the parkway. The East Gate Parkway of this item is the
Southern Parkway built under b2-mobility-southern-parkway (one tree-lined Large Road from the East Bank spine to the
SE highway diamond), so this item adds the airport, its forecourt, the passenger flights and the airport bus.

Layout: Terminal Road (Medium Road, Trees) along z=-1250 in front of the lot (front faces south, angle 180, lot
x 3438..4798, z -1236..28), linked to the parkway by the Airport Gate roundabout at the parkway node (3900,-1360) and
by a curved west link to the parkway near (3462,-1366). Bus station and taxi depot between Terminal Road and the
parkway.

Stages: tiles | spot | dry | roads | airport | ...
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import call, get, post, paused, road, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-east"
LOG = r"C:\soft\cityskyline\logs\council\builder-east_airport.json"
TILES = [(3740, -623), (4363, -623), (4986, -623), (3740, 0), (4363, 0), (4986, 0)]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def tiles():
    spent = 0
    for t in TILES:
        tl = get("/tiles")
        if t in {tuple(c) for c in tl["ownedCentres"]}:
            print("owned", t)
            continue
        cand = [c for c in tl["candidates"] if tuple(c["centre"]) == t]
        if not cand:
            print("not buyable yet", t)
            break
        c = cand[0]
        with paused(WHO):
            r = post("/buytile", {"x": t[0], "z": t[1]})
        print("buy", t, c["price"], c["upkeepIncrease"], short(r))
        log(f"tile {t}", {"price": c["price"], "upkeep": c["upkeepIncrease"], "result": short(r)})
        if r.get("error") or r.get("errors"):
            break
        spent += c["price"]
    print("tiles spent", spent)


GATE = [3900, -1360]
ROADS = [
    ("terminal road", {"prefab": "Medium Road", "points": [[4770, -1250], [3900, -1250], [3560, -1250]]}),
    ("west link", {"prefab": "Medium Road", "curve": "smooth", "points": [[3560, -1250], [3490, -1285], [3462, -1366]],
                   "startDir": [-1, 0]}),
    ("gate link", {"prefab": "Medium Road", "points": [[3900, -1250], GATE]}),
]
AIRPORT = {"prefab": "InternationalAirport01", "x": 4118, "z": -604, "angle": 180}


def show(label, r):
    out = short(r, keys=("applied", "cost", "errors", "problems", "error", "placedId", "roadConnected", "wouldDemolish",
                         "conflicts", "corners"))
    g = r.get("geometry") or {}
    if g:
        out["geo"] = {k: g.get(k) for k in ("length", "minRadius", "warnings") if g.get(k) is not None}
    snaps = [x.get("snappedTo") for x in r.get("snapping") or []]
    if any(snaps):
        out["snap"] = snaps
    print(f"{label:<16} {json.dumps(out)[:900]}", flush=True)
    return r


def roads(dry):
    for label, spec in ROADS:
        r = show(label + " (dry)", post("/road", {**spec, "dryRun": True}))
        if dry:
            continue
        if r.get("errors") != 0 or r.get("wouldDemolish"):
            sys.exit(f"stopped at {label}")
        r = show(label, post("/road", spec))
        log(label, {"cost": r.get("cost"), "edges": r.get("newEdgeIds")})


if __name__ == "__main__":
    MODE = sys.argv[1] if len(sys.argv) > 1 else "spot"
    if MODE == "tiles":
        tiles()
    elif MODE == "dry":
        with paused(WHO):
            roads(dry=True)
            show("airport (dry)", post("/building", {**AIRPORT, "dryRun": True}))
    elif MODE == "roads":
        with paused(WHO):
            roads(dry=False)
            d = show("gate roundabout (dry)", post("/roundabout", {"at": GATE, "size": "medium", "dryRun": True}))
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                log("gate roundabout", short(show("gate roundabout", post("/roundabout", {"at": GATE, "size": "medium"}))))
            u = post("/roadupgrade", {"box": [3440, -1370, 4790, -1240], "prefabFilter": "Medium Road", "upgrade": "Trees",
                                      "side": "both"})
            print("Medium Road Trees", u.get("applied"), u.get("cost"), u.get("errors"))
    elif MODE == "airport":
        with paused(WHO):
            d = show("airport (dry)", post("/building", {**AIRPORT, "dryRun": True}))
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                r = show("airport", post("/building", AIRPORT))
                log("airport", short(r, keys=("applied", "cost", "placedId", "roadConnected", "corners", "seed")))
    elif MODE == "spot":
        r = get("/findspot", prefab="InternationalAirport01", x=4100, z=-600, radius=1000, n=8)
        print(json.dumps({k: r.get(k) for k in ("searched", "rejected", "hint")}))
        for c in r.get("candidates") or []:
            print(json.dumps(c)[:400])
