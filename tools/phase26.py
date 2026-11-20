"""Phase 26 - "Claude Park": the free strip between the northern residential district (x ~ -1200) and the
highway becomes a park + attraction zone. It buffers the homes against highway noise and gives the
north the park coverage that the Entertainment happiness factor is computed from."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import building, get, paused, post, road, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")

TS = (0.5, 0.35, 0.65, 0.2, 0.8, 0.05, 0.95)


def segs_in(x0, x1, z0, z1, filt="Road"):
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    r = max(x1 - x0, z1 - z0) / 2 + 60
    out = []
    for s in get("/nets", x=cx, z=cz, radius=r, filter=filt).get("segments") or []:
        mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
        if x0 <= mx <= x1 and z0 <= mz <= z1 and "Highway" not in (s.get("prefab") or ""):
            out.append(s)
    return out


def fit(label, prefab, box, near=None, sides=("left", "right")):
    """Try every road segment in box (x0,x1,z0,z1), several t and both sides; build at the first valid spot,
    preferring segments closest to `near`."""
    segs = segs_in(*box)
    if near:
        segs.sort(key=lambda s: ((s["start"][0] + s["end"][0]) / 2 - near[0]) ** 2 + ((s["start"][1] + s["end"][1]) / 2 - near[1]) ** 2)
    last = None
    for s in segs:
        for t in TS:
            for side in sides:
                d = post("/building", {"prefab": prefab, "edge": s["id"], "t": t, "side": side, "dryRun": True})
                last = d
                if d.get("errors") == 0:
                    r = building(prefab, edge=s["id"], t=t, side=side)
                    print(f"{label:<28} {short(r)}", flush=True)
                    return r
    print(f"{label:<28} no spot ({short(last or {})})", flush=True)
    return None


def step(label, r):
    print(f"{label:<28} {short(r)}", flush=True)
    return r


NORTH = (-1260, -600, 3045, 3420)
SOUTH = (-1260, -760, 2760, 3040)

with paused():
    # park roads: a spine north from the z=3040 avenue and a loop road along the top
    step("park spine x=-960", road("Small Road", [[-960, 3040], [-960, 3150], [-960, 3260], [-960, 3400]]))
    step("park top z=3400", road("Small Road", [[-1249, 3315], [-1249, 3400], [-1100, 3400], [-960, 3400], [-800, 3400]]))
    step("park south spine", road("Small Road", [[-1000, 3040], [-1000, 2900], [-1000, 2790]]))

    fit("castle (south)", "OldCastle01", SOUTH, near=(-1050, 2950))
    fit("observation tower", "ObservationTower01", NORTH, near=(-860, 3150))
    fit("bronze statue", "BronzeStatue01", NORTH, near=(-1100, 3100))
    for prefab, near in (("CityPark04", (-1100, 3250)), ("CityPark11", (-860, 3330)), ("CityPark07", (-1150, 3350)),
                         ("SunkenPlaza01", (-1050, 3060)), ("FootballField01", (-1100, 2850)), ("CommunityPool01", (-900, 3060)),
                         ("CityPark03", (-880, 2900)), ("ChirperPark04", (-1150, 2950)), ("TennisCourt01", (-1000, 3300)),
                         ("SkatePark01", (-900, 3380)), ("ParkMaintenanceDepot01", (-1200, 3380))):
        fit(prefab, prefab, NORTH if near[1] > 3040 else SOUTH, near=near)
print(get("/status"))
