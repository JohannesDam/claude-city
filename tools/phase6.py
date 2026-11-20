"""Phase 6 (milestone 5, ~7.4k people): second highway connection in the north, utilities,
second landfill, north services, and a new western district on two bought map tiles."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import building, edge_near, get, post, road, short, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")


def step(label, res):
    print(f"{label:<30} {short(res)}")
    return res


def place(label, prefab, x, z, filt="Small", radius=60):
    e = edge_near(x, z, filt, radius=radius)
    r = {"error": "no edge"}
    for side in ("left", "right"):
        r = building(prefab, edge=e, t=0.5, side=side, dryRun=True)
        if r.get("errors") == 0:
            return step(f"{label} ({side})", building(prefab, edge=e, t=0.5, side=side))
    return step(f"{label} FAILED", r)


# 1. second highway connection, linking north district and waterfront
step("highway crossing z=1500", road("Medium Road", [[-1249, 1500], [-1115, 1500], [-1095, 1500], [-900, 1500]]))
step("x=-1249 to 1500", road("Small Road", [[-1249, 1450], [-1249, 1500]]))
step("x=-900 to 1500", road("Small Road", [[-900, 1450], [-900, 1500]]))

# 2. utilities
for svc in ("Water & Sewage", "Electricity"):
    post("/budget", {"service": svc, "percent": 100})
for x in (-1760, -1860):
    place(f"wind turbine {x}", "WindTurbine01", x, -100)

# 3. second landfill next to the first (south of the south road)
place("landfill 2", "Landfill01", -1290, -100)

# 4. north services
place("clinic north", "MedicalClinic02", -1359, 1340)
place("fire house north", "FireHouse01", -1799, 1450)

# 5. west tiles + district
for x, z in ((-2493, 623), (-2493, 1247)):
    step(f"buy tile {x},{z}", post("/buytile", {"x": x, "z": z}))
XS = [-2129, -2349, -2569, -2789]
for z in (430, 540, 650, 760, 900, 1010, 1120, 1230, 1340, 1450):
    step(f"west street z={z}", road("Small Road", [[x, z] for x in XS]))
for x in XS[1:]:
    step(f"west street x={x}", road("Small Road", [[x, 430], [x, 540], [x, 650], [x, 760], [x, 900], [x, 1010], [x, 1120], [x, 1230], [x, 1340], [x, 1450]]))
step("west residential low", zone("NA Residential Low", mode="rect", points=[[-2840, 380], [-2135, 1500]], overwrite=False))
mid = [s["id"] for s in get("/nets", x=-2569, z=940, radius=560, filter="Small")["segments"]
       if abs(s["start"][0] + 2569) < 2 and abs(s["end"][0] + 2569) < 2]
step("west mixed x=-2569", zone("NA Residential Mixed", mode="road", edges=mid, depth=3))
print(get("/status"))
