"""builder-west, item b2-housing-westbrook-north-woods - garden village on the owned empty land
x -5280..-4660, z 830..1640 (west road x=-5290, villa street z=762, street z=1712, lane x=-4600).

  - "Woodland Lane": tree-lined Medium Road stadium loop from the lane nodes (-4600,1120) and (-4600,1450)
    (4-way with the existing cross streets east of the lane; replaces 6 single-family homes), apex x=-5015;
  - "Green Close": village-centre cul-de-sac from the loop apex east to (-4760,1285): CityPark02,
    PoliceStation02, FireHouse02, Mixed Old Town;
  - two crescents and two closes into the land south and north of the loop (Small Road + Trees, cul-de-sacs with
    pocket parks at the forest edge); the westernmost ~180 m (x < -5100) stays woodland.
Zoning overwrite=False: Low only at the forest edge, Medium Row on the crescents, Medium on the arms.
Usage: python -I builders/builder-west_north-woods.py roads|centre|zone [dry]
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, demolition_cost, find_spot, get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[2:]
LOG = r"C:\soft\cityskyline\logs\council\demolitions.jsonl"
WB = [-5290, -920, -4310, 2170]
LOOP = [[-4600, 1120], [-4850, 1120], [-4967, 1168], [-5015, 1285], [-4967, 1402], [-4850, 1450], [-4600, 1450]]
SMALL = {
    "Green Close": [[-5015, 1285], [-4900, 1295], [-4760, 1285]],
    "South Crescent": [[-4850, 1120], [-4905, 1010], [-4990, 915], [-5060, 880]],
    "South Close": [[-4720, 1120], [-4730, 1000], [-4720, 870]],
    "North Crescent": [[-4850, 1450], [-4905, 1550], [-4990, 1615], [-5060, 1630]],
    "North Close": [[-4720, 1450], [-4730, 1550], [-4720, 1640]],
}
CENTRE = (-4850, 1285)


def log(what, would):
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": WHO, "prefab": what,
                            "item": "b2-housing-westbrook-north-woods",
                            "ids": [w["id"] for w in would or []], "cost": demolition_cost(would)}) + "\n")


def road(prefab, name, pts, max_hh=15):
    d = post("/road", {"prefab": prefab, "points": pts, "curve": "smooth", "dryRun": True, "allowDemolish": True})
    cost = demolition_cost(d.get("wouldDemolish"))
    g = d.get("geometry") or {}
    print(f"{name} dry: {short(d, ('errors', 'cost', 'problems'))} len {g.get('length')} minR {g.get('minRadius')} hh {cost['households']}", flush=True)
    if d.get("errors") or cost["households"] > max_hh or cost["companies"] or DRY:
        return False
    r = post("/road", {"prefab": prefab, "points": pts, "curve": "smooth", "allowDemolish": True})
    print(f"{name}: {short(r, ('applied', 'cost', 'errors'))}", flush=True)
    if d.get("wouldDemolish"):
        log(f"{prefab} ({name})", d.get("wouldDemolish"))
    time.sleep(1)
    return True


def stage_roads():
    with paused(WHO):
        if road("Medium Road", "Woodland Lane", LOOP):
            t = post("/roadupgrade", {"box": [-5025, 1110, -4605, 1460], "prefabFilter": "Medium Road", "upgrade": "Trees", "side": "both"})
            print("  trees:", short(t, ("applied", "cost", "errors")), flush=True)
        for name, pts in SMALL.items():
            if road("Small Road", name, pts):
                t = post("/roadupgrade", {"polyline": pts, "tolerance": 12, "prefabFilter": "Small Road", "upgrade": "Trees", "side": "both"})
                print("  trees:", short(t, ("applied", "cost", "errors")), flush=True)


def side_facing(s, px, pz):
    (sx, sz), (ex, ez) = s["start"], s["end"]
    dx, dz = ex - sx, ez - sz
    mx, mz = (sx + ex) / 2, (sz + ez) / 2
    return "right" if dz * (px - mx) - dx * (pz - mz) > 0 else "left"


def other(side):
    return "left" if side == "right" else "right"


def village_edges():
    segs = {}
    for cx, cz in ((-4850, 950), (-4850, 1285), (-4850, 1580)):
        for s in get("/nets", x=cx, z=cz, radius=300).get("segments") or []:
            mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
            if -5070 < mx < -4640 and 850 < mz < 1650 and s.get("prefab") in ("Medium Road", "Small Road"):
                segs[s["id"]] = s
    return list(segs.values())


def place(prefab, x, z, box):
    for c in find_spot(prefab, x, z, radius=200, n=40, within=WB):
        if box[0] <= c["x"] <= box[1] and box[2] <= c["z"] <= box[3]:
            b = dict(c["building"])
            b.pop("prefab", None)
            d = post("/building", {"prefab": prefab, **b, "dryRun": True})
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                r = building(prefab, **b)
                print(f"  {prefab}: {short(r, ('applied', 'cost', 'placedId', 'roadConnected'))} at ({c['x']}, {c['z']})", flush=True)
                return r.get("placedId")
    print(f"  {prefab}: no spot in {box}", flush=True)
    return None


def stage_centre():
    out = {}
    with paused(WHO):
        out["green"] = place("CityPark02", -4880, 1250, (-4960, -4800, 1220, 1280))
        out["police"] = place("PoliceStation02", -4800, 1320, (-4860, -4765, 1295, 1350))
        out["fire"] = place("FireHouse02", -4800, 1255, (-4860, -4765, 1220, 1280))
        out["signature"] = place("NA_ResidentialMediumRowSignature01", -4930, 980, (-5000, -4860, 900, 1100))
        out["park_s"] = place("PocketPark05", -5050, 880, (-5070, -4990, 850, 930))
        out["park_n"] = place("PocketPark05", -5050, 1630, (-5070, -4990, 1590, 1650))
    print(json.dumps(out))


def stage_zone():
    cells = {}
    with paused(WHO):
        for s in village_edges():
            mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
            jobs = []
            if s["prefab"] == "Medium Road":
                if min(s["start"][0], s["end"][0]) > -4855:          # straight arms near the lane
                    jobs.append(("NA Residential Medium", "both", 4))
                else:                                                 # the arc: villas only on the woodland side
                    inner = side_facing(s, *CENTRE)
                    jobs.append(("NA Residential Medium Row", inner, 5))
                    jobs.append(("NA Residential Low", other(inner), 4))
            elif 1240 < mz < 1330 and mx < -4750:                     # Green Close: village centre
                jobs.append(("NA Residential Mixed Old Town", "both", 4))
            elif mx < -4985:                                          # crescent ends at the forest edge
                jobs.append(("NA Residential Low", "both", 4))
            else:
                jobs.append(("NA Residential Medium Row", "both", 5))
            for name, side, depth in jobs:
                z = zone(name, mode="road", edges=[s["id"]], depth=depth, side=side, overwrite=False)
                cells[name] = cells.get(name, 0) + (z.get("cellsChanged") or 0)
    print("zoned cells:", cells, flush=True)


if __name__ == "__main__":
    {"roads": stage_roads, "centre": stage_centre, "zone": stage_zone}[sys.argv[1]]()
