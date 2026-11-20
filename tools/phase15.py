"""Phase 15 (milestone 10, ~35k people, homes full): NW extension east of the outside power line,
NE lakeside extension north, and the five free signature buildings."""
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
    return step(label, r if r else ("no spot", short(info) if isinstance(info, dict) else info))


with paused():
    # NW: pylons at (-2446,1572) (-2539,1716) (-2614,1833) (-2690,1950) -> stay east of x=-2400
    step("buy tile -2493,1870", post("/buytile", {"x": -2493, "z": 1870}))
    NWZ = [1610, 1720, 1830, 1940, 2050, 2160]
    step("x=-2349", road("Small Road", [[-2349, z] for z in NWZ]))
    for z in NWZ:
        step(f"NW z={z}", road("Small Road", [[-2129, z], [-2349, z]]))
    step("NW residential", zone("NA Residential Medium", mode="rect", points=[[-2400, 1580], [-2135, 2200]], overwrite=False))

    # NE: further north along the lake
    step("buy tile -623,2493", post("/buytile", {"x": -623, "z": 2493}))
    NEZ = [2270, 2380, 2490, 2600, 2710]
    for x in (-680, -470):
        step(f"NE x={x}", road("Small Road", [[x, 2160]] + [[x, z] for z in NEZ]))
    for z in NEZ:
        step(f"NE z={z}", road("Small Road", [[-680, z], [-470, z], [-330, z]]))
    step("NE residential", zone("NA Residential Low", mode="rect", points=[[-760, 2200], [-320, 2760]], overwrite=False))

    # free signature buildings
    for label, prefab, x, z in (("sig res low 2", "NA_ResidentialLowSignature02", -2240, 1830),
                                ("sig res low 3", "NA_ResidentialLowSignature03", -575, 2380),
                                ("sig res medium 2", "NA_ResidentialMediumSignature02", -575, 2600),
                                ("sig office 1", "OfficeLowSignature01", -2459, 100),
                                ("sig office 2", "OfficeLowSignature02", -1689, 60)):
        put(label, prefab, x, z, radius=250)
print(get("/status"))
