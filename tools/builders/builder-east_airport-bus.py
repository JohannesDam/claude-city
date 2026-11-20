"""builder-east / b2-growth-eg-international-airport-parkway: Airport Express bus.

Closed loop from the airport bus station (BusStation02 2128960:49 on Terminal Road) along the Southern Parkway and
the East Bank spine to the East Bank bus station (transfer), over the Lake Bridge to the x=-1140 street, to the T1 /
Office Park interchange on the x=-1909 avenue and into Downtown (z=500), and back. Uses existing stops only.
Usage: python -I builders/builder-east_airport-bus.py dry|build
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused  # noqa: E402

WHO = "builder-east"
STOPS = [
    "2128960:49",   # airport bus station (BusStation02)
    "283568:33",    # East Bank bus station (transit square, (376,-845))
    "268949:121",   # x=-1140 northbound (-1133.7,-300)
    "429941:285",   # x=-1909 avenue northbound (-1899.7,-200), T1 / Office Park
    "289857:133",   # Downtown northbound (-1899.7,500)
    "198106:67",    # Downtown southbound (-1918.3,500)
    "799390:73",    # x=-1909 southbound (-1918.3,-200), T1
    "162415:29",    # x=-1140 southbound (-1146.3,-300)
    "283568:33",    # East Bank bus station again (return)
]
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
body = {"stops": STOPS, "color": "#00897B", "stopPrefab": "NA_BusStop01"}
with paused(WHO):
    r = post("/busline", {**body, "dryRun": MODE != "build"}, timeout=300)
print(json.dumps({k: r.get(k) for k in ("lineId", "error", "warning", "pathCheck")})[:2500])
print([(s.get("index"), s.get("stopId"), s.get("pos")) for s in r.get("stops", [])])
