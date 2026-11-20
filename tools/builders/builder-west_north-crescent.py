"""builder-west: Westbrook "North Crescent" - housing capacity zoned BEFORE each SWQ renewal phase
(conductor gate: zone at least the same housing capacity elsewhere in the west first).

A curved Small Road with Trees continues the Green Lane north from its junction on the Village Loop's north
arm (-4680,435) and sweeps west to a cul-de-sac at (-5045,600), short of the woodland belt (x < -5100).
Replaces 2 new level-1 buildings (26 households) at the junction. Zoning: NA Residential Medium on the eastern
half (near the bus lane), Medium Row further west, Low villas at the woodland end.
Capacity estimate: Medium ~2 households/cell, Medium Row ~1.3, Low ~0.1 (from /zonestats and /districts).
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import demolition_cost, get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[1:]
PTS = [[-4680.1, 435.5], [-4690, 530], [-4760, 610], [-4880, 648], [-5000, 632], [-5045, 600]]
PER_CELL = {"NA Residential Medium": 2.0, "NA Residential Medium Row": 1.3, "NA Residential Low": 0.1}
LOG = r"C:\soft\cityskyline\logs\council\demolitions.jsonl"


def crescent_edges():
    out = []
    for cx, cz in ((-4720, 540), (-4900, 640)):
        for s in get("/nets", x=cx, z=cz, radius=200, filter="Small Road").get("segments") or []:
            mz = (s["start"][1] + s["end"][1]) / 2
            if mz > 445 and s["id"] not in [o["id"] for o in out]:
                out.append(s)
    return out


def main():
    d = post("/road", {"prefab": "Small Road", "points": PTS, "curve": "smooth", "dryRun": True, "allowDemolish": True})
    would = d.get("wouldDemolish") or []
    cost = demolition_cost(would)
    print("dry:", short(d, ("errors", "cost")), cost, flush=True)
    if d.get("errors") or cost["households"] > 40 or DRY:
        return
    capacity = 0.0
    with paused(WHO):
        r = post("/road", {"prefab": "Small Road", "points": PTS, "curve": "smooth", "allowDemolish": True})
        print("road:", short(r, ("applied", "cost", "errors")), flush=True)
        if would:
            with open(LOG, "a", encoding="utf-8") as f:
                f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": WHO, "prefab": "Small Road (North Crescent)",
                                    "ids": [w["id"] for w in would], "cost": cost}) + "\n")
        t = post("/roadupgrade", {"box": [-5060, 445, -4670, 665], "prefabFilter": "Small Road", "upgrade": "Trees", "side": "both"})
        print("trees:", short(t, ("applied", "cost", "errors")), flush=True)
    time.sleep(1)
    with paused(WHO):
        for s in crescent_edges():
            mx = (s["start"][0] + s["end"][0]) / 2
            name = ("NA Residential Medium" if mx > -4820 else
                    "NA Residential Medium Row" if mx > -4990 else "NA Residential Low")
            z = zone(name, mode="road", edges=[s["id"]], depth=5, side="both")
            n = z.get("cellsChanged") or 0
            capacity += n * PER_CELL[name]
            print(f"  {name:28} {s['id']:>12} cells {n}", flush=True)
    print(json.dumps({"capacityHouseholds": round(capacity)}))


if __name__ == "__main__":
    main()
