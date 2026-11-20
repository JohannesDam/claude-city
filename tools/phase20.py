"""Phase 20 (51k people; low and medium housing demand 100): westernmost districts on three new tiles
(x -4052..-3428, z -311..2181) - upwind of every polluter - with medium density and services."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import get, paused, place_in_area, post, road, short, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")


def step(label, res):
    print(f"{label:<28} {short(res) if isinstance(res, dict) else res}")
    return res


def put(label, prefab, x, z, radius=300):
    r, info = place_in_area(prefab, x, z, radius=radius, max_tries=40)
    return step(label, r if r else ("no spot", short(info or {})))


XS = [-3400, -3620, -3840, -4030]
ZS = [-100, 60, 200, 320, 430, 540, 650, 760, 900, 1010, 1120, 1230, 1340, 1450, 1610, 1720, 1830, 1940, 2050]
with paused():
    for x, z in ((-3739, 623), (-3739, 1247), (-3739, 0), (-3739, 1870)):
        step(f"buy tile {x},{z}", post("/buytile", {"x": x, "z": z}))
    for z in ZS:
        step(f"WW z={z}", road("Small Road", [[x, z] for x in XS]))
    for x in XS[1:]:
        # N-S streets in two halves (long polylines through many fresh nodes are split)
        step(f"WW x={x} south", road("Small Road", [[x, z] for z in ZS[:10]]))
        step(f"WW x={x} north", road("Small Road", [[x, z] for z in ZS[9:]]))
    step("WW medium", zone("NA Residential Medium", mode="rect", points=[[-4045, -150], [-3410, 2100]], overwrite=False))
    for label, prefab, x, z in (("WW school S", "ElementarySchool02", -3730, 260), ("WW school N", "ElementarySchool02", -3730, 1500),
                                ("WW high school", "HighSchool02", -3730, 900), ("WW clinic S", "MedicalClinic01", -3950, 540),
                                ("WW clinic N", "MedicalClinic01", -3950, 1720), ("WW fire", "FireHouse01", -3510, 1120),
                                ("WW police", "PoliceStation02", -3510, 430), ("WW park S", "CityPark02", -3950, 100),
                                ("WW park N", "CityPark02", -3510, 1940), ("WW cemetery", "Cemetery02", -3950, 1300)):
        put(label, prefab, x, z)
print(get("/status"))
