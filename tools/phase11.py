"""Phase 11 (~24k people, happiness 45, many service notifications): spread health, fire, police,
garbage and parks over the districts. Builds while paused, finds free spots automatically."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import get, paused, place_in_area, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")

PLAN = [
    ("clinic north", "MedicalClinic01", -1700, 1800, 350),
    ("clinic west", "MedicalClinic01", -2459, 1100, 350),
    ("clinic far-west", "MedicalClinic02", -3100, 600, 350),
    ("fire west", "FireHouse01", -2459, 700, 350),
    ("fire far-west", "FireHouse01", -3119, 1100, 350),
    ("fire south", "FireHouse02", -1700, -200, 350),
    ("police west", "PoliceStation02", -2459, 540, 350),
    ("police far-west", "PoliceStation02", -3119, 1340, 350),
    ("landfill 7", "Landfill01", -1500, -850, 450),
    ("landfill 8", "Landfill01", -2000, -850, 450),
    ("park far-west", "CityPark02", -3000, 1000, 350),
    ("park west", "CityPark02", -2459, 1300, 350),
    ("park north", "CityPark06", -1600, 1950, 350),
    ("park central", "CityPark06", -1600, 600, 350),
    ("playground far-west", "Playground05", -3200, 500, 350),
]

with paused():
    for label, prefab, x, z, radius in PLAN:
        r, info = place_in_area(prefab, x, z, radius=radius, max_tries=40)
        print(f"{label:<22} {short(r) if r else ('no spot', short(info) if isinstance(info, dict) else info)}")
print(get("/status"))
