"""builder-east / b2-growth-south-rail-east-gate: Southern Rail, phase 1 (freight).

Route (west -> east), Double Train Track:
 A  branch off the SW mainline (edge 227838:171, (-2607,-2076)->(-2387,-1877)) at (-2560,-2034), S-curve
    (R >= 250 m) to the city's southern edge strip z -2160 at x -2150;
 C  the strip x -1860..-1262 is taken down to z -2175 by three ore/stone extractor lots, so the track dives into a
    tunnel (-15 m) from x -1930 to -1230 and comes back up at x -1050;
 D  ramp up to a TrussArchBridge01 (deck 520 m abs., ~13 m above the water, one 600 m span x -860..-260) and down
    again east of the river at x -110 (parallel to and ~100 m south of the leg's road bridge);
 E  east through tile (0,-1870) (owned) and the bought tiles (623,-1870), (1247,-1870) at z ~ -1650, then
    north-east into the owned triangle south of the southern leg, where CargoTrainTerminal02 stands.
Stages: geo | dry | tiles | build-west | build-east | terminal | route
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import call, get, post, paused  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-east"
LOG = r"C:\soft\cityskyline\logs\council\builder-east_southern-rail.json"
TRACK = "Double Train Track"
BRIDGE = "TrussArchBridge01"
DECK = 520.0
Z = -2160.0

BRANCH = {"prefab": TRACK, "start": [-2560, -2034], "end": [-2150, Z], "startDir": [0.743, 0.669], "endDir": [1, 0],
          "fit": {"tolerance": 40, "minRadius": 250}}
TUNNEL = [[-2150, Z, 0], [-1930, Z, -15], [-1230, Z, -15], [-1050, Z, 0]]


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def deck_pts(xs):
    t = call("POST", "/terrain", {"points": [[float(x), Z] for x in xs]})["points"]
    return [[x, Z, round(DECK - q["height"], 1)] for x, q in zip(xs, t)]


def river():
    b = deck_pts([-860, -260])
    west = [[-1050, Z, 0], b[0]]
    east = [b[1], [-110, Z, 0]]
    return [("west ramp", {"prefab": TRACK, "points": west}), ("bridge", {"prefab": BRIDGE, "points": b}),
            ("east ramp", {"prefab": TRACK, "points": east})]


def show(label, r):
    keys = ("applied", "cost", "errors", "problems", "error", "wouldDemolish")
    out = {k: r.get(k) for k in keys if r.get(k) not in (None, [])}
    if r.get("conflicts"):
        out["conflicts"] = [(c.get("error"), c.get("prefab"), c.get("at")) for c in r["conflicts"][:4]]
    g = r.get("geometry") or {}
    if g.get("warnings"):
        out["warn"] = g["warnings"]
    print(f"{label:<12} {json.dumps(out)[:700]}", flush=True)


stage = sys.argv[1] if len(sys.argv) > 1 else "dry"
if stage == "dry":
    r = post("/roadfit", {**BRANCH, "dryRun": True})
    ch = r.get("chosen") or {}
    print("branch", ch.get("variant"), (ch.get("geometry") or {}).get("minRadius"), ch.get("errors"), r.get("error"))
    show("tunnel", post("/road", {"prefab": TRACK, "points": TUNNEL, "dryRun": True}))
    for nm, body in river():
        show(nm, post("/road", {**body, "dryRun": True}))
