"""Opportunity scan: what the city could use but doesn't (yet).

Read-only. Run before every advisory round and after every milestone, so the city keeps growing in kind
(new transport modes, trade, landmarks), not only in size:
  - unspent development points and buyable dev-tree nodes;
  - unlocked service/transport buildings that were never built (grouped by service);
  - transport modes: unlocked line types vs lines in use, outside connections (ship/air/train/road)
    and whether a matching terminal exists;
  - unlocks that only wait for "build X first" (objectBuilt requirements);
  - zoning mix (share of low-density homes, high-density, offices) as a growth-in-kind signal.

Usage: python -I opportunities.py [--json]
"""
import json
import os
import sys
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cc  # noqa: E402

TERMINALS = {  # outside connection kind -> prefab name fragments of a terminal that uses it
    "Ship": ("Harbor",),
    "Airplane": ("Airport",),
    "Train": ("TrainStation", "CargoTrainTerminal"),
}


def service_buildings():
    out, off = [], 0
    while True:
        r = cc.get("/buildings", kind="service", offset=off, limit=500)
        out += r.get("buildings") or []
        if not r.get("truncated"):
            return out, r
        off += 500


def scan():
    rep = {}
    dt = cc.get("/devtree")
    buyable = sorted(((n["name"], n["cost"]) for n in dt.get("nodes", []) if n.get("locked") and n.get("buyable")), key=lambda x: x[1])
    rep["devPoints"] = dt.get("points")
    rep["buyableNodes"] = [{"node": n, "cost": c} for n, c in buyable]

    blds, head = service_buildings()
    built = Counter(b["prefab"] for b in blds)
    prefabs = cc.get("/prefabs", category="services")
    items = prefabs.get("prefabs") if isinstance(prefabs, dict) else prefabs
    never = defaultdict(list)
    for p in items or []:
        if not p.get("locked") and built[p["name"]] == 0:
            never[p.get("service") or p.get("group") or "?"].append(p["name"])
    rep["unlockedNeverBuilt"] = {k: sorted(v) for k, v in sorted(never.items())}

    tp = cc.get("/transitprefabs")
    lines = cc.get("/lines", type="all")
    in_use = Counter((l.get("type") or l.get("kind")) for l in (lines.get("lines") if isinstance(lines, dict) else lines) or [])
    rep["transitLines"] = [{"line": l["name"], "type": l["type"], "unlocked": not l.get("locked"), "linesInUse": in_use.get(l["type"], 0)}
                           for l in tp.get("lines", [])]

    ai = cc.get("/areainfo")
    oc = Counter()
    for c in ai.get("outsideConnections", []):
        kind = c["prefab"].split(" Outside")[0]
        oc[kind] += 1
    rep["outsideConnections"] = dict(oc)
    rep["unusedConnections"] = [k for k, frags in TERMINALS.items()
                                if oc.get(k) and not any(any(f in name for f in frags) for name in built)]

    waits = []
    for l in tp.get("lines", []):
        if not l.get("locked"):
            continue
        info = cc.get("/unlockinfo", name=l["name"])

        def leaves(n):
            req = n.get("requires") or []
            if not req:
                yield n
            for r in req:
                if r.get("locked"):
                    yield from leaves(r)
        for p in (info.get("prefabs") or [])[:1]:
            for leaf in leaves(p):
                waits.append({"unlocks": l["name"], "needs": leaf.get("name"), "kind": leaf.get("kind")})
    rep["waitingOn"] = waits

    z = head.get("zonedByZone") or {}
    tot = sum(z.values()) or 1
    rep["zoningMix"] = {
        "buildings": tot,
        "lowDensityHomesPct": round(100 * sum(v for k, v in z.items() if "Residential Low" in k) / tot, 1),
        "highDensityHomesPct": round(100 * sum(v for k, v in z.items() if "Residential High" in k or "Mixed" in k) / tot, 1),
        "officePct": round(100 * sum(v for k, v in z.items() if "Office" in k) / tot, 1),
    }
    return rep


def main():
    rep = scan()
    if "--json" in sys.argv:
        print(json.dumps(rep, indent=1))
        return
    print(f"dev points unspent: {rep['devPoints']}  buyable: " + ", ".join(f"{n['node']}({n['cost']})" for n in rep["buyableNodes"]))
    print("outside connections:", rep["outsideConnections"], " unused (no terminal):", rep["unusedConnections"])
    print("transit:", ", ".join(f"{t['line']}={'on' if t['unlocked'] else 'locked'}/{t['linesInUse']}" for t in rep["transitLines"]))
    for w in rep["waitingOn"]:
        print(f"  {w['unlocks']} waits on {w['kind']} {w['needs']}")
    print("zoning mix:", rep["zoningMix"])
    print("unlocked but never built:")
    for svc, names in rep["unlockedNeverBuilt"].items():
        print(f"  {svc}: {', '.join(names)}")


if __name__ == "__main__":
    main()
