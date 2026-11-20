"""Troubleshooter helper: is an extension slot clean (no zoned buildings under it)?

POST /extension does not report demolition yet. This helper looks at the slot's square footprint and
reports (1) zone cells / buildings on the ASCII map inside it and (2) zoned buildings whose estimated lot
(from the _WxD suffix of the prefab name, both orientations) overlaps it.

Usage as a module:  from troubleshooter_extcheck import slot_report
CLI:  python -I builders/troubleshooter_extcheck.py <owner> "<extension prefab>"
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import get  # noqa: E402

ZONE_CHARS = set("rmhciox")


def lot_of(prefab):
    m = re.search(r"_(\d+)x(\d+)$", prefab or "")
    return (int(m.group(1)) * 8, int(m.group(2)) * 8) if m else None


def rect_overlap(a, b):
    return a[0] < b[1] and b[0] < a[1] and a[2] < b[3] and b[2] < a[3]


def footprint_hits(cx, cz, half_x, half_z, shrink=2.0):
    """Zone/building characters of the /map grid inside the footprint (shrunk a little)."""
    size = int(2 * max(half_x, half_z) + 24)
    m = get("/map", x=round(cx), z=round(cz), size=size, cell=4)
    x0, z0, cell, rows = m["x0"], m["z0"], m["cell"], m["grid"].splitlines()[1:]
    hits = []
    for line in rows:
        parts = line.split(None, 1)
        if len(parts) < 2:
            continue
        zc = float(parts[0])
        cells = line[8:] if len(line) > 8 else ""  # the row label takes 8 characters
        for i, ch in enumerate(cells):
            xc = x0 + i * cell + cell / 2
            if abs(xc - cx) <= half_x - shrink and abs(zc - cz) <= half_z - shrink:
                if ch.lower() in ZONE_CHARS or ch in "SBA#H":
                    hits.append((ch, round(xc), round(zc)))
    return hits


def buildings_hit(cx, cz, half_x, half_z, owner_ids=()):
    fp = (cx - half_x, cx + half_x, cz - half_z, cz + half_z)
    r = get("/buildings", kind="all", x=round(cx), z=round(cz), radius=int(max(half_x, half_z) + 45))
    out = []
    for b in r.get("buildings", []):
        if b["id"] in owner_ids or b.get("service"):
            continue
        lot = lot_of(b["prefab"])
        if not lot:
            continue
        bx, bz = b["pos"]
        # both orientations; a hit in the narrower one (min of both) is a sure overlap, the other "maybe"
        hits = []
        for w, d in (lot, lot[::-1]):
            rb = (bx - w / 2 + 1, bx + w / 2 - 1, bz - d / 2 + 1, bz + d / 2 - 1)
            hits.append(rect_overlap(fp, rb))
        if all(hits):
            out.append(("SURE", b["id"], b["prefab"], b["pos"]))
        elif any(hits):
            out.append(("maybe", b["id"], b["prefab"], b["pos"]))
    return out


def slot_report(owner, prefab, quiet=False):
    info = get("/extensions", owner=owner)
    off = next((o for o in info.get("offered", []) if o["name"] == prefab), None)
    if not off:
        return {"error": "not offered", "owner": owner, "prefab": prefab}
    lot = off.get("lot") or [0, 0]
    res = []
    for s in off.get("sides", []):
        cx, cz = s["pos"]
        # the extension's width runs along the owner side, depth outwards; squares are symmetric
        w, d = lot[0] * 8 / 2, lot[1] * 8 / 2
        side_axis_x = abs(info["facing"][0]) < 0.5  # facing along z: front/back sides run along x
        if s["side"] in ("left", "right"):
            side_axis_x = not side_axis_x
        hx, hz = (w, d) if side_axis_x else (d, w)
        mh = footprint_hits(cx, cz, hx, hz)
        bh = buildings_hit(cx, cz, hx, hz, owner_ids={owner})
        clean = not [h for h in mh if h[0] != "."] and not [b for b in bh if b[0] == "SURE"]
        res.append({"side": s["side"], "pos": s["pos"], "half": [hx, hz], "mapHits": mh[:12], "nMapHits": len(mh),
                    "buildings": bh, "clean": clean})
        if not quiet:
            print(f"  {s['side']:5} {s['pos']} half={hx:.0f}x{hz:.0f} clean={clean} map={len(mh)} {mh[:6]} bld={bh[:4]}")
    return {"owner": owner, "prefab": prefab, "cost": off.get("cost"), "canBuild": off.get("canBuild"), "slots": res}


if __name__ == "__main__":
    print(json.dumps(slot_report(sys.argv[1], sys.argv[2], quiet=True), indent=1))
