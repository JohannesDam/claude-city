"""Council round 1 - Health & Safety - resilient-neighbourhoods.

1) EmergencyShelterNode (1 pt) + 8 EmergencyShelter01 (first ring).
2) FireHouse01 at the north edge of the NE industry estate (-198.2,3378.2).
3) SW safety post: the planned strip east of the x=-2902 road (x -2886..-2822) was zoned and built up this
   round (medium homes at x~-2884, z -475..-855) and Education put ElementarySchool01 (-2859.5,-934) south of
   it. Replacement: west side of Education's school lane (Small Road 1549176:13, x=-2900, z -870..-1010):
   FireHouse01 at (-2928.5,-916.7) and PoliceStation02 at (-2928.5,-963.3), both footprints free.

Every placement: dry run, footprint free of buildings, and no building within 30 m of the spot (plan rule);
a shelter that fails is retried at nearby t on the same edge.

Usage: python -I council/r1_health_safety_resilient_neighbourhoods.py baseline|check|unlock|shelters|fire|police|verify
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools\council")
from r1_health_safety_common import (get, post, paused, building, show, log, load, dry_ok, footprint,  # noqa: E402
                                     buildings_in_box, DEPT)

MODE = sys.argv[1] if len(sys.argv) > 1 else "check"
NAME = "resilient_neighbourhoods"

SHELTERS = [
    ("downtown", "303251:1", 0.526, "right", (-1800.9, 1246.5)),
    ("downtown_nw", "353181:9", 0.526, "left", (-2240.9, 1923.5)),
    ("west_1", "303345:1", 0.722, "right", (-2772.5, 810.6)),
    ("west_2", "425116:51", 0.458, "right", (-3129.4, -303.5)),
    ("north", "469040:19", 0.071, "left", (-1705.5, 2768.9)),
    ("east_lakeside", "355117:23", 0.222, "left", (-524.4, 2286.5)),
    ("university", "959672:11", 0.182, "left", (-1485.5, 4003.4)),
    ("office_park", "302733:1", 0.143, "left", (-1815.0, -116.5)),
]
FIRE_NE = [dict(edge="189781:119", t=0.161, side="left", gap=0.5), dict(edge="189781:119", t=0.355, side="left", gap=0.5)]
FIRE_NE_EXPECT = (-198.2, 3378.2)
FIRE_SW = dict(edge="1549176:13", t=0.333, side="right", gap=0.5)
FIRE_SW_EXPECT = (-2928.5, -916.7)
POLICE_SW = dict(edge="1549176:13", t=0.667, side="right", gap=0.5)
POLICE_SW_EXPECT = (-2928.5, -963.3)
NUDGE = (0.0, 0.08, -0.08, 0.16, -0.16)


def near30(pos, own=None):
    bs = get("/buildings", kind="all", x=pos[0], z=pos[1], radius=30).get("buildings") or []
    return [b for b in bs if b["id"] != own]


def place_checked(label, prefab, spec, expect=None, tol=12.0, dry=False, r30=True):
    """dry run -> footprint free -> nothing within 30 m -> build. Returns result or None."""
    d = show(f"{label} (dry)", building(prefab, **spec, dryRun=True))
    if not dry_ok(d, expect, tol):
        print("  -> skip: dry run not clean / moved", flush=True)
        return None
    box = footprint(d["pos"], d["lot"], d.get("facing") or [0, 1])
    hits = buildings_in_box(box, 4)
    if hits:
        print(f"  -> skip: footprint {tuple(round(v) for v in box)} has {[(h['prefab'], h['pos']) for h in hits[:4]]}", flush=True)
        return None
    if r30:
        n = near30(d["pos"])
        if n:
            print(f"  -> skip: {len(n)} building(s) within 30 m: {[(h['prefab'][:30], h['pos']) for h in n[:4]]}", flush=True)
            return None
    if dry:
        print("  -> ok", flush=True)
        return d
    r = show(label, building(prefab, **spec))
    return r if r.get("applied") else None


PROTECTED = [(-4300, -4030, -900, 1960), (-1260, -600, 2760, 3420)]  # Westpark, Claude Park (x0, x1, z0, z1)


def protected(x, z):
    return any(a <= x <= b and c <= z <= d for a, b, c, d in PROTECTED)


def alt_specs(exp, radius=300, keep=3):
    """Fresh /findspot shelter spots near exp with nothing within 30 m (pre-filter, no lock held).
    /findspot returns at most ~50 candidates, all in the dense blocks around the centre, so when that finds
    nothing clear, probe a ring of centres 200 m and 450 m out (radius 150 each)."""
    def clear(cands):
        res = []
        for c in cands:
            if protected(c["x"], c["z"]) or near30((c["x"], c["z"])):
                continue
            b = dict(c["building"])
            b.pop("prefab", None)
            res.append((b, (c["x"], c["z"])))
        return res

    out = clear(get("/findspot", prefab="EmergencyShelter01", x=exp[0], z=exp[1], radius=radius, n=60).get("candidates") or [])
    if not out:
        seen = set()
        for rr in (200, 450):
            for k in range(8):
                a = k * math.pi / 4
                cs = get("/findspot", prefab="EmergencyShelter01", x=exp[0] + rr * math.cos(a), z=exp[1] + rr * math.sin(a),
                         radius=150, n=50).get("candidates") or []
                for b, p in clear(cs):
                    key = (round(p[0], 1), round(p[1], 1))
                    if key not in seen:
                        seen.add(key)
                        out.append((b, p))
    out.sort(key=lambda bp: math.hypot(bp[1][0] - exp[0], bp[1][1] - exp[1]))
    return out[:keep]


def shelters(dry=False):
    done = (load(NAME).get("shelters") or {}) if not dry else {}
    out = dict(done)
    for key, edge, t, side, exp in SHELTERS:
        if done.get(key):
            continue
        specs = [(dict(edge=edge, t=round(min(0.95, max(0.05, t + dt)), 3), side=side, gap=0.5), exp, 40 if dt else 12)
                 for dt in NUDGE[:3]]
        # pre-check the planned spot cheaply: if something stands within 30 m of it, go straight to alternatives
        if near30(exp):
            specs = []
            print(f"shelter {key}: planned spot {exp} has buildings within 30 m -> alternatives", flush=True)
        specs += [(b, p, 3) for b, p in alt_specs(exp)]
        res = None
        with paused(DEPT):  # one shelter per lock hold
            for spec, p, tol in specs:
                res = place_checked(f"shelter {key} {spec['edge']} t={spec['t']}", "EmergencyShelter01", spec,
                                    expect=p, tol=tol, dry=dry)
                if res:
                    break
        out[key] = res
        if res:
            print(f"  => {key} at {res.get('pos')} ({math.hypot(res['pos'][0] - exp[0], res['pos'][1] - exp[1]):.0f} m from plan)", flush=True)
        if not dry:
            log(NAME, "shelters", out)
    return out


def coverage_snapshot():
    snap = {}
    for svc in ("FireRescue", "Police", "EmergencyShelter"):
        c = get("/coverage", service=svc, layer="homes")
        snap[svc] = {"summary": c.get("summary"),
                     "worst": [{k: w.get(k) for k in ("center", "households", "avgCoverage", "minCoverage")}
                               for w in (c.get("worstResidentialCells") or [])[:6]]}
    return snap


if __name__ == "__main__":
    if MODE == "baseline":
        snap = coverage_snapshot()
        log(NAME, "baseline", snap)
        print(json.dumps(snap)[:3000])
    elif MODE == "check":
        with paused(DEPT):
            for spec in FIRE_NE:
                place_checked("FireHouse01 NE", "FireHouse01", spec, dry=True, r30=False)
            place_checked("FireHouse01 SW", "FireHouse01", FIRE_SW, expect=FIRE_SW_EXPECT, dry=True)
            place_checked("PoliceStation02 SW", "PoliceStation02", POLICE_SW, expect=POLICE_SW_EXPECT, dry=True)
    elif MODE == "unlock":
        dt = get("/devtree")
        node = [n for n in dt.get("nodes") or [] if n["name"] == "EmergencyShelterNode"][0]
        print("points", dt.get("points"), json.dumps(node))
        if node.get("locked"):
            r = post("/devtree", {"node": "EmergencyShelterNode"})
            print(json.dumps(r)[:500])
            log(NAME, "devtree", r)
    elif MODE in ("shelters", "shelters-dry"):
        res = shelters(dry=(MODE == "shelters-dry"))
        if MODE == "shelters":
            print("SPENT", sum((r or {}).get("cost") or 0 for r in res.values()))
    elif MODE == "fire":
        out = load(NAME).get("fire") or {}
        with paused(DEPT):
            if not out.get("ne"):
                for spec in FIRE_NE:
                    out["ne"] = place_checked("FireHouse01 NE", "FireHouse01", spec, r30=False)
                    if out["ne"]:
                        break
            if not out.get("sw"):
                out["sw"] = place_checked("FireHouse01 SW", "FireHouse01", FIRE_SW, expect=FIRE_SW_EXPECT)
        log(NAME, "fire", out)
        print("SPENT", sum((r or {}).get("cost") or 0 for r in out.values()))
    elif MODE == "police":
        out = load(NAME).get("police")
        if not out:
            with paused(DEPT):
                out = place_checked("PoliceStation02 SW", "PoliceStation02", POLICE_SW, expect=POLICE_SW_EXPECT)
            log(NAME, "police", out)
        print("SPENT", (out or {}).get("cost") or 0)
    elif MODE == "verify":
        snap = coverage_snapshot()
        base = load(NAME).get("baseline") or {}
        log(NAME, "after", snap)
        for svc in snap:
            print("==", svc)
            print(" before", json.dumps((base.get(svc) or {}).get("summary"))[:400])
            print(" after ", json.dumps(snap[svc]["summary"])[:400])
            print(" worst before", json.dumps((base.get(svc) or {}).get("worst"))[:500])
            print(" worst after ", json.dumps(snap[svc]["worst"])[:500])
        for key in ("shelters", "fire", "police"):
            v = load(NAME).get(key)
            print(key, json.dumps(v if not isinstance(v, dict) else {k: (x or {}).get("placedId") for k, x in v.items()} if key != "police" else (v or {}).get("placedId")))
