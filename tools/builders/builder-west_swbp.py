"""builder-west, item b2-growth-swbp-southwest-business-park (tiles (-3740,-1247) and (-4363,-1247) bought,
district 'Southwest Business Park' drawn x -4665..-3438, z -1548..-945).

Stage "roads":
  - South Boulevard: Large Road + Trees + Grass, smooth S-curve (min R ~390 m) from Memorial Drive's end
    (-3390,-1010) west to the Westbrook lane / Market Street corner (-4600,-870): the southern bypass
    Westbrook - SWQ - Old Southern Works - Southgate;
  - Business Loop: tree-lined Medium Road loop south of the boulevard between its nodes (-4220,-1080) and
    (-3620,-1080);
  - Square Lane: tree-lined Small Road from the boulevard node (-3920,-1105) south through the business square to
    the loop's south point (-3920,-1460).
Stage "centre": business square services + zoning (overwrite=False).
Usage: python -I builders/builder-west_swbp.py roads|centre [dry]
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, demolition_cost, find_spot, get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[2:]
LOG = r"C:\soft\cityskyline\logs\council\demolitions.jsonl"
SWBP = [-4665, -1548, -3438, -945]
BOULEVARD = [[-3390, -1010], [-3620, -1080], [-3920, -1105], [-4220, -1080], [-4440, -1010], [-4600, -870]]
LOOP = [[-4220, -1080], [-4240, -1230], [-4130, -1400], [-3920, -1460], [-3710, -1400], [-3600, -1230], [-3620, -1080]]
SQUARE_LANE = [[-3920, -1105], [-3935, -1280], [-3920, -1460]]


def build(prefab, pts, max_hh=20):
    d = post("/road", {"prefab": prefab, "points": pts, "curve": "smooth", "dryRun": True, "allowDemolish": True})
    cost = demolition_cost(d.get("wouldDemolish"))
    g = d.get("geometry") or {}
    print(f"{prefab} dry: {short(d, ('errors', 'cost', 'problems'))} len {g.get('length')} minR {g.get('minRadius')} {cost}", flush=True)
    if d.get("errors") or cost["households"] > max_hh or cost["companies"] or DRY:
        return False
    r = post("/road", {"prefab": prefab, "points": pts, "curve": "smooth", "allowDemolish": True})
    print(f"{prefab}: {short(r, ('applied', 'cost', 'errors'))}", flush=True)
    if d.get("wouldDemolish"):
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": WHO, "prefab": f"{prefab} (SW Business Park)",
                                "item": "b2-growth-swbp-southwest-business-park",
                                "ids": [w["id"] for w in d.get("wouldDemolish")], "cost": cost}) + "\n")
    time.sleep(1)
    return bool(r.get("applied"))


def stage_roads():
    with paused(WHO):
        if build("Large Road", BOULEVARD):
            for upg in ("Trees", "Grass"):
                t = post("/roadupgrade", {"box": [-4610, -1120, -3385, -860], "prefabFilter": "Large Road", "upgrade": upg, "side": "both"})
                print(f"  boulevard {upg}:", short(t, ("applied", "cost", "errors")), flush=True)
        if build("Medium Road", LOOP):
            t = post("/roadupgrade", {"box": [-4250, -1470, -3590, -1085], "prefabFilter": "Medium Road", "upgrade": "Trees", "side": "both"})
            print("  loop trees:", short(t, ("applied", "cost", "errors")), flush=True)
        if build("Small Road", SQUARE_LANE):
            t = post("/roadupgrade", {"box": [-3950, -1455, -3900, -1110], "prefabFilter": "Small Road", "upgrade": "Trees", "side": "both"})
            print("  square lane trees:", short(t, ("applied", "cost", "errors")), flush=True)




def side_facing(s, px, pz):
    (sx, sz), (ex, ez) = s["start"], s["end"]
    dx, dz = ex - sx, ez - sz
    mx, mz = (sx + ex) / 2, (sz + ez) / 2
    return "right" if dz * (px - mx) - dx * (pz - mz) > 0 else "left"


def other(side):
    return "left" if side == "right" else "right"


def edges_of(prefab, box):
    out = {}
    cx, cz = (box[0] + box[2]) / 2, (box[1] + box[3]) / 2
    for s in get("/nets", x=cx, z=cz, radius=max(box[2] - box[0], box[3] - box[1]) / 2 + 50, filter=prefab).get("segments") or []:
        mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
        if box[0] <= mx <= box[2] and box[1] <= mz <= box[3] and s.get("prefab") == prefab:
            out[s["id"]] = s
    return list(out.values())


def place(prefab, x, z, box, side_x=None):
    for c in find_spot(prefab, x, z, radius=200, n=40, within=SWBP):
        if not (box[0] <= c["x"] <= box[1] and box[2] <= c["z"] <= box[3]):
            continue
        b = dict(c["building"])
        b.pop("prefab", None)
        d = post("/building", {"prefab": prefab, **b, "dryRun": True})
        if d.get("errors") or d.get("wouldDemolish"):
            continue
        r = building(prefab, **b)
        print(f"  {prefab}: {short(r, ('applied', 'cost', 'placedId', 'roadConnected'))} at ({c['x']}, {c['z']})", flush=True)
        return r.get("placedId")
    print(f"  {prefab}: no spot in {box}", flush=True)
    return None


def stage_centre():
    out = {}
    with paused(WHO):
        # the square and civic services on Square Lane (x ~ -3930), park facing Westbrook at the west end
        out["square"] = place("CityPark11", -3880, -1220, (-3900, -3820, -1290, -1150))
        out["busstation"] = place("BusStation02", -3970, -1180, (-4010, -3945, -1240, -1120))
        out["police"] = place("PoliceStation02", -3970, -1280, (-4010, -3945, -1340, -1240))
        out["fire"] = place("FireHouse01", -3880, -1330, (-3915, -3850, -1400, -1290))
        out["post"] = place("PostOffice02", -3970, -1360, (-4010, -3945, -1430, -1335))
        out["westpark"] = place("CityPark04", -4450, -1080, (-4520, -4380, -1160, -1020))
        jobs = []
        for s in edges_of("Large Road", [-4610, -1120, -3385, -860]):
            mx = (s["start"][0] + s["end"][0]) / 2
            mz = (s["start"][1] + s["end"][1]) / 2
            north = side_facing(s, mx, mz + 60)
            high = -4180 <= mx <= -3660
            jobs.append(("Office High" if high else "Office Low", s["id"], other(north), 6))
            jobs.append(("Office Low", s["id"], north, 4))   # leaves a ~100 m green strip to the SWQ homes
        for s in edges_of("Small Road", [-3950, -1455, -3900, -1110]):
            mz = (s["start"][1] + s["end"][1]) / 2
            jobs.append(("NA Commercial Low" if -1260 < mz < -1140 else "Office High", s["id"], "both", 3 if -1260 < mz < -1140 else 5))
        for s in edges_of("Medium Road", [-4250, -1470, -3590, -1085]):
            jobs.append(("Office Low", s["id"], "both", 5))
        cells = {}
        for name, eid, side, depth in jobs:
            z = zone(name, mode="road", edges=[eid], depth=depth, side=side, overwrite=False)
            cells[name] = cells.get(name, 0) + (z.get("cellsChanged") or 0)
        print("zoned cells:", cells, flush=True)
        out["cells"] = cells
    print(json.dumps(out))


if __name__ == "__main__":
    {"roads": stage_roads, "centre": stage_centre}[sys.argv[1]]()
