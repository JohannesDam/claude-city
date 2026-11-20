"""builder-west, item b1-parks-west-greenway - first cycling greenway, "Westpark Greenway" on the z=760 street.

z=760 is the one continuous east-west street from Westbrook (x=-4380 lane, over the new tree-lined Westpark
crossing) through all of Westside to the Downtown edge (x -2422): 1.96 km. Trees both sides first (Westside
part has none), then Bikelane both sides (it replaces the Grass strip on the Medium Road crossing, Trees stay).
Bicycle parking at the Westpark end and at local centres along the route, pocket parks on spare corner lots at
junctions (findspot only, no demolition). The Downtown section (x -2422..-1909) is a follow-up for builder-central.
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, find_spot, paused, post, short  # noqa: E402

WHO = "builder-west"
ROUTE = [[-4380, 760], [-2422, 760]]
WEST = [-4380, 600, -2440, 920]   # Westbrook lane to the Westside/Downtown edge, close to the route
BIKE = [(-4000, 760), (-3680, 760), (-3120, 760), (-2620, 760)]
PARKS = [(-3850, 760), (-3400, 760), (-2900, 760)]


def near_route(prefab, x, z, maxdz=40, maxdx=150):
    for c in find_spot(prefab, x, z, radius=200, n=40, within=WEST):
        if abs(c["z"] - 760) <= maxdz and abs(c["x"] - x) <= maxdx:
            return c
    return None


def put(prefab, c):
    b = dict(c["building"])
    b.pop("prefab", None)
    d = post("/building", {"prefab": prefab, **b, "dryRun": True})
    if d.get("errors") or d.get("wouldDemolish"):
        return None
    r = building(prefab, **b)
    print(f"  {prefab}: {short(r, ('applied', 'cost', 'placedId', 'roadConnected'))} at ({c['x']}, {c['z']})", flush=True)
    return r.get("placedId")


def main():
    out = {"bike": [], "parks": []}
    with paused(WHO):
        for upg in ("Trees", "Bikelane"):
            r = post("/roadupgrade", {"polyline": ROUTE, "tolerance": 5, "upgrade": upg, "side": "both"})
            print(upg, short(r, ("applied", "cost", "errors")), "edges", len(r.get("edges") or []), flush=True)
        for x, z in BIKE:
            for pf in ("BicycleParkingArea02", "BicycleParkingArea01", "BicycleParkingArea03"):
                c = near_route(pf, x, z)
                if c:
                    out["bike"].append(put(pf, c))
                    break
            else:
                print(f"  bicycle parking: no spot near {x}", flush=True)
        for x, z in PARKS:
            for pf in ("PocketPark05", "PocketPark03", "PocketPark01"):
                c = near_route(pf, x, z, maxdx=200)
                if c:
                    out["parks"].append(put(pf, c))
                    break
            else:
                print(f"  pocket park: no spot near {x}", flush=True)
    print(json.dumps(out))


if __name__ == "__main__":
    main()
