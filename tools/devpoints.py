"""Development points: never let them pile up.

Points come with every milestone. Twice they piled up unspent (91, later 48) because nobody looked, so the
autopilot now calls spend() every few minutes and buys nodes in the order of PLAN. The conductor owns the
plan; the advisory board may propose changes. A node that is not buyable yet (prerequisite missing) is
skipped; when the next buyable node in the plan is too expensive, points are saved up for it instead of being
spent on something further down the list.

CLI: python -I devpoints.py          show points, owned/locked nodes and what the plan would buy
     python -I devpoints.py --spend  buy now
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cc  # noqa: E402

# priority order (2026-11-20). Fossil power (gas, large coal, and nuclear behind them) is left out on purpose:
# clean air is part of the brief, and power is covered by wind, solar, hydro and trade.
PLAN = [
    "InternationalAirportNode",       # trade and tourism: two unused airplane connections
    "RadioTelescopeNode",             # research landmark; leads to the Large Hadron Collider
    "LargeEmergencyShelterNode",      # safety; leads to the early disaster warning system
    "SatelliteUplinkNode",            # telecom landmark
    "SpaceCenterNode",                # landmark, tourism
    "GeologicalResearchStationNode",  # research; with the telescope unlocks the collider
    "LargeHadronColliderNode",
    "IndustrialWasteProcessingNode",
    "EarlyDisasterWarningSystemNode",
    "DesalinationPlantNode",
    "UndergroundParkingNode",
    "AutomatedParkingBuildingNode",
    "GrandBridgeNode",
    "HeliportNode",
]


def state():
    d = cc.get("/devtree")
    nodes = {n["name"]: n for n in d.get("nodes", [])}
    return d.get("points", 0), nodes


def plan_now(points, nodes):
    """Nodes to buy now, in order, with the given points."""
    buy = []
    for name in PLAN:
        n = nodes.get(name)
        if not n or not n.get("locked"):
            continue  # owned or unknown
        if not n.get("buyable"):
            # its prerequisite may be bought earlier in this same pass
            if not all(r in buy or not nodes.get(r, {}).get("locked") for r in n.get("requires") or []):
                continue
        if n["cost"] > points:
            break  # save up for it
        buy.append(name)
        points -= n["cost"]
    return buy, points


def spend(log=print):
    points, nodes = state()
    buy, _ = plan_now(points, nodes)
    bought = []
    for name in buy:
        r = cc.post("/devtree", {"node": name})
        if r.get("purchased"):
            bought.append(name)
            log(f"[devpoints] bought {name} ({r.get('cost')}), {r.get('pointsAfter')} left")
        else:
            log(f"[devpoints] {name}: {json.dumps(r)[:160]}")
            break
    return bought


def main():
    points, nodes = state()
    if "--spend" in sys.argv:
        spend()
        points, nodes = state()
    buy, rest = plan_now(points, nodes)
    locked = [n for n in nodes.values() if n.get("locked")]
    print(f"points {points}; owned {len(nodes) - len(locked)}/{len(nodes)} nodes; locked {len(locked)} "
          f"(total cost {sum(n['cost'] for n in locked)})")
    nxt = next((nodes[p] for p in PLAN if nodes.get(p, {}).get("locked")), None)
    print(f"would buy now: {buy or '-'} (left {rest}); next in plan: {nxt['name'] + ' (' + str(nxt['cost']) + ')' if nxt else 'plan done'}")
    off = [n["name"] for n in locked if n["name"] not in PLAN]
    if off:
        print(f"not in plan: {', '.join(off)}")


if __name__ == "__main__":
    main()
