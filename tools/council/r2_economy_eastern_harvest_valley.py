"""Council round 2 - Economic Affairs - eastern-harvest-valley.

Buy tiles 616824:1 and 616700:1 south of Eastern Works, build the tree-lined Medium Road 'Harvest Crescent'
from node 448071:91 (1530,2820) to node 213142:51 (653.3,2820), place IndustrialManufacturingSignature05/06
facing the z=2820 road, zone Industrial Manufacturing along z=2820 (south side) and on the crescent's inner
side, and lay out Grain / Vegetable / Livestock farms (hub + work area) on the crescent's outer (south) side.

Usage: python -I council/r2_economy_eastern_harvest_valley.py check|buy|road|trees|landmarks|zone|farms|cotton|verify
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
sys.path.insert(0, r"C:\soft\cityskyline\tools\council")
from cc import get, post, paused, zone, building, find_spot, short  # noqa: E402
from r2_economy_common import DEPT, Log, show, ledger_overlaps  # noqa: E402

MODE = sys.argv[1] if len(sys.argv) > 1 else "check"
ARG = sys.argv[2] if len(sys.argv) > 2 else None
PID = "eastern-harvest-valley"
LOG = Log(r"C:\soft\cityskyline\logs\council\r2_economy_eastern_harvest_valley.json")
TILES = [("616824:1", {"x": 1247, "z": 2493}), ("616700:1", {"x": 623, "z": 2493})]
# West end moved from node 213142:51 (653.3,2820) to the existing 3-way junction 213059:307 (580,2820): ending at
# the degree-2 node would demolish two factories at (628.7,2852) and (674,2852) (wouldDemolish in the dry run);
# at x=580 the crescent continues the x=580 Small Road south as a 4-way crossing and demolishes nothing.
CRESCENT = [[1530, 2820], [1505, 2690], [1420, 2610], [1280, 2575], [1100, 2570], [920, 2590], [790, 2650],
            [650, 2700], [595, 2760], [580, 2820]]
Z2820_EDGES = ["213628:217", "213667:259", "213836:425", "288822:95", "406117:161", "446895:35", "446904:75"]
BBOX = (560, 1558, 2182, 2812)
FARMS = [
    ("grain", "Agriculture Area Placeholder - Grain", [1290, 2560], [[1160, 2510], [1430, 2510], [1430, 2290], [1160, 2290]]),
    ("vegetable", "Agriculture Area Placeholder - Vegetable", [1000, 2560], [[870, 2510], [1130, 2510], [1130, 2290], [870, 2290]]),
    ("livestock", "Agriculture Area Placeholder - Livestock", [745, 2625], [[580, 2590], [830, 2590], [830, 2390], [580, 2390]]),
]
# Optional Cotton farm. The proposal's lane at x=1145 would end inside the Cotton strip, where a 48 m hub has no
# room between the fields; the lane runs instead through the 40 m Livestock/Vegetable hedgerow gap at x=850 and
# the hub sits on its west side with the field behind it (30 m hedgerow kept to the Livestock field at z 2390).
COTTON_LANE = [[850, 2621], [850, 2205]]
COTTON = [
    ("cotton", "Agriculture Area Placeholder - Cotton", [835, 2250], [[600, 2360], [790, 2360], [790, 2190], [600, 2190]]),
]


def crescent_z(x):
    """z of the crescent's southern (outer) arc at x (linear between the design points)."""
    pts = sorted(CRESCENT[1:-1], key=lambda p: p[0])
    for (xa, za), (xb, zb) in zip(pts, pts[1:]):
        if xa <= x <= xb:
            return za + (zb - za) * (x - xa) / (xb - xa)
    return 2820


def crescent_dry(points):
    return post("/road", {"prefab": "Medium Road", "points": points, "curve": "smooth", "dryRun": True})


def crescent_edges():
    """Medium Road segments south of z 2815 between x 640 and 1545 (the crescent)."""
    out, off = [], 0
    while True:
        n = get("/nets", x=1100, z=2650, radius=520, filter="Medium Road", offset=off, limit=200)
        segs = n.get("segments") or []
        out += segs
        if not n.get("truncated") or not segs:
            break
        off += len(segs)
    res = []
    for s in out:
        if s.get("prefab") != "Medium Road":
            continue
        (x0, z0), (x1, z1) = s["start"], s["end"]
        if max(z0, z1) <= 2821 and min(z0, z1) < 2815 and 570 <= min(x0, x1) and max(x0, x1) <= 1545:
            res.append(s)
    return res


