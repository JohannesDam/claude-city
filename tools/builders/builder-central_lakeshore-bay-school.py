"""builder-central: Lakeshore Bay School (b2-education-b2-edu-lakeshore-bay-school).

Elementary seats: 21 of 29 schools full, shortfall ~3.9k seats city-wide (2033-01). No clean ElementarySchool03
site in Lakeshore, so /place with make_room limits: the least-demolition candidate removes 3 low homes on the
x~-517 street at z 2388..2452 (160 m from the worst cell (-591,2560); outside the Lakeshore North Green, z>=2600).
Then a playground as schoolyard via /place.

usage: python -I builders/builder-central_lakeshore-bay-school.py school|yard [dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused  # noqa: E402

WHO = "builder-central"
STAGE = sys.argv[1] if len(sys.argv) > 1 else "school"
DRY = "dry" in sys.argv[2:]
LAKESHORE = [-796, 1504, -320, 2600]  # stop below the Lakeshore North Green

if STAGE == "school":
    body = {"prefab": "ElementarySchool03", "near": [-450, 2400], "within": LAKESHORE, "radius": 450,
            "maxHouseholds": 6, "maxCompanies": 0, "all": True, "dryRun": DRY}
elif STAGE == "yard":
    body = {"prefab": "Playground03", "near": [-517, 2420], "within": LAKESHORE, "radius": 150,
            "maxHouseholds": 0, "maxCompanies": 0, "dryRun": DRY}
with paused(WHO):
    r = post("/place", body, timeout=300)
c = r.get("chosen") or {}
print(json.dumps({k: r.get(k) for k in ("applied", "placedId", "error")}),
      json.dumps({k: c.get(k) for k in ("x", "z", "cost", "errors", "demolition", "roadConnected", "corners")}))
