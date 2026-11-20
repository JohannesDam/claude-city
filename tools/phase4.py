"""Phase 4 (milestone 4): office band between homes and industry (z=320), medium-density infill
west (x -2129) and east (z=830)."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import road, short, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")


def step(label, res):
    print(f"{label:<30} {short(res)}")
    return res


# B: office band
step("z=320", road("Small Road", [[-2129, 320], [-1909, 320], [-1689, 320], [-1469, 320], [-1140, 320], [-809, 320], [-720, 320]]))
for x in (-1909, -1689, -1469, -809):
    step(f"connector x={x}", road("Small Road", [[x, 320], [x, 430]]))
step("office band", zone("Office Low", mode="rect", points=[[-2180, 268], [-700, 380]]))

# A: west strip
step("x=-2129 south", road("Small Road", [[-2129, 320], [-2129, 430], [-2129, 540], [-2129, 650], [-2129, 760], [-2129, 900]]))
for z in (430, 540, 650, 760):
    step(f"west link z={z}", road("Small Road", [[-1909, z], [-2129, z]]))
step("west medium", zone("NA Residential Medium", mode="rect", points=[[-2180, 385], [-1915, 895]]))

# C: east block north of z=760
step("z=830 east", road("Small Road", [[-1138.5, 830], [-809, 830], [-720, 830]]))
step("x=-809 north", road("Small Road", [[-809, 760], [-809, 830], [-809, 900]]))
step("x=-720 south", road("Small Road", [[-720, 830], [-720, 850]]))
step("east medium", zone("NA Residential Medium", mode="rect", points=[[-1080, 775], [-730, 890]]))
