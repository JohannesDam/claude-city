"""Road-network connectivity audit for the TrafficPenalty hunt.

TrafficPenalty (happiness) comes from Citizen.m_PenaltyCounter, set to 255 by TripNeededSystem when a GoingHome or
MovingAway trip finds no path within the trip's max cost (TripPriorityParametersData.GetMaxCost), and by
CitizenTravelPurposeSystem for citizens stuck without road access. So disconnected islands, one-way traps and very
long detours all feed it.

This script pulls every net segment (GET /nets, paged), builds the road graph (one-way prefabs directed start->end)
and reports:
  - undirected components (islands not connected to the main network),
  - strongly connected components of the directed graph (areas you can enter but not leave, or the reverse),
  - dead-end nodes (degree 1) count.
Output: a summary on stdout and the raw graph cached in the scratch file given by --cache (optional).

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_netgraph.py
"""
import json
import sys
from collections import defaultdict

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get  # noqa: E402

NON_ROAD = ("Track", "Pipe", "Line", "Pipeline", "Cable", "Fence", "Path", "Quay", "Wall", "Lane", "Tram Track",
            "Train", "Subway")


def is_road(seg):
    if not seg.get("road"):
        return False
    p = seg["prefab"]
    return not any(k in p for k in ("Pipe", "Power", "High-voltage", "Fence", "Pedestrian", "Bicycle"))


def fetch_all():
    segs, off = [], 0
    while True:
        r = get("/nets", x=0, z=0, radius=20000, offset=off, limit=5000)
        segs.extend(r.get("segments", []))
        if not r.get("truncated"):
            break
        off = r["nextOffset"]
    return segs


def main():
    cache = None
    if "--cache" in sys.argv:
        cache = sys.argv[sys.argv.index("--cache") + 1]
    segs = fetch_all()
    roads = [s for s in segs if is_road(s)]
    prefabs = defaultdict(int)
    for s in roads:
        prefabs[s["prefab"]] += 1
    print("segments", len(segs), "road segments", len(roads))
    print("road prefabs", dict(sorted(prefabs.items(), key=lambda t: -t[1])))
    pos = {}
    und = defaultdict(set)
    out = defaultdict(set)
    inn = defaultdict(set)
    for s in roads:
        a, b = s["startNode"], s["endNode"]
        pos[a] = s["start"]
        pos[b] = s["end"]
        und[a].add(b)
        und[b].add(a)
        oneway = "Oneway" in s["prefab"] or "One-way" in s["prefab"] or "oneway" in s["prefab"].lower()
        out[a].add(b)
        inn[b].add(a)
        if not oneway:
            out[b].add(a)
            inn[a].add(b)
    # undirected components
    seen, comps = set(), []
    for n in und:
        if n in seen:
            continue
        stack, comp = [n], []
        seen.add(n)
        while stack:
            u = stack.pop()
            comp.append(u)
            for v in und[u]:
                if v not in seen:
                    seen.add(v)
                    stack.append(v)
        comps.append(comp)
    comps.sort(key=len, reverse=True)
    print("undirected components", len(comps), "sizes", [len(c) for c in comps[:12]])
    for c in comps[1:25]:
        xs = [pos[n][0] for n in c]
        zs = [pos[n][1] for n in c]
        print("  island nodes", len(c), "bbox x", round(min(xs)), round(max(xs)), "z", round(min(zs)), round(max(zs)))
    # SCC (iterative Kosaraju) on the main component
    main_nodes = set(comps[0])
    order, visited = [], set()
    for n in main_nodes:
        if n in visited:
            continue
        stack = [(n, iter(out[n]))]
        visited.add(n)
        while stack:
            u, it = stack[-1]
            nxt = next(it, None)
            if nxt is None:
                stack.pop()
                order.append(u)
            elif nxt not in visited and nxt in main_nodes:
                visited.add(nxt)
                stack.append((nxt, iter(out[nxt])))
    comp_of, sccs = {}, []
    for n in reversed(order):
        if n in comp_of:
            continue
        cid = len(sccs)
        stack, members = [n], []
        comp_of[n] = cid
        while stack:
            u = stack.pop()
            members.append(u)
            for v in inn[u]:
                if v in main_nodes and v not in comp_of:
                    comp_of[v] = cid
                    stack.append(v)
        sccs.append(members)
    sccs.sort(key=len, reverse=True)
    print("strongly connected components in main network", len(sccs), "largest", len(sccs[0]))
    big = set(sccs[0])

    def reach(starts, adj):
        seen = set(starts)
        stack = list(starts)
        while stack:
            u = stack.pop()
            for v in adj[u]:
                if v not in seen:
                    seen.add(v)
                    stack.append(v)
        return seen

    from_main = reach(big, out)   # nodes you can drive to from the main network
    to_main = reach(big, inn)     # nodes from which you can drive to the main network
    traps = [n for n in main_nodes if n not in to_main]
    unreachable = [n for n in main_nodes if n not in from_main]
    print("trap nodes (enterable, no way back to the main network):", len(traps))
    for n in traps[:30]:
        print("   ", n, pos[n], "deg", len(und[n]))
    print("unreachable nodes (cannot be driven to from the main network):", len(unreachable))
    for n in unreachable[:30]:
        print("   ", n, pos[n], "deg", len(und[n]))
    dead = [n for n in und if len(und[n]) == 1]
    print("dead-end nodes", len(dead))
    if cache:
        json.dump({"roads": roads}, open(cache, "w"))


if __name__ == "__main__":
    main()
