"""builder-central: sweep my districts for broken zone blocks (zoned residential cells that stay empty).

The Waterfront strip (z 772-820) had a block whose road row was Residential Low (1 cell deep) with Medium
behind it and an unzoned row in between: nothing could grow. This survey reads /map at 8 m and lists clusters
of zoned-but-empty residential cells (lowercase r/m/h/x/l in the map) per district, largest first, with the
zone letters found in the cluster and the rows/columns it spans. Fixes are done per cluster with
/zone mode "road", depth 6, one uniform zone, overwrite only where the dry run removes no buildings.

usage: python -I builders/builder-central_zone-sweep.py [minCells]
"""
import json
import sys
from collections import Counter

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get  # noqa: E402

MIN = int(sys.argv[1]) if len(sys.argv) > 1 else 12
CELL = 8
TILE = 480
DISTRICTS = {
    "Downtown": [-2500, 400, -1100, 2200],
    "Office Park": [-2490, -300, -1120, 380],
    "Waterfront": [-1100, 130, -600, 1500],
    "Lakeshore": [-796, 1504, -320, 2720],
}
EMPTY = set("rmhxl")  # residential zone letters without a building


def grid(x0, z0, x1, z1):
    """Return {(x, z): char} sampled every CELL metres."""
    out = {}
    x = x0
    while x < x1:
        z = z0
        while z < z1:
            cx, cz = x + TILE / 2, z + TILE / 2
            m = get("/map", x=cx, z=cz, size=TILE, cell=CELL)
            txt = m.get("map") or m.get("grid") or ""
            lines = txt.splitlines()[1:]  # first line = x labels
            for line in lines:
                parts = line.split(" ", 1)
                head = line[:7].strip()
                try:
                    rz = float(head)
                except ValueError:
                    continue
                row = line[8:]
                for i, ch in enumerate(row):
                    gx = cx - TILE / 2 + i * CELL
                    if x0 <= gx < x1 and z0 <= rz < z1:
                        out[(round(gx), round(rz))] = ch
            z += TILE
        x += TILE
    return out


BUILT = set("RMHXLCIOSBA:")  # building samples (uppercase = building of that zone, S/B/A, internal paths)
WIN = 32  # a lot of up to 8x8 cells has a building sample within this many metres


def empty_cells(g):
    """Lowercase residential samples with no building sample within WIN metres (in /map a built lot shows
    lowercase around its uppercase centre, so lowercase alone does not mean empty)."""
    built = [k for k, v in g.items() if v in BUILT]
    idx = {}
    for k in built:
        idx.setdefault((k[0] // 64, k[1] // 64), []).append(k)
    out = set()
    for k, v in g.items():
        if v not in EMPTY:
            continue
        near = False
        for bx in (k[0] // 64 - 1, k[0] // 64, k[0] // 64 + 1):
            for bz in (k[1] // 64 - 1, k[1] // 64, k[1] // 64 + 1):
                for b in idx.get((bx, bz), ()):
                    if abs(b[0] - k[0]) <= WIN and abs(b[1] - k[1]) <= WIN:
                        near = True
                        break
                if near:
                    break
            if near:
                break
        if not near:
            out.add(k)
    return out


def unzoned_frontage(g):
    """Owned, unzoned land ('.') within 24 m of a road ('#') and with no building sample within WIN metres."""
    roads = {k for k, v in g.items() if v == "#"}
    built = {k for k, v in g.items() if v in BUILT or v in "Hp~^T"}
    out = set()
    for k, v in g.items():
        if v != ".":
            continue
        near_road = any((k[0] + dx, k[1] + dz) in roads for dx in (-24, -16, -8, 0, 8, 16, 24)
                        for dz in (-24, -16, -8, 0, 8, 16, 24))
        if not near_road:
            continue
        if any((k[0] + dx, k[1] + dz) in built for dx in range(-WIN, WIN + 1, 8) for dz in range(-WIN, WIN + 1, 8)):
            continue
        out.add(k)
    return out


def clusters(g, keyfn=None):
    seen = set()
    res = []
    keys = (keyfn or empty_cells)(g)
    for k in keys:
        if k in seen:
            continue
        stack, members = [k], []
        seen.add(k)
        while stack:
            c = stack.pop()
            members.append(c)
            for dx, dz in ((CELL, 0), (-CELL, 0), (0, CELL), (0, -CELL)):
                n = (c[0] + dx, c[1] + dz)
                # tolerate rounding of the sampled coordinates (+-1 m)
                for nn in (n, (n[0] + 1, n[1]), (n[0] - 1, n[1]), (n[0], n[1] + 1), (n[0], n[1] - 1)):
                    if nn in keys and nn not in seen:
                        seen.add(nn)
                        stack.append(nn)
                        break
        if len(members) >= MIN:
            xs = [m[0] for m in members]
            zs = [m[1] for m in members]
            res.append({"cells": len(members), "bbox": [min(xs), min(zs), max(xs), max(zs)],
                        "zones": dict(Counter(g[m] for m in members))})
    return sorted(res, key=lambda r: -r["cells"])


if __name__ == "__main__":
    for name, b in DISTRICTS.items():
        g = grid(*b)
        cs = clusters(g)
        print(f"== {name}: {len(g)} samples, {len(empty_cells(g))} empty residential, "
              f"{len(cs)} clusters >= {MIN}")
        for c in cs[:15]:
            print("  ", json.dumps(c))
        us = clusters(g, unzoned_frontage)
        print(f"   unzoned frontage clusters >= {MIN}: {len(us)}")
        for c in us[:12]:
            print("    u", json.dumps(c))
