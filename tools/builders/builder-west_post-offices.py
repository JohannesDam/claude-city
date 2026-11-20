"""builder-west, item b1-utilities-west-post-offices.

  - PostOffice02 on the reserved Westbrook civic-strip lot (edge 563535:209, t=0.55, left);
  - PostOffice01 for south Westbrook: already built on the Village Loop (844536:229, garden-village script);
  - PostOffice01 (20 vans) for north Westside, where the three PostOffice02 run out of vans. Westside has no
    free lot that size, so it makes room on the long z=1450 street: 5 single-family homes (5 households,
    ~$450/month), the cheapest make-room site; logged to logs/council/demolitions.jsonl.
  - Trees on the frontages.
"""
import json
import os
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, demolition_cost, find_spot, paused, post, short  # noqa: E402

WHO = "builder-west"
DEMOLOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "logs", "council", "demolitions.jsonl")


def westside_site():
    for c in find_spot("PostOffice01", -3503, 1490, radius=40, n=20, growables="free"):
        if abs(c["x"] + 3503.06) < 3 and abs(c["z"] - 1490.5) < 3:
            b = dict(c["building"])
            b.pop("prefab", None)
            return b
    return None


def main():
    out = {}
    b = westside_site()
    d = post("/building", {"prefab": "PostOffice01", **b, "dryRun": True, "allowDemolish": True}) if b else {}
    would = d.get("wouldDemolish") or []
    cost = demolition_cost(would)
    print("westside dry:", short(d, ("errors", "cost")), cost, flush=True)
    with paused(WHO):
        r = building("PostOffice02", edge="563535:209", t=0.55, side="left")
        print("civic strip PO02:", short(r, ("applied", "cost", "placedId", "roadConnected", "errors")), flush=True)
        out["civic"] = r.get("placedId")
        if b and d.get("errors") == 0 and cost["households"] <= 10 and not cost["companies"]:
            r = building("PostOffice01", **b, allowDemolish=True)
            print("westside PO01:", short(r, ("applied", "cost", "placedId", "roadConnected", "errors")), flush=True)
            out["westside"] = r.get("placedId")
            if r.get("applied"):
                with open(DEMOLOG, "a", encoding="utf-8") as f:
                    f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": WHO, "prefab": "PostOffice01",
                                        "placedId": r.get("placedId"), "pos": [b.get("x"), b.get("z")],
                                        "ids": [w.get("id") for w in would], "cost": cost}) + "\n")
        for poly in ([[-3620, 1450], [-3400, 1450]],):  # the civic strip lane x=-4380 already has Trees
            t = post("/roadupgrade", {"polyline": poly, "tolerance": 6, "prefabFilter": "Small Road",
                                      "upgrade": "Trees", "side": "both"})
            print("trees", poly, short(t, ("applied", "cost", "errors", "wouldDemolish")), flush=True)
    print(json.dumps(out))


if __name__ == "__main__":
    main()
