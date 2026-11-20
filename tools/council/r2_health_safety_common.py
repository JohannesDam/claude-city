"""Shared helpers for the Health & Safety council crew (round 2).

Import from a council script with:
    sys.path.insert(0, r"C:\\soft\\cityskyline\\tools\\council"); from r2_health_safety_common import *
"""
import json
import math
import os
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, building, short  # noqa: E402,F401

sys.stdout.reconfigure(encoding="utf-8")
DEPT = "health_safety"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "blocked",
        "wouldDemolish", "pos", "facing", "lot", "slot", "id", "placedIds")
LOGDIR = r"C:\soft\cityskyline\logs\council"
LEDGER = os.path.join(LOGDIR, "sites.jsonl")
ANGLE = {(0, 1): 0, (1, 0): 90, (0, -1): 180, (-1, 0): -90}


def show(label, r):
    d = short(r or {}, keys=KEYS)
    if d.get("wouldDemolish"):
        d["wouldDemolish"] = [(w.get("id"), w.get("prefab"), w.get("renters")) for w in d["wouldDemolish"]]
    print(f"{label:<44} {json.dumps(d)}", flush=True)
    return r


def log(project, key, val):
    path = os.path.join(LOGDIR, f"r2_health_safety_{project}.json")
    try:
        d = json.load(open(path, encoding="utf-8"))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(path, "w", encoding="utf-8"), indent=1)
    return d


def load(project):
    try:
        return json.load(open(os.path.join(LOGDIR, f"r2_health_safety_{project}.json"), encoding="utf-8"))
    except Exception:
        return {}


def footprint(pos, lot, facing):
    """Axis-aligned box (x0, x1, z0, z1) of a lot; lot = [width, depth] in 8 m cells."""
    w, d = lot[0] * 8, lot[1] * 8
    fx, fz = facing
    if abs(fx) > abs(fz):  # facing east/west: depth runs along x
        w, d = d, w
    return [round(pos[0] - w / 2), round(pos[0] + w / 2), round(pos[1] - d / 2), round(pos[1] + d / 2)]


def ledger(project, box, what, ids):
    line = {"round": 2, "department": DEPT, "project": project, "x0": box[0], "x1": box[1], "z0": box[2],
            "z1": box[3], "what": what, "ids": ids}
    with open(LEDGER, "a", encoding="utf-8") as f:
        f.write(json.dumps(line) + "\n")
    print("ledger +", json.dumps(line), flush=True)


def ledger_boxes(project=None):
    out = []
    for ln in open(LEDGER, encoding="utf-8"):
        ln = ln.strip()
        if ln:
            d = json.loads(ln)
            if project is None or d.get("project") != project:
                out.append(d)
    return out


def overlaps(box, others, margin=0):
    x0, x1, z0, z1 = box
    hits = []
    for o in others:
        if x0 < o["x1"] + margin and x1 > o["x0"] - margin and z0 < o["z1"] + margin and z1 > o["z0"] - margin:
            hits.append(o)
    return hits


def exists(bid, near, radius=40):
    return any(b["id"] == bid for b in get("/buildings", kind="all", x=near[0], z=near[1], radius=radius).get("buildings") or [])


def ext_proxy(prefab_full, pos, facing):
    """wouldDemolish of an extension footprint: /extension dry runs do not report zoned buildings they would
    replace, so place the same prefab as a plain building (dry run) at the slot position and read it there."""
    r = post("/building", {"prefab": prefab_full, "x": pos[0], "z": pos[1], "angle": ANGLE[tuple(facing)],
                           "dryRun": True})
    return r.get("wouldDemolish") or [], r


def ext_scan(owner, prefab, sides=("back", "left", "right"), offsets=(0, -8, 8, -16, 16, -24, 24), forbid=(),
             quiet=False):
    """First (side, offset, dry result) whose extension dry run is clean and whose footprint demolishes no
    zoned building. Returns None when nothing fits."""
    for side in sides:
        if side in forbid:
            continue
        for off in offsets:
            body = {"owner": owner, "prefab": prefab, "side": side, "dryRun": True}
            if off:
                body["offset"] = off
            r = post("/extension", body, timeout=120)
            if r.get("error") or r.get("errors"):
                if not quiet:
                    print(f"   {owner} {prefab} {side}{off:+d}: {r.get('error') or r.get('problems')}", flush=True)
                continue
            wd, _ = ext_proxy(r["prefab"], r["pos"], r["facing"])
            if wd:
                if not quiet:
                    print(f"   {owner} {prefab} {side}{off:+d}: would demolish {[(w['id'], w['prefab']) for w in wd]}",
                          flush=True)
                continue
            return side, off, r
    return None


def ext_build(owner, prefab, side=None, offset=0):
    body = {"owner": owner, "prefab": prefab}
    if side:
        body["side"] = side
    if offset:
        body["offset"] = offset
    r = post("/extension", body, timeout=120)
    if r.get("applied"):
        time.sleep(1.0)
    return r


def installed(owner):
    e = get("/extensions", owner=owner)
    return [i.get("name") if isinstance(i, dict) else i for i in e.get("installed") or []], e
