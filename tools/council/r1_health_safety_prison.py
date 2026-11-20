"""Council round 1 - Health & Safety - prison.

Planned lot (-1051.5,-1504.9) east of the x=-1156 road is gone: Public Works' lakeside energy park filled it
with wind turbines (x=-989.5) and a battery station (-1107.5,-1555.6) earlier this round. New site in the same
SE utilities zone, south of their z=-1780 link road: Prison01 at (-1144.1,-1884.5) facing north (lot
x -1248..-1040, z -1980..-1789), south-southeast of the incinerator (-1272,-1592), so its plume (SW->NE wind)
blows away from the prison; no homes within 700 m.

Usage: python -I council/r1_health_safety_prison.py check|build|verify
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools\council")
from r1_health_safety_common import (get, paused, show, log, place, zoned_near, find_candidates,  # noqa: E402
                                     DEPT)

MODE = sys.argv[1] if len(sys.argv) > 1 else "check"
PREFAB = "Prison01"
PLAN = dict(edge="875595:93", t=0.15, side="right", gap=0.5)
EXPECT = (-1144.1, -1884.5)
ZONE = (-1560, -930, -2180, -1400)  # SE utilities bbox (x0, x1, z0, z1): stay inside it


def in_zone(c):
    xs = [p[0] for p in c["corners"]]
    zs = [p[1] for p in c["corners"]]
    return ZONE[0] <= min(xs) and max(xs) <= ZONE[1] and ZONE[2] <= min(zs) and max(zs) <= ZONE[3]


def specs():
    """Planned spec first, then fresh /findspot candidates inside the SE utilities zone."""
    out = [(PLAN, EXPECT)]
    for c in find_candidates(PREFAB, EXPECT[0], EXPECT[1], radius=250, n=8):
        if in_zone(c):
            b = dict(c["building"])
            b.pop("prefab", None)
            if b != PLAN:
                out.append((b, (c["x"], c["z"])))
    return out


def homes_within(x, z, r):
    return len(zoned_near(x, z, r, kinds=("Residential", "Mixed")))


if __name__ == "__main__":
    if MODE in ("check", "build"):
        res = None
        with paused(DEPT):
            for spec, exp in specs()[:4]:
                if homes_within(exp[0], exp[1], 500):
                    print(f"skip {spec}: homes within 500 m")
                    continue
                res = place(f"Prison01 {spec['edge']} t={spec['t']}", PREFAB, spec, expect=exp, dry=(MODE == "check"))
                if res:
                    break
        if MODE == "build":
            log("prison", "build", res)
            print("SPENT", (res or {}).get("cost") if (res or {}).get("applied") else 0)
    elif MODE == "verify":
        p = get("/services", kind="police")["kinds"]["police"]
        print(json.dumps(p["totals"]))
        print(json.dumps(p.get("demand")))
        print(p.get("diagnosis"))
        for b in p["buildings"]:
            if b.get("type") != "station" or "prison" in json.dumps(b).lower():
                print(json.dumps(b)[:700])
        for b in p["buildings"]:
            print(b["id"], b["prefab"], b["pos"], b.get("status"), b.get("flags"), b.get("jailed"))
