"""Council round 1 - Housing - clear-abandoned-ruin.

Bulldoze only the collapsed abandoned factory 540803:35 (IndustrialManufacturing01_L1_6x6) at (-47,3182).
The burned ruins 277015:115 and 239159:65 belong to Safety's 'Burned Down' ticket: not touched.

Usage: python -I council/r1_housing_clear_ruin.py
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
ID = "540803:35"
LOG = r"C:\soft\cityskyline\logs\council\r1_housing_clear_ruin.json"

out = {"spent": 0}
before = get("/buildings", kind="abandoned", x=-47, z=3182, radius=60)
ids = [b["id"] for b in before.get("buildings") or []]
print("abandoned here:", ids, flush=True)
if ID not in ids:
    print("ruin already gone - nothing to do")
else:
    with paused("housing"):
        d = post("/bulldoze", {"ids": [ID], "dryRun": True})
        print("bulldoze (dry)", json.dumps(short(d, keys=("applied", "cost", "errors", "error", "removed", "ids"))), flush=True)
        if d.get("errors") in (0, None) and not d.get("error"):
            r = post("/bulldoze", {"ids": [ID]})
            print("bulldoze", json.dumps(r)[:600], flush=True)
            out["bulldoze"] = r
            if r.get("applied"):
                out["spent"] += r.get("cost") or 0
after = get("/buildings", kind="abandoned", x=-47, z=3182, radius=60)
out["after"] = [b["id"] for b in after.get("buildings") or []]
print("abandoned after:", out["after"])
json.dump(out, open(LOG, "w"), indent=1)
print("SPENT", out["spent"])
