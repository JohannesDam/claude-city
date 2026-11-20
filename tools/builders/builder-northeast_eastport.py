"""builder-northeast - b1-economy-eastport-harbour-oil: Eastport (phase A land, B bridge, E first industry).

Tiles (2493,3117) and (2493,2493) bought. Eastport Bridge: Medium Road at 10 m (slopes 4.5%) from the
Riverside Drive east leg (2111,3040) across the channel, landing in the wedge at (2680,2972); Harbour
Boulevard: tree-lined Medium Road from the landing south through the wedge between channel and highway.
Industrial Manufacturing on both sides of the boulevard, >= 40 m from the channel and the highway.

Usage: python -I builders/builder-northeast_eastport.py dry|bridge|boulevard|zone|trees
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_eastport.json"
KEYS = ("applied", "cost", "errors", "problems", "wouldDemolish", "conflicts")
PIECES = {
    "bridge": {"prefab": "Medium Road", "points": [[2111, 3040, 0], [2230, 3030, 10], [2550, 2990, 10], [2680, 2972, 0]]},
    "boulevard": {"prefab": "Medium Road", "points": [[2680, 2972], [2640, 2880], [2590, 2760], [2565, 2600],
                                                      [2570, 2420], [2610, 2260]], "curve": "smooth"},
}
ZONES = [[[2440, 2230], [2700, 2940]]]


def show(label, r):
    out = short(r, keys=KEYS)
    g = r.get("geometry") or {}
    if g:
        out["geo"] = {k: g.get(k) for k in ("length", "minRadius", "maxSlope", "warnings") if g.get(k) is not None}
    out["snap"] = [s.get("snappedTo") for s in r.get("snapping") or []] or None
    print(f"{label:<10} {json.dumps(out)[:600]}", flush=True)
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
        for k, b in PIECES.items():
            show(k, post("/road", {**b, "dryRun": True}))
    elif MODE in PIECES:
        with paused(WHO):
            log(MODE, show(MODE, post("/road", PIECES[MODE])))
    elif MODE == "zone":
        for pts in ZONES:
            b = {"zone": "Industrial Manufacturing", "mode": "rect", "points": pts}
            d = show("zone dry", post("/zone", {**b, "dryRun": True}))
            print("cells", d.get("cellsChanged"))
            with paused(WHO):
                log(f"zone {pts}", show("zone", post("/zone", b)))
    elif MODE == "trees":
        with paused(WHO):
            r = post("/roadupgrade", {"box": [2440, 2230, 2700, 2990], "prefabFilter": "Medium Road",
                                      "upgrade": "Trees", "side": "both"})
        show("trees", r)
