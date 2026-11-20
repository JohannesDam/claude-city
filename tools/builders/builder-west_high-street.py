"""builder-west, item b1-economy-west-high-streets (conductor jobs push: Westbrook 24k residents, ~1.2k jobs;
office demand high, commercial low-moderate).

"Market Street": a tree-lined Medium Road through the free owned land south of the z=-650 villa street, from
the x=-4600 lane's south corner (-4600,-870) curving west and north to the South Lane / villa street junction
(-4935,-650): a second route for south Westbrook and a clean work street (offices and shops are allowed upwind).
Replaces 1 Medium (32 hh) and 3 villas; pays back in ~600-800 jobs.
Zoning (overwrite=False): Office Low on the south side and on the north side away from the homes,
NA Commercial Low on the north side near both ends (village shops), NA Residential Mixed (shops below homes) on
the east side of the north-south stretch next to the South Lane junction; the west side stays woodland.
Usage: python -I builders/builder-west_high-street.py [dry]
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, demolition_cost, find_spot, get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[1:]
LOG = r"C:\soft\cityskyline\logs\council\demolitions.jsonl"
PTS = [[-4600, -870], [-4720, -888], [-4850, -875], [-4925, -810], [-4945, -730], [-4935, -650]]
WESTBROOK = [-5290, -920, -4310, 2170]


def side_facing(s, px, pz):
    (sx, sz), (ex, ez) = s["start"], s["end"]
    dx, dz = ex - sx, ez - sz
    mx, mz = (sx + ex) / 2, (sz + ez) / 2
    return "right" if dz * (px - mx) - dx * (pz - mz) > 0 else "left"


def market_edges():
    out = {}
    for cx, cz in ((-4700, -880), (-4900, -780)):
        for s in get("/nets", x=cx, z=cz, radius=170, filter="Medium Road").get("segments") or []:
            mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
            if -4960 < mx < -4600 and -900 < mz < -655:
                out[s["id"]] = s
    return list(out.values())


def main():
    d = post("/road", {"prefab": "Medium Road", "points": PTS, "curve": "smooth", "dryRun": True, "allowDemolish": True})
    cost = demolition_cost(d.get("wouldDemolish"))
    print("dry:", short(d, ("errors", "cost")), cost, flush=True)
    if d.get("errors") or cost["households"] > 60 or cost["companies"] or DRY:
        return
    out = {}
    with paused(WHO):
        r = post("/road", {"prefab": "Medium Road", "points": PTS, "curve": "smooth", "allowDemolish": True})
        print("market street:", short(r, ("applied", "cost", "errors")), flush=True)
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": WHO, "prefab": "Medium Road (Westbrook Market Street)",
                                "item": "b1-economy-west-high-streets", "ids": [w["id"] for w in d.get("wouldDemolish") or []],
                                "cost": cost}) + "\n")
        time.sleep(1)
        for upg in ("Trees", "Grass"):
            t = post("/roadupgrade", {"box": [-4960, -900, -4598, -655], "prefabFilter": "Medium Road", "upgrade": upg, "side": "both"})
            print(f"{upg}:", short(t, ("applied", "cost", "errors")), flush=True)
        c = next((k for k in find_spot("PocketPark08", -4900, -760, radius=150, n=30, within=WESTBROOK)
                  if -4935 < k["x"] < -4860 and -800 < k["z"] < -700), None)
        if c:
            b = dict(c["building"])
            b.pop("prefab", None)
            r = building("PocketPark08", **b)
            out["square"] = r.get("placedId")
            print("square:", short(r, ("applied", "cost", "placedId")), flush=True)
        jobs = []
        for s in market_edges():
            (sx, sz), (ex, ez) = s["start"], s["end"]
            mx, mz = (sx + ex) / 2, (sz + ez) / 2
            if abs(ex - sx) >= abs(ez - sz):          # east-west stretch
                north = side_facing(s, mx, mz + 60)
                south = "left" if north == "right" else "right"
                near_end = mx > -4680 or mx < -4880
                jobs.append(("NA Commercial Low" if near_end else "Office Low", s["id"], north, 3 if near_end else 5))
                jobs.append(("Office Low", s["id"], south, 4))
            else:                                       # north-south stretch at the woodland edge
                east = side_facing(s, mx + 60, mz)
                jobs.append(("NA Residential Mixed", s["id"], east, 4))
        for name, eid, side, depth in jobs:
            z = zone(name, mode="road", edges=[eid], depth=depth, side=side, overwrite=False)
            print(f"  {name:22} {eid:>12} {side:5} d{depth} {short(z, ('cellsChanged', 'occupiedCellsRezoned'))}", flush=True)
    print(json.dumps(out))


if __name__ == "__main__":
    main()
