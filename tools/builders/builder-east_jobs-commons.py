"""builder-east / jobs push 2033-01: East Gate Commons rezoning.

The Commons' NA Commercial High cells (Lakefront Drive and the north half of Commons Drive, ~970 cells) never
grew in a year (commercial demand ~0-31, 38 No Customers icons city-wide), while office demand is 70-100 and
56k highly educated citizens are underemployed. This script:
1. rezones the commercial block x 940..1565, z -495..-95 to Office High (overwrite: the cells carry no
   buildings; parks/bus station keep their lots), and
2. zones the west crescent (unzoned) as NA Residential Mixed (overwrite=False, so no office cell is touched).
Stages: dry | build
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused  # noqa: E402

WHO = "builder-east"
OFFICE = {"zone": "Office High", "mode": "rect", "points": [[940, -95], [1565, -495]], "overwrite": True}
CRESCENT = ["1754616:83", "1753732:67", "1833635:109", "1833116:131", "1754835:79", "1761821:59"]
MIXED = {"zone": "NA Residential Mixed", "mode": "road", "edges": CRESCENT, "depth": 4, "side": "both",
         "overwrite": False}
KEYS = ("applied", "cellsChanged", "cellsFromZone", "occupiedCellsRezoned", "errors", "error", "wouldDemolish")

stage = sys.argv[1] if len(sys.argv) > 1 else "dry"
dry = stage != "build"


def show(label, r):
    print(label, json.dumps({k: r.get(k) for k in KEYS if r.get(k) is not None}))


if dry:
    show("office", post("/zone", {**OFFICE, "dryRun": True}))
    show("mixed", post("/zone", {**MIXED, "dryRun": True}))
else:
    with paused(WHO):
        r = post("/zone", OFFICE)
        show("office", r)
        if r.get("applied"):
            show("mixed", post("/zone", MIXED))
