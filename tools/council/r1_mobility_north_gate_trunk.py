"""Council round 1 - Mobility - north-gate-trunk.

Widen the north highway trunk (Highway Oneway - 2 lanes -> 3 lanes) between the distributor node
(-1136,1015) and the tile edge (z~3428), junction approaches first; try Large Road on the z=3040 crossing
road and the x=-1140 distributor only where the dry run shows no error AND no zoned building lot would be
clipped by the 4 m wider road (CS2 removes clipped growables silently, so a clean dry run is not enough).

Usage: python -I council/r1_mobility_north_gate_trunk.py probe|trunk|large|trees|tile|verify
"""
import json
import math
import re
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, upgrade, segs_in, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "verify"
KEYS = ("applied", "cost", "errors", "warnings", "problems", "segmentsReplaced", "error", "warning")
LOG = r"C:\soft\cityskyline\logs\council\r1_mobility_north_gate_trunk.json"
HW2, HW3 = "Highway Oneway - 2 lanes", "Highway Oneway - 3 lanes"

JUNCTION_A = ["395359:1", "395334:1", "395353:1", "395345:1"]   # NB approach + exit
JUNCTION_B = ["395357:1", "395358:1", "395350:1", "395337:1"]   # SB approach + exit
CROSSING_1500 = ["395474:1", "395539:1", "395536:1", "395537:1"]
CROSS_W = ["1048260:25", "715784:25", "567603:59", "569777:53"]
CROSS_E = ["570565:13", "570807:17", "571229:33", "571594:7"]
TILE = (-623, 3740)


def show(label, r):
    print(f"{label:<52} {json.dumps(short(r, keys=KEYS))}", flush=True)
    return r


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def trunk_segments(z_max=3428):
    """All 2-lane one-way highway segments of the trunk with their midpoint inside the owned stretch."""
    segs = get("/nets", x=-815, z=2220, radius=1350, filter="Highway").get("segments") or []
    out = []
    for s in segs:
        mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
        if -1160 <= mx <= -380 and 1010 <= mz <= z_max:
            out.append(s)
    return out


def depth_cells(prefab):
    m = re.search(r"_(\d+)x(\d+)$", prefab or "")
    return int(m.group(2)) if m else None


def clipped_buildings(seg, new_half, margin=0.5):
    """Zoned buildings whose lot front would lie under a road widened to half-width new_half."""
    (x0, z0), (x1, z1) = seg["start"], seg["end"]
    dx, dz = x1 - x0, z1 - z0
    L = math.hypot(dx, dz) or 1.0
    ux, uz = dx / L, dz / L
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    hits = []
    for b in get("/buildings", kind="all", x=cx, z=cz, radius=L / 2 + 70).get("buildings") or []:
        px, pz = b["pos"][0] - x0, b["pos"][1] - z0
        along = px * ux + pz * uz
        if along < -4 or along > L + 4:
            continue
        perp = abs(px * uz - pz * ux)
        dc = depth_cells(b["prefab"])
        if b.get("service") is None and dc is not None:
            front = perp - dc * 4.0
            if front < new_half - margin:
                hits.append((b["id"], b["prefab"], round(perp, 1)))
        elif perp < new_half + 25:  # service / unknown footprint near the road: the dry run decides, flag it
            hits.append((b["id"], b["prefab"], round(perp, 1), "service?"))
    return hits


def seg_lookup(ids, x, z, radius, filt):
    segs = get("/nets", x=x, z=z, radius=radius, filter=filt).get("segments") or []
    m = {s["id"]: s for s in segs}
    return [m[i] for i in ids if i in m]


