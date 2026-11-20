"""builder-central: Lake Avenue gate (b2-mobility-lake-avenue-gate).

Survey 2032-01 (game): the bottleneck icons named in the item are gone. The live queue is on the INBOUND highway
at z 1548..1767, upstream of the at-grade crossing at z=1500, where the z=1500 Medium Road crosses both highway
carriageways on traffic lights (nodes 395765:1, 395766:1). Widening x=-1140 to Large Road is not viable: every
batch removes 6-12 level 3-5 shops/offices and overlaps a wind turbine, ElementarySchool02 and Cemetery02.

Fix: grade separation.
  1. z=1610 overpass: Medium Road from the x=-1249 street node (-1249,1610) over the highway (deck 12 m, ramps
     ~9%) to the x=-900 street node (-900,1610). Removes 4 low homes at the ramp feet.
  2. Remove the level crossing: bulldoze the z=1500 Medium Road (827045:81, 303166:1, 831041:25) and rebuild both
     halves as dead-end stubs that keep the groundwater pump, the crematorium and the homes on z=1500 served:
     west (-1249,1500)->(-1150,1500), east (-900,1500)->(-1062,1500). Trees + Grass on all new edges.

usage: python -I builders/builder-central_lake-avenue-gate.py dry|build|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

WHO = "builder-central"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
OVERPASS = [[-1249, 1610, 0], [-1125, 1610, 12], [-1060, 1610, 12], [-930, 1610, 0], [-900, 1610, 0]]
CROSSING = ["827045:81", "303166:1", "831041:25"]
STUBS = {"west": [[-1249, 1500], [-1150, 1500]], "east": [[-900, 1500], [-1062, 1500]]}
MAX_HOMES = 4


def build_overpass(dry):
    r = post("/road", {"prefab": "Medium Road", "points": OVERPASS, "dryRun": True, "allowDemolish": True})
    wd = r.get("wouldDemolish") or []
    print("overpass dry", json.dumps(short(r, keys=("applied", "cost", "errors", "problems", "error"))),
          [(w["prefab"], w["center"]) for w in wd])
    bad = [w for w in wd if "Residential" not in w["prefab"]]
    if r.get("errors") or r.get("error") or bad or len(wd) > MAX_HOMES:
        return None
    if dry:
        return r
    r = post("/road", {"prefab": "Medium Road", "points": OVERPASS, "allowDemolish": True})
    print("overpass", json.dumps(short(r, keys=("applied", "cost", "errors", "newEdgeIds", "builtEdges")))[:600])
    return r if r.get("applied") else None


def remove_crossing_and_stubs():
    r = post("/bulldoze", {"edges": CROSSING, "dryRun": True})
    print("crossing dry", json.dumps(r)[:500])
    r = post("/bulldoze", {"edges": CROSSING})
    print("crossing removed", json.dumps(short(r, keys=("applied", "cost", "errors", "error", "removed")))[:400])
    for name, pts in STUBS.items():
        d = post("/road", {"prefab": "Medium Road", "points": pts, "dryRun": True})
        print(name, "stub dry", json.dumps(short(d))[:400])
        if d.get("errors") or d.get("wouldDemolish") or d.get("error"):
            continue
        b = post("/road", {"prefab": "Medium Road", "points": pts})
        print(name, "stub", json.dumps(short(b, keys=("applied", "cost", "errors", "newEdgeIds")))[:300])


def greenery():
    for pts in ([[-1249, 1610], [-900, 1610]], STUBS["west"], STUBS["east"]):
        for up in ("Trees", "Grass"):
            u = post("/roadupgrade", {"polyline": [p[:2] for p in pts], "tolerance": 6, "prefabFilter": "Medium Road",
                                      "upgrade": up, "side": "both"})
            print(up, pts[0], u.get("applied"), u.get("cost"), u.get("errors"), u.get("error"))


if MODE == "dry":
    build_overpass(True)
    print(json.dumps(post("/bulldoze", {"edges": CROSSING, "dryRun": True}))[:500])
elif MODE == "build":
    with paused(WHO):
        if not build_overpass(False):
            print("overpass not built - crossing kept")
            sys.exit(1)
        remove_crossing_and_stubs()
        greenery()
elif MODE == "check":
    for pos in ((-1115.8, 1500.1), (-1095.7, 1500.1), (-1249, 1610), (-900, 1610), (-1249, 1500), (-900, 1500)):
        n = get("/nodes", x=pos[0], z=pos[1], radius=8)
        print(pos, [(x["id"], x["degree"], x.get("trafficLights"), [e["prefab"] for e in x["edges"]]) for x in n["nodes"]])
    L = get("/lines")
    print("broken lines", [(l["id"], l["type"], l["number"]) for l in L["lines"] if l.get("broken")])
    p = get("/problems", type="Traffic Bottleneck Notification", limit=100)
    near = [e["pos"] for x in p["problems"] for e in x.get("examples", [])
            if -1300 < e["pos"][0] < -900 and 900 < e["pos"][1] < 1900]
    print("bottlenecks near the gate", near)
    for t in ("No Car Access", "No Pedestrian Access"):
        print(t, json.dumps(get("/problems", type=t))[:400])
