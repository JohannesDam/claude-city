"""builder-central: post for the lake shore (b2-utilities-b2-utilities-lake-post).

Lakeshore's PostOffice02 296122:33 (-575,1964.5) has no vans left; Pearl Bay grows next door. A PostOffice01
(20+ vans) goes on the free land at Lakeshore's east edge (x -412..-320, z 2082..2202), reached by "Post Lane":
a tree-lined Medium Road from the z=2160 street node (-470,2160) east (removes 1 low home at the junction).
Vans reach Pearl Bay via x=-470 -> z=2270 -> Bay Boulevard.
Site 1 (Waterfront, a second PO01 near (-1000,800)) is dropped: PostOffice01 175520:119 at (-1262.8,690.5) is
330 m from the Waterfront PO02 with 22 idle vans and 2 km range; another PO01 would add ~140k/month upkeep.

usage: python -I builders/builder-central_lake-post.py road|post [dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

WHO = "builder-central"
STAGE = sys.argv[1] if len(sys.argv) > 1 else "road"
DRY = "dry" in sys.argv[2:]
LANE = [[-470, 2160], [-318, 2160]]

if STAGE == "road":
    with paused(WHO):
        d = post("/road", {"prefab": "Medium Road", "points": LANE, "dryRun": True, "allowDemolish": True})
        wd = d.get("wouldDemolish") or []
        print("dry", d.get("errors"), d.get("cost"), [(w["prefab"], w["center"]) for w in wd])
        if d.get("errors") or len(wd) > 1 or any("Residential" not in w["prefab"] for w in wd) or DRY:
            sys.exit(0)
        r = post("/road", {"prefab": "Medium Road", "points": LANE, "allowDemolish": True})
        ids = r.get("newEdgeIds") or []
        print("lane", r.get("applied"), r.get("cost"), ids)
        for up in ("Trees", "Grass"):
            u = post("/roadupgrade", {"edges": ids, "upgrade": up, "side": "both"})
            ids = u.get("edgeIdsAfter") or ids
            print(up, u.get("applied"), u.get("cost"))
elif STAGE == "post":
    with paused(WHO):
        r = post("/place", {"prefab": "PostOffice01", "near": [-370, 2140], "within": [-796, 1504, -320, 2720],
                            "radius": 150, "maxHouseholds": 0, "maxCompanies": 0, "dryRun": DRY}, timeout=300)
    print(json.dumps({k: v for k, v in r.items() if k != "candidates"})[:1200])
