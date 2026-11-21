"""builder-south: East Bank phase 2 'Lakeside Terraces' (worklist b3-housing-east-bank-p2-lakeside-terraces).

Free land south of the WWTP (849,-1062): x 650..925, z -1180..-1440. Ground pollution from the plant reaches
z ~-1160, so the first 200 m south of Works Lane stay unzoned (tree buffer).
  - Works Lane entrance (925,-877)->(922,-950) becomes Medium Road (Trees+Grass).
  - 'Terrace Crescent' (Medium, Trees+Grass): (922,-950) south along x~905, then curving west around the green
    to (655,-1355).
  - 'Terrace Walk' (Small, Trees): the return leg (655,-1355) -> north-east -> back onto the crescent near (903,-1205).
  - CityPark03 green and EU_ResidentialMediumSignature01 inside the loop, then zoning: EU High/Mixed on the
    crescent's outer side, EU Medium Row inside, EU Medium on the walk, ~10% Residential LowRent.
No demolition anywhere. Roads carry water/sewage/power into the existing East Bank network (no pipe nets here).

usage: python -I builders/builder-south_lakeside-terraces.py dry|roads|walk|green|zone|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short, access_check  # noqa: E402

WHO = "builder-south"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"

ENTRANCE = [[925, -877], [922, -950]]
CRESCENT = [[922, -950], [912, -1080], [900, -1200], [870, -1310], [805, -1380], [715, -1392], [655, -1355]]
WALK_START = [655, -1355]
WALK_MID = [[668, -1282], [722, -1232], [800, -1212]]
CENTRE = (790, -1300)          # inside the loop
BBOX = [640, -1445, 932, -940]  # our part of East Bank for this phase


def edges_in(bbox, prefab):
    x0, z0, x1, z1 = bbox
    r = get("/nets", x=(x0 + x1) / 2, z=(z0 + z1) / 2, radius=max(x1 - x0, z1 - z0) * 0.75, limit=500)
    out = []
    for e in r.get("segments", []):
        if e["prefab"] != prefab:
            continue
        mx, mz = (e["start"][0] + e["end"][0]) / 2, (e["start"][1] + e["end"][1]) / 2
        if x0 <= mx <= x1 and z0 <= mz <= z1:
            out.append(e)
    return out


def inner_side(e):
    s, t = e["start"], e["end"]
    dx, dz = t[0] - s[0], t[1] - s[1]
    cx, cz = CENTRE[0] - s[0], CENTRE[1] - s[1]
    cross = dx * cz - dz * cx
    return "left" if cross > 0 else "right"


def outer_side(e):
    return "right" if inner_side(e) == "left" else "left"


def stage_roads(dry):
    r = post("/upgrade", {"prefab": "Medium Road", "polyline": ENTRANCE, "tolerance": 8, "prefabFilter": "Small Road",
                          "dryRun": dry})
    print("entrance -> Medium", json.dumps(short(r, keys=("applied", "cost", "errors", "error", "wouldDemolish",
                                                            "edgeIdsAfter"))), flush=True)
    r = post("/road", {"prefab": "Medium Road", "points": CRESCENT, "curve": "smooth", "dryRun": dry})
    print("crescent", json.dumps(short(r, keys=("applied", "cost", "errors", "problems", "error", "wouldDemolish",
                                                 "conflicts", "newEdgeIds"))), json.dumps(r.get("geometry"))[:400],
          flush=True)
    return r


def stage_walk(dry):
    # end on the crescent near (900,-1205): roadfit slides the junction along the edge if needed
    body = {"prefab": "Small Road", "points": [WALK_START] + WALK_MID + [[900, -1205]], "curve": "smooth",
            "fit": {"tolerance": 30, "minRadius": 40}, "dryRun": dry}
    r = post("/roadfit", body)
    print("walk", json.dumps(short(r, keys=("applied", "cost", "errors", "problems", "error", "wouldDemolish",
                                             "conflicts", "newEdgeIds"))), json.dumps(r.get("geometry"))[:400],
          flush=True)
    if r.get("error"):
        for c in r.get("candidates", [])[:5]:
            print("  cand", json.dumps(c)[:300])
    return r


def stage_trees(dry):
    for pf, ups in (("Medium Road", ("Trees", "Grass")), ("Small Road", ("Trees",))):
        ids = [e["id"] for e in edges_in(BBOX, pf) if (e["start"][1] < -940 and e["end"][1] < -940)]
        for up in ups:
            for k in range(0, len(ids), 4):
                r = post("/roadupgrade", {"edges": ids[k:k + 4], "upgrade": up, "side": "both", "dryRun": dry})
                print(up, pf, ids[k:k + 4], r.get("applied"), r.get("cost"), r.get("errors"), r.get("error"))


def stage_green(dry):
    within = [700, -1360, 880, -1230]
    for prefab, near in (("CityPark03", [790, -1300]), ("EU_ResidentialMediumSignature01", [760, -1250])):
        body = {"prefab": prefab, "near": near, "within": within, "radius": 120, "maxHouseholds": 0,
                "maxCompanies": 0, "dryRun": dry}
        r = post("/place", body)
        c = r.get("chosen") or {}
        print(prefab, "chosen", c.get("x"), c.get("z"), c.get("road"), c.get("edge"), "placed", r.get("placedId"),
              r.get("error"), flush=True)


def zone_edges(name, edges, side, depth, dry):
    if not edges:
        return
    r = post("/zone", {"zone": name, "mode": "road", "edges": edges, "side": side, "depth": depth, "dryRun": dry})
    print(f"zone {name:<26} {side:<5} d{depth} n={len(edges)}",
          json.dumps(short(r, keys=("applied", "cost", "cells", "zonedCells", "occupiedCellsRezoned", "errors",
                                    "error"))), flush=True)


def stage_zone(dry):
    med = [e for e in edges_in(BBOX, "Medium Road") if min(e["start"][1], e["end"][1]) < -1170]
    small = [e for e in edges_in(BBOX, "Small Road") if min(e["start"][1], e["end"][1]) < -1170]
    for e in med + small:
        print(e["id"], e["prefab"], [round(v) for v in e["start"]], [round(v) for v in e["end"]], "inner",
              inner_side(e))
    # crescent outer side: east leg (x > 880) shallow (district edge x 934), south arc deeper; west end LowRent
    for e in med:
        mx = (e["start"][0] + e["end"][0]) / 2
        mz = (e["start"][1] + e["end"][1]) / 2
        out, inn = outer_side(e), inner_side(e)
        if mx > 860:
            zone_edges("EU Residential Mixed", [e["id"]], out, 3, dry)
        elif mx < 720:
            zone_edges("Residential LowRent", [e["id"]], out, 4, dry)
        else:
            zone_edges("EU Residential High", [e["id"]], out, 4 if mz < -1370 else 5, dry)
        zone_edges("EU Residential Medium Row", [e["id"]], inn, 3, dry)
    for e in small:
        zone_edges("EU Residential Medium", [e["id"]], inner_side(e), 3, dry)
        mz = (e["start"][1] + e["end"][1]) / 2
        if mz < -1225:  # outer (north/west) side only away from the plant
            zone_edges("EU Residential Medium", [e["id"]], outer_side(e), 3, dry)


if MODE == "dry":
    stage_roads(True)
elif MODE == "roads":
    with paused(WHO):
        stage_roads(False)
elif MODE == "walk-dry":
    stage_walk(True)
elif MODE == "walk":
    with paused(WHO):
        stage_walk(False)
elif MODE == "trees":
    with paused(WHO):
        stage_trees(False)
elif MODE == "green-dry":
    stage_green(True)
elif MODE == "green":
    with paused(WHO):
        stage_green(False)
elif MODE == "zone-dry":
    stage_zone(True)
elif MODE == "zone":
    with paused(WHO):
        stage_zone(False)
elif MODE == "check":
    for t in ("Water Notification", "Sewage Notification", "No Car Access", "No Pedestrian Access"):
        p = get("/problems", type=t)
        print(t, json.dumps(p)[:300])
    d = get("/districts")
    for x in d.get("districts", []):
        if x.get("name") == "East Bank":
            print(json.dumps({k: x.get(k) for k in ("residents", "households", "happiness", "workplaces")}))
