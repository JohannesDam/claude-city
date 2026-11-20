"""Phase 7 (milestone 6, ~11.5k people): east industry -> offices (air pollution near homes),
second water pump, landfills and a northern district on newly bought tiles, high school, clinic."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import building, edge_near, get, post, road, short, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")


def step(label, res):
    print(f"{label:<30} {short(res)}")
    return res


def place(label, prefab, x, z, filt="Small", radius=60, ts=(0.5, 0.3, 0.7, 0.15, 0.85)):
    """Try a few positions along the nearest segment, both sides, and build at the first valid one."""
    e = edge_near(x, z, filt, radius=radius)
    r = {"error": "no edge near %s,%s" % (x, z)}
    for t in ts:
        for side in ("left", "right"):
            r = building(prefab, edge=e, t=t, side=side, dryRun=True)
            if r.get("errors") == 0:
                return step(f"{label} (t={t} {side})", building(prefab, edge=e, t=t, side=side))
    return step(f"{label} FAILED", r)


# offices replace the eastern industry strip (wind carries its smog into the homes)
step("east industry -> office", zone("Office Low", mode="rect", points=[[-1100, 140], [-640, 262]]))

# second water pump on the waterfront shore road
place("water pump 2", "WaterPumpingStation01", -720, 1285, ts=(0.5, 0.3, 0.7))

# bigger clinic on the office street
place("clinic central", "MedicalClinic01", -2019, 320)

# land: north row and south row tiles
for x, z in ((-1870, 1870), (-1247, 1870), (-1870, -623), (-1247, -623)):
    step(f"buy tile {x},{z}", post("/buytile", {"x": x, "z": z}))

# northern district (west of the highway), continuing the 220 m x 110 m grid
# the outside high-voltage line runs diagonally west of x=-2290 (pylons at z~1716/1833/1950)
XS = [-1249, -1469, -1689, -1909, -2129]
ZN = [1610, 1720, 1830, 1940, 2050]
step("save before", post("/save", {"name": "Claude City"}))
for z in ZN:
    step(f"north z={z}", road("Small Road", [[x, z] for x in XS]))
for x in XS:
    start = [[x, 1450]] if x != -1249 else [[x, 1500]]
    step(f"north x={x}", road("Small Road", start + [[x, 1500]] * (x != -1249) + [[x, z] for z in ZN]))
step("north residential", zone("NA Residential Low", mode="rect", points=[[-2180, 1510], [-1200, 2100]], overwrite=False))
cs = [s["id"] for s in get("/nets", x=-1689, z=1720, radius=500, filter="Small")["segments"]
      if abs(s["start"][1] - 1720) < 2 and abs(s["end"][1] - 1720) < 2]
step("north mixed z=1720", zone("NA Residential Mixed", mode="road", edges=cs, depth=2))
place("high school", "HighSchool02", -1799, 1940)
place("elementary north", "ElementarySchool02", -2019, 1830)
place("park north", "CityPark02", -1359, 1830)

# south: landfills along a new road
step("south road z=-420", road("Small Road", [[-1140, -100], [-1140, -420], [-1500, -420], [-1900, -420]]))
place("landfill 3", "Landfill01", -1320, -420)
place("landfill 4", "Landfill01", -1700, -420)
print(get("/status"))
