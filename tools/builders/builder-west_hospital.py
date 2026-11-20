"""builder-west, item b1-west-westbrook-hospital.

Hospital01 (184x80 m lot) has no clean site in central Westbrook; the clean sites are in the woodland belt on the
x=-5290 edge road. To make that site work for the village:
  - "Hospital Lane": a short tree-lined Small Road from the Village Loop node (-4985,240) west to the x=-5290 road
    (replaces 2 villas), so ambulances and visitors reach the loop (and bus line 9) directly;
  - Hospital01 fronting the lane or the edge road just south of it, among the trees.
Usage: python -I builders/builder-west_hospital.py [dry]
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, demolition_cost, find_spot, get, paused, post, short  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[1:]
LANE = [[-4985, 240], [-5080, 232], [-5190, 236], [-5290, 236]]
WB = [-5290, -920, -4310, 2170]
LOG = r"C:\soft\cityskyline\logs\council\demolitions.jsonl"


def main():
    d = post("/road", {"prefab": "Small Road", "points": LANE, "curve": "smooth", "dryRun": True, "allowDemolish": True})
    cost = demolition_cost(d.get("wouldDemolish"))
    print("lane dry:", short(d, ("errors", "cost")), cost, flush=True)
    if d.get("errors") or cost["households"] > 10 or DRY:
        return
    with paused(WHO):
        r = post("/road", {"prefab": "Small Road", "points": LANE, "curve": "smooth", "allowDemolish": True})
        print("lane:", short(r, ("applied", "cost", "errors")), flush=True)
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": WHO, "prefab": "Small Road (Westbrook Hospital Lane)",
                                "item": "b1-west-westbrook-hospital", "ids": [w["id"] for w in d.get("wouldDemolish") or []], "cost": cost}) + "\n")
        t = post("/roadupgrade", {"polyline": LANE, "tolerance": 8, "prefabFilter": "Small Road", "upgrade": "Trees", "side": "both"})
        print("trees:", short(t, ("applied", "cost", "errors")), flush=True)
        lane = {s["id"] for s in get("/nets", x=-5140, z=236, radius=170, filter="Small Road").get("segments") or []
                if abs((s["start"][1] + s["end"][1]) / 2 - 236) < 15}
        best = None
        for c in find_spot("Hospital01", -5150, 160, radius=300, n=40, within=WB):
            if c.get("edge") in lane or (c["x"] < -5150 and 60 < c["z"] < 236):
                b = dict(c["building"])
                b.pop("prefab", None)
                dd = post("/building", {"prefab": "Hospital01", **b, "dryRun": True})
                if dd.get("errors") == 0 and not dd.get("wouldDemolish"):
                    best = (c, b)
                    break
        if not best:
            print("no hospital site next to the lane", flush=True)
            return
        c, b = best
        r = building("Hospital01", **b)
        print("hospital:", short(r, ("applied", "cost", "placedId", "roadConnected", "errors")), (c["x"], c["z"]), c.get("edge"), flush=True)


if __name__ == "__main__":
    main()
