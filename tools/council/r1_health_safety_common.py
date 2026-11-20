"""Shared helpers for the Health & Safety council crew (round 1).

Import from a council script with:
    sys.path.insert(0, r"C:\\soft\\cityskyline\\tools\\council"); from r1_health_safety_common import *
"""
import json
import math
import os
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, building, short  # noqa: E402,F401

sys.stdout.reconfigure(encoding="utf-8")
DEPT = "health_safety"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "pos", "facing", "lot")
LOGDIR = r"C:\soft\cityskyline\logs\council"
RESIDENTIAL = ("Residential", "Mixed")
ZONED = ("Residential", "Mixed", "Commercial", "Industrial", "Office")


def show(label, r):
    print(f"{label:<40} {json.dumps(short(r or {}, keys=KEYS))}", flush=True)
    return r


def log(name, key, val):
    path = os.path.join(LOGDIR, f"r1_health_safety_{name}.json")
    try:
        d = json.load(open(path, encoding="utf-8"))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(path, "w", encoding="utf-8"), indent=1)
    return d


def load(name):
    try:
        return json.load(open(os.path.join(LOGDIR, f"r1_health_safety_{name}.json"), encoding="utf-8"))
    except Exception:
        return {}


def footprint(pos, lot, facing):
    """Axis-aligned box (x0, x1, z0, z1) of a placed lot; lot = [width, depth] in 8 m cells."""
    w, d = lot[0] * 8, lot[1] * 8
    fx, fz = facing
    if abs(fx) > abs(fz):  # facing east/west: depth runs along x
        w, d = d, w
    return (pos[0] - w / 2, pos[0] + w / 2, pos[1] - d / 2, pos[1] + d / 2)


def buildings_in_box(box, margin=6.0, kind="all"):
    """Buildings whose centre lies inside the box grown by `margin` (zoned houses are small, so a centre
    test plus margin catches every overlap that matters)."""
    x0, x1, z0, z1 = box
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    r = math.hypot(x1 - x0, z1 - z0) / 2 + margin + 30
    out = []
    for b in get("/buildings", kind=kind, x=cx, z=cz, radius=r).get("buildings") or []:
        x, z = b["pos"]
        if x0 - margin <= x <= x1 + margin and z0 - margin <= z <= z1 + margin:
            out.append(b)
    return out


def zoned_near(x, z, radius, kinds=ZONED):
    """Zoned (non-service) buildings near (x, z) whose prefab name matches one of `kinds`."""
    bs = get("/buildings", kind="all", x=x, z=z, radius=radius).get("buildings") or []
    return [b for b in bs if not b.get("service") and any(k in (b.get("prefab") or "") for k in kinds)]


def find_candidates(prefab, x, z, radius=200, n=8):
    """Raw /findspot candidates (each has x, z, angle, corners, building)."""
    return get("/findspot", prefab=prefab, x=x, z=z, radius=min(radius, 1000), n=n).get("candidates") or []


def dry_ok(r, expect=None, tol=12.0):
    if not r or r.get("error") or r.get("errors") not in (0,):
        return False
    if expect and r.get("pos"):
        return math.hypot(r["pos"][0] - expect[0], r["pos"][1] - expect[1]) <= tol
    return True


def place(label, prefab, spec, expect=None, tol=12.0, margin=6.0, dry=False):
    """Dry run, check the footprint is free of buildings, then build (unless dry). Returns the build
    result (or the dry-run result when dry) or None. Call it while holding paused()."""
    d = show(f"{label} (dry)", building(prefab, **spec, dryRun=True))
    if not dry_ok(d, expect, tol):
        print(f"  -> skip: dry run not clean or moved away from {expect}", flush=True)
        return None
    box = footprint(d["pos"], d["lot"], d.get("facing") or [0, 1])
    hits = buildings_in_box(box, margin)
    if hits:
        print(f"  -> skip: {len(hits)} building(s) inside footprint {tuple(round(v) for v in box)}: "
              f"{[(h['prefab'], h['pos']) for h in hits[:5]]}", flush=True)
        return None
    if dry:
        print(f"  -> footprint {tuple(round(v) for v in box)} free", flush=True)
        return d
    r = show(label, building(prefab, **spec))
    return r if r.get("applied") else None
