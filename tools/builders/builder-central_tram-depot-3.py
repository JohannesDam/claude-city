"""builder-central: third tram depot near the T1 south end (approved by the conductor).

All 30 tram slots (two TramDepot01 + garages) are in use; T1, T2 and Tram Line 3 lack trams.
Site: free land south of the Depot Road (z=-420) between the wind turbines at x=-1800 and x=-1600,
lot x -1768..-1616, z -565..-428 (the first depot 1421663:33 sits on the same road at x=-2068).
  - TramDepot01 facing the Depot Road (no demolition), + Garage Extension (15 trams in all)
  - Double Tram Track + Trees on the Depot Road from the T1 node (-1900,-420) east to x=-1580

usage: python -I builders/builder-central_tram-depot-3.py dry|build|garage|check
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

WHO = "builder-central"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
DEPOT_ROAD_EAST = [[-1900, -420], [-1580, -420]]
SPOT = {"prefab": "TramDepot01", "edge": "302889:1", "t": 0.93, "side": "left"}


def find_depot():
    r = get("/buildings", kind="service", x=-1692, z=-497, radius=60)
    for b in r.get("buildings", []):
        if b["prefab"] == "TramDepot01":
            return b["id"]
    return None


def tracks(dry):
    for pf in ("Medium Road", "Small Road"):
        for up in ("Double Tram Track", "Trees"):
            r = post("/roadupgrade", {"polyline": DEPOT_ROAD_EAST, "tolerance": 6, "prefabFilter": pf,
                                      "upgrade": up, "side": "both", "dryRun": dry})
            edges = [e["id"] for e in r.get("edges", [])]
            print(f"{up:<18} {pf:<12} applied={r.get('applied')} cost={r.get('cost')} err={r.get('errors')} "
                  f"demolish={r.get('wouldDemolish')} edges={edges} {r.get('error') or ''}", flush=True)


if MODE == "dry":
    r = post("/building", {**SPOT, "dryRun": True})
    print("depot", json.dumps(short(r)), r.get("corners"), r.get("roadConnected"), r.get("seed"))
    tracks(True)
elif MODE == "build":
    with paused(WHO):
        d = post("/building", {**SPOT, "dryRun": True})
        if d.get("errors") or d.get("wouldDemolish") or d.get("error"):
            print("dry run not clean", json.dumps(short(d))[:800])
            sys.exit(1)
        r = post("/building", {**SPOT, "seed": d.get("seed")})
        print("TramDepot01", json.dumps(short(r)))
    time.sleep(1)
    with paused(WHO):
        tracks(False)
elif MODE == "garage":
    dep = find_depot()
    print("depot", dep)
    with paused(WHO):
        r = post("/extension", {"owner": dep, "prefab": "TramDepot01 Garage Extension"}, timeout=120)
        print("garage", json.dumps(short(r)))
elif MODE == "check":
    dep = find_depot()
    ext = get("/extensions", owner=dep)
    print("depot", dep, ext.get("combined", {}).get("TransportDepotData"))
    for t in ("Track Not Connected", "Pathfind Failed", "No Car Access"):
        p = get("/problems", type=t)
        print(t, json.dumps(p)[:600])
