"""builder-northeast - b1-economy-uh-software-park: University Hill Software Park.

- ServerFarm01 on the free strip south of the z=3700 street (east end, lot x -1692..-1500, z 3548..3691).
- Research Loop: curved Medium Road (trees, grass) from the x=-1910 street through the free land west of the
  City Hall and south through the gap east of the landfill to the z=3700 street, with a short east spur;
  a PocketPark on the loop as the green square.
- Office High zoning (depth 4) along the loop and on the rest of the strip (office demand is high now).

Usage: python -I builders/builder-northeast_software_park.py farm|loop-dry|loop|park|zone-dry|zone|trees
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short, find_spot  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "loop-dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_software_park.json"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "wouldDemolish", "pos")
LOOP = [[-1909, 3962], [-1800, 3995], [-1690, 3985], [-1640, 3900], [-1645, 3790], [-1644, 3700]]
SPUR = [[-1672, 3990], [-1590, 4004], [-1525, 3998]]


def show(label, r):
    out = short(r, keys=KEYS)
    g = r.get("geometry") or {}
    if g:
        out["geo"] = {k: g.get(k) for k in ("length", "minRadius", "warnings") if g.get(k) is not None}
    out["snap"] = [s.get("snappedTo") for s in r.get("snapping") or []] or None
    print(f"{label:<12} {json.dumps(out)[:700]}", flush=True)
    return r


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


if __name__ == "__main__":
    if MODE == "farm":
        c = [c for c in find_spot("ServerFarm01", -1596, 3620, radius=120, n=6) if c["x"] > -1650]
        b = dict(c[0]["building"]); b.pop("prefab", None)
        d = show("farm (dry)", post("/building", {"prefab": "ServerFarm01", **b, "dryRun": True}))
        if d.get("errors") == 0:
            with paused(WHO):
                log("farm", show("farm", post("/building", {"prefab": "ServerFarm01", **b})))
    elif MODE in ("loop-dry", "loop"):
        for name, pts in (("loop", LOOP), ("spur", SPUR)):
            body = {"prefab": "Medium Road", "points": pts, "curve": "smooth"}
            if MODE == "loop-dry":
                show(name, post("/road", {**body, "dryRun": True}))
            else:
                with paused(WHO):
                    log(name, show(name, post("/road", body)))