if __name__ == "__main__":
    if MODE == "check":
        t = get("/tiles")
        for c in t.get("candidates", []):
            if c.get("id") in ("616824:1", "616700:1"):
                print("tile", json.dumps(c))
        print("industrial demand (/city):", get("/city").get("demand", {}).get("industrial"))
        print("ledger overlaps:", ledger_overlaps(*BBOX))
        for tid, p in TILES:
            show(f"buytile dry {tid}", post("/buytile", {**p, "dryRun": True}))
        d = crescent_dry(CRESCENT)
        show("crescent dry", d)
        print(json.dumps({k: d.get(k) for k in ("geometry", "selection", "warnings")}, default=str)[:2500])
    elif MODE == "buy":
        with paused(DEPT):
            for tid, p in TILES:
                r = show(f"buytile {tid}", post("/buytile", p))
                LOG.put(f"buytile {tid}", r)
                if r.get("bought") or r.get("applied"):
                    LOG.spend(r.get("cost") or r.get("price") or 0, f"tile {tid}")
        print(json.dumps(get("/tiles").get("tileUpkeep")))
    elif MODE == "road":
        parts = [CRESCENT]
        if ARG == "split":
            parts = [CRESCENT[:5], CRESCENT[4:7], CRESCENT[6:]]  # split at (1100,2570) and (790,2650)
        for p in parts:
            d = crescent_dry(p)
            show(f"dry {p[0]}->{p[-1]}", d)
            if d.get("errors") or d.get("error") or d.get("blocked") or d.get("wouldDemolish"):
                print("STOP: dry run failed", json.dumps(d, default=str)[:1500])
                sys.exit(1)
        with paused(DEPT):
            for p in parts:
                r = post("/road", {"prefab": "Medium Road", "points": p, "curve": "smooth"})
                show(f"road {p[0]}->{p[-1]}", r)
                LOG.put(f"road {p[0]}", short(r, keys=("applied", "cost", "newEdgeIds", "builtEdges", "errors", "problems")))
                if r.get("applied"):
                    LOG.spend(r.get("cost") or 0, f"Harvest Crescent {p[0]}->{p[-1]}")
    elif MODE == "trees":
        segs = crescent_edges()
        ids = [s["id"] for s in segs]
        print("crescent edges:", len(ids), ids)
        d = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both", "dryRun": True})
        show("trees dry", d)
        if not (d.get("errors") or d.get("error") or d.get("blocked")):
            r = show("trees", post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"}))
            LOG.put("trees", short(r))
            if r.get("applied"):
                LOG.spend(r.get("cost") or 0, "crescent trees")
    elif MODE == "edges":
        for s in crescent_edges():
            print(s["id"], s["start"], s["end"], s["startNode"], s["endNode"], s.get("upgrades"))
    elif MODE == "landmarks":
        for prefab, (x, z) in (("IndustrialManufacturingSignature06", (1030, 2750)),
                               ("IndustrialManufacturingSignature05", (1300, 2735))):
            for c in find_spot(prefab, x, z, radius=200, n=8):
                print(prefab[-2:], "cand", json.dumps(c, default=str)[:400])
    elif MODE == "place_landmarks":
        # facing the z=2820 road from the south; the lot must stay north of z 2660 (clear of the crescent's band)
        for prefab, (x, z) in (("IndustrialManufacturingSignature06", (1030, 2750)),
                               ("IndustrialManufacturingSignature05", (1300, 2735))):
            done = False
            for c in find_spot(prefab, x, z, radius=200, n=8):
                zmin = min(p[1] for p in c["corners"])
                front = c.get("front") or [0, 0]
                if zmin < 2660 or abs(front[1] - 2820) > 2:
                    continue
                b = dict(c["building"])
                b.pop("prefab", None)
                d = show(f"{prefab[-11:]} dry {c['x']},{c['z']}", post("/building", {"prefab": prefab, **b, "dryRun": True}))
                if d.get("errors") != 0 or d.get("wouldDemolish") or d.get("blocked"):
                    continue
                with paused(DEPT):
                    r = show(prefab[-11:], building(prefab, **b))
                LOG.put(prefab, {**short(r), "corners": c["corners"], "building": b})
                if r.get("applied"):
                    LOG.spend(r.get("cost") or 0, prefab)
                    done = True
                    break
            if not done:
                print("NO SPOT for", prefab)
    elif MODE in ("farms", "cotton"):
        # crescent edges run east->west, so 'left' is the outer (south) side; hubs go there
        farms = FARMS if MODE == "farms" else COTTON
        only = ARG
        if MODE == "cotton" and not LOG.load().get("cotton lane"):
            d = show("cotton lane dry", post("/road", {"prefab": "Small Road", "points": COTTON_LANE, "dryRun": True}))
            if d.get("errors") or d.get("error") or d.get("wouldDemolish") or d.get("blocked"):
                sys.exit("STOP: lane dry run failed")
            with paused(DEPT):
                r = show("cotton lane", post("/road", {"prefab": "Small Road", "points": COTTON_LANE}))
                if r.get("applied"):
                    LOG.spend(r.get("cost") or 0, "cotton farm lane x=850")
                    LOG.put("cotton lane", short(r, keys=("applied", "cost", "newEdgeIds")))
                    t = show("lane trees", post("/roadupgrade", {"edges": r.get("newEdgeIds") or [], "upgrade": "Trees", "side": "both"}))
                    if t.get("applied"):
                        LOG.spend(t.get("cost") or 0, "cotton lane trees")
        for key, prefab, near, poly in farms:
            if only and key != only:
                continue
            done = LOG.load().get(f"farm {key}") or {}
            hub = done.get("placedId")
            hub_pos = done.get("pos")
            if not hub:
                hd = None
                for side in ("auto", "left", "right"):
                    d = post("/building", {"prefab": prefab, "near": near, "side": side, "dryRun": True})
                    show(f"{key} hub dry side={side}", d)
                    pos = d.get("pos") or [0, 0]
                    crest_z = crescent_z(pos[0]) if MODE == "farms" else None
                    south_ok = pos[0] < 850 and pos[1] < 2330 if MODE == "cotton" else pos[1] < crest_z
                    if d.get("errors") == 0 and not d.get("wouldDemolish") and south_ok:
                        hd = side
                        break
                if hd is None:
                    print("NO HUB SPOT for", key)
                    continue
                with paused(DEPT):
                    r = show(f"{key} hub", building(prefab, near=near, side=hd))
                if not r.get("applied"):
                    continue
                LOG.spend(r.get("cost") or 0, f"{key} hub")
                hub, hub_pos = r.get("placedId"), r.get("pos")
                LOG.put(f"farm {key}", {"placedId": hub, "pos": hub_pos, "roadConnected": r.get("roadConnected")})
            # fit the work area: corners within 290 m of the hub (maxRadius 300), pull far edges 20 m toward it
            pts = [list(p) for p in poly]
            for _ in range(8):
                far = [p for p in pts if math.dist(p, hub_pos) > 290]
                if not far:
                    break
                for p in far:
                    p[1] += 20 if p[1] < hub_pos[1] else -20
            d = post("/area", {"owner": hub, "points": pts, "dryRun": True})
            print(f"{key} area dry pts={pts}", json.dumps(d, default=str)[:900])
            if d.get("errors") or d.get("error") or not (d.get("applied") is False):
                print("STOP area", key)
                continue
            with paused(DEPT):
                r = post("/area", {"owner": hub, "points": pts})
            print(f"{key} area", json.dumps(r, default=str)[:900])
            done = LOG.load().get(f"farm {key}") or {}
            done.update({"area": pts, "areaResult": short(r, keys=("applied", "cost", "errors", "resourceAmount", "area", "preview"))})
            LOG.put(f"farm {key}", done)
            if r.get("applied"):
                LOG.spend(r.get("cost") or 0, f"{key} area")
    elif MODE == "verify":
        print(json.dumps(LOG.load(), default=str)[:3000])
        print("total spent:", LOG.total())
