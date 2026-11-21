"""Sample trucks around a point: how many are stopped (speed < 3) and on which edges, and what they target.
Used to measure the Old Southern Works / CargoTrainTerminal01 truck queue before and after junction changes.
Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_truckqueue.py [x z radius]
"""
import sys
from collections import Counter

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get  # noqa: E402

x, z, rad = (float(v) for v in sys.argv[1:4]) if len(sys.argv) >= 4 else (-1800, -1550, 450)
r = get("/vehicles", kind="truck", x=x, z=z, radius=rad, limit=500)
vs = r.get("vehicles", [])
stopped = [v for v in vs if v.get("speed", 0) < 3 and "Invisible" not in ((v.get("on") or {}).get("prefab") or "")]
edges = Counter(((v.get("on") or {}).get("id"), (v.get("on") or {}).get("prefab")) for v in stopped)
tgt = Counter((v.get("target") or {}).get("buildingPrefab") for v in vs)
print(get("/city")["date"], "trucks", len(vs), "stopped on roads", len(stopped), "edges", edges.most_common(8),
      "to terminal", tgt.get("CargoTrainTerminal01", 0))
