"""builder-northeast - Campus Village bus: one-way loop from the campus core (z=4250 road, tram stops of the
Knowledge Line, University01, City Hall) along Lakeside Drive to the Campus Village and back.
Right-hand traffic: every stop sits ~7 m right of the travel direction."""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
STOPS = [
    (-1800, 4243),   # z=4250 eastbound, by the Knowledge Line stop (-1700,4243)
    (-1483, 4120),   # x=-1476 southbound, Police HQ / tram stop (-1465,4030)
    (-1750, 4001),   # z~3995 westbound, office quarter
    (-1902, 4060),   # x=-1909 northbound, tram stop (-1905,4130)
    (-2060, 4257),   # z=4250 westbound, Technical University south gate
    (-2215, 4420),   # Lakeside Drive northbound
    (-1950, 4613),   # College Row eastbound
    (-1724, 4760),   # Fellows Way northbound
    (-1583, 5030),   # Campus Loop east, northbound
    (-1760, 5257),   # Campus Loop north, westbound
    (-1946, 5100),   # Campus Loop west, southbound (lakeside parks)
    (-2229, 4420),   # Lakeside Drive southbound
    (-2060, 4243),   # z=4250 eastbound
]
body = {"stops": [{"x": x, "z": z} for x, z in STOPS], "color": "#7E57C2", "stopPrefab": "NA_BusStop01"}
if len(sys.argv) > 1 and sys.argv[1] == "dry":
    r = post("/busline", {**body, "dryRun": True}, timeout=300)
else:
    with paused("builder-northeast"):
        r = post("/busline", body, timeout=300)
print(json.dumps({k: r.get(k) for k in ("lineId", "applied", "error", "warnings", "pathCheck")})[:1500])
print([(s.get("index"), s.get("side"), s.get("edge"), s.get("error")) for s in r.get("stops") or []])
