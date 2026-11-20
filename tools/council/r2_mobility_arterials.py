"""Council round 2 - Mobility - arterial-hierarchy-phase1.

West Arterial z=900 (Medium -> Large), z=-100 boulevard (Small -> Medium), z=3040 west (Small -> Medium),
roundabouts at arterial junctions. Every edge is dry-run on its own first; only edges whose dry run demolishes
nothing are upgraded (no Housing/Economy consent for demolitions this round), and only in contiguous runs.

Reserved by higher-ranked projects this round (not touched):
  Housing 'Midtown Green' box x -2900..-2145, z -320..-100 -> z=-100 west of x -2145 stays Small;
  Health 'Hearse Garage' box x -2060..-1940, z -215..-80 -> z=-100 edge 738064:81 (x -2024..-1909) stays Small.
So the z=-100 boulevard is x -1909..-1140 this round.

Usage: python -I council/r2_mobility_arterials.py survey|z900|z-100|z3040|roundabouts|trees|verify
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short, upgrade  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "survey"
LOG = r"C:\soft\cityskyline\logs\council\r2_mobility_arterial_hierarchy_phase1.json"


def load():
    try:
        with open(LOG, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def save(d):
    with open(LOG, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=1)


def along(cx, cz, r, zz, x0, x1, prefab=None):
    """Edges lying on the line z=zz between x0 and x1, west to east."""
    segs, off = [], 0
    while True:
        d = get("/nets", x=cx, z=cz, radius=r, limit=200, offset=off)
        segs += d.get("segments", [])
        if not d.get("truncated"):
            break
        off += 200
    out = [s for s in segs if abs(s["start"][1] - zz) < 10 and abs(s["end"][1] - zz) < 10 and "Road" in s["prefab"]
           and min(s["start"][0], s["end"][0]) >= x0 - 1 and max(s["start"][0], s["end"][0]) <= x1 + 1
           and (prefab is None or s["prefab"] == prefab)]
    out.sort(key=lambda s: min(s["start"][0], s["end"][0]))
    return out


def wd(r):
    return [(w.get("id"), w.get("prefab"), w.get("renters")) for w in (r.get("wouldDemolish") or [])]


def dry_each(edges, prefab):
    rows = []
    for s in edges:
        r = upgrade(prefab, [s["id"]], dryRun=True)
        rows.append({"id": s["id"], "x": [round(min(s["start"][0], s["end"][0])), round(max(s["start"][0], s["end"][0]))],
                     "errors": r.get("errors"), "problems": r.get("problems"), "cost": r.get("cost"), "wouldDemolish": wd(r),
                     "error": r.get("error")})
    return rows


CORRIDORS = {
    "z900": dict(geo=(-2270, 900, 1200, 900, -3400, -1137.8), to="Large Road"),
    "z-100": dict(geo=(-1525, -100, 420, -100, -1909, -1140), to="Medium Road"),
    "z3040": dict(geo=(-1855, 3040, 640, 3040, -2463.7, -1249), to="Medium Road"),
}
ROUNDABOUTS = ["395759:1", "379765:1", "600213:23", "395760:1"]

log = load()

if MODE == "survey":
    with paused("mobility"):
        for name, c in CORRIDORS.items():
            edges = [s for s in along(*c["geo"]) if s["prefab"] != c["to"]]
            rows = dry_each(edges, c["to"])
            log[f"survey_{name}"] = rows
            print("==", name, c["to"])
            for r in rows:
                print(r)
    save(log)

elif MODE in CORRIDORS:
    # upgrade the clean edges (dry run again right before), batches of <= 4 contiguous clean edges
    c = CORRIDORS[MODE]
    edges = [s for s in along(*c["geo"]) if s["prefab"] != c["to"]]
    skip = set(sys.argv[2].split(",")) if len(sys.argv) > 2 else set()
    done, refused = [], []
    with paused("mobility"):
        t0 = time.time()
        clean = []
        for s in edges:
            if s["id"] in skip:
                refused.append({"id": s["id"], "reason": "skipped (keeps a contiguous run)"})
                continue
            r = upgrade(c["to"], [s["id"]], dryRun=True)
            if r.get("errors") == 0 and not r.get("wouldDemolish") and not r.get("error"):
                clean.append(s["id"])
            else:
                refused.append({"id": s["id"], "errors": r.get("errors"), "problems": r.get("problems"), "wouldDemolish": wd(r), "error": r.get("error")})
        for i in range(0, len(clean), 4):
            batch = clean[i:i + 4]
            d = upgrade(c["to"], batch, dryRun=True)
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                r = upgrade(c["to"], batch)
                done.append({"batch": batch, **short(r), "newEdgeIds": r.get("newEdgeIds")})
            else:
                refused.append({"batch": batch, "errors": d.get("errors"), "wouldDemolish": wd(d)})
            print(done[-1] if done and done[-1]["batch"] == batch else refused[-1])
        print(f"{MODE}: {time.time() - t0:.0f} s")
    log[f"upgrade_{MODE}"] = {"done": done, "refused": refused}
    save(log)

elif MODE == "roundabouts":
    which = sys.argv[2].split(",") if len(sys.argv) > 2 else ROUNDABOUTS
    dry_only = len(sys.argv) > 3 and sys.argv[3] == "dry"
    out = {}
    with paused("mobility"):
        for node in which:
            res = None
            for size in ("medium", "small"):
                d = post("/roundabout", {"node": node, "size": size, "dryRun": True, "allowDemolish": False})
                row = {"size": size, "prefab": d.get("prefab"), **short(d)}
                print(node, "dry", row)
                out.setdefault(node, []).append(row)
                if d.get("errors") == 0 and not d.get("wouldDemolish") and not d.get("error") and not d.get("blocked"):
                    if not dry_only:
                        r = post("/roundabout", {"node": node, "size": size, "allowDemolish": False})
                        res = {"size": size, "prefab": d.get("prefab"), **short(r), "nodeAfter": r.get("nodeAfter"),
                               "newEdgeIds": r.get("newEdgeIds"), "staleEdgeIds": r.get("staleEdgeIds")}
                        print(node, "built", json.dumps(res)[:600])
                        out[node].append(res)
                    break
    log.setdefault("roundabouts", {}).update(out)
    save(log)

elif MODE == "trees":
    # re-apply street trees (+ grass) on both sides of the upgraded corridors; box selection per corridor
    boxes = {"z900": ([-3405, 885, -1130, 915], "Large Road"), "z-100": ([-1912, -112, -1136, -88], "Medium Road"),
             "z3040": ([-2466, 3028, -1246, 3052], "Medium Road")}
    which = sys.argv[2].split(",") if len(sys.argv) > 2 else list(boxes)
    out = {}
    with paused("mobility"):
        for nm in which:
            box, pf = boxes[nm]
            for up in ("Trees", "Grass"):
                d = post("/roadupgrade", {"box": box, "prefabFilter": pf, "upgrade": up, "side": "both", "dryRun": True})
                if d.get("errors") == 0 and not d.get("wouldDemolish") and not d.get("error"):
                    r = post("/roadupgrade", {"box": box, "prefabFilter": pf, "upgrade": up, "side": "both"})
                else:
                    r = d
                out[f"{nm}_{up}"] = {**short(r), "selection": r.get("selection")}
                print(nm, up, out[f"{nm}_{up}"])
    log.setdefault("trees", {}).update(out)
    save(log)

elif MODE == "verify":
    v = {}
    for name, c in CORRIDORS.items():
        v[name] = [(s["id"], s["prefab"], [round(min(s["start"][0], s["end"][0])), round(max(s["start"][0], s["end"][0]))], list((s.get("upgrades") or {}).keys()))
                   for s in along(*c["geo"])]
        print(name, v[name])
    v["roundabouts"] = get("/roundabouts", x=-2200, z=1500, radius=3000).get("placed")
    print("roundabouts", v["roundabouts"])
    v["lines"] = [{k: l.get(k) for k in ("id", "number", "complete", "flags", "waitingPassengers", "vehicles", "length", "duration")} for l in get("/lines").get("lines", [])]
    for l in v["lines"]:
        print(l)
    log["verify"] = v
    save(log)
