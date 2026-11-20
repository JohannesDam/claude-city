"""Council round 2 - Economic Affairs - ne-stone-works (relocated: 'South Stone Works').

The approved NE site (x -935..-440, z 3420..4050) is reserved this round by Utilities' higher-ranked North Wind
Gate (box x -935..-436, z 3395..4045), so the quarry and the mine move to the second-richest owned ore body: the
empty owned land south of the old industry, between the cargo terminal / landfills and the SE utilities zone
(x -1990..-1570, z -2175..-1500). No homes lie NE (downwind) of it - only the SE energy park and the lake -,
the CargoTrainTerminal01 is next door for stone/ore exports, and the land is outside every protected zone.

Layout: a tree-lined Medium Road 'Quarry Lane' from the existing 3-way junction (-1689,-1330) on the z=-1330
Small Road south between the two landfills, curving east at the bottom; the Stone quarry hub on its west side
with the work area behind it (x -1990..-1850), the Ore mine hub on the south side of the bottom bend with its
work area to the south (z -1995..-2175).

Usage: python -I council/r2_economy_ne_stone_works.py check|road|stone|ore|verify
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
sys.path.insert(0, r"C:\soft\cityskyline\tools\council")
from cc import get, post, paused, building, short  # noqa: E402
from r2_economy_common import DEPT, Log, show, ledger_overlaps  # noqa: E402

MODE = sys.argv[1] if len(sys.argv) > 1 else "check"
PID = "ne-stone-works"
LOG = Log(r"C:\soft\cityskyline\logs\council\r2_economy_ne_stone_works.json")
LANE = [[-1689, -1330], [-1700, -1470], [-1735, -1600], [-1760, -1720], [-1745, -1830], [-1690, -1885], [-1600, -1900]]
SITE = (-1995, -1565, -2180, -1330)
HUBS = {
    "stone": ("Ore Area Placeholder - Stone", [-1760, -1680],
              [[-1905, -1500], [-1850, -1500], [-1850, -1840], [-1990, -1840], [-1990, -1685], [-1905, -1685]],
              lambda pos: pos[0] < -1760),
    "ore": ("Ore Area Placeholder - Ore", [-1650, -1905],
            [[-1860, -1995], [-1570, -1995], [-1570, -2175], [-1860, -2175]],
            lambda pos: pos[1] < -1900),
}


def lane_edges():
    out = []
    for s in get("/nets", x=-1700, z=-1620, radius=330, filter="Medium Road", limit=200).get("segments") or []:
        (x0, z0), (x1, z1) = s["start"], s["end"]
        if s.get("prefab") == "Medium Road" and min(x0, x1) >= -1775 and max(z0, z1) <= -1329 and min(z0, z1) < -1335:
            out.append(s)
    return out


if __name__ == "__main__":
    if MODE == "check":
        print("ledger overlaps:", ledger_overlaps(*SITE))
        for name, (prefab, near, poly, _) in HUBS.items():
            print(name, "freeland", json.dumps(get("/freeland", x=near[0], z=near[1], radius=300, prefab=prefab).get("rects", [])[:2])[:600])
        d = post("/road", {"prefab": "Medium Road", "points": LANE, "curve": "smooth", "dryRun": True})
        show("lane dry", d)
        print(json.dumps(d.get("geometry"), default=str)[:1200])
    elif MODE == "road":
        d = show("lane dry", post("/road", {"prefab": "Medium Road", "points": LANE, "curve": "smooth", "dryRun": True}))
        if d.get("errors") or d.get("error") or d.get("wouldDemolish") or d.get("blocked"):
            sys.exit("STOP: lane dry run failed")
        with paused(DEPT):
            r = show("Quarry Lane", post("/road", {"prefab": "Medium Road", "points": LANE, "curve": "smooth"}))
            if r.get("applied"):
                LOG.spend(r.get("cost") or 0, "Quarry Lane (Medium Road)")
                LOG.put("lane", short(r, keys=("applied", "cost", "newEdgeIds")))
                t = show("lane trees", post("/roadupgrade", {"edges": r.get("newEdgeIds") or [], "upgrade": "Trees", "side": "both"}))
                if t.get("applied"):
                    LOG.spend(t.get("cost") or 0, "Quarry Lane trees")
    elif MODE in HUBS:
        prefab, near, poly, ok_side = HUBS[MODE]
        done = LOG.load().get(MODE) or {}
        hub, hub_pos = done.get("placedId"), done.get("pos")
        if not hub:
            side_ok = None
            for side in ("auto", "left", "right"):
                d = show(f"{MODE} hub dry side={side}", post("/building", {"prefab": prefab, "near": near, "side": side, "dryRun": True}))
                if d.get("errors") == 0 and not d.get("wouldDemolish") and ok_side(d.get("pos") or [0, 0]):
                    side_ok = side
                    break
            if side_ok is None:
                sys.exit(f"NO HUB SPOT for {MODE}")
            with paused(DEPT):
                r = show(f"{MODE} hub", building(prefab, near=near, side=side_ok))
            if not r.get("applied"):
                sys.exit("hub failed")
            LOG.spend(r.get("cost") or 0, f"{MODE} hub")
            hub, hub_pos = r.get("placedId"), r.get("pos")
            LOG.put(MODE, {"placedId": hub, "pos": hub_pos, "roadConnected": r.get("roadConnected")})
        far = [p for p in poly if math.dist(p, hub_pos) > 390]
        if far:
            sys.exit(f"corners too far from hub {hub_pos}: {far}")
        d = post("/area", {"owner": hub, "points": poly, "dryRun": True})
        pv = d.get("preview") or {}
        print(f"{MODE} area dry", json.dumps({"errors": d.get("errors"), "error": d.get("error"), "preview": pv}, default=str)[:700])
        if d.get("errors") or d.get("error") or pv.get("error") or pv.get("wouldDelete"):
            sys.exit("STOP area")
        with paused(DEPT):
            r = post("/area", {"owner": hub, "points": poly})
        a = r.get("area") or {}
        print(f"{MODE} area", json.dumps({"applied": r.get("applied"), "cost": r.get("cost"), "size": a.get("size"),
                                          "resourceAmount": a.get("resourceAmount"), "polygon": a.get("polygon")}))
        done = LOG.load().get(MODE) or {}
        done.update({"area": poly, "workArea": r.get("workArea"), "size": a.get("size"), "resourceAmount": a.get("resourceAmount")})
        LOG.put(MODE, done)
        if r.get("applied"):
            LOG.spend(r.get("cost") or 0, f"{MODE} area")
    elif MODE == "verify":
        for s in lane_edges():
            print(s["id"], s["start"], s["end"], s.get("upgrades"))
        print(json.dumps(LOG.load(), default=str)[:2500])
        print("total spent:", LOG.total())
