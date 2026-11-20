"""builder-northeast - b1-utilities-ne-wind-fields: Northeast Wind Fields.

Tile (623,3740) (x 312..935, z 3428..4052) was bought for this item. Layout:
- Wind Fields Lane: one curved Small Road loop from the NE utilities road (x=140) at z~3530, east along the
  south of the tile, up the east side and back west along the north edge to the x=140 dead end (140,3900).
- Meadow Spur: one curved Small Road cul-de-sac from the east side of the loop west into the middle of the field.
- WindTurbine01 on both sides of the lanes, staggered (~120 m apart along each side), no zoning.
- Street trees on both sides of every lane.

Usage: python -I builders/builder-northeast_wind_fields.py lane-dry|lane|spur-dry|spur|turbines-dry|turbines|trees|verify
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, building, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "verify"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_wind_fields.json"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "wouldDemolish",
        "conflicts", "builtEdges", "newEdgeIds", "geometry")

# loop: from the x=140 utilities road (south entry) round the field and back to the dead end at (140,3900)
LANE = [[140, 3530], [300, 3518], [480, 3492], [680, 3500], [830, 3555], [882, 3700], [868, 3850],
        [770, 3962], [590, 3990], [410, 3982], [260, 3968], [180, 3962], [146, 3940], [140, 3900]]
# cul-de-sac from the east side of the loop into the middle of the meadow
SPUR = [[884.5, 3745], [760, 3752], [620, 3735], [470, 3748]]
# second cul-de-sac from the north side of the loop south into the west meadow (added after the dry run showed
# that turbines need ~55 m clearance from the tile edge, which rules out the outer side of the north/east legs)
WEST = [[400, 3981.4], [386, 3880], [402, 3770], [392, 3650]]


def show(label, r):
    print(f"{label:<36} {json.dumps(short(r, keys=KEYS))[:1500]}", flush=True)
    return r


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def lane(points, dry):
    return post("/road", {"prefab": "Small Road", "points": points, "curve": "smooth", "dryRun": dry})


TILE = (312, 935, 3428, 4052)  # x0, x1, z0, z1 of the owned wind-field tile
STEP = 65.0        # arc distance between consecutive turbines (they alternate sides: 130 m along each side)
OFFSET = 20.0      # lot centre distance from the road centreline (measured: ~20 m for a Small Road)
MIN_GAP = 58.0     # minimum distance between turbine centres
JUNCTIONS = ((885.6, 3745.0), (400.0, 3981.4))


def chord_polyline(edges):
    pts = [edges[0]["start"]] + [e["end"] for e in edges]
    return [tuple(p) for p in pts]


def sample(poly, s0, step, s_end=None):
    """Points (x, z, tx, tz, s) every `step` m of arc along the polyline, starting at arc s0."""
    out, acc, s = [], 0.0, s0
    for (ax, az), (bx, bz) in zip(poly, poly[1:]):
        L = math.hypot(bx - ax, bz - az)
        while s <= acc + L and (s_end is None or s <= s_end):
            u = (s - acc) / L
            out.append((ax + u * (bx - ax), az + u * (bz - az), (bx - ax) / L, (bz - az) / L, s))
            s += step
        acc += L
    return out


def plan():
    d = json.load(open(LOG))
    targets = []
    # main loop, counter-clockwise: left = inner side (towards the meadow), right = outer side
    for i, (x, z, tx, tz, s) in enumerate(sample(chord_polyline(d["lane"]["builtEdges"]), 0.0, STEP)):
        side = "right" if i % 2 == 0 else "left"
        if side == "left" and any(math.hypot(x - jx, z - jz) < 50 for jx, jz in JUNCTIONS):
            continue  # spur junctions on the inner side
        targets.append(("lane", x, z, tx, tz, side))
    for i, (x, z, tx, tz, s) in enumerate(sample(chord_polyline(d["spur"]["builtEdges"]), 55.0, STEP, s_end=400)):
        targets.append(("spur", x, z, tx, tz, "left" if i % 2 == 0 else "right"))
    if "west" in d:
        for i, (x, z, tx, tz, s) in enumerate(sample(chord_polyline(d["west"]["builtEdges"]), 50.0, STEP, s_end=330)):
            targets.append(("west", x, z, tx, tz, "right" if i % 2 == 0 else "left"))
    keep = []
    for lbl, x, z, tx, tz, side in targets:
        nx, nz = (-tz, tx) if side == "left" else (tz, -tx)
        qx, qz = x + OFFSET * nx, z + OFFSET * nz
        if not (TILE[0] + 16 <= qx <= TILE[1] - 16 and TILE[2] + 16 <= qz <= TILE[3] - 16):
            continue
        keep.append((lbl, round(x, 1), round(z, 1), round(qx, 1), round(qz, 1)))
    return keep


def edge_and_t(x, z, qx, qz):
    """Current Small Road edge under (x, z), the chord parameter t and the side towards (qx, qz)."""
    segs = get("/nets", x=x, z=z, radius=25, filter="Small Road").get("segments") or []
    best = None
    for sg in segs:
        (sx, sz), (ex, ez) = sg["start"], sg["end"]
        dx, dz = ex - sx, ez - sz
        L2 = dx * dx + dz * dz
        if not L2:
            continue
        t = max(0.0, min(1.0, ((x - sx) * dx + (z - sz) * dz) / L2))
        dist = math.hypot(sx + t * dx - x, sz + t * dz - z)
        if best is None or dist < best[0]:
            cross = dx * (qz - sz) - dz * (qx - sx)
            best = (dist, sg["id"], t, "left" if cross > 0 else "right")
    return best


def existing_turbines():
    b = get("/buildings", kind="service", x=620, z=3740, radius=520).get("buildings") or []
    return [tuple(x["pos"]) for x in b if x.get("prefab", "").startswith("WindTurbine")]


def place(dry):
    placed = existing_turbines()
    done, results = 0, []
    for lbl, x, z, qx, qz in plan():
        if any(math.hypot(qx - px, qz - pz) < MIN_GAP for px, pz in placed):
            print(f"skip {lbl} ({qx},{qz}): too close to a turbine", flush=True)
            continue
        hit = edge_and_t(x, z, qx, qz)
        if not hit or hit[0] > 12:
            print(f"skip {lbl} ({x},{z}): no lane edge found ({hit})", flush=True)
            continue
        _, eid, t0, side = hit
        req, r, pos = None, {}, None
        # curved edges: a lot can clip the road at one t and fit at a neighbouring one ("Invalid Shape")
        for dt in (0, 0.1, -0.1, 0.2, -0.2, 0.3, -0.3):
            t = t0 + dt
            if not 0.05 <= t <= 0.95 and dt:
                continue
            cand = {"prefab": "WindTurbine01", "edge": eid, "t": round(min(max(t, 0.02), 0.98), 3), "side": side}
            r = post("/building", {**cand, "dryRun": True})
            p = r.get("pos")
            if r.get("errors") == 0 and p and not r.get("wouldDemolish") and \
                    not any(math.hypot(p[0] - px, p[1] - pz) < MIN_GAP for px, pz in placed):
                req, pos = cand, p
                break
            if "Exceeds City Limits" in (r.get("problems") or []):
                break  # sliding along the edge will not bring it inside the tile
        if not req:
            print(f"skip {lbl} {eid} t~{t0:.2f} {side}: {json.dumps(short(r, keys=KEYS))[:200]}", flush=True)
            continue
        if not dry:
            r = building(**req)
            if not r.get("applied"):
                print(f"FAILED {lbl} {req}: {json.dumps(short(r, keys=KEYS))[:300]}", flush=True)
                continue
            pos = r.get("pos") or pos
        placed.append(tuple(pos))
        done += 1
        results.append({"id": r.get("placedId"), "pos": pos, "cost": r.get("cost"), **req})
        print(f"{'plan' if dry else 'built'} {done:>2} {lbl} {pos} {r.get('placedId', '')}", flush=True)
    return results


# Lesson from the first pass: a WindTurbine01 reserves a rotor circle, so turbines overlap below ~120 m and
# need ~55 m from a non-owned tile edge ("Exceeds City Limits"); building next to a road also renews the
# road's edge ids. The fill pass therefore re-reads edges and validates against the live city per turbine.
GAP2 = 122.0
LIMITS = (150, 880, 3440, 3997)  # lot-centre box: west/south border owned land, north/east need the margin


def fill(dry):
    placed = existing_turbines()
    segs = [sg for sg in get("/nets", x=520, z=3740, radius=520, filter="Small Road").get("segments") or []
            if sg["start"][0] > 140 or sg["end"][0] > 140]
    cands = []
    for sg in segs:
        (sx, sz), (ex, ez) = sg["start"], sg["end"]
        L = math.hypot(ex - sx, ez - sz)
        if L < 1:
            continue
        tx, tz = (ex - sx) / L, (ez - sz) / L
        for k in range(1, int(L // 8)):
            x, z = sx + tx * 8 * k, sz + tz * 8 * k
            for nx, nz in ((-tz, tx), (tz, -tx)):
                qx, qz = x + OFFSET * nx, z + OFFSET * nz
                if LIMITS[0] <= qx <= LIMITS[1] and LIMITS[2] <= qz <= LIMITS[3]:
                    cands.append((qx, qz, x, z))
    built = []
    for qx, qz, x, z in sorted(cands, key=lambda c: (c[1], c[0])):
        if any(math.hypot(qx - px, qz - pz) < GAP2 for px, pz in placed):
            continue
        hit = edge_and_t(x, z, qx, qz)
        if not hit or hit[0] > 12:
            continue
        _, eid, t, side = hit
        req = {"prefab": "WindTurbine01", "edge": eid, "t": round(min(max(t, 0.02), 0.98), 3), "side": side}
        r = post("/building", {**req, "dryRun": True})
        pos = r.get("pos")
        if r.get("errors") != 0 or not pos or r.get("wouldDemolish") or                 any(math.hypot(pos[0] - px, pos[1] - pz) < GAP2 for px, pz in placed):
            continue
        if not dry:
            with paused(WHO):
                r = building(**req)
            if not r.get("applied"):
                print(f"FAILED {req} {json.dumps(short(r, keys=KEYS))[:200]}", flush=True)
                continue
            pos = r.get("pos") or pos
        placed.append(tuple(pos))
        built.append({"id": r.get("placedId"), "pos": pos, "cost": r.get("cost") if not dry else 0, **req})
        print(f"{'plan' if dry else 'built'} {len(built):>2} {pos} {r.get('placedId', '')}", flush=True)
    return built


if __name__ == "__main__":
    if MODE in ("fill-dry", "fill"):
        res = fill(MODE == "fill-dry")
        if MODE == "fill":
            log("turbines_fill", res)
            print("BUILT", len(res), "SPENT", sum(r.get("cost") or 0 for r in res))
    elif MODE == "plan":
        for p in plan():
            print(p)
    elif MODE == "turbines-dry":
        place(True)
    elif MODE == "turbines":
        # validate outside the lock, then build the validated edge/t/side requests in short locked batches
        planned = place(True)
        res = []
        for i in range(0, len(planned), 7):
            with paused(WHO):
                for p in planned[i:i + 7]:
                    req = {k: p[k] for k in ("prefab", "edge", "t", "side")}
                    r = building(**req)
                    print(f"{'built' if r.get('applied') else 'FAILED'} {req} {json.dumps(short(r, keys=KEYS))[:200]}",
                          flush=True)
                    if r.get("applied"):
                        res.append({"id": r.get("placedId"), "pos": r.get("pos"), "cost": r.get("cost"), **req})
        log("turbines", res)
        print("BUILT", len(res), "SPENT", sum(r.get("cost") or 0 for r in res))
    elif MODE in ("lane-dry", "lane"):
        dry = MODE == "lane-dry"
        if dry:
            show("lane (dry)", lane(LANE, True))
        else:
            with paused(WHO):
                r = show("lane", lane(LANE, False))
            log("lane", r)
    elif MODE in ("spur-dry", "spur"):
        dry = MODE == "spur-dry"
        if dry:
            show("spur (dry)", lane(SPUR, True))
        else:
            with paused(WHO):
                r = show("spur", lane(SPUR, False))
            log("spur", r)
    elif MODE in ("west-dry", "west"):
        if MODE == "west-dry":
            show("west spur (dry)", lane(WEST, True))
        else:
            with paused(WHO):
                r = show("west spur", lane(WEST, False))
            log("west", r)
    elif MODE == "verify":
        print(json.dumps(get("/city").get("electricity")))
