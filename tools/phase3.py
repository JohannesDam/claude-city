"""Phase 3: northern band (z = 1340, 1450) on owned land, medium density along the middle street,
plus first police station and fire house in the centre."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import building, edge_near, get, post, road, short, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
XS = [-1249, -1469, -1689, -1909, -2129]


def step(label, res):
    print(f"{label:<34} {short(res)}")
    return res


def place(label, prefab, x, z, filt):
    e = edge_near(x, z, filt)
    for side in ("left", "right"):
        r = building(prefab, edge=e, t=0.5, side=side, dryRun=True)
        if r.get("errors") == 0:
            return step(f"{label} ({side})", building(prefab, edge=e, t=0.5, side=side))
    return step(f"{label} FAILED", r)


for z in (1340, 1450):
    step(f"z={z}", road("Small Road", [[x, z] for x in XS]))
for x in XS:
    step(f"x={x}", road("Small Road", [[x, 1230], [x, 1340], [x, 1450]]))
step("res low north", zone("NA Residential Low", mode="rect", points=[[-2180, 1245], [-1200, 1510]]))
mid = [s["id"] for s in get("/nets", x=-1689, z=1340, radius=130, filter="Small")["segments"]
       if abs(s["start"][0] + 1689) < 2 and abs(s["end"][0] + 1689) < 2 and min(s["start"][1], s["end"][1]) >= 1225]
step("medium x=-1689 north", zone("NA Residential Medium", mode="road", edges=mid, depth=4))

place("police", "PoliceStation02", -1139, 480, "Medium")
place("fire house", "FireHouse01", -1579, 650, "Small")
print(post("/ui", {"action": "resume"}))
print(post("/speed", {"speed": 3}))
print(get("/status"))
