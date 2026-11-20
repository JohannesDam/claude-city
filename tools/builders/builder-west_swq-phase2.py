"""builder-west, item b1-housing-sw-quarter-renewal-p1 - SWQ renewal phase 2 (conductor: go, <= 300 households,
capacity first, services and rezoning in the same lock as the demolition).

Stage "capacity": Westbrook "South Lane" - a curved Small Road with Trees from the Village Loop node (-4905,-195)
south to the z=-650 villa street (replaces 5 villas, 5 households); a second link for south Westbrook and the
future approach to the Woodland Cemetery. Zoned Medium (east) / Medium Row (west), overwrite=False.

Stage "phase2": in one paused block -
  1. remove my EmergencyShelter01 613149:45 (-3196,-607), which blocks the widening;
  2. x=-3180 spine z -540..-650: Small -> Medium Road (+Trees, Grass), SmallRoundabout at (-3180,-650);
  3. new EmergencyShelter01 near the old cell, a PocketPark at the roundabout, NA Residential Mixed on the
     spine's own new frontage (overwrite=False).
Usage: python -I builders/builder-west_swq-phase2.py capacity|phase2 [dry]
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, demolition_cost, find_spot, get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[2:]
LOG = r"C:\soft\cityskyline\logs\council\demolitions.jsonl"
SWQ = [-4030, -920, -2826, -310]
SOUTH_LANE = [[-4905, -195], [-4890, -300], [-4900, -420], [-4930, -530], [-4935, -637]]
PER_CELL = {"NA Residential Medium": 2.0, "NA Residential Medium Row": 1.3}


def log(what, would, item="b1-housing-sw-quarter-renewal-p1 phase 2"):
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": WHO, "prefab": what, "item": item,
                            "ids": [w.get("id") for w in would or []], "cost": demolition_cost(would)}) + "\n")


def stage_capacity():
    d = post("/road", {"prefab": "Small Road", "points": SOUTH_LANE, "curve": "smooth", "dryRun": True, "allowDemolish": True})
    cost = demolition_cost(d.get("wouldDemolish"))
    print("south lane dry:", short(d, ("errors", "cost")), cost, flush=True)
    if d.get("errors") or cost["households"] > 15 or DRY:
        return
    cap = 0.0
    with paused(WHO):
        r = post("/road", {"prefab": "Small Road", "points": SOUTH_LANE, "curve": "smooth", "allowDemolish": True})
        print("south lane:", short(r, ("applied", "cost", "errors")), flush=True)
        log("Small Road (Westbrook South Lane)", d.get("wouldDemolish"), item="capacity for SWQ phase 2")
        time.sleep(1)
        t = post("/roadupgrade", {"polyline": SOUTH_LANE, "tolerance": 12, "prefabFilter": "Small Road",
                                  "upgrade": "Trees", "side": "both"})
        print("trees:", short(t, ("applied", "cost", "errors")), flush=True)
        segs = []
        for cz in (-300, -500):
            for s in get("/nets", x=-4910, z=cz, radius=140, filter="Small Road").get("segments") or []:
                mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
                # only the new lane: not the z=-650 villa street, not the Green Crescent (x > -4800)
                if -4960 < mx < -4870 and -630 < mz < -205 and s["id"] not in [o["id"] for o in segs]:
                    segs.append(s)
        for s in segs:
            # travelling south the east side is on the left
            east = "left" if s["end"][1] < s["start"][1] else "right"
            west = "right" if east == "left" else "left"
            for name, side in (("NA Residential Medium", east), ("NA Residential Medium Row", west)):
                z = zone(name, mode="road", edges=[s["id"]], depth=5, side=side, overwrite=False)
                cap += (z.get("cellsChanged") or 0) * PER_CELL[name]
                print(f"  {name:26} {s['id']:>12} {side:5} {short(z, ('cellsChanged', 'occupiedCellsRezoned'))}", flush=True)
    print(json.dumps({"capacityHouseholds": round(cap)}))


def spine_edges(prefab):
    segs = get("/nets", x=-3180, z=-595, radius=70, filter=prefab).get("segments") or []
    return [s["id"] for s in segs if abs(s["start"][0] + 3180) < 3 and abs(s["end"][0] + 3180) < 3
            and min(s["start"][1], s["end"][1]) >= -651 and max(s["start"][1], s["end"][1]) <= -539]


def place(prefab, x, z, box):
    c = next((k for k in find_spot(prefab, x, z, radius=150, n=40, within=SWQ)
              if box[0] <= k["x"] <= box[1] and box[2] <= k["z"] <= box[3]), None)
    if not c:
        print(f"  {prefab}: no spot", flush=True)
        return None
    b = dict(c["building"])
    b.pop("prefab", None)
    r = building(prefab, **b)
    print(f"  {prefab}: {short(r, ('applied', 'cost', 'placedId', 'roadConnected'))} at ({c['x']}, {c['z']})", flush=True)
    return r.get("placedId")


def stage_phase2():
    out = {}
    with paused(WHO):
        b = post("/bulldoze", {"ids": ["613149:45"], "dryRun": True})
        print("shelter remove dry:", short(b, ("errors", "applied")), flush=True)
        if DRY:
            return
        print("shelter removed:", short(post("/bulldoze", {"ids": ["613149:45"]}), ("applied", "errors")), flush=True)
        edges = spine_edges("Small Road")
        up = post("/upgrade", {"prefab": "Medium Road", "edges": edges, "dryRun": True, "allowDemolish": True})
        ra = post("/roundabout", {"at": [-3180, -650], "size": "small", "dryRun": True, "allowDemolish": True})
        u = {w["id"]: w for w in (up.get("wouldDemolish") or []) + (ra.get("wouldDemolish") or [])}
        cost = demolition_cost(list(u.values()))
        print("phase 2 dry:", edges, short(up, ("errors",)), short(ra, ("errors",)), cost, flush=True)
        if up.get("errors") or cost["households"] > 300 or cost["companies"]:
            print("STOP: phase 2 outside limits", flush=True)
            return
        r = post("/upgrade", {"prefab": "Medium Road", "edges": edges, "allowDemolish": True})
        print("upgrade:", short(r, ("applied", "cost", "errors")), flush=True)
        log("Medium Road upgrade x=-3180 z -540..-650", up.get("wouldDemolish"))
        r = post("/roundabout", {"at": [-3180, -650], "size": "small", "allowDemolish": True})
        print("roundabout:", short(r, ("applied", "cost", "errors")), flush=True)
        if r.get("applied"):
            log("SmallRoundabout (-3180,-650)", ra.get("wouldDemolish"))
        for upg in ("Trees", "Grass"):
            t = post("/roadupgrade", {"box": [-3190, -762, -3170, -535], "prefabFilter": "Medium Road", "upgrade": upg, "side": "both"})
            print(f"{upg}:", short(t, ("applied", "cost", "errors")), flush=True)
        out["shelter"] = place("EmergencyShelter01", -3160, -600, (-3175, -3100, -650, -545))
        out["pocket"] = place("PocketPark05", -3200, -625, (-3240, -3120, -650, -545))
        new = spine_edges("Medium Road")
        if new:
            z = zone("NA Residential Mixed", mode="road", edges=new, depth=3, side="both", overwrite=False)
            print("mixed on spine:", short(z, ("applied", "cellsChanged", "occupiedCellsRezoned")), flush=True)
    print(json.dumps(out))


if __name__ == "__main__":
    {"capacity": stage_capacity, "phase2": stage_phase2}[sys.argv[1]]()
