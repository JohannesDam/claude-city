"""builder-west, item b1-housing-sw-quarter-renewal-p1 - Southwest Quarter renewal, phase 1 "Local Centre".

Conductor gate (checkpoint 1): phases of <= 300 households, the same housing capacity zoned elsewhere first
(done: Westbrook North Crescent, ~630 households of capacity), then dry run, demolish, rebuild.

Phase 1 starts the quarter's spine at its local centre, where the z=-650 street (the future link to the
Southgate South Ring) crosses the x=-3180 street:
  - the x=-3180 street becomes a tree-lined Medium Road from z=-650 to z=-760 (later phases extend it north to
    the z=-100 bus street and south to z=-870); the widening clears the four corner buildings;
  - a small roundabout at the z=-650 crossing;
  - on the cleared corners: MedicalClinic02 (healthcare for the quarter's worst cells), a pocket square and
    NA Residential Mixed (shops below homes) on the spine's own new frontage.
Usage: python -I builders/builder-west_swq-phase1.py [dry]
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, demolition_cost, find_spot, get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[1:]
MAX_HH = 300
LOG = r"C:\soft\cityskyline\logs\council\demolitions.jsonl"
SWQ = [-4030, -920, -2826, -310]


def spine_edges(z0, z1, prefab="Small Road"):
    segs = get("/nets", x=-3180, z=(z0 + z1) / 2, radius=abs(z1 - z0) / 2 + 30, filter=prefab).get("segments") or []
    return [s["id"] for s in segs if abs(s["start"][0] + 3180) < 3 and abs(s["end"][0] + 3180) < 3
            and min(s["start"][1], s["end"][1]) >= min(z0, z1) - 1 and max(s["start"][1], s["end"][1]) <= max(z0, z1) + 1]


def log(what, would, extra=None):
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": WHO, "prefab": what,
                            "item": "b1-housing-sw-quarter-renewal-p1 phase 1",
                            "ids": [w.get("id") for w in would or []], "cost": demolition_cost(would), **(extra or {})}) + "\n")


def place(prefab, x, z, box):
    c = next((k for k in find_spot(prefab, x, z, radius=120, n=40, within=SWQ)
              if box[0] <= k["x"] <= box[1] and box[2] <= k["z"] <= box[3]), None)
    if not c:
        print(f"  {prefab}: no spot in {box}", flush=True)
        return None
    b = dict(c["building"])
    b.pop("prefab", None)
    r = building(prefab, **b)
    print(f"  {prefab}: {short(r, ('applied', 'cost', 'placedId', 'roadConnected', 'errors'))} at ({c['x']}, {c['z']})", flush=True)
    return r.get("placedId")


def main():
    edges = spine_edges(-650, -760)
    up = post("/upgrade", {"prefab": "Medium Road", "edges": edges, "dryRun": True, "allowDemolish": True})
    ra = post("/roundabout", {"at": [-3180, -650], "size": "small", "dryRun": True})
    would = {w["id"]: w for w in (up.get("wouldDemolish") or []) + (ra.get("wouldDemolish") or [])}
    cost = demolition_cost(list(would.values()))
    print("spine edges", edges, "| upgrade", short(up, ("errors", "cost")), "| roundabout", short(ra, ("errors", "cost")),
          "| demolish", cost, flush=True)
    if up.get("errors") or ra.get("errors") or cost["households"] > MAX_HH or cost["companies"] or DRY:
        return
    out = {}
    with paused(WHO):
        r = post("/upgrade", {"prefab": "Medium Road", "edges": edges, "allowDemolish": True})
        print("upgrade:", short(r, ("applied", "cost", "errors")), flush=True)
        log("Medium Road upgrade x=-3180 z -650..-760", up.get("wouldDemolish"))
        time.sleep(1)
        ra = post("/roundabout", {"at": [-3180, -650], "size": "small", "dryRun": True})
        if not ra.get("errors") and demolition_cost(ra.get("wouldDemolish"))["households"] <= 100:
            r = post("/roundabout", {"at": [-3180, -650], "size": "small"})
            print("roundabout:", short(r, ("applied", "cost", "errors")), flush=True)
            if ra.get("wouldDemolish"):
                log("SmallRoundabout at (-3180,-650)", ra.get("wouldDemolish"))
        time.sleep(1)
        for upg in ("Trees", "Grass"):
            t = post("/roadupgrade", {"box": [-3190, -765, -3170, -640], "prefabFilter": "Medium Road", "upgrade": upg, "side": "both"})
            print(f"{upg}:", short(t, ("applied", "cost", "errors")), flush=True)
    time.sleep(2)
    with paused(WHO):
        out["clinic"] = place("MedicalClinic02", -3148, -684, (-3172, -3120, -712, -655))
        out["square"] = place("CityPark05", -3150, -732, (-3172, -3120, -760, -705))
        out["pocket"] = place("PocketPark06", -3205, -732, (-3240, -3184, -760, -705))
        new = spine_edges(-650, -760, prefab="Medium Road")
        if new:
            z = zone("NA Residential Mixed", mode="road", edges=new, depth=3, side="both", overwrite=False)
            print("mixed zoning on the spine:", short(z, ("applied", "errors", "cellsChanged", "occupiedCellsRezoned")), flush=True)
    print(json.dumps(out))


if __name__ == "__main__":
    main()
