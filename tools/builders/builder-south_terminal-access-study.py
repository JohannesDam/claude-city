"""builder-south: DRY RUNS ONLY - second truck access for CargoTrainTerminal01 610973:49.

The terminal fronts one Medium approach road that runs SW from node (-2040,-1455) past the terminal, RailYard02
895488:65 and TrainStation03 541246:307 to a dead end at (-2547,-1940). All trucks enter at (-2040,-1455).
usage: python -I builders/builder-south_terminal-access-study.py
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post  # noqa: E402

OPTIONS = [
    ("O1 north link: approach (-2237,-1633) -> z=-1330 street (-2290,-1330)", "/road",
     {"prefab": "Medium Road", "points": [[-2237, -1633], [-2262, -1520], [-2290, -1330]], "curve": "smooth"}),
    ("O1b north link, roadfit from the approach to the z=-1330 street", "/roadfit",
     {"prefab": "Medium Road", "start": [-2200, -1597], "end": [-2290, -1330], "fit": {"tolerance": 40, "minRadius": 60}}),
    ("O2 west loop: dead end (-2547,-1940) north to z=-1330 street (-2625,-1330)", "/road",
     {"prefab": "Medium Road", "points": [[-2547, -1940], [-2600, -1880], [-2625, -1700], [-2630, -1500], [-2625, -1330]],
      "curve": "smooth"}),
    ("O3 east link: Gateway Medium road (-1760,-1720) west to approach (-2343,-1730)", "/road",
     {"prefab": "Medium Road", "points": [[-1760, -1720], [-1950, -1790], [-2150, -1800], [-2280, -1780], [-2343, -1730]],
      "curve": "smooth"}),
    ("O3b east link south of the stone lot, z -1860", "/road",
     {"prefab": "Medium Road", "points": [[-1745, -1830], [-1900, -1860], [-2150, -1850], [-2300, -1800], [-2343, -1730]],
      "curve": "smooth"}),
]

for label, path, body in OPTIONS:
    r = post(path, {**body, "dryRun": True}, timeout=300)
    keep = {k: r.get(k) for k in ("cost", "errors", "problems", "error")}
    wd = r.get("wouldDemolish") or []
    conf = [(c.get("error"), c.get("prefab"), c.get("id"), c.get("at")) for c in (r.get("conflicts") or [])][:6]
    print(label)
    print("   ", json.dumps(keep), "wouldDemolish", len(wd), [(w.get("prefab"), w.get("renters")) for w in wd][:8])
    print("    conflicts", conf)
    if r.get("error") and r.get("candidates"):
        for c in r["candidates"][:3]:
            print("    cand", c.get("variant"), c.get("reason"))
    print("    geometry", json.dumps(r.get("geometry"))[:200], flush=True)

r = post("/upgrade", {"prefab": "Large Road", "polyline": [[-2040, -1455], [-2156, -1560], [-2237, -1633]], "tolerance": 10,
                      "prefabFilter": "Medium Road", "dryRun": True})
print("O4 approach -> Large Road", json.dumps({k: r.get(k) for k in ("cost", "errors", "error", "wouldDemolish")})[:500])
r = post("/roundabout", {"at": [-2040, -1455], "size": "medium", "dryRun": True})
print("O4 roundabout at (-2040,-1455)", json.dumps({k: r.get(k) for k in ("cost", "errors", "error", "wouldDemolish", "problems")})[:500])
