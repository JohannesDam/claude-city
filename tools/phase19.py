"""Phase 19: industry downwind. The prevailing wind carries pollution to the north-east, so new
industry goes NE of the city, east of the highway (z 2820-3260), with a medium road crossing the
highway at z=3040 that links the north district, the estate and the highway."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import get, paused, post, road, short, zone  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")


def step(label, res):
    print(f"{label:<30} {short(res) if isinstance(res, dict) else res}")
    return res


def lane_x(z, which):
    """x of a highway lane at height z (interpolated from its segments)."""
    segs = get("/nets", x=-660, z=z, radius=200, filter="Highway")["segments"]
    best = None
    for s in segs:
        (x0, z0), (x1, z1) = s["start"], s["end"]
        if min(z0, z1) <= z <= max(z0, z1) and z1 != z0:
            x = x0 + (x1 - x0) * (z - z0) / (z1 - z0)
            best = x if best is None else (min(best, x) if which == "west" else max(best, x))
    return best


with paused():
    for x, z in ((-623, 3117), (0, 3117)):
        step(f"buy tile {x},{z}", post("/buytile", {"x": x, "z": z}))
    w, e = lane_x(3040, "west"), lane_x(3040, "east")
    print("highway lanes at z=3040:", w, e)
    step("crossing z=3040 (medium)", road("Medium Road", [[-1249, 3040], [-1000, 3040], [round(w, 1), 3040], [round(e, 1), 3040], [-500, 3040], [-300, 3040], [-80, 3040], [140, 3040]]))
    for z in (2820, 2930, 3150):
        step(f"ind z={z}", road("Small Road", [[-500, z], [-300, z], [-80, z], [140, z]]))
    step("ind z=3260", road("Small Road", [[-400, 3260], [-300, 3260], [-80, 3260], [140, 3260]]))
    for x in (-300, -80, 140):
        step(f"ind x={x}", road("Small Road", [[x, 2820], [x, 2930], [x, 3040], [x, 3150], [x, 3260]]))
    step("NE link x=-470", road("Small Road", [[-470, 2710], [-470, 2820], [-500, 2820]]))
    step("industry NE", zone("Industrial Manufacturing", mode="rect", points=[[-560, 2790], [200, 3310]], overwrite=False))
print(get("/status"))
