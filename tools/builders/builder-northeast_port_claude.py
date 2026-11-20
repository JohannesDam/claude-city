"""builder-northeast - b1-growth-port-claude-harbour: Port Claude phase 1.

Tiles (1247,3740), (1870,3740), (2493,3740) bought. Layout:
- Port Link: tree-lined Medium Road from the Eastern Works freight stub end (1530,3412) north-east to the SW
  junction J_sw, then an overpass (9 m, ~7% ramps) across the NE highway pair perpendicular to it, landing at
  the NE junction J_ne.
- Diamond interchange: four 1-lane highway ramps. SE-bound carriageway (L1, SW side): off-ramp before the
  crossing -> J_sw, on-ramp J_sw -> L1 after the crossing. NW-bound carriageway (L2, NE side): off-ramp before
  the crossing -> Harbour Road, on-ramp J_ne -> L2 after the crossing.
- Harbour Road: tree-lined Medium Road from J_ne curving east to the Quay Road.
- Quay Road: straight Medium Road at x=2367.5 (z 3510..4010) along the cargo harbour's land side.
- CargoHarbor01 at (2512,3760), angle 270 (front faces west onto the Quay Road, quay on the sea).

Usage: python -I builders/builder-northeast_port_claude.py dry|<piece>|harbour-dry|harbour|verify
"""
import json
import math
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_port_claude.json"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "wouldDemolish",
        "conflicts", "pos")

U = (0.805, -0.593)   # highway direction, NW -> SE (SE-bound carriageway L1)
N = (0.593, 0.805)    # perpendicular, towards the NE (L2 lies ~21 m NE of L1)
C0 = (1796.5, 3738.5)  # crossing point on L1 (middle of edge (1724,3792)-(1869,3685))


def C(s, e=None):
    p = [round(C0[0] + s * N[0], 1), round(C0[1] + s * N[1], 1)]
    return p + [e] if e is not None else p


J_SW, J_NE = C(-170), C(190)
L1_DIR, L2_DIR = list(U), [-U[0], -U[1]]

def P(along, s):
    """Point at `along` m along the highway (SE positive) and `s` m across it (NE positive) from C0."""
    return [round(C0[0] + along * U[0] + s * N[0], 1), round(C0[1] + along * U[1] + s * N[1], 1)]


