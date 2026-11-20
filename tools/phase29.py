"""Phase 29 - Westpark. GET /coverage?service=Park&layer=homes showed 30% of households without any park
coverage, almost all in the dense west and south-west (Entertainment happiness penalty). The whole west
edge is one road (x=-4030) with homes on its east side and nothing on its west side. Buy the five
western tiles and line the west side of that road with parks, sports and play areas: every block of
the west gets a park across the street. Upwind (wind SW->NE), so clean air."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import fit, get, paused, post  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")

EDGE = (-4045, -4015, -900, 1960)   # segments of the x=-4030 road

PARKS = [  # (prefab, z) from south to north, alternating large parks, sports and small play areas
    ("CityPark04", -820), ("Playground04", -700), ("FootballField01", -560), ("CityPark03", -420),
    ("CommunityPool01", -300), ("CityPark11", -170), ("DogPark02", -40), ("CityPark07", 90),
    ("TennisCourt01", 210), ("SunkenPlaza01", 320), ("CityPark04", 450), ("BasketballCourt04", 580),
    ("CityPark10", 700), ("AmericanFootballField01", 840), ("CityPark03", 980), ("ChirperPark04", 1100),
    ("CityPark11", 1230), ("SkatePark01", 1350), ("CityPark07", 1470), ("CityPark08", 1600),
    ("CommunityPool01", 1700), ("CityPark04", 1830), ("ParkMaintenanceDepot01", 400), ("ParkMaintenanceDepot01", 1300),
]

with paused():
    for z in (-623, 0, 623, 1247, 1870):
        r = post("/buytile", {"x": -4363, "z": z})
        print(f"tile -4363,{z}: bought={r.get('bought')} cost={r.get('cost')} status={r.get('status')}", flush=True)
    for prefab, z in PARKS:
        fit(f"{prefab} z={z}", prefab, EDGE, near=(-4030, z), ts=(0.5, 0.25, 0.75, 0.05, 0.95))
print(get("/status"))
