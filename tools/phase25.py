"""Phase 25 (92k people). Discovery: 91 development points had never been spent, which is why hospitals,
incinerators, crematoria, universities and large parks never showed up. Spent 65 of them (GET/POST /devtree).
Batch 1 tackles the top happiness penalty (garbage) and the hearse backlog: incinerators downwind, recycling,
crematoria and hospitals."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import get, paused, place_in_area, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")


def put(label, prefab, x, z, radius=350, tries=40, filt="Road"):
    r, info = place_in_area(prefab, x, z, radius=radius, filt=filt, max_tries=tries)
    ok = bool(r and r.get("applied"))
    print(f"{label:<30} {short(r) if r else ('no spot', short(info or {}))}", flush=True)
    return ok


BATCH = sys.argv[1] if len(sys.argv) > 1 else "1"

with paused():
    if BATCH == "1":
        put("incinerator NE", "IncinerationPlant01", 0, 3700, radius=450)
        put("incinerator SE", "IncinerationPlant01", -1300, -1360, radius=350)
        put("recycling S", "RecyclingCenter01", -2200, -1360, radius=450)
        put("crematorium north", "Crematorium01", -1300, 3750)
        put("crematorium south", "Crematorium01", -1700, -800)
        put("crematorium south-west", "Crematorium01", -2900, -1150, radius=450)
        put("hospital north", "Hospital01", -1250, 3780)
        put("hospital south", "Hospital01", -1700, -560, radius=300)
print(get("/status"))
