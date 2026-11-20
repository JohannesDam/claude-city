"""Phase 12 (~22.7k people): far-north district on two new tiles (z 2160-2710), west of the highway,
with its own services. Built while paused, in small steps."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import get, paused, place_in_area, post, road, short, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
XS = [-1249, -1469, -1689, -1909, -2129]
ZS = [2160, 2270, 2380, 2490, 2600, 2710]


def step(label, res):
    print(f"{label:<26} {short(res) if isinstance(res, dict) else res}")
    return res


with paused():
    for x, z in ((-1870, 2493), (-1247, 2493)):
        step(f"buy tile {x},{z}", post("/buytile", {"x": x, "z": z}))
    for z in ZS:
        step(f"z={z}", road("Small Road", [[x, z] for x in XS]))
    for x in XS:
        step(f"x={x}", road("Small Road", [[x, 2050]] + [[x, z] for z in ZS]))
    step("residential low", zone("NA Residential Low", mode="rect", points=[[-2180, 2110], [-1200, 2760]], overwrite=False))
    mid = [s["id"] for s in get("/nets", x=-1689, z=2435, radius=330, filter="Small")["segments"]
           if abs(s["start"][0] + 1689) < 2 and abs(s["end"][0] + 1689) < 2]
    step("medium x=-1689", zone("NA Residential Medium", mode="road", edges=mid, depth=4))
    cs = [s["id"] for s in get("/nets", x=-1689, z=2380, radius=500, filter="Small")["segments"]
          if abs(s["start"][1] - 2380) < 2 and abs(s["end"][1] - 2380) < 2]
    step("commercial z=2380", zone("NA Commercial Low", mode="road", edges=cs, depth=2))
    for label, prefab, x, z in (("school", "ElementarySchool02", -1909, 2300), ("clinic", "MedicalClinic01", -1469, 2600),
                                ("fire", "FireHouse01", -2019, 2600), ("police", "PoliceStation02", -1359, 2270),
                                ("park", "CityPark02", -1799, 2490), ("high school", "HighSchool02", -2019, 2160)):
        r, info = place_in_area(prefab, x, z, radius=300, max_tries=40)
        step(label, r if r else ("no spot", short(info) if isinstance(info, dict) else info))
print(get("/status"))