if __name__ == "__main__":
    if MODE == "probe":
        trunk = trunk_segments(z_max=3600)
        print("trunk 2-lane segments:", len(trunk))
        cross = seg_lookup(CROSS_W + CROSS_E, -800, 3040, 500, "Medium")
        dist = segs_in(-1150, -1128, -1060, 1020, filt="Medium Road")
        res = {"trunk": {}, "large": {}}
        with paused("mobility"):
            for s in trunk:
                r = upgrade(HW3, [s["id"]], dryRun=True)
                res["trunk"][s["id"]] = short(r, keys=KEYS)
                show(f"HW3 {s['id']} {s['start']}->{s['end']}", r)
            for s in cross + dist:
                r = upgrade("Large Road", [s["id"]], dryRun=True)
                res["large"][s["id"]] = short(r, keys=KEYS)
                show(f"LR {s['id']} {s['start']}->{s['end']}", r)
        for s in cross + dist:
            res["large"][s["id"]]["clipped"] = clipped_buildings(s, 16.0)
            print(s["id"], "clipped:", res["large"][s["id"]]["clipped"])
        log("probe", res)

    elif MODE == "trunk":
        # junction approaches first, then the (-1105,1500) crossing, then the rest south->north
        segs = {s["id"]: s for s in trunk_segments(z_max=3428)}
        rest = sorted([i for i in segs if i not in JUNCTION_A + JUNCTION_B + CROSSING_1500], key=lambda i: segs[i]["start"][1])
        batches = [JUNCTION_A, JUNCTION_B, CROSSING_1500] + [rest[i:i + 4] for i in range(0, len(rest), 4)]
        done, spent, skipped = [], 0, []
        for b in batches:
            b = [i for i in b if i in segs]  # already 3 lanes (re-run) or gone -> skip
            if not b:
                continue
            with paused("mobility"):
                d = show(f"HW3 batch {b} (dry)", upgrade(HW3, b, dryRun=True))
                if d.get("errors") == 0 and not d.get("error"):
                    r = show(f"HW3 batch {b}", upgrade(HW3, b))
                    if r.get("applied"):
                        done += b
                        spent += r.get("cost") or 0
                else:  # one by one, skipping the offending segment
                    for i in b:
                        d1 = upgrade(HW3, [i], dryRun=True)
                        if d1.get("errors") == 0 and not d1.get("error"):
                            r = show(f"HW3 {i}", upgrade(HW3, [i]))
                            if r.get("applied"):
                                done.append(i)
                                spent += r.get("cost") or 0
                        else:
                            skipped.append((i, short(d1, keys=KEYS)))
        log("trunk", {"done": done, "skipped": skipped, "spent": spent})
        print("DONE", len(done), "SKIPPED", skipped, "SPENT", spent)
    elif MODE == "large":
        # only the two junction approaches came out clean (no error, no clipped lot); 570552:9 stays Medium
        cross = seg_lookup(["569777:53", "570565:13"], -660, 3040, 150, "Medium")
        done, spent = [], 0
        with paused("mobility"):
            for s in cross:
                clip = clipped_buildings(s, 16.0)
                d = show(f"LR {s['id']} (dry) clipped={clip}", upgrade("Large Road", [s["id"]], dryRun=True))
                if clip or d.get("errors") != 0 or d.get("error"):
                    continue
                r = show(f"LR {s['id']}", upgrade("Large Road", [s["id"]]))
                if r.get("applied"):
                    done.append(s["id"])
                    spent += r.get("cost") or 0
        log("large", {"done": done, "spent": spent})
        print("SPENT", spent)
    elif MODE == "trees":
        segs = get("/nets", x=-668, z=3040, radius=120, filter="Large Road").get("segments") or []
        ids = [s["id"] for s in segs if s["prefab"] == "Large Road"]
        print("Large Road ids:", ids)
        with paused("mobility"):
            d = show("Trees both (dry)", post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both", "dryRun": True}))
            r = show("Trees both", post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"})) if d.get("errors") == 0 else d
        log("trees", {"ids": ids, "result": short(r, keys=KEYS)})
        print("SPENT", r.get("cost") if r.get("applied") else 0)
    elif MODE == "tile":
        # step 7: buy (-623,3740) and widen the trunk on through the owned (0,3740) tile up to z~3990
        NE = ["395346:1", "395356:1", "395333:1", "395355:1", "395348:1", "395354:1", "395313:1", "395312:1"]
        budget_left = float(sys.argv[2]) if len(sys.argv) > 2 else 400000
        with paused("mobility"):
            d = show("buytile (dry)", post("/buytile", {"x": TILE[0], "z": TILE[1], "dryRun": True}))
            print(json.dumps(d))
            if d.get("status") != "None" or (d.get("cost") or 1e12) > budget_left:
                print("tile not bought: status/cost", d.get("status"), d.get("cost"))
                log("tile", d)
                sys.exit(0)
            t = post("/buytile", {"x": TILE[0], "z": TILE[1]})
            print("buytile", json.dumps(t))
        log("tile", t)
        spent = 0
        done = []
        for b in (NE[:4], NE[4:]):
            with paused("mobility"):
                d = show(f"HW3 NE {b} (dry)", upgrade(HW3, b, dryRun=True))
                if d.get("errors") == 0 and not d.get("error"):
                    r = show(f"HW3 NE {b}", upgrade(HW3, b))
                    if r.get("applied"):
                        done += b
                        spent += r.get("cost") or 0
        log("tile_trunk", {"done": done, "spent": spent})
        print("SPENT tile", t.get("cost") if t.get("bought") else 0, "roads", spent)
    elif MODE == "verify":
        segs = get("/nets", x=-500, z=2500, radius=1700, filter="Highway").get("segments") or []
        from collections import Counter
        print("trunk prefabs:", Counter(s["prefab"] for s in segs if s["start"][0] < 300 and 1000 < s["start"][1] < 4100))
        print("still 2-lane in trunk:", [s["id"] for s in segs if s["prefab"] == HW2 and -1160 < s["start"][0] < 0 and 1000 < s["start"][1] < 4000])
        print("crossing:", [(s["id"], s["prefab"]) for s in get("/nets", x=-668, z=3040, radius=100, filter="Road").get("segments") or [] if "Highway" not in s["prefab"]])
        h = get("/hotspots", type="Traffic")
        print("traffic hotspots total", h.get("total"), h.get("iconTypes"))
        for hs in (h.get("hotspots") or [])[:8]:
            print("  ", hs["center"], hs["count"], hs["topPrefabs"])
        print("happiness TrafficPenalty:", [f for f in get("/happiness").get("negative", []) if f.get("factor") == "TrafficPenalty"])
