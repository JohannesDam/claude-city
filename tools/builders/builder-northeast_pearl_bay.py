"""builder-northeast - b1-parks-pearl-bay: Pearl Bay lakeside resort quarter.

Tiles (0,2493), (0,1870), (623,1870), (1247,1870) bought. The north shore runs from (-130,2062) south-east to
(580,1570). Layout:
- Bay Boulevard: tree-lined Medium Road ~190 m inland, parallel to the shore, from the Lakeshore dead end
  (-330,2270) east to (1450,1880); Farm Link (Medium) north from the boulevard to the farm lane end (850,2205).
- Shore Promenade: Small Road with trees and wide sidewalks 35-80 m from the water, (-200,2150) -> (660,1610),
  linked to the boulevard at its ends and twice in between (hotel blocks between promenade and boulevard).
- Beach parks on the water side of the promenade; Commercial High (hotels, dining) between promenade and
  boulevard; Residential Medium/Row on curved crescents north of the boulevard (>= 150 m from the farms north
  of z 2190 at x > 580).

Usage: python -I builders/builder-northeast_pearl_bay.py dry|<piece>|...
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_pearl_bay.json"
KEYS = ("applied", "cost", "errors", "problems", "wouldDemolish", "conflicts")

ROADS = {
    # Small Road stub from the Lakeshore dead end: a Medium Road junction there would clip two homes
    "stub": {"prefab": "Small Road", "points": [[-330, 2270], [-265, 2266]]},
    "boulevard": {"prefab": "Medium Road", "curve": "smooth",
                  "points": [[-265, 2266], [-180, 2255], [-40, 2200], [90, 2110], [200, 1990], [330, 1910],
                             [480, 1870], [640, 1820], [800, 1790], [1000, 1790], [1200, 1820], [1450, 1880]]},
    "farm link": {"prefab": "Medium Road", "curve": "smooth", "points": [[850, 1793], [862, 1950], [850, 2205]]},
    "promenade": {"prefab": "Small Road", "curve": "smooth",
                  "points": [[-200, 2150], [-90, 2092], [-5, 2030], [62, 1935], [120, 1842], [230, 1797],
                             [400, 1752], [520, 1702], [600, 1652], [660, 1612]]},
    "link west": {"prefab": "Small Road", "curve": "smooth", "points": [[-200, 2150], [-195, 2200], [-180, 2255]]},
    "link mid": {"prefab": "Small Road", "curve": "smooth", "points": [[62, 1935], [110, 1990], [150, 2047]]},
    "link east": {"prefab": "Small Road", "curve": "smooth", "points": [[400, 1752], [415, 1820], [430, 1886]]},
    "link head": {"prefab": "Small Road", "curve": "smooth", "points": [[660, 1612], [690, 1700], [705, 1808]]},
}


def show(label, r):
    out = short(r, keys=KEYS)
    g = r.get("geometry") or {}
    if g:
        out["geo"] = {k: g.get(k) for k in ("length", "minRadius", "warnings") if g.get(k) is not None}
    out["snap"] = [s.get("snappedTo") for s in r.get("snapping") or []] or None
    print(f"{label:<12} {json.dumps(out)[:600]}", flush=True)
    return r


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


if __name__ == "__main__":
    if MODE == "dry":
        for k, b in ROADS.items():
            show(k, post("/road", {**b, "dryRun": True}))
    elif MODE in ROADS:
        with paused(WHO):
            log(MODE, show(MODE, post("/road", ROADS[MODE])))
