"""builder-northeast - b1-economy-ew-riverside-industry (+ b1-parks-signature-industrial-07): Eastern Works Riverside.

Owned tile (1870,3117): x 1559..2181, z 2805..3428, east of the x=1530 Medium Road; fire station at x 1542-1742,
z 3036-3156 stays. Layout:
- Riverside Drive: curved Medium Road loop from the x=1530 freight stub end (1530,3412) along the north edge,
  down the channel side and back west to the existing junction (1530,2930). Street trees both sides.
- Riverside Crescent: one curved Small Road from the north leg (x~1950) through the estate to the south leg
  (x~1835), so trucks can circulate and the deep interior gets frontage.
- IndustrialManufacturingSignature07 (240 x 176 m) fronting the north leg at the Port Link corner (lot about
  x 1580-1820, z 3207-3383): the estate's landmark next to the future Port Link (the curved east leg is too
  curved for a 240 m frontage).
- Industrial Manufacturing zoning by rectangles; unzoned: a 50-60 m green bank along the channel (east of the
  east leg) and the bridge corridor south-east of the loop (x > 1940, z < 2935).

Usage: python -I builders/builder-northeast_riverside.py drive-dry|drive|closes-dry|closes|sig-dry|sig|zone-dry|zone|trees|verify
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "verify"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_riverside.json"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "wouldDemolish",
        "conflicts", "pos", "builtEdges")

DRIVE = [[1530, 3412], [1660, 3398], [1830, 3392], [1990, 3372], [2085, 3305], [2118, 3190], [2112, 3070],
         [2068, 2982], [1960, 2938], [1780, 2922], [1640, 2926], [1530, 2930]]
# cul-de-sacs south from the north leg (start points sit on the drive's north leg)
CLOSES = {
    "riverside crescent": [[1950, 3380.5], [1966, 3262], [1928, 3150], [1858, 3050], [1836, 2923]],
}
SIG = "IndustrialManufacturingSignature07"
ZONE_RECTS = [  # Industrial Manufacturing; cells only exist next to roads, existing zoning is not overwritten
    [[1540, 2945], [2110, 3404]],   # inside the loop + north leg + west strip along x=1530
    [[1540, 2860], [1940, 2924]],   # south side of the south leg (the corridor x > 1940 stays free)
]


def show(label, r):
    print(f"{label:<28} {json.dumps(short(r, keys=KEYS))[:900]}", flush=True)
    return r


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def road(prefab, pts, dry):
    return post("/road", {"prefab": prefab, "points": pts, "curve": "smooth", "dryRun": dry})


if __name__ == "__main__":
    if MODE in ("drive-dry", "drive"):
        dry = MODE == "drive-dry"
        if dry:
            show("drive (dry)", road("Medium Road", DRIVE, True))
        else:
            with paused(WHO):
                log("drive", show("drive", road("Medium Road", DRIVE, False)))
    elif MODE in ("closes-dry", "closes"):
        dry = MODE == "closes-dry"
        for name, pts in CLOSES.items():
            if dry:
                show(name + " (dry)", road("Small Road", pts, True))
            else:
                with paused(WHO):
                    log(name, show(name, road("Small Road", pts, False)))
    elif MODE in ("sig-dry", "sig"):
        req = {"prefab": SIG, "edge": "1863988:7", "t": 0.5, "side": "right"}  # validated by dry run
        if len(sys.argv) > 2:
            req = json.loads(sys.argv[2])
        if MODE == "sig-dry":
            show("signature (dry)", post("/building", {**req, "dryRun": True}))
        else:
            with paused(WHO):
                log("signature", show("signature", post("/building", req)))
    elif MODE in ("zone-dry", "zone"):
        for pts in ZONE_RECTS:
            body = {"zone": "Industrial Manufacturing", "mode": "rect", "points": pts, "overwrite": False}
            if MODE == "zone-dry":
                show(f"zone {pts} (dry)", post("/zone", {**body, "dryRun": True}))
            else:
                with paused(WHO):
                    log(f"zone {pts}", show(f"zone {pts}", post("/zone", body)))
    elif MODE == "trees":
        with paused(WHO):
            r = post("/roadupgrade", {"polyline": DRIVE, "tolerance": 30, "prefabFilter": "Medium Road",
                                      "upgrade": "Trees", "side": "both"})
        show("drive trees", r)
        print("edges", len(r.get("edges") or []))
    elif MODE == "verify":
        print(json.dumps(get("/zonestats"))[:1500])
