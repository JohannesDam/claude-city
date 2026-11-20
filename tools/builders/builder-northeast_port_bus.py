"""builder-northeast - Port Line bus: Eastern Works (transfer to line 5 at x=-100) along the z=3040 spine,
north on x=1530 and the Port Link, over the highway to Harbour Road and the Port Claude ferry terminal.
Closed loop, right-hand traffic: outbound stops on the right, return stops on the other side (~7 m)."""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
OUT = [(-100, 3033), (450, 3033), (1090, 3033), (1537, 3250), (1644.7, 3545.3), (2150, 3986), (2470.5, 4300)]
BACK = [(2456.5, 4330), (2150, 4000), (1635.3, 3555.7), (1523, 3250), (1090, 3047), (450, 3047), (-100, 3047)]
body = {"stops": [{"x": x, "z": z} for x, z in OUT + BACK], "color": "#00897B", "stopPrefab": "NA_BusStop01"}
dry = len(sys.argv) > 1 and sys.argv[1] == "dry"
if dry:
    r = post("/busline", {**body, "dryRun": True}, timeout=300)
else:
    with paused("builder-northeast"):
        r = post("/busline", body, timeout=300)
print(json.dumps({k: r.get(k) for k in ("applied", "lineId", "id", "complete", "errors", "error", "cost", "stops", "warnings")})[:1500])
