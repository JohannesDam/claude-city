"""Council round 1 - Utilities - grid-hookup.

1) TransformerStation02 beside the loose end of the outside high-voltage line (node 344419:1 at (-2022.2,1515.8)).
2) Probe: dry-run a High-voltage Line from that end to the transformer's own connection node; the
   snapping report must show a node snap at BOTH ends.
3) STOP RULE: if the transformer end does not snap, no loose lines are built and the incinerator transformers
   are skipped (mod ticket: Build.cs SnapPoint ignores building-owned nodes).
4) If the probe snaps: build the line, then the same for both incinerators.
6) EmergencyBatteryStation01 in SE utilities (edge 1123630:69, t=0.152, left).

Usage: python -I council/r1_utilities_grid_hookup.py ts02|probe|line|incinerators|battery|verify
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, paused, building, road, find_spot, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "verify"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "facing", "pos", "snapping")
LOG = r"C:\soft\cityskyline\logs\council\r1_utilities_grid_hookup.json"
LINE_END = [-2022.2, 1515.8]  # node 344419:1, degree 1
PLANNED_TS02 = dict(edge="302922:1", t=0.632, side="right")
INCINERATORS = {
    "NE": dict(ts=dict(edge="674860:45", t=0.379, side="right"), icon=[-116.7, 3631.2], via=[[-40, 3470]]),
    "SE": dict(ts=dict(edge="645239:77", t=0.586, side="right"), icon=[-1380.5, -1592.5], via=[[-1380.5, -1735]]),
}
BATTERY = dict(edge="1123630:69", t=0.152, side="left")


def show(label, r):
    print(f"{label:<44} {json.dumps(short(r, keys=KEYS))}", flush=True)
    return r


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def not_connected():
    p = get("/problems", type="Powerline Not Connected").get("problems") or []
    return p[0]["examples"] if p else []


def place_first(prefab, opts, label):
    """Dry-run each option in order and build the first one with 0 errors."""
    for b in opts:
        d = show(f"{label} {b} (dry)", building(prefab, **b, dryRun=True))
        if d.get("errors") == 0 and not d.get("error"):
            return show(label, building(prefab, **b))
    return None


if __name__ == "__main__":
    if MODE == "ts02":
        opts = []
        for c in find_spot("TransformerStation02", -2022, 1500, radius=120, n=4):
            b = dict(c["building"])
            b.pop("prefab", None)
            opts.append(b)
        opts.insert(0, PLANNED_TS02)
        with paused("utilities"):
            r = place_first("TransformerStation02", opts, "TS02 outside line")
        log("ts02", r)
        print("SPENT", (r or {}).get("cost") if (r or {}).get("applied") else 0)
    elif MODE == "probe":
        d = json.load(open(LOG))
        ts = d["ts02"]["placedId"]
        icons = not_connected()
        print("Powerline Not Connected:", json.dumps(icons))
        mine = [i for i in icons if i.get("owner") == ts]
        print("high-voltage nets near TS02:", json.dumps([(s["id"], s["prefab"], s["start"], s["end"]) for s in
                                                           get("/nets", x=d["ts02"]["pos"][0], z=d["ts02"]["pos"][1], radius=60, filter="voltage")["segments"]]))
        targets = [m["pos"] for m in mine] or [d["ts02"]["pos"]]
        with paused("utilities"):
            for tp in targets:
                show(f"HV line {LINE_END}->{tp} (dry)", road("High-voltage Line", [LINE_END, tp], dryRun=True))
    elif MODE == "battery":
        with paused("utilities"):
            r = place_first("EmergencyBatteryStation01", [BATTERY], "Battery SE utilities")
        log("battery", r)
        print("SPENT", (r or {}).get("cost") if (r or {}).get("applied") else 0)
    elif MODE == "verify":
        print("Powerline Not Connected:", json.dumps(not_connected()))
        print("city electricity:", json.dumps(get("/city").get("electricity")))
        print(json.dumps(get("/buildings", kind="service", x=-2022, z=1480, radius=60))[:800])
        print(json.dumps(get("/buildings", kind="service", x=-1107, z=-1555, radius=60))[:800])
