"""Council round 1 - Economic Affairs - eastern-works.

Extend the NE industry estate one tile east (tile idx 381, centre [1247,3117]) with a road grid that ties
into the existing x=800 Small Road, keep block x 1180..1530 / z 3150..3420 for IndustrialManufacturingSignature09,
zone Industrial Manufacturing and plant trees on the Medium Road spine (z=3040) and east edge (x=1530).

Usage: python -I council/r1_economy_eastern_works.py check|buy|roads1|roads2|roads3|landmark|zone|trees|verify
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, zone, building, place_smart, find_spot, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "check"
DEPT = "economy"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "pos", "angle", "bought", "status")
LOG = r"C:\soft\cityskyline\logs\council\r1_economy_eastern_works.json"
TILE = {"x": 1247, "z": 3117}
XS = [800, 990, 1180, 1370, 1530]


def show(label, r):
    print(f"{label:<46} {json.dumps(short(r or {}, keys=KEYS))}", flush=True)
    return r


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def logged():
    try:
        return json.load(open(LOG))
    except Exception:
        return {}


def spent_add(amount, what):
    d = logged()
    d.setdefault("spent", []).append([what, amount])
    json.dump(d, open(LOG, "w"), indent=1)


def seg(prefab, a, b):
    """Dry-run one straight segment; build it when the game accepts it."""
    label = f"{prefab[:12]} {a}->{b}"
    d = post("/road", {"prefab": prefab, "points": [a, b], "dryRun": True})
    if d.get("errors") or d.get("error"):
        show(label + " DRY-FAIL", d)
        return None
    r = road(prefab, [a, b])
    show(label, r)
    if r.get("applied"):
        spent_add(r.get("cost") or 0, label)
    return r


def chain(prefab, pts):
    out = []
    for a, b in zip(pts, pts[1:]):
        out.append(seg(prefab, a, b))
    return out


def ew(z, xs):
    return [[x, z] for x in xs]


def ns(x, zs):
    return [[x, z] for z in zs]


if __name__ == "__main__":
    if MODE == "check":
        t = get("/tiles")
        print("tile 381 candidate:", [c for c in t.get("candidates", []) if c.get("index") == 381])
        show("buytile dry", post("/buytile", {**TILE, "dryRun": True}))
        print("industrial demand:", get("/city").get("demand"))
    elif MODE == "buy":
        with paused(DEPT):
            r = show("buytile", post("/buytile", TILE))
        log("buytile", r)
        if r.get("bought") or r.get("applied"):
            spent_add(r.get("cost") or 0, "tile 381")
    elif MODE == "roads1":
        # a) Medium spine z=3040 and b) Medium east edge x=1530
        with paused(DEPT):
            chain("Medium Road", ew(3040, XS))
            chain("Medium Road", ns(1530, [2820, 2930, 3040, 3150, 3260, 3370, 3412]))  # 3420 exceeds city limits
    elif MODE == "roads2":
        # c) Small E-W roads
        with paused(DEPT):
            for z in (2820, 2930, 3150):
                chain("Small Road", ew(z, XS))
            chain("Small Road", ew(3260, [800, 990, 1180]))
            seg("Small Road", [800, 3260], [800, 3370])
            chain("Small Road", ew(3370, [800, 990, 1180]))
    elif MODE == "roads3":
        # d) Small N-S roads
        with paused(DEPT):
            for x in (990, 1180):
                chain("Small Road", ns(x, [2820, 2930, 3040, 3150, 3260, 3370]))
            chain("Small Road", ns(1370, [2820, 2930, 3040, 3150]))
    elif MODE == "landmark":
        prefab = "IndustrialManufacturingSignature09"
        cands = find_spot(prefab, 1355, 3290, radius=200, n=8)
        for c in cands:
            print("cand", json.dumps(c)[:300])
        with paused(DEPT):
            r, c = place_smart(prefab, 1355, 3290, radius=200, dry=True)
            show("Signature09 dry", r if r else c)
            if r is not None:
                b = dict(c["building"])
                b.pop("prefab", None)
                rr = show("Signature09", building(prefab, **b))
                log("landmark", rr)
                if rr.get("applied"):
                    spent_add(rr.get("cost") or 0, "Signature09")
            else:
                log("landmark", {"failed": c})
    elif MODE == "landmark_fallback":
        with paused(DEPT):
            chain("Small Road", ew(3260, [1180, 1370, 1530]))
            chain("Small Road", ew(3370, [1180, 1370, 1530]))
            chain("Small Road", ns(1370, [3150, 3260, 3370]))
    elif MODE == "zone":
        with paused(DEPT):
            d = show("zone dry", zone("Industrial Manufacturing", mode="rect", points=[[808, 2812], [1545, 3426]], overwrite=False, dryRun=True))
            if not d.get("error"):
                r = show("zone", zone("Industrial Manufacturing", mode="rect", points=[[808, 2812], [1545, 3426]], overwrite=False))
                log("zone", r)
                if r.get("applied"):
                    spent_add(r.get("cost") or 0, "zone")
    elif MODE == "trees":
        segs = get("/nets", x=1165, z=3120, radius=420, filter="Medium").get("segments") or []
        ids = []
        for s in segs:
            (x0, z0), (x1, z1) = s["start"], s["end"]
            on_spine = abs(z0 - 3040) < 2 and abs(z1 - 3040) < 2 and min(x0, x1) >= 799
            on_east = abs(x0 - 1530) < 2 and abs(x1 - 1530) < 2
            if s.get("prefab") == "Medium Road" and (on_spine or on_east):
                ids.append(s["id"])
        print("medium edges:", ids)
        with paused(DEPT):
            d = show("trees dry", post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both", "dryRun": True}))
            if not d.get("error"):
                r = show("trees", post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"}))
                log("trees", {"edges": ids, "result": r})
                if r.get("applied"):
                    spent_add(r.get("cost") or 0, "trees")
    elif MODE == "verify":
        zs = get("/zonestats")
        print("zonestats:", json.dumps([z for z in (zs.get("zones") or []) if "Industrial" in (z.get("zone") or "")] or zs)[:800])
        m = get("/map", x=1180, z=3117, size=800, cell=20)
        print(m.get("grid"))
        print("demand:", get("/city").get("demand"))
        h = get("/happiness")
        print("happiness:", json.dumps(h)[:600])
        nc = get("/problems", type="Not Connected") or {}
        print("problems Not Connected:", json.dumps(nc)[:400])
        print("spent:", sum(a for _, a in logged().get("spent", [])), logged().get("spent"))
