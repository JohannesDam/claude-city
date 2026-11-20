"""Phase 2: north-west residential district on already-owned land (west of the highway).
Grid: E-W streets every 110 m (z = 900, 1010, 1120, 1230), N-S streets every 220 m continuing the
existing ones (x = -1249, -1469, -1689, -1909, -2129). Low density along streets, row houses
along the middle N-S street, a commercial strip along z = 1010."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import get, road, short, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
XS = [-1249, -1469, -1689, -1909, -2129]
ZS = [900, 1010, 1120, 1230]


def step(label, res):
    print(f"{label:<34} {short(res)}")
    return res


# west continuation of the z=900 road from the spine junction
step("z=900 west", road("Small Road", [[-1137.8, 900]] + [[x, 900] for x in XS]))
for z in ZS[1:]:
    step(f"z={z}", road("Small Road", [[x, z] for x in XS]))
for x in XS:
    pts = ([[x, 760]] if x in (-1469, -1689, -1909) else []) + [[x, z] for z in ZS]
    step(f"x={x}", road("Small Road", pts))

step("res low", zone("NA Residential Low", mode="rect", points=[[-2180, 770], [-1200, 1240]]))
mid = [s["id"] for s in get("/nets", x=-1689, z=1065, radius=200, filter="Small")["segments"]
       if abs(s["start"][0] + 1689) < 2 and abs(s["end"][0] + 1689) < 2 and min(s["start"][1], s["end"][1]) >= 890]
step("row houses x=-1689", zone("NA Residential Medium Row", mode="road", edges=mid, depth=4))
cs = [s["id"] for s in get("/nets", x=-1689, z=1010, radius=500, filter="Small")["segments"]
      if abs(s["start"][1] - 1010) < 2 and abs(s["end"][1] - 1010) < 2]
step("commercial z=1010", zone("NA Commercial Low", mode="road", edges=cs, depth=2))
print(get("/status"))
