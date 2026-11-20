"""Phase 22 - towards a liveable city. The happiness factors (GET /happiness) showed the biggest
penalty is MAIL (-12), then garbage, TELECOM and air pollution: no post offices or radio masts had
ever been built. Spread post offices and radio masts over all districts."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import get, paused, place_in_area, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")

# district centres (x, z)
DISTRICTS = {
    "centre": (-1300, 600), "east": (-950, 650), "west": (-2459, 900), "far-west": (-3119, 900),
    "westmost": (-3730, 900), "westmost-N": (-3730, 1700), "south-west": (-3400, -500), "far-west-S": (-3119, 100),
    "north": (-1689, 1250), "north-2": (-1689, 1800), "far-north": (-1689, 2430), "north-3": (-1689, 3100),
    "far-north-W": (-2459, 2430), "north-3-W": (-2459, 3100), "NW": (-2240, 1830), "waterfront": (-900, 1230),
    "lakeside-NE": (-575, 1900), "lakeside-NE-2": (-575, 2490), "office park": (-2459, 60),
}
BIG = {"centre", "west", "north-2", "far-north", "westmost", "south-west"}


def put(label, prefab, x, z, radius=350):
    r, info = place_in_area(prefab, x, z, radius=radius, max_tries=40)
    print(f"{label:<34} {short(r) if r else ('no spot', short(info or {}))}", flush=True)
    return r


with paused():
    for name, (x, z) in DISTRICTS.items():
        put(f"post {name}", "PostOffice01" if name in BIG else "PostOffice02", x, z)
        put(f"radio mast {name}", "RadioMast01", x + 150, z - 150, radius=300)
print(get("/status"))
