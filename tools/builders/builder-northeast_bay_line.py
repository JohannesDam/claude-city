"""builder-northeast - b3-mobility-pearl-bay-bay-junction-and-bay-bus: Bay Line bus.

Junction survey (2033): (1067,1797) is a degree-2 bend of Bay Boulevard, not a junction (no node of
degree >= 3 within 120 m) and no accident icon there now, so no roundabout/lights are needed.
Bay Line: M1 Eastern Works station (-253,2800) -> z=2820 street east -> diagonal Medium -> x=850 street
south -> a clockwise loop round east Pearl Bay (Bay Boulevard east, Small Road ring past the new police
station, back to (862,1950)) -> north again. Shares bus 13's stop at (1000.8,1780.7).

Usage: python -I builders/builder-northeast_bay_line.py dry|build|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_bay_line.json"
OUT = [(-240, 2814), (300, 2814), (844, 2440), (848, 2142)]
RING = [(849, 1870), (1000.8, 1780.7), (1262.5, 1826.2), (1228.5, 1973.8), (1014, 2025.5)]
BACK = [(862, 2143), (856, 2440), (300, 2826), (-240, 2826)]
BODY = {"stops": [{"x": x, "z": z} for x, z in OUT + RING + BACK], "color": "#00897B", "stopPrefab": "NA_BusStop01"}


def show(r):
    out = {k: r.get(k) for k in ("lineId", "applied", "error", "warnings", "cost")}
    out["pathCheck"] = json.dumps(r.get("pathCheck"))[:500]
    out["stops"] = [(s.get("index"), s.get("side"), s.get("edge"), s.get("reused") or s.get("existing")) for s in r.get("stops") or []]
    return out


if MODE == "dry":
    print(json.dumps(show(post("/busline", {**BODY, "dryRun": True}, timeout=300)), indent=1))
elif MODE == "build":
    with paused(WHO):
        r = post("/busline", BODY, timeout=300)
    s = show(r)
    print(json.dumps(s, indent=1))
    json.dump(s, open(LOG, "w"), indent=1)
elif MODE == "check":
    lid = json.load(open(LOG)).get("lineId")
    for l in get("/lines")["lines"]:
        if l["id"] == lid:
            print(json.dumps({k: l.get(k) for k in ("id", "number", "complete", "broken", "vehicles", "vehicleInterval", "failedSegments")}))
            for st in l["stops"]:
                print("  ", st["pos"], st.get("waiting"), st.get("avgWait"))
