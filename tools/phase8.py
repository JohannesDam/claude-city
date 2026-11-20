"""Phase 8 (milestone 7, ~13k people, happiness falling): lower residential tax, more landfills and
wind power on the southern tiles, extra clinic and police."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import building, edge_near, get, post, road, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")


def step(label, res):
    print(f"{label:<28} {short(res)}")
    return res


def place(label, prefab, x, z, filt="Small", radius=60, ts=(0.5, 0.3, 0.7, 0.15, 0.85)):
    e = edge_near(x, z, filt, radius=radius)
    r = {"error": "no edge near %s,%s" % (x, z)}
    for t in ts:
        for side in ("left", "right"):
            r = building(prefab, edge=e, t=t, side=side, dryRun=True)
            if r.get("errors") == 0:
                return step(f"{label} (t={t} {side})", building(prefab, edge=e, t=t, side=side))
    return step(f"{label} FAILED", r)


print(post("/taxes", {"residential": 10}))
step("spine south to -700", road("Small Road", [[-1140, -420], [-1140, -560], [-1140, -700]]))
step("z=-700", road("Small Road", [[-1140, -700], [-1500, -700], [-1900, -700], [-2100, -700]]))
place("landfill 5", "Landfill01", -1350, -700)
place("landfill 6", "Landfill01", -1750, -700)
for x in (-1250, -1450, -1650, -1850):
    place(f"turbine z=-420 x={x}", "WindTurbine01", x, -420, ts=(0.5, 0.3, 0.7))
place("clinic east", "MedicalClinic02", -900, 1230)
place("police north", "PoliceStation02", -1579, 1610)
print(get("/status"))
