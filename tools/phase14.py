"""Phase 14 (~31k people, power at 80%, homes full): coal plant + wind in the far south, north-east
lakeside district east of the highway (tile -623,1870)."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import get, paused, place_in_area, post, road, short, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")


def step(label, res):
    print(f"{label:<26} {short(res) if isinstance(res, dict) else res}")
    return res


def put(label, prefab, x, z, radius=300):
    r, info = place_in_area(prefab, x, z, radius=radius, max_tries=40)
    return step(label, r if r else ("no spot", short(info) if isinstance(info, dict) else info))


with paused():
    put("coal power plant", "SmallCoalPowerPlant01", -1140, -1170)
    for i, (x, z) in enumerate(((-1469, -1330), (-1689, -1330), (-1909, -1330), (-2129, -1220))):
        put(f"wind {i}", "WindTurbine01", x, z, radius=150)

    step("buy tile -623,1870", post("/buytile", {"x": -623, "z": 1870}))
    ZS = [1610, 1720, 1830, 1940, 2050, 2160]
    step("x=-900 north", road("Small Road", [[-900, 1500]] + [[-900, z] for z in ZS]))
    for x in (-680, -470):
        zs = [z for z in ZS if not (x == -470 and z < 1720)]
        step(f"x={x}", road("Small Road", [[x, z] for z in zs]))
    for z in ZS:
        xs = [-900, -680] + ([-470] if z >= 1720 else [])
        step(f"z={z}", road("Small Road", [[x, z] for x in xs]))
    step("residential NE", zone("NA Residential Low", mode="rect", points=[[-930, 1560], [-380, 2200]], overwrite=False))
    mid = [s["id"] for s in get("/nets", x=-680, z=1885, radius=300, filter="Small")["segments"]
           if abs(s["start"][0] + 680) < 2 and abs(s["end"][0] + 680) < 2]
    step("medium x=-680", zone("NA Residential Medium", mode="road", edges=mid, depth=4))
    put("NE school", "ElementarySchool02", -790, 1830)
    put("NE park", "CityPark02", -575, 2050)
    put("NE clinic", "MedicalClinic02", -790, 2050)
print(get("/status"))
