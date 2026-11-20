"""Phase 10 (milestone 8, ~20.7k people): western arterial (z=900) upgraded to Medium Road with
high-density housing along it, an office park on a new south-west tile, cemetery, more wind."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import building, edge_near, get, paused, post, road, short, upgrade, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")


def step(label, res):
    print(f"{label:<30} {short(res)}")
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


def z900(x0, x1, kind):
    return [s["id"] for s in get("/nets", x=(x0 + x1) / 2, z=900, radius=abs(x1 - x0) / 2 + 20, filter=kind)["segments"]
            if abs(s["start"][1] - 900) < 2 and abs(s["end"][1] - 900) < 2 and min(s["start"][0], s["end"][0]) >= min(x0, x1) - 1]


with paused():
    # western arterial
    for x0, x1 in ((-1909, -1249), (-2129, -1909), (-2789, -2129), (-3400, -2789)):
        ids = z900(x0, x1, "Small Road")
        for k in range(0, len(ids), 4):  # small batches: big replacements once crashed the game
            step(f"upgrade z=900 {x0}..{x1} #{k // 4}", upgrade("Medium Road", ids[k:k + 4]))
    ids = z900(-2129, -1249, "Medium Road")
    step("high density along z=900", zone("NA Residential High", mode="road", edges=ids, depth=4))

    # office park on the south-west tile
    step("buy tile -2493,0", post("/buytile", {"x": -2493, "z": 0}))
    XO = [-2129, -2349, -2569, -2789]
    for z in (-100, 60, 200):
        step(f"office z={z}", road("Small Road", [[x, z] for x in XO]))
    for x in XO[1:]:
        step(f"office x={x}", road("Small Road", [[x, -100], [x, 60], [x, 200], [x, 320]]))
    step("office z=320 west", road("Small Road", [[-2129, 320], [-2349, 320], [-2569, 320], [-2789, 320]]))
    step("office park", zone("Office Low", mode="rect", points=[[-2795, -150], [-2135, 370]], overwrite=False))

    # services and power in the south
    place("cemetery large", "Cemetery01", -1950, -700)
    for x in (-1300, -1500, -1700, -1900):
        place(f"turbine z=-700 x={x}", "WindTurbine01", x, -700, ts=(0.3, 0.7, 0.15, 0.85))
    print(get("/status"))
