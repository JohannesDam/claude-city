"""Phase 18 (~48k people; garbage/hearse notices in the far north, office demand 97): a northern
service yard (landfills + large cemetery) above the newest district on tile (-1870,3741), with an
office strip as buffer between homes and the yard. Wind blows NE, away from the homes."""
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
    return step(label, r if r else ("no spot", short(info or {})))


with paused():
    step("buy tile -1870,3741", post("/buytile", {"x": -1870, "z": 3741}))
    step("buy tile -1247,3741", post("/buytile", {"x": -1247, "z": 3741}))
    XS = [-1249, -1469, -1689, -1909, -2129]
    step("office street z=3480", road("Small Road", [[x, 3480] for x in XS]))
    for x in (-1469, -1909):
        step(f"x={x} north", road("Small Road", [[x, 3370], [x, 3480], [x, 3700], [x, 3900]]))
    step("yard road z=3700", road("Small Road", [[-2129, 3700], [-1909, 3700], [-1689, 3700], [-1469, 3700], [-1249, 3700]]))
    step("office buffer", zone("Office Low", mode="rect", points=[[-2180, 3425], [-1200, 3540]], overwrite=False))
    put("landfill north 1", "Landfill01", -1800, 3800)
    put("landfill north 2", "Landfill01", -1350, 3800)
    put("cemetery north large", "Cemetery01", -2050, 3800)
print(get("/status"))
