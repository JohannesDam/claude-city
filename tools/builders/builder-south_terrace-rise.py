"""builder-south: Lakeside Terraces north part 'Terrace Rise' (b3-housing-east-bank-p2-lakeside-terraces).

Free land north-east of the crescent, x 790..934, z -830..-520 (builder-east owns x > 934).
  - Stem (Medium, Trees+Grass) north from the spine node (925,-877) to (912,-680).
  - Loop (Small, Trees) around a small inner green: (912,-680) -> (905,-590) -> (850,-535) -> (800,-575)
    -> (805,-660) -> (860,-700) -> back to (912,-680).
  - Zoning: EU High on the loop's outer side, EU Medium Row inside, EU Mixed on the stem's west side.
usage: python -I builders/builder-south_terrace-rise.py dry|roads|zone-dry|zone
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

WHO = "builder-south"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
STEM = [[925, -877], [920, -760], [912, -680]]
LOOP_A = [[912, -680], [905, -590], [850, -535], [800, -575]]
LOOP_B = [[800, -575], [805, -660], [860, -700], [912, -680]]
CENTRE = (855, -620)
BBOX = [780, -880, 934, -520]


def show(label, r):
    print(label, json.dumps(short(r, keys=("applied", "cost", "errors", "problems", "error", "wouldDemolish",
                                           "conflicts", "newEdgeIds"))), flush=True)


def roads(dry):
    show("stem", post("/road", {"prefab": "Medium Road", "points": STEM, "curve": "smooth", "dryRun": dry}))
    show("loopA", post("/road", {"prefab": "Small Road", "points": LOOP_A, "curve": "smooth", "dryRun": dry}))
    show("loopB", post("/road", {"prefab": "Small Road", "points": LOOP_B, "curve": "smooth", "dryRun": dry}))


def edges(prefab):
    x0, z0, x1, z1 = BBOX
    r = get("/nets", x=(x0 + x1) / 2, z=(z0 + z1) / 2, radius=260, limit=500)
    out = []
    for e in r.get("segments", []):
        if e["prefab"] != prefab:
            continue
        mx, mz = (e["start"][0] + e["end"][0]) / 2, (e["start"][1] + e["end"][1]) / 2
        if x0 <= mx <= x1 and z0 <= mz <= z1 and mz > -870:
            out.append(e)
    return out


def inner(e):
    s, t = e["start"], e["end"]
    cross = (t[0] - s[0]) * (CENTRE[1] - s[1]) - (t[1] - s[1]) * (CENTRE[0] - s[0])
    return "left" if cross > 0 else "right"


def outer(e):
    return "right" if inner(e) == "left" else "left"


def zone_one(name, eid, side, depth, dry):
    body = {"zone": name, "mode": "road", "edges": [eid], "side": side, "depth": depth, "dryRun": dry}
    if dry:
        r = post("/zone", body)
    else:
        with paused(WHO):
            r = post("/zone", body)
    print(f"zone {name:<26} {eid} {side} d{depth}",
          json.dumps(short(r, keys=("applied", "occupiedCellsRezoned", "errors", "error"))), flush=True)


def trees(dry):
    for pf, ups in (("Medium Road", ("Trees", "Grass")), ("Small Road", ("Trees",))):
        for up in ups:
            ids = [e["id"] for e in edges(pf) if e["start"][0] > 790 and e["end"][0] > 790]
            for k in range(0, len(ids), 4):
                r = post("/roadupgrade", {"edges": ids[k:k + 4], "upgrade": up, "side": "both", "dryRun": dry})
                print(up, pf, ids[k:k + 4], r.get("applied"), r.get("cost"), r.get("error"))


def zone(dry):
    for e in edges("Medium Road"):
        if min(e["start"][0], e["end"][0]) < 900:
            continue  # only the new stem (x ~912..925)
        west = outer(e) if CENTRE[0] < e["start"][0] and inner(e) == ("left" if e["end"][1] > e["start"][1] else "right") else None
        # stem runs north (z increasing): west = left of travel when going north
        going_north = e["end"][1] > e["start"][1]
        west_side = "left" if going_north else "right"
        east_side = "right" if going_north else "left"
        zone_one("EU Residential Mixed", e["id"], west_side, 4, dry)
        zone_one("EU Residential Mixed", e["id"], east_side, 1, dry)
    for e in edges("Small Road"):
        if min(e["start"][0], e["end"][0]) < 795:
            continue
        mx = (e["start"][0] + e["end"][0]) / 2
        zone_one("EU Residential High", e["id"], outer(e), 2 if mx > 890 else 4, dry)
        zone_one("EU Residential Medium Row", e["id"], inner(e), 3, dry)


if MODE == "dry":
    roads(True)
elif MODE == "roads":
    with paused(WHO):
        roads(False)
    with paused(WHO):
        trees(False)
elif MODE == "zone-dry":
    zone(True)
elif MODE == "zone":
    zone(False)
