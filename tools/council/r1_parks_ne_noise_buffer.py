"""Council round 1 - Recreation, Beauty & Environment - ne-noise-buffer.

Replace the row of six small industrial buildings on the south side of the z=2820 Small Road
(x -500..-318, z ~2788-2812) with a park strip between the NE industry estate and the homes at z 2720-2752.
Mayor-approved project (the bulldoze step needed Economy's or the mayor's approval; the mayor approved it).

Steps: bulldoze only buildings whose prefab starts with 'Industrial' and whose centre lies in the strip;
de-zone the strip's leftover industrial cells (z 2786..2812) so nothing regrows; CityPark06 (40x40) facing the
z=2820 road east of the x=-470 access road; CityPark05 (2x3) on the 22 m sliver west of it; small pocket
parks in leftover gaps; street trees on the z=2820 road and the x=-470 access road.

Usage: python -I "council/r1_Recreation, Beauty & Environment_ne-noise-buffer.py" check|build|trees|verify [--dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, building, zone, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
DEPT = "Recreation, Beauty & Environment"
MODE = sys.argv[1] if len(sys.argv) > 1 else "verify"
DRY = "--dry" in sys.argv
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "pos", "quads", "zone")
LOG = r"C:\soft\cityskyline\logs\council\r1_recreation_ne_noise_buffer.json"
STRIP = (-502, -316, 2780, 2815)        # building centres to remove
DEZONE = [[-502, 2785], [-314, 2811]]   # south-side zone cells of the z=2820 road only
TS = [round(0.025 * i, 3) for i in range(1, 40)]


def show(label, r):
    print(f"{label:<34} {json.dumps(short(r or {}, keys=KEYS))}", flush=True)
    return r


def load():
    try:
        return json.load(open(LOG))
    except Exception:
        return {}


def log(key, val):
    d = load()
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def spent_add(r):
    if r and r.get("applied"):
        d = load()
        d["spent"] = d.get("spent", 0) + (r.get("cost") or 0)
        json.dump(d, open(LOG, "w"), indent=1)


def targets():
    x0, x1, z0, z1 = STRIP
    out = []
    for b in get("/buildings", kind="all", x=-410, z=2795, radius=130).get("buildings") or []:
        p = b.get("pos") or [0, 0]
        if (b.get("prefab") or "").startswith("Industrial") and x0 <= p[0] <= x1 and z0 <= p[1] <= z1:
            out.append(b)
    return out


def road_z2820(x0, x1):
    segs = get("/nets", x=(x0 + x1) / 2, z=2820, radius=(x1 - x0) / 2 + 60, filter="Small").get("segments") or []
    return [s for s in segs if abs(s["start"][1] - 2820) < 2 and abs(s["end"][1] - 2820) < 2
            and x0 <= (s["start"][0] + s["end"][0]) / 2 <= x1]


def south_side(s):
    """Side name that faces south (z smaller) for this horizontal segment: heading east -> right."""
    return "right" if s["end"][0] > s["start"][0] else "left"


def place_strip(label, prefab, box, near):
    """Place prefab on the south side of the z=2820 road with its centre inside box."""
    x0, x1, z0, z1 = box
    cands = []
    for s in road_z2820(-520, -290):
        for t in TS:
            sx = s["start"][0] + (s["end"][0] - s["start"][0]) * t
            cands.append((abs(sx - near), s["id"], t, south_side(s)))
    cands.sort(key=lambda c: c[0])
    last = None
    for _, eid, t, side in cands:
        d = post("/building", {"prefab": prefab, "edge": eid, "t": t, "side": side, "dryRun": True})
        last = d
        p = d.get("pos")
        if d.get("errors") == 0 and not d.get("error") and p and x0 <= p[0] <= x1 and z0 <= p[1] <= z1:
            if DRY:
                return show(f"{label} (dry)", d)
            r = building(prefab, edge=eid, t=t, side=side)
            spent_add(r)
            return show(label, r)
    show(f"{label} NO SPOT", last or {})
    return None


if __name__ == "__main__":
    if MODE == "check":
        for b in targets():
            print(b["id"], b["prefab"], b["pos"])
        print("segments z=2820", [(s["id"], s["start"], s["end"]) for s in road_z2820(-520, -290)])
    elif MODE == "build":
        tg = targets()
        ids = [b["id"] for b in tg]
        print("bulldoze targets", [(b["id"], b["prefab"], b["pos"]) for b in tg])
        if len(ids) > 8:
            raise SystemExit("more targets than expected - stop and re-check")
        with paused(DEPT):
            if ids:
                d = show("bulldoze (dry)", post("/bulldoze", {"ids": ids, "dryRun": True}))
                if not DRY and not d.get("error"):
                    r = show("bulldoze", post("/bulldoze", {"ids": ids}))
                    log("bulldozed", [(b["id"], b["prefab"], b["pos"]) for b in tg])
                    spent_add(r)
            d = show("dezone strip (dry)", zone("none", mode="rect", points=DEZONE, dryRun=True))
            if not DRY and not d.get("error"):
                r = show("dezone strip", zone("none", mode="rect", points=DEZONE))
                log("dezoned", DEZONE)
        # parks east of the x=-470 access road (road half-width 8 -> lots from x -462), 40 m each
        with paused(DEPT):
            for i, nx in enumerate((-441, -400, -359)):
                place_strip(f"noise buffer {i} CityPark06", "CityPark06", (-462, -318, 2770, 2805), nx)
            place_strip("noise buffer W CityPark05", "CityPark05", (-500, -478, 2780, 2812), -489)
        # leftover gap east of the third park (x ~ -339..-318): small parks
        with paused(DEPT):
            for prefab in ("Playground05", "PocketPark05", "PocketPark06", "PocketPark03"):
                if not place_strip(f"noise buffer gap {prefab}", prefab, (-345, -312, 2780, 2812), -328):
                    continue
                break
    elif MODE == "trees":
        segs = road_z2820(-520, -290)
        acc = get("/nets", x=-470, z=2765, radius=70, filter="Small").get("segments") or []
        acc = [s for s in acc if abs(s["start"][0] + 470) < 2 and abs(s["end"][0] + 470) < 2 and 2705 <= min(s["start"][1], s["end"][1])]
        ids = [s["id"] for s in segs + acc]
        print("tree edges", [(s["id"], s["start"], s["end"]) for s in segs + acc])
        with paused(DEPT):
            d = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both", "dryRun": True})
            show("trees (dry)", d)
            todo = [e["id"] for e in d.get("edges") or [] if e.get("changes")]
            print("skipped", {e["id"]: e.get("skipped") for e in d.get("edges") or [] if e.get("skipped")})
            if todo and not DRY:
                r = show("trees", post("/roadupgrade", {"edges": todo, "upgrade": "Trees", "side": "both"}))
                spent_add(r)
                log("trees", todo)
    elif MODE == "verify":
        left = targets()
        print("industrial left in strip", [(b["id"], b["prefab"], b["pos"]) for b in left])
        parks = [b for b in get("/buildings", kind="all", x=-410, z=2790, radius=110).get("buildings") or []
                 if any(k in (b.get("prefab") or "") for k in ("Park", "Playground"))]
        for b in parks:
            print(b["id"], b["prefab"], b["pos"], "roadConnected", b.get("roadConnected"))
        print("hotspots", json.dumps(get("/hotspots", type="Noise").get("hotspots"))[:600])
        d = get("/pollution", type="noise", x=-420, z=2760, size=400, cell=40)
        print(d.get("grid"))
        log("verify", {"parks": [(b["id"], b["prefab"], b["pos"]) for b in parks], "industrialLeft": len(left)})
        print("SPENT", load().get("spent"))
