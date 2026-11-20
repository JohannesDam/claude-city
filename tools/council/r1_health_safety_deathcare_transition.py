"""Council round 1 - Health & Safety - deathcare-transition.

Both planned crematorium lots were taken earlier this round by Education:
  - A (-772.6,2208.5): HighSchool01 1552809:15 at (-830,2232.5) now covers it (edge 752399:9 no longer exists).
  - B (-2001.6,-51.5): ElementarySchool01 548208:43 at (-2040.2,-59.5).
Replacements (both footprints free of any building; no homes within 60 m):
  - A': (-999.4,1552.5) facing south onto the z=1505 Medium Road (edge 302977:1), east of the highway, between
        the downtown-east and East-lakeside hearse hotspots (~485 m from (-704,1930)).
  - B': (-2001.6,-148.5) on the same office-park road edge 302816:1 as planned, south side (facing north).
Cemetery rebuilds as approved: bulldoze full Cemetery02 526398:45 and 339953:1 and rebuild each on its own lot
(findspot candidate within 10 m of the old position only; skip otherwise).

Usage: python -I council/r1_health_safety_deathcare_transition.py check|crem|cem|verify
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools\council")
from r1_health_safety_common import (get, post, paused, show, log, load, place, zoned_near,  # noqa: E402
                                     find_candidates, DEPT)

MODE = sys.argv[1] if len(sys.argv) > 1 else "check"
CREM = [
    ("crem_A", dict(edge="302977:1", t=0.471, side="left", gap=0.5), (-999.4, 1552.5)),
    ("crem_B", dict(edge="302816:1", t=0.263, side="left", gap=0.5), (-2001.6, -148.5)),
]
CEMS = [("cem_north", "526398:45", (-1799.0, 2567.5)), ("cem_lakeside", "339953:1", (-1103.5, -9.3))]
# exact-position specs (edge mode, so the lot fronts the same road): used when /findspot has nothing within 10 m.
# cem_north: findspot's nearest candidates were 11.2 / 20.3 m off; t=0.04 on the same edge reproduces the old lot.
EXACT = {"cem_north": dict(edge="612342:15", t=0.04, side="left", gap=0.5)}
HOMES = ("Residential", "Mixed", "Commercial", "Industrial")  # plan rule: none within 60 m (offices allowed)


def crematoria(dry):
    out = {}
    for key, spec, exp in CREM:
        near = zoned_near(exp[0], exp[1], 60, kinds=HOMES)
        if near:
            print(f"{key}: skip, zoned buildings within 60 m: {[(b['prefab'], b['pos']) for b in near[:4]]}")
            out[key] = None
            continue
        with paused(DEPT):
            out[key] = place(f"Crematorium01 {key}", "Crematorium01", spec, expect=exp, dry=dry)
    return out


def cemetery(key, cid, old):
    """Bulldoze one full Cemetery02 and rebuild it on its own lot (one short paused batch)."""
    with paused(DEPT):
        cur = [b for b in get("/buildings", kind="all", x=old[0], z=old[1], radius=15).get("buildings") or []
               if b["id"] == cid]
        if cur:
            show(f"bulldoze {cid}", post("/bulldoze", {"ids": [cid]}))
        else:
            left = get("/buildings", kind="all", x=old[0], z=old[1], radius=20).get("buildings") or []
            if left:
                print(f"{key}: {cid} gone and the lot holds {[(b['id'], b['prefab']) for b in left]} - skipped")
                return None
            print(f"{key}: {cid} already bulldozed, lot empty - rebuilding")
        cands = find_candidates("Cemetery02", old[0], old[1], radius=60, n=8)
        cands = [c for c in cands if math.hypot(c["x"] - old[0], c["z"] - old[1]) <= 10]
        print(f"{key}: {len(cands)} candidate(s) within 10 m: {[(round(c['x'], 1), round(c['z'], 1), c['angle']) for c in cands]}")
        specs = []
        for c in cands:
            b = dict(c["building"])
            b.pop("prefab", None)
            specs.append((b, (c["x"], c["z"])))
        if key in EXACT:
            specs.append((EXACT[key], old))
        for b, exp in specs:
            r = place(f"Cemetery02 {key}", "Cemetery02", b, expect=exp, tol=3, margin=4)
            if r:
                return r
        print(f"{key}: no rebuild on the old lot - left empty (skip rule)")
        return None


def deathcare():
    d = get("/services", kind="deathcare")["kinds"]["deathcare"]
    return d


if __name__ == "__main__":
    if MODE == "check":
        crematoria(dry=True)
        for key, cid, old in CEMS:
            b = [x for x in get("/buildings", kind="all", x=old[0], z=old[1], radius=15).get("buildings") or []]
            print(key, cid, [(x["id"], x["prefab"], x["pos"]) for x in b])
    elif MODE == "crem":
        res = crematoria(dry=False)
        log("deathcare_transition", "crematoria", res)
        print("SPENT", sum((r or {}).get("cost") or 0 for r in res.values()))
    elif MODE == "cem":
        prev = load("deathcare_transition").get("cemeteries") or {}
        res = {}
        for key, cid, old in CEMS:
            res[key] = prev.get(key) or cemetery(key, cid, old)
        log("deathcare_transition", "cemeteries", res)
        print("SPENT", sum((r or {}).get("cost") or 0 for r in res.values()))
    elif MODE == "verify":
        d = deathcare()
        print(json.dumps(d["totals"]))
        print(json.dumps(d.get("demand")))
        print(d.get("diagnosis"))
        for b in d["buildings"]:
            print(b["id"], b["prefab"], b["pos"], b.get("status"), json.dumps({k: b.get(k) for k in ("bodies", "vehicles", "processing")}))
        print(json.dumps(get("/hotspots", type="Hearse", cell=300).get("hotspots"))[:1500])
