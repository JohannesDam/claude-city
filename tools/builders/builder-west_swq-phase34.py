"""builder-west, item b1-housing-sw-quarter-renewal-p1 - SWQ renewal phases 3 and 4 (conductor: go, same rule).

Capacity zoned beforehand (Westbrook South Lane ~590 hh, North Crescent remainder, freed z=760 corners): about
640 households, enough for both phases (190 + 256 households).
Each phase, in one paused block:
  - remove a shelter of mine that blocks the widening and re-place it nearby;
  - upgrade the x=-3180 spine section to Medium Road (+Trees, Grass), clearing the corner buildings;
  - a pocket park on a cleared corner and NA Residential Mixed on the spine's own frontage (overwrite=False).
Usage: python -I builders/builder-west_swq-phase34.py 3|4|5 [dry]
(phase 5 added later: z -320..-430, capacity from the Westbrook Civic Lane ~730 hh)
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
PHASES = {
    "3": {"z": (-430, -540), "shelter": "891706:103", "shelter_box": (-3175, -3100, -540, -440), "park_box": (-3240, -3120, -540, -440)},
    "4": {"z": (-760, -870), "shelter": None, "shelter_box": None, "park_box": (-3240, -3120, -870, -765)},
    "5": {"z": (-320, -430), "shelter": None, "shelter_box": None, "park_box": (-3240, -3120, -430, -325)},
}


def spine_edges(z0, z1, prefab):
    segs = get("/nets", x=-3180, z=(z0 + z1) / 2, radius=abs(z1 - z0) / 2 + 30, filter=prefab).get("segments") or []
    return [s["id"] for s in segs if abs(s["start"][0] + 3180) < 3 and abs(s["end"][0] + 3180) < 3
            and min(s["start"][1], s["end"][1]) >= min(z0, z1) - 1 and max(s["start"][1], s["end"][1]) <= max(z0, z1) + 1]


def place(prefab, box):
    x, z = (box[0] + box[1]) / 2, (box[2] + box[3]) / 2
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


def main():
    ph = PHASES[sys.argv[1]]
    z0, z1 = ph["z"]
    out = {}
    with paused(WHO):
        if ph["shelter"] and not DRY:
            print("shelter removed:", short(post("/bulldoze", {"ids": [ph["shelter"]]}), ("applied", "errors")), flush=True)
        edges = spine_edges(z0, z1, "Small Road")
        up = post("/upgrade", {"prefab": "Medium Road", "edges": edges, "dryRun": True, "allowDemolish": True})
        cost = demolition_cost(up.get("wouldDemolish"))
        print(f"phase {sys.argv[1]} dry:", edges, short(up, ("errors", "problems")), cost, flush=True)
        if DRY or up.get("errors") or cost["households"] > 300 or cost["companies"]:
            if not DRY:
                print("STOP: outside limits; shelter must be re-placed by hand", flush=True)
            return
        r = post("/upgrade", {"prefab": "Medium Road", "edges": edges, "allowDemolish": True})
        print("upgrade:", short(r, ("applied", "cost", "errors")), flush=True)
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": WHO, "prefab": f"Medium Road upgrade x=-3180 z {z0}..{z1}",
                                "item": f"b1-housing-sw-quarter-renewal-p1 phase {sys.argv[1]}",
                                "ids": [w["id"] for w in up.get("wouldDemolish") or []], "cost": cost}) + "\n")
        box = [-3190, min(z0, z1) - 5, -3170, max(z0, z1) + 5]
        for upg in ("Trees", "Grass"):
            t = post("/roadupgrade", {"box": box, "prefabFilter": "Medium Road", "upgrade": upg, "side": "both"})
            print(f"{upg}:", short(t, ("applied", "cost", "errors")), flush=True)
        if ph["shelter_box"]:
            out["shelter"] = place("EmergencyShelter01", ph["shelter_box"])
        out["park"] = place("PocketPark05", ph["park_box"])
        new = spine_edges(z0, z1, "Medium Road")
        if new:
            z = zone("NA Residential Mixed", mode="road", edges=new, depth=3, side="both", overwrite=False)
            print("mixed on spine:", short(z, ("applied", "cellsChanged", "occupiedCellsRezoned")), flush=True)
    print(json.dumps(out))


if __name__ == "__main__":
    main()
