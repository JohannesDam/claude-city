"""builder-northeast - OfficeHigh02 289227:109 (-1678,3861) No Car Access.

Survey: the Research Loop edge 1953035:61 is a smooth curve that bulges east; the west (inner) zone block's
front is a straight line at x ~-1655, so in the middle of the curve the road edge is ~4.4 m from the lot
front (lot front -1653.6, road centre -1637.2 at z 3864). Rebuilding the edge straight would remove the 5
offices fronting it (bulldoze dry run: buildingsFrontingDeletedEdges 5, 138 occupied cells).
Cheapest fix: remove only the broken office and let the block regrow; check the new building's access.

Usage: python -I builders/builder-northeast_office_access.py dry|clear|check
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, access_check  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
OFFICE = "289227:109"

if MODE == "dry":
    print(json.dumps(post("/bulldoze", {"ids": [OFFICE], "dryRun": True}))[:600])
elif MODE == "clear":
    with paused(WHO):
        print(json.dumps(post("/bulldoze", {"ids": [OFFICE]}))[:600])
elif MODE == "check":
    r = get("/buildings", kind="all", x=-1660, z=3860, radius=45)
    for b in r.get("buildings", []):
        print(b)
    print("access", access_check([b["id"] for b in r.get("buildings", [])]))
