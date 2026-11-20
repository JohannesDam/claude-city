"""builder-central: Downtown transit core, renewal sub-phase 1b (worklist b1-housing-downtown-transit-core-p1).

Box x -1895..-1705, z 1240..1600, east of the T1 avenue (1a west of it has grown back: 19 high + 11 mixed).
Only the 34 detached low-density homes (34 households) are cleared. The avenue-side high-rises (x~-1880), the
medium row at x~-1714, the FireHouse 393144:1, PoliceStation03 432128:5, EmergencyShelter01 and PostOffice02
stay. The cleared cells become NA Residential High, with a Residential LowRent pocket (~10%).
Rect zoning avoids the medium row (x > -1736) and the firehouse lot.

Run: cd C:/soft/cityskyline/tools && python -I builders/builder-central_transit-core-1b.py clear|zone [--dry]
"""
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short  # noqa: E402

WHO = "builder-central"
DRY = "--dry" in sys.argv
STAGE = next((a for a in sys.argv[1:] if not a.startswith("--")), "clear")
BOX = [-1895, 1240, -1705, 1600]
ZONES = [
    ("NA Residential High", [[-1866, 1296], [-1738, 1340]]),    # south side of the z=1340 street
    ("NA Residential High", [[-1866, 1348], [-1824, 1446]]),    # z 1340..1450 block, west of the firehouse
    ("NA Residential High", [[-1784, 1348], [-1738, 1446]]),    # east of the firehouse
    ("NA Residential High", [[-1824, 1348], [-1784, 1398]]),    # south of the firehouse
    ("NA Residential High", [[-1866, 1456], [-1738, 1600]]),    # z 1450..1610 block
    ("Residential LowRent", [[-1772, 1456], [-1738, 1530]]),    # ~10%
]


def stage_clear():
    d = post("/bulldoze", {"box": BOX, "buildings": True, "dryRun": True})
    ids = [b["id"] for b in d["buildings"]["list"] if b["kind"] == "zoned: NA Residential Low"]
    print(len(ids), "low-density homes")
    if DRY:
        return
    with paused(WHO):
        print("bulldoze", short(post("/bulldoze", {"ids": ids}), keys=("applied", "errors", "error")))


def stage_zone():
    with paused(WHO):
        for name, pts in ZONES:
            z = post("/zone", {"zone": name, "mode": "rect", "points": pts, "overwrite": True, "dryRun": DRY})
            print(name, pts, {k: z.get(k) for k in ("applied", "cellsChanged", "cellsFromZone", "occupiedCellsRezoned", "error")})


if __name__ == "__main__":
    {"clear": stage_clear, "zone": stage_zone}[STAGE]()
