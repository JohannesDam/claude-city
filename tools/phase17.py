"""Phase 17 (~42.8k people, low-density districts fill within 20 min): bigger expansion per round -
far-north district extended west (tile -2493,2493) and a new northern district (z 2820-3370) on
tiles (-1870,3117) and (-1247,3117), with services."""
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


def segs_on(axis, value, cx, cz, radius):
    out = []
    for s in get("/nets", x=cx, z=cz, radius=radius, filter="Small")["segments"]:
        i = 0 if axis == "x" else 1
        if abs(s["start"][i] - value) < 2 and abs(s["end"][i] - value) < 2:
            out.append(s["id"])
    return out


post("/budget", {"service": "Garbage Management", "percent": 150})
with paused():
    for x, z in ((-2493, 2493), (-1870, 3117), (-1247, 3117)):
        step(f"buy tile {x},{z}", post("/buytile", {"x": x, "z": z}))

    # far-north extended west
    ZF = [2160, 2270, 2380, 2490, 2600, 2710]
    XW = [-2129, -2349, -2569, -2789]
    for z in ZF:
        step(f"FN-west z={z}", road("Small Road", [[x, z] for x in XW]))
    for x in XW[1:]:
        step(f"FN-west x={x}", road("Small Road", [[x, z] for z in ZF]))
    step("FN-west residential", zone("NA Residential Low", mode="rect", points=[[-2840, 2110], [-2135, 2760]], overwrite=False))

    # new northern district
    XN = [-1249, -1469, -1689, -1909, -2129]
    ZN = [2820, 2930, 3040, 3150, 3260, 3370]
    for z in ZN:
        step(f"N z={z}", road("Small Road", [[x, z] for x in XN]))
    for x in XN:
        step(f"N x={x}", road("Small Road", [[x, 2710]] + [[x, z] for z in ZN]))
    step("N residential", zone("NA Residential Low", mode="rect", points=[[-2180, 2765], [-1200, 3420]], overwrite=False))
    step("N medium x=-1689", zone("NA Residential Medium", mode="road", edges=segs_on("x", -1689, -1689, 3095, 300), depth=4))
    step("N commercial z=3040", zone("NA Commercial Low", mode="road", edges=segs_on("z", 3040, -1689, 3040, 480), depth=2))

    for label, prefab, x, z in (("FN-west school", "ElementarySchool02", -2459, 2380), ("FN-west park", "CityPark02", -2679, 2600),
                                ("FN-west clinic", "MedicalClinic02", -2459, 2600), ("N school", "ElementarySchool02", -1909, 2930),
                                ("N high school", "HighSchool02", -1469, 3260), ("N clinic", "MedicalClinic01", -1909, 3260),
                                ("N fire", "FireHouse01", -1579, 2930), ("N police", "PoliceStation02", -2019, 3150),
                                ("N park", "CityPark02", -1359, 3040)):
        put(label, prefab, x, z)
print(get("/status"))
