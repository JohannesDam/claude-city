"""builder-northeast - b2-utilities-port-claude-recovery-park: Port Claude Resource Recovery Park.

Tile (1870,4363) bought for depth (only its south part is used; z > 4250 stays free for the airport).
Recovery Lane (Medium Road, trees) leaves Harbour Road at the J_ne node (1975,3965), runs north and curves west at
z ~4190; RecyclingCenter01 (west) and IncinerationPlant01 (east, downwind) front it from the south, between the
lane and the NE highway ramp (z 3891), behind a tree/park belt toward the highway.

Usage: python -I builders/builder-northeast_recovery_park.py lane|place-dry|place|belt
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "place-dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_recovery_park.json"
LANE = [[1975, 3965], [1988, 4060], [1950, 4150], [1850, 4188], [1700, 4190], [1570, 4165]]
PARK_BOX = [1500, 3900, 2000, 4240]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def place(prefab, near, dry):
    r = post("/place", {"prefab": prefab, "near": near, "within": PARK_BOX, "radius": 250, "maxHouseholds": 0,
                        "maxCompanies": 0, "dryRun": dry})
    ch = r.get("chosen") or {}
    print(prefab, "placed" if r.get("placed") else "dry", r.get("placedId"), r.get("error"), ch.get("x"), ch.get("z"),
          ch.get("corners") and [[round(a) for a in p] for p in ch["corners"]], ch.get("cost"), flush=True)
    return r


if __name__ == "__main__":
    if MODE == "lane":
        with paused(WHO):
            r = post("/road", {"prefab": "Medium Road", "points": LANE, "curve": "smooth"})
            print("lane", short(r))
            log("lane", r)
            for up in ("Trees", "Grass"):
                u = post("/roadupgrade", {"polyline": LANE, "tolerance": 15, "prefabFilter": "Medium Road",
                                          "upgrade": up, "side": "both"})
                print(up, u.get("applied"), u.get("cost"), u.get("errors"))
    elif MODE in ("place-dry", "place"):
        dry = MODE == "place-dry"
        if dry:
            place("RecyclingCenter01", [1640, 4080], True)
            place("IncinerationPlant01", [1830, 4060], True)
        else:
            with paused(WHO):
                log("recycling", place("RecyclingCenter01", [1640, 4080], False))
                log("incinerator", place("IncinerationPlant01", [1830, 4060], False))
