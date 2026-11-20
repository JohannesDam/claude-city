"""builder-central: T2 West Gate link (b2-central-t2-west-gate-link).

Double Tram Track on the z=900 West Arterial from the T2 circle corner (-2129,900) west to x=-2569, where
builder-west's West Gate Circus tram loop starts (track on the arterial x -2569..-2789, links x=-2684 / x=-2789,
the ring's south arc). T2 1357113:51 gets a branch: after its z=900 westbound stop 396599:169 it runs west to the
loop and back before turning north at (-2129,900); new stops at x=-2290 and x=-2510 (both directions) and one on
the x=-2789 link (southbound side).

usage: python -I builders/builder-central_t2-west-gate.py track|line [dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused  # noqa: E402

WHO = "builder-central"
T2 = "1357113:51"
STAGE = sys.argv[1] if len(sys.argv) > 1 else "line"
DRY = "dry" in sys.argv[2:]
ANCHOR = "543792:31"  # T2 stop (-2125.2,1037), first northbound stop on x=-2129
NEW_STOPS = [{"x": -2290, "z": 906.8}, {"x": -2510, "z": 906.8}, {"x": -2795.8, "z": 930},
             {"x": -2510, "z": 893.2}, {"x": -2290, "z": 893.2}]

if STAGE == "track":
    with paused(WHO):
        r = post("/roadupgrade", {"polyline": [[-2129, 900], [-2569, 900]], "tolerance": 6, "prefabFilter": "Medium Road",
                                  "upgrade": "Double Tram Track", "side": "both", "dryRun": DRY})
        print(r.get("applied"), r.get("cost"), r.get("errors"), r.get("edgeIdsAfter"), r.get("error"))
elif STAGE == "line":
    body = {"line": T2, "dryRun": DRY, "add": [{"stop": s, "before": ANCHOR} for s in NEW_STOPS]}
    with paused(WHO):
        r = post("/line/edit", body, timeout=300)
    print("applied", r.get("applied"), r.get("error"), r.get("blocked"))
    print(json.dumps(r.get("pathCheck"))[:2500])
    print(json.dumps(r.get("newStops"))[:1500])
