"""builder-central: Downtown post (worklist b1-utilities-downtown-post-vans).

Survey (2032-01-02): Downtown already gained PostOffice01s at (-2389,847) and (-1648,1775) since the board's
assessment; all Downtown post offices are 'ok' and post coverage is 'good' across Downtown. The remaining gap
is the south end of Downtown (z 200..600 along the avenue), where van requests fail pathfinding and are served
from (-1263,690) and (-3455,-580). One PostOffice01 goes there, 90 m east of the avenue (tram T1 / Medium Road),
replacing 8 low-density homes (8 households, ~$700/month tax) - within the owner's make-room limits.

Run: cd C:/soft/cityskyline/tools && python -I builders/builder-central_post-vans.py [--dry]
"""
import json
import os
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short, building, demolition_cost, revenue_rates  # noqa: E402

WHO = "builder-central"
DRY = "--dry" in sys.argv
SITE = {"x": -1816.37, "z": 499.5, "angle": 0}  # faces the z=540 street
MAX_HOUSEHOLDS = 20


def main():
    rates = revenue_rates()
    with paused(WHO):
        d = post("/building", {"prefab": "PostOffice01", **SITE, "dryRun": True, "allowDemolish": True})
        cost = demolition_cost(d.get("wouldDemolish"), rates)
        print("dry", short(d, keys=("errors", "cost", "problems")), cost)
        if d.get("errors") or cost["companies"] or cost["households"] > MAX_HOUSEHOLDS or DRY:
            return
        r = building("PostOffice01", **SITE, allowDemolish=True)
        print("PostOffice01", short(r, keys=("applied", "cost", "errors", "placedId", "roadConnected")))
        if r.get("applied"):
            p = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "logs", "council", "demolitions.jsonl")
            with open(p, "a", encoding="utf-8") as f:
                f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": WHO, "prefab": "PostOffice01",
                                    "placedId": r.get("placedId"), "cost": cost, "pos": [SITE["x"], SITE["z"]]}) + "\n")
            u = post("/roadupgrade", {"polyline": [[-1909, 540], [-1689, 540]], "tolerance": 6, "prefabFilter": "Small Road",
                                      "upgrade": "Trees", "side": "both"})
            print("trees z=540 street", short(u, keys=("applied", "cost", "errors")))


if __name__ == "__main__":
    main()