L2S = 20.8  # L2 (NW-bound) lies 20.8 m NE of L1
RAMP = "Highway Oneway - 1 lane"
PIECES = {
    # Port Link at grade: freight stub end -> J_sw, arriving in the overpass direction
    "link": {"prefab": "Medium Road", "points": [[1530, 3412], [1585, 3500], J_SW], "curve": "smooth",
             "endDir": list(N)},
    # overpass: flat at 9 m from s=-40 to s=+60 (L1 footprint s -6..6, L2 s 15..27)
    "overpass": {"prefab": "Medium Road", "points": [J_SW + [0], C(-40, 9), C(60, 9), J_NE + [0]]},
    # Harbour Road: J_ne north-east, then east to the north end of the Quay Road
    "harbour road": {"prefab": "Medium Road", "points": [J_NE, [1975, 3965], [2110, 3992], [2250, 3995], [2367.5, 3995]],
                     "curve": "smooth", "startDir": list(N), "endDir": [1, 0]},
    "quay road": {"prefab": "Medium Road", "points": [[2367.5, 3995], [2367.5, 3510]]},
    # diamond: every ramp ends/starts at J_sw or J_ne and joins its carriageway 300 m from the crossing
    "ramp L1 off": {"prefab": RAMP, "points": [P(-300, 0), P(-200, -25), P(-100, -95), J_SW], "curve": "smooth",
                    "startDir": L1_DIR},
    "ramp L1 on": {"prefab": RAMP, "points": [J_SW, P(100, -95), P(200, -25), P(300, 0)], "curve": "smooth",
                   "endDir": L1_DIR},
    "ramp L2 off": {"prefab": RAMP, "points": [P(300, L2S), P(200, L2S + 25), P(100, L2S + 95), J_NE], "curve": "smooth",
                    "startDir": L2_DIR},
    "ramp L2 on": {"prefab": RAMP, "points": [J_NE, P(-100, L2S + 95), P(-200, L2S + 25), P(-300, L2S)], "curve": "smooth",
                   "endDir": L2_DIR},
}
# Port logistics quarter (jobs push: industrial demand with no free lots). SW of the highway: Logistics Way
# runs from the Port Link west towards the old x=1190 Eastern Works street; Dock Lane loops north of it; Ramp Close is a
# cul-de-sac kept >80 m from the SE-bound off-ramp. NE of the highway: Cargo Street from the Quay Road to
# Harbour Road. All zoning stays >= 50 m from the highway and ramps (tree/grass buffer).
QUARTER = {
    "logistics way": {"prefab": "Medium Road", "points": [[1563, 3470], [1440, 3560], [1300, 3610], [1190, 3545],
                                                          [1182, 3465]], "curve": "smooth"},  # stops short of the
    # x=1190 corner: joining it would demolish two factories (891354:79, 940633:71)
    "dock lane": {"prefab": "Medium Road", "points": [[1360, 3592], [1350, 3700], [1300, 3820], [1180, 3900],
                                                      [1040, 3880], [990, 3760], [1030, 3640], [1232, 3577]],
                  "curve": "smooth"},
    "ramp close": {"prefab": "Small Road", "points": [[1480, 3537], [1492, 3660], [1500, 3790]], "curve": "smooth"},
    "cargo street": {"prefab": "Medium Road", "points": [[2367.5, 3640], [2250, 3650], [2120, 3720], [2080, 3830],
                                                         [2075, 3986]], "curve": "smooth"},
}
QUARTER_ZONES = [[[940, 3440], [1610, 4040]], [[1990, 3500], [2360, 3984]]]
HARBOUR = {"prefab": "CargoHarbor01", "x": 2512, "z": 3760, "angle": 270}


def show(label, r):
    out = short(r, keys=KEYS)
    g = r.get("geometry") or {}
    if g:
        out["geo"] = {k: g.get(k) for k in ("length", "minRadius", "maxSlope", "warnings") if g.get(k) is not None}
    print(f"{label:<16} {json.dumps(out)[:700]}", flush=True)
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
        print("J_sw", J_SW, "J_ne", J_NE)
        for name, spec in PIECES.items():
            show(name, post("/road", {**spec, "dryRun": True}))
    elif MODE == "quarter-dry":
        for name, spec in QUARTER.items():
            r = show(name, post("/road", {**spec, "dryRun": True}))
            print("   snap", [x.get("snappedTo") for x in r.get("snapping") or []])
    elif MODE in QUARTER:
        with paused(WHO):
            log(MODE, show(MODE, post("/road", QUARTER[MODE])))
    elif MODE in ("qzone-dry", "qzone"):
        for pts in QUARTER_ZONES:
            body = {"zone": "Industrial Manufacturing", "mode": "rect", "points": pts, "overwrite": False}
            if MODE == "qzone-dry":
                show(f"zone {pts}", post("/zone", {**body, "dryRun": True}))
            else:
                with paused(WHO):
                    log(f"zone {pts}", show(f"zone {pts}", post("/zone", body)))
    elif MODE in PIECES:
        with paused(WHO):
            log(MODE, show(MODE, post("/road", PIECES[MODE])))
    elif MODE in ("harbour-dry", "harbour"):
        if MODE == "harbour-dry":
            show("harbour (dry)", post("/building", {**HARBOUR, "dryRun": True}))
        else:
            with paused(WHO):
                log("harbour", show("harbour", post("/building", HARBOUR)))
    elif MODE == "verify":
        print(json.dumps(get("/lines"))[:800])
