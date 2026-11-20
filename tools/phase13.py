"""Phase 13 (~25k people, industrial demand 95, landfills filling): far-south industrial estate and
landfills on two new tiles (z -935..-1558), >1 km from homes."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import get, paused, place_in_area, post, road, short, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
XS = [-1140, -1469, -1689, -1909, -2129]
ZS = [-1000, -1110, -1220, -1330]


def step(label, res):
    print(f"{label:<26} {short(res) if isinstance(res, dict) else res}")
    return res


with paused():
    for x, z in ((-1870, -1247), (-1247, -1247)):
        step(f"buy tile {x},{z}", post("/buytile", {"x": x, "z": z}))
    step("spine south", road("Small Road", [[-1140, -700], [-1140, -850], [-1140, -1000]]))
    for z in ZS:
        step(f"z={z}", road("Small Road", [[x, z] for x in XS]))
    for x in XS:
        step(f"x={x}", road("Small Road", [[x, z] for z in ZS]))
    step("industry", zone("Industrial Manufacturing", mode="rect", points=[[-2180, -1280], [-1160, -950]], overwrite=False))
    for i, x in enumerate((-1300, -1600, -1900)):
        r, info = place_in_area("Landfill01", x, -1330, radius=200, max_tries=30)
        step(f"landfill south {i}", r if r else ("no spot", short(info) if isinstance(info, dict) else info))
print(get("/status"))
