"""builder-east / b2-growth-eg-international-airport-parkway: Claude International Airport.

InternationalAirport01 (lot 1360 x 1264 m) north of the Southern Parkway between x~3440 and ~4800, terminal side
facing the parkway; access from an 'Airport Gate' roundabout on the parkway. The East Gate Parkway of this item is the
Southern Parkway built under b2-mobility-southern-parkway (one tree-lined Large Road from the East Bank spine to the
SE highway diamond), so this item adds the airport, its forecourt, the passenger flights and the airport bus.

Stages: tiles | spot | ...
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


if __name__ == "__main__":
    MODE = sys.argv[1] if len(sys.argv) > 1 else "spot"
    if MODE == "tiles":
        tiles()
    elif MODE == "spot":
        r = get("/findspot", prefab="InternationalAirport01", x=4100, z=-600, radius=1000, n=8)
        print(json.dumps({k: r.get(k) for k in ("searched", "rejected", "hint")}))
        for c in r.get("candidates") or []:
            print(json.dumps(c)[:400])
