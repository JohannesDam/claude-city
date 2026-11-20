"""builder-west, item b1-health_safety-westbrook-woodland-remembrance (no crematorium: builder-south's OSW campus).

  - "Woodland Way": a curved Small Road with Trees from the South Lane (-4900,-420) west into the woodland belt,
    ending in a cul-de-sac turning circle; no zoning along it (woodland);
  - Cemetery01 (128x200 m) set in the woods on the Woodland Way (deathcare still short: handled < deaths);
  - FirewatchTower01 on the western edge road over the woods;
  - south-Westbrook local centre at the school street z=-320: MedicalClinic02 + EmergencyShelter01.
Usage: python -I builders/builder-west_woodland.py [dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, demolition_cost, find_spot, get, paused, post, short  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[1:]
WB = [-5290, -920, -4310, 2170]
WAY = [[-4900, -420], [-4990, -418], [-5070, -400], [-5120, -360]]


def way_edges():
    segs = get("/nets", x=-5010, z=-400, radius=140, filter="Small Road").get("segments") or []
    return {s["id"] for s in segs if min(s["start"][0], s["end"][0]) < -4930 and -440 < (s["start"][1] + s["end"][1]) / 2 < -340}


def place(prefab, x, z, box=None, edges=None, radius=250):
    c = next((k for k in find_spot(prefab, x, z, radius=radius, n=40, within=WB)
              if (box is None or (box[0] <= k["x"] <= box[1] and box[2] <= k["z"] <= box[3]))
              and (edges is None or k.get("edge") in edges)), None)
    if not c:
        print(f"  {prefab}: no spot", flush=True)
        return None
    b = dict(c["building"])
    b.pop("prefab", None)
    d = post("/building", {"prefab": prefab, **b, "dryRun": True})
    if d.get("errors") or d.get("wouldDemolish") or DRY:
        print(f"  {prefab}: dry {short(d, ('errors', 'cost', 'wouldDemolish'))} at ({c['x']}, {c['z']})", flush=True)
        return None
    r = building(prefab, **b)
    print(f"  {prefab}: {short(r, ('applied', 'cost', 'placedId', 'roadConnected'))} at ({c['x']}, {c['z']})", flush=True)
    return r.get("placedId")


def main():
    d = post("/road", {"prefab": "Small Road", "points": WAY, "curve": "smooth", "dryRun": True, "allowDemolish": True})
    cost = demolition_cost(d.get("wouldDemolish"))
    print("woodland way dry:", short(d, ("errors", "cost")), cost, [s.get("snappedTo") for s in d.get("snapping") or []], flush=True)
    if d.get("errors") or cost["households"] > 30 or DRY:
        return
    out = {}
    with paused(WHO):
        r = post("/road", {"prefab": "Small Road", "points": WAY, "curve": "smooth", "allowDemolish": True})
        print("woodland way:", short(r, ("applied", "cost", "errors")), flush=True)
        t = post("/roadupgrade", {"polyline": WAY, "tolerance": 10, "prefabFilter": "Small Road", "upgrade": "Trees", "side": "both"})
        print("trees:", short(t, ("applied", "cost", "errors")), flush=True)
        edges = way_edges()
        print("way edges:", edges, flush=True)
        out["cemetery"] = place("Cemetery01", -5080, -300, edges=edges)
        out["firewatch"] = place("FirewatchTower01", -5267, 8, box=(-5290, -5200, -100, 120))
        out["clinic"] = place("MedicalClinic02", -4462, -291, box=(-4480, -4440, -310, -270))
        out["shelter"] = place("EmergencyShelter01", -4540, -303, box=(-4560, -4520, -320, -290))
    print(json.dumps(out))


if __name__ == "__main__":
    main()
