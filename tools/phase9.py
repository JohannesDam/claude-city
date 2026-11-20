"""Phase 9 (~16k people; demand R/C/I all high): southern industrial band, far-west district on two
new tiles with a commercial main street."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import building, edge_near, get, post, road, short, zone  # noqa: E402

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


# southern industry band
step("ind z=-260", road("Small Road", [[-2129, -260], [-1909, -260], [-1689, -260], [-1469, -260], [-1140, -260]]))
for x in (-1909, -1469):
    step(f"ind x={x}", road("Small Road", [[x, -100], [x, -260], [x, -420]]))
step("ind x=-2129", road("Small Road", [[-2129, -100], [-2129, -260]]))
step("ind z=-100 west", road("Small Road", [[-1909, -100], [-2129, -100]]))
step("industry south band", zone("Industrial Manufacturing", mode="rect", points=[[-2160, -370], [-1160, -150]], overwrite=False))

# far-west tiles and district
for x, z in ((-3116, 623), (-3116, 1247)):
    step(f"buy tile {x},{z}", post("/buytile", {"x": x, "z": z}))
XS = [-2789, -3009, -3229, -3400]  # tile edge at x=-3428
ZS = [430, 540, 650, 760, 900, 1010, 1120, 1230, 1340, 1450]
for z in ZS:
    step(f"fw z={z}", road("Small Road", [[x, z] for x in XS]))
for x in XS[1:]:  # N-S streets
    step(f"fw x={x}", road("Small Road", [[x, z] for z in ZS]))
step("fw residential", zone("NA Residential Low", mode="rect", points=[[-3420, 380], [-2795, 1500]], overwrite=False))
main = [s["id"] for s in get("/nets", x=-3100, z=900, radius=330, filter="Small")["segments"]
        if abs(s["start"][1] - 900) < 2 and abs(s["end"][1] - 900) < 2]
step("fw commercial z=900", zone("NA Commercial Low", mode="road", edges=main, depth=3))
mid = [s["id"] for s in get("/nets", x=-3229, z=940, radius=560, filter="Small")["segments"]
       if abs(s["start"][0] + 3229) < 2 and abs(s["end"][0] + 3229) < 2]
step("fw medium x=-3229", zone("NA Residential Medium", mode="road", edges=mid, depth=4))
place("fw school", "ElementarySchool02", -3119, 650)
place("fw park", "CityPark02", -3119, 1230)
place("fw clinic", "MedicalClinic02", -3314, 1010)
print(get("/status"))
