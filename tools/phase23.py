"""Phase 23 (65.6k people, happiness 74): sewage at 82%, unemployment/industrial demand, traffic and
garbage are the remaining penalties. Third sewage outlet, NE industry extension (downwind), x=-1909
upgraded to a medium-road arterial in small batches, two more downwind landfills."""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import building, get, paused, place_in_area, post, road, short, upgrade, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")


def step(label, res):
    print(f"{label:<30} {short(res) if isinstance(res, dict) else res}", flush=True)
    return res


with paused():
    # 1. sewage outlet 3
    step("road to shore z=120", road("Small Road", [[-1140, 120], [-900, 120], [-610, 120]]))
    for x in (-594, -596, -598, -592):
        r = building("SewageOutlet01", x=x, z=120, angle=-90, dryRun=True)
        if r.get("errors") == 0:
            step("sewage outlet 3", building("SewageOutlet01", x=x, z=120, angle=-90))
            break
    else:
        step("sewage outlet 3 FAILED", r)

    # 2. NE industry extension
    step("buy tile 623,3117", post("/buytile", {"x": 623, "z": 3117}))
    for z in (2820, 2930, 3040, 3150, 3260):
        step(f"ind east z={z}", road("Small Road", [[140, z], [360, z], [580, z], [800, z]]))
    for x in (360, 580, 800):
        step(f"ind east x={x}", road("Small Road", [[x, z] for z in (2820, 2930, 3040, 3150, 3260)]))
    step("industry NE east", zone("Industrial Manufacturing", mode="rect", points=[[150, 2790], [850, 3310]], overwrite=False))
    for i, (x, z) in enumerate(((470, 3380), (690, 3380))):
        r, info = place_in_area("Landfill01", x, z, radius=200, max_tries=20)
        step(f"landfill NE {i}", r if r else ("no spot", short(info or {})))

    # 3. x=-1909 arterial
    segs = []
    for zc in range(-50, 3500, 400):
        for s in get("/nets", x=-1909, z=zc, radius=220, filter="Small Road")["segments"]:
            if abs(s["start"][0] + 1909) < 2 and abs(s["end"][0] + 1909) < 2 and s["id"] not in segs:
                segs.append(s["id"])
    print("x=-1909 small segments:", len(segs))
    for k in range(0, len(segs), 4):
        step(f"upgrade x=-1909 #{k // 4}", upgrade("Medium Road", segs[k:k + 4]))
        time.sleep(0.5)
print(get("/status"))
