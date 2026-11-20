"""builder-northeast - Pearl Bay Line bus: Lakeshore z=2270 street (near bus 7 on x=-680) along Bay Boulevard
to the east headland and back. Right-hand traffic: outbound stops on the right, return stops opposite."""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
OUT = [(-500, 2263), (-44, 2194), (327, 1904), (639, 1813), (1000.6, 1783)]
BACK = [(1000, 1797), (641, 1827), (333, 1916), (-36, 2206), (-500, 2277)]
body = {"stops": [{"x": x, "z": z} for x, z in OUT + BACK], "color": "#26C6DA", "stopPrefab": "NA_BusStop01"}
if len(sys.argv) > 1 and sys.argv[1] == "dry":
    r = post("/busline", {**body, "dryRun": True}, timeout=300)
else:
    with paused("builder-northeast"):
        r = post("/busline", body, timeout=300)
print(json.dumps({k: r.get(k) for k in ("lineId", "error", "warnings")}), [(s.get("index"), s.get("side"), s.get("edge")) for s in r.get("stops") or []])
