"""Phase 21 (milestone 12, ~55k people, medium demand back at 54): south-west medium-density district on
two upwind tiles (x -4030..-2900, z -900..-200) and the three new signature buildings."""
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


XS = [-2900, -3180, -3400, -3620, -3840, -4030]
ZS = [-210, -320, -430, -540, -650, -760, -870]
with paused():
    for x, z in ((-3739, -623), (-3116, -623)):
        step(f"buy tile {x},{z}", post("/buytile", {"x": x, "z": z}))
    for x in (-3009, -3229, -3400, -3620, -3840, -4030):
        step(f"link x={x}", road("Small Road", [[x, -100], [x, -210]]))
    for z in ZS:
        step(f"SW z={z}", road("Small Road", [[x, z] for x in XS]))
    for x in XS:
        step(f"SW x={x}", road("Small Road", [[x, z] for z in ZS]))
    step("SW medium", zone("NA Residential Medium", mode="rect", points=[[-4045, -900], [-2880, -160]], overwrite=False))
    for label, prefab, x, z in (("SW school", "ElementarySchool02", -3510, -430), ("SW clinic", "MedicalClinic01", -3730, -650),
                                ("SW park", "CityPark02", -3290, -650), ("SW police", "PoliceStation02", -3950, -320),
                                ("sig residential medium 3", "NA_ResidentialMediumSignature03", -3730, -320),
                                ("sig office 3", "OfficeLowSignature03", -1689, -260),
                                ("sig industry 5", "IndustrialManufacturingSignature05", -80, 3200)):
        put(label, prefab, x, z)
print(get("/status"))
