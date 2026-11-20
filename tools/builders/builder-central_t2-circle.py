"""builder-central: Tram T2 rebuilt as the Downtown Circle (worklist b2-mobility-t2-downtown-circle).

The old T2 (442850:165, z=900 x -2405..-1225) failed at both ends: trams cannot reverse and the east end ran
into the x=-1138 roundabout. The new T2 is one closed one-way loop through the dense core, so it has no
ends at all:
    z=900 westbound (x -1469 -> -2129) -> x=-2129 northbound (z 900 -> 1610)
    -> z=1610 eastbound (x -2129 -> -1469) -> x=-1469 southbound (z 1610 -> 900)
Clockwise, so every corner is a right turn. x=-2349 (the board's first choice) has no road between
z 1450 and 1610, so the circle uses x=-2129. Transfers to T1 on the avenue at z=900 and z=1610; the loop
passes the school cluster on x=-1469. The old z=900 stubs west of x=-2129 and east of x=-1469 lose their
track.

Run: cd C:/soft/cityskyline/tools && python -I builders/builder-central_t2-circle.py delete|track|stubs|line [--dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short  # noqa: E402

WHO = "builder-central"
DRY = "--dry" in sys.argv
STAGE = next((a for a in sys.argv[1:] if not a.startswith("--")), "delete")
OLD_T2 = "442850:165"

TRACK = [
    ("x=-2129", [[-2129, 900], [-2129, 1610]]),
    ("z=1610", [[-2129, 1610], [-1469, 1610]]),
    ("x=-1469", [[-1469, 1610], [-1469, 900]]),
]
STUBS = [
    ("z=900 west stub", [[-2464, 900], [-2129, 900]], "Medium Road"),
    ("z=900 east stub", [[-1469, 900], [-1193.4, 900]], "Medium Road"),
]
# clockwise loop, stops on the right-hand side (mid-block): {x,z} on the side the tram drives
STOPS = [
    {"x": -1530, "z": 909},    # z=900 westbound (north side)
    {"x": -1966, "z": 909},    # z=900 westbound, T1 transfer + taxi depot
    {"x": -2120, "z": 1037},   # x=-2129 northbound (east side)
    {"x": -2120, "z": 1422},
    {"x": -1946, "z": 1601},   # z=1610 eastbound (south side), T1 transfer
    {"x": -1526, "z": 1601},
    {"x": -1478, "z": 1367},   # x=-1469 southbound (west side), school cluster
    {"x": -1478, "z": 1092},
]


def stage_delete():
    with paused(WHO):
        d = post("/line/delete", {"line": OLD_T2, "dryRun": True})
        print("dry", json.dumps(d)[:400])
        if not DRY and d.get("wouldDelete"):
            print("delete", json.dumps(post("/line/delete", {"line": OLD_T2}))[:400])


def stage_track():
    with paused(WHO):
        for label, line in TRACK:
            for up in ("Double Tram Track", "Trees"):
                r = post("/roadupgrade", {"polyline": line, "tolerance": 6, "prefabFilter": "Small Road", "upgrade": up,
                                          "side": "both", "dryRun": DRY})
                print(f"{label:<9} {up:<17}", short(r, keys=("applied", "cost", "errors", "problems", "wouldDemolish", "error")),
                      len(r.get("edges", [])), "edges", flush=True)


def stage_stubs():
    with paused(WHO):
        for label, line, pf in STUBS:
            r = post("/roadupgrade", {"polyline": line, "tolerance": 6, "prefabFilter": pf, "upgrade": "Double Tram Track",
                                      "side": "both", "remove": True, "dryRun": DRY})
            print(label, short(r, keys=("applied", "cost", "errors", "error")), [e["id"] for e in r.get("edges", [])], flush=True)


def stage_line():
    with paused(WHO):
        r = post("/line", {"type": "tram", "stopPrefab": "NA_TramStop01", "stops": STOPS, "color": "#C2185B", "dryRun": DRY},
                 timeout=300)
        print(json.dumps({k: r.get(k) for k in ("lineId", "error", "pathCheck", "warning", "line")})[:3000])
        print([(s.get("index"), s.get("stopId"), s.get("new"), s.get("pos"), s.get("side")) for s in r.get("stops", [])])


if __name__ == "__main__":
    {"delete": stage_delete, "track": stage_track, "stubs": stage_stubs, "line": stage_line}[STAGE]()
