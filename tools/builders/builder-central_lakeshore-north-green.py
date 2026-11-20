"""builder-central: Lakeshore North Green (worklist b1-parks-lakeshore-north-quiet-green).

All 16 Noise Pollution icons in the city sit on low-density homes at x -600..-340, z 2630..2740, 100-200 m
south of the Eastern Works industry (z >= 2835). Step 1 of the item (sound barriers / trees on the z=2820
road) is impossible: Small Roads take no sound barrier, and trees/grass overlap the path of CityPark06
242878:355. Noise does not stop at parks, so distance is the fix: the 16 icon homes plus 3 neighbours on
the noisiest row are written off (19 households, residential demand 100), their cells are dezoned so no
new homes grow there, and the land becomes the North Green (parks fronting the existing streets, trees
on the streets). The signature building NA_ResidentialMediumSignature02 469972:23 is kept.

Run: cd C:/soft/cityskyline/tools && python -I builders/builder-central_lakeshore-north-green.py clear|parks|trees [--dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short, find_spot, building  # noqa: E402

WHO = "builder-central"
DRY = "--dry" in sys.argv
STAGE = next((a for a in sys.argv[1:] if not a.startswith("--")), "clear")

ICON_HOMES = ["806680:19", "823595:1", "235823:109", "462156:7", "820206:19", "821745:7", "821428:13", "823988:1",
              "824106:1", "816215:27", "819863:17", "822127:3", "374861:51", "799944:5", "564993:7", "807094:23"]
ROW_NEIGHBOURS = ["813532:7", "649967:15", "760053:3"]  # z=2690 row west of x=-526, inside the 150 m buffer
KEEP = {"469972:23"}  # signature building

# cells to dezone after clearing (x0, z0, x1, z1); they avoid the lots that stay
DEZONE = [
    (-620, 2716, -330, 2762),   # north side of the z=2710 street
    (-620, 2680, -526, 2704),   # south side of the z=2710 street, west of the x=-470 lots
    (-526, 2655, -330, 2704),   # north half of the block x -526..-330
    (-414, 2604, -330, 2655),   # lots of 823988:1 / 824106:1 on the z=2600 street
    (-462, 2628, -414, 2654),   # lot of 799944:5 on the x=-470 street
]

# parks of the North Green: (label, prefab, near x, near z)
PARKS = [
    ("west meadow", "CityPark06", -590, 2740),
    ("dog park", "DogPark01", -540, 2740),
    ("playground north", "Playground03", -500, 2740),
    ("central green", "CityPark06", -430, 2740),
    ("east green", "CityPark06", -370, 2740),
    ("south green", "CityPark02", -372, 2630),
    ("x=-470 pocket", "CityPark01", -440, 2676),
    ("west pocket", "CityPark01", -575, 2690),
]


def stage_clear():
    ids = [i for i in ICON_HOMES + ROW_NEIGHBOURS if i not in KEEP]
    with paused(WHO):
        d = post("/bulldoze", {"ids": ids, "dryRun": True})
        print("dry bulldoze", short(d, keys=("errors", "buildings", "error")), flush=True)
        if d.get("error") or DRY:
            return
        r = post("/bulldoze", {"ids": ids})
        print("bulldoze", short(r, keys=("applied", "errors", "buildings", "error")), flush=True)
        for x0, z0, x1, z1 in DEZONE:
            z = post("/zone", {"zone": "none", "mode": "rect", "points": [[x0, z0], [x1, z1]]})
            print("dezone", (x0, z0, x1, z1), short(z, keys=("applied", "errors", "cells", "changed", "error")), flush=True)


def stage_parks():
    placed = []
    with paused(WHO):
        for label, prefab, x, z in PARKS:
            done = False
            for c in find_spot(prefab, x, z, radius=60, n=6):
                b = dict(c["buildingXZ"])
                b.pop("prefab", None)
                d = post("/building", {"prefab": prefab, **b, "dryRun": True})
                if d.get("errors") or d.get("wouldDemolish"):
                    continue
                if DRY:
                    print(f"{label:<18} dry ok at {c['x']},{c['z']}", flush=True)
                    done = True
                    break
                r = building(prefab, **b)
                print(f"{label:<18} {prefab:<13} {short(r)}", flush=True)
                if r.get("applied"):
                    placed.append({"label": label, "prefab": prefab, "id": r.get("placedId"), "x": c["x"], "z": c["z"]})
                done = True
                break
            if not done:
                print(f"{label:<18} no spot", flush=True)
    print(json.dumps(placed))


def stage_trees():
    with paused(WHO):
        for label, line in (("z=2710 street", [[-680, 2710], [-330, 2710]]), ("x=-470 street", [[-470, 2600], [-470, 2710]]),
                            ("z=2600 street", [[-540, 2600], [-330, 2600]])):
            r = post("/roadupgrade", {"polyline": line, "tolerance": 6, "prefabFilter": "Small Road", "upgrade": "Trees",
                                      "side": "both", "dryRun": DRY})
            print(f"trees {label:<16}", short(r, keys=("applied", "cost", "errors", "problems")), flush=True)


if __name__ == "__main__":
    {"clear": stage_clear, "parks": stage_parks, "trees": stage_trees}[STAGE]()
