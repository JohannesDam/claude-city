"""Phase 5: waterfront district between the highway and the lake (x -1080..-720, z 900..1450),
with the two unlocked signature residences."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import building, edge_near, road, short, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
ZS = [1010, 1120, 1230, 1340, 1450]


def step(label, res):
    print(f"{label:<28} {short(res)}")
    return res


step("shore road x=-720", road("Small Road", [[-720, 990]] + [[-720, z] for z in ZS]))
step("x=-900", road("Small Road", [[-900, 900]] + [[-900, z] for z in ZS]))
for z in ZS:
    step(f"z={z}", road("Small Road", [[-1070, z], [-900, z], [-720, z]]))
step("res low waterfront", zone("NA Residential Low", mode="rect", points=[[-1090, 1000], [-700, 1505]], overwrite=False))
for label, prefab, x, z in (("signature low", "NA_ResidentialLowSignature01", -810, 1175),
                            ("signature medium", "NA_ResidentialMediumSignature01", -985, 1285)):
    e = edge_near(x, z, "Small", radius=60)
    for side in ("left", "right"):
        r = building(prefab, edge=e, t=0.5, side=side, dryRun=True)
        if r.get("errors") == 0:
            step(f"{label} ({side})", building(prefab, edge=e, t=0.5, side=side))
            break
    else:
        step(f"{label} FAILED", r)
