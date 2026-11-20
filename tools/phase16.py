"""Phase 16 (~41k people, growth stalls on housing; hearse/garbage notices in the far districts):
two far-west districts on new tiles (north of the far-west grid and next to the office park),
a northern cemetery, more trucks via budgets."""
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


for svc, pct in (("Garbage Management", 130), ("Health & Deathcare", 125)):
    post("/budget", {"service": svc, "percent": pct})

XS = [-2789, -3009, -3229, -3400]
with paused():
    # far-west north (pylons start east of x~-2765 above z~2066, so stop at z=2050 there)
    step("buy tile -3116,1870", post("/buytile", {"x": -3116, "z": 1870}))
    for z in (1610, 1720, 1830, 1940, 2050):
        step(f"FWN z={z}", road("Small Road", [[x, z] for x in XS[1:]]))
    for x in XS[1:]:
        step(f"FWN x={x}", road("Small Road", [[x, 1450], [x, 1610], [x, 1720], [x, 1830], [x, 1940], [x, 2050]]))
    step("FWN residential", zone("NA Residential Low", mode="rect", points=[[-3420, 1510], [-2950, 2100]], overwrite=False))

    # far-west south, next to the office park
    step("buy tile -3116,0", post("/buytile", {"x": -3116, "z": 0}))
    for z in (-100, 60, 200, 320):
        step(f"FWS z={z}", road("Small Road", [[x, z] for x in XS]))
    for x in XS[1:]:
        step(f"FWS x={x}", road("Small Road", [[x, -100], [x, 60], [x, 200], [x, 320], [x, 430]]))
    step("FWS residential", zone("NA Residential Low", mode="rect", points=[[-3420, -150], [-2795, 380]], overwrite=False))
    mid = [s["id"] for s in get("/nets", x=-3229, z=110, radius=280, filter="Small")["segments"]
           if abs(s["start"][0] + 3229) < 2 and abs(s["end"][0] + 3229) < 2]
    step("FWS medium x=-3229", zone("NA Residential Medium", mode="road", edges=mid, depth=4))

    for label, prefab, x, z in (("FWN school", "ElementarySchool02", -3119, 1830), ("FWN park", "CityPark02", -3300, 1940),
                                ("FWS school", "ElementarySchool02", -3119, 140), ("FWS clinic", "MedicalClinic02", -3300, 260),
                                ("FWS fire", "FireHouse01", -2900, 0), ("FWS park", "CityPark06", -3300, -40),
                                ("cemetery north", "Cemetery02", -1800, 2600)):
        put(label, prefab, x, z)
print(get("/status"))
