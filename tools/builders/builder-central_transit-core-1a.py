"""builder-central: Downtown transit core, renewal sub-phase 1a (worklist b1-housing-downtown-transit-core-p1).

Box x -2125..-1950, z 1240..1600 (west of the T1 avenue, inside the T2 Downtown Circle): 40 detached low-density
homes (40 households) on 220 x 110 m grid blocks between the avenue (T1), x=-2129 (T2), z=1340, z=1450 and
z=1610 (T2). Residential High demand is 100. Plan: clear the 40 homes (services ES03 381515:7, Playground04
1270728:25 and the transformer stay), then rezone: NA Residential High facing the avenue and the z=1340/1450
streets, NA Residential Mixed on the corners at the avenue, Residential LowRent on ~10%, NA Residential Medium
on x=-2129 (the T2 side). Then a square (CityPark02) and NA_ResidentialHighSignature01 facing the avenue.

Run: cd C:/soft/cityskyline/tools && python -I builders/builder-central_transit-core-1a.py clear|square|zone [--dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short, building  # noqa: E402

WHO = "builder-central"
DRY = "--dry" in sys.argv
STAGE = next((a for a in sys.argv[1:] if not a.startswith("--")), "clear")
BOX = [-2125, 1240, -1950, 1600]


def homes_in_box():
    d = post("/bulldoze", {"box": BOX, "buildings": True, "dryRun": True})
    return [b["id"] for b in d["buildings"]["list"] if b["kind"] == "zoned: NA Residential Low"]


def stage_clear():
    ids = homes_in_box()
    print(len(ids), "low-density homes")
    with paused(WHO):
        d = post("/bulldoze", {"ids": ids, "dryRun": True})
        print("dry", short(d, keys=("errors", "error")), len(d.get("buildings") or []))
        if not DRY and not d.get("error"):
            r = post("/bulldoze", {"ids": ids})
            print("bulldoze", short(r, keys=("applied", "errors", "error")))


# the square: two CityPark02 back to back through the middle of the block between z=1340 and z=1450
# (NA_ResidentialHighSignature01 is unique and already stands in Southgate)
SQUARE = [("square north", "CityPark02", {"x": -2043.17, "z": 1417.5, "angle": 0}),
          ("square south", "CityPark02", {"x": -2043.17, "z": 1372.5, "angle": -180})]

# rezoning of the cleared cells (overwrite: the cells are cleared, apart from 5 brand-new L1 homes that regrew
# at z~1312 since the clearance); ES03/Playground strip z 1238..1290 west of x=-2024 is left alone
ZONES = [
    ("NA Residential High", [[-2125, 1295], [-1950, 1600]]),
    ("NA Residential Mixed", [[-2020, 1240], [-1950, 1290]]),   # vacant strip at the T1 stop z=1175 / avenue
    ("NA Residential Mixed", [[-1995, 1295], [-1950, 1600]]),   # avenue corners: shops below homes
    ("Residential LowRent", [[-2125, 1520], [-2080, 1600]]),    # ~10%, next to the T2 corner
]
TREES = [("z=1340", [[-2129, 1340], [-1909, 1340]]), ("z=1450", [[-2129, 1450], [-1909, 1450]])]


def stage_square():
    with paused(WHO):
        for label, prefab, b in SQUARE:
            d = post("/building", {"prefab": prefab, **b, "dryRun": True})
            if d.get("errors") or d.get("wouldDemolish"):
                print(label, "SKIP", short(d))
                continue
            print(label, "dry ok" if DRY else short(building(prefab, **b)))


def stage_zone():
    with paused(WHO):
        for name, pts in ZONES:
            z = post("/zone", {"zone": name, "mode": "rect", "points": pts, "overwrite": True, "dryRun": DRY})
            print(name, pts, {k: z.get(k) for k in ("applied", "errors", "cellsChanged", "cellsFromZone", "occupiedCellsRezoned", "error")})
        for label, line in TREES:
            r = post("/roadupgrade", {"polyline": line, "tolerance": 6, "prefabFilter": "Small Road", "upgrade": "Trees",
                                      "side": "both", "dryRun": DRY})
            print("trees", label, short(r, keys=("applied", "cost", "errors", "problems")))


if __name__ == "__main__":
    {"clear": stage_clear, "square": stage_square, "zone": stage_zone}[STAGE]()
