"""builder-east / b2-economy-econ-eastgate-logistics: East Gate Works bus (workers from East Bank).

Loop: East Bank bus station (transit square) -> Southern Parkway -> Airport Gate -> Works Way south -> Foundry Loop
(clockwise: up the east side, back west along the north side) -> Works Cross -> Airport bus station (transfer to
the Airport Express) -> parkway -> East Bank. Stops ~7 m right of the centre line in the travel direction.
Usage: python -I builders/builder-east_works-bus.py dry|build
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused  # noqa: E402

WHO = "builder-east"


def right(a, b, t=0.5, off=7.0):
    """Point at fraction t along a->b, `off` m to the right of the travel direction."""
    dx, dz = b[0] - a[0], b[1] - a[1]
    n = math.hypot(dx, dz)
    rx, rz = dz / n, -dx / n
    return {"x": round(a[0] + t * dx + off * rx, 1), "z": round(a[1] + t * dz + off * rz, 1)}


STOPS = [
    "283568:33",                                                   # East Bank bus station
    right((3905, -1500), (3960, -1680), 0.5, 9),                  # Works Way south (Large Road)
    right((4098.1, -1856.2), (4260, -1960), 0.5, 9),              # Works Way south-east
    right((4586.5, -1963.1), (4644.4, -1786.5), 0.88),            # Foundry Loop east side, northbound (near the node: the edge bulges east)
    right((4390, -1480), (4270.7, -1461.7)),                      # Foundry Loop north side, westbound
    "2129105:49",                                                  # airport bus station (Airport Express)
]
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
with paused(WHO):
    r = post("/busline", {"stops": STOPS, "color": "#8D6E63", "stopPrefab": "NA_BusStop01", "dryRun": MODE != "build"},
             timeout=300)
print(json.dumps({k: r.get(k) for k in ("lineId", "error", "warning")}))
pc = r.get("pathCheck") or {}
print("pathCheck ok", pc.get("ok"), [(s.get("from"), s.get("to"), s.get("length"), s.get("status")) for s in pc.get("segments", [])])
print([(s.get("index"), s.get("stopId"), s.get("new"), s.get("pos"), s.get("side")) for s in r.get("stops", [])])
