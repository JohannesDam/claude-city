"""Council round 1 - Recreation, Beauty & Environment - lakeside-leisure-quarter.

WaterPark01 + IceflakeArena01 + a Pedestrian Street promenade along the SE lakeshore (x=-643, between the
z=-260 and z=120 Small Roads), with parks along it, a park on the south road and a green buffer strip
toward the WWTP. Street trees on the bounding roads.

Plan fixes found while re-checking the site (2031-01):
- the north road runs at z=120 and the south road at z=-260 (not 115/-265): the promenade snaps to them;
- the planned fit() boxes (x -760..-648 for the promenade, z 20..115 for the cafe, z -439..-271 for the
  buffer) do not contain the road midpoints they need, so placement here uses place_in_box(): sweep the
  given road edges at many t / both sides and accept only a dry run whose building centre lies in the
  target box.

Usage: python -I "council/r1_Recreation, Beauty & Environment_lakeside-leisure-quarter.py" core|promenade|parks|buffer|trees|verify [--dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, building, road, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
DEPT = "Recreation, Beauty & Environment"
MODE = sys.argv[1] if len(sys.argv) > 1 else "verify"
DRY = "--dry" in sys.argv
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "pos", "snapping", "segments")
LOG = r"C:\soft\cityskyline\logs\council\r1_recreation_lakeside_leisure_quarter.json"
PROM_X = -643
TS = [round(0.05 * i, 2) for i in range(1, 20)]


def show(label, r):
    print(f"{label:<34} {json.dumps(short(r or {}, keys=KEYS))}", flush=True)
    return r


def load():
    try:
        return json.load(open(LOG))
    except Exception:
        return {}


def log(key, val):
    d = load()
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def spent_add(r):
    if r and r.get("applied"):
        d = load()
        d["spent"] = d.get("spent", 0) + (r.get("cost") or 0)
        json.dump(d, open(LOG, "w"), indent=1)


def segs(x, z, radius, pred=None, filt=None):
    out = get("/nets", x=x, z=z, radius=radius, filter=filt).get("segments") or []
    return [s for s in out if pred is None or pred(s)]


def on_line_z(zv, x0, x1):
    """Pred: horizontal segment at z=zv whose midpoint x lies in [x0, x1]."""
    def p(s):
        return abs(s["start"][1] - zv) < 2 and abs(s["end"][1] - zv) < 2 and x0 <= (s["start"][0] + s["end"][0]) / 2 <= x1
    return p


def on_line_x(xv, z0, z1):
    def p(s):
        return abs(s["start"][0] - xv) < 2 and abs(s["end"][0] - xv) < 2 and z0 <= (s["start"][1] + s["end"][1]) / 2 <= z1
    return p


def place_in_box(label, prefab, edges, box, near=None, sides=("left", "right"), ts=TS):
    """Dry-run prefab on each edge/t/side; build the first spot whose centre is inside box (x0, x1, z0, z1)."""
    x0, x1, z0, z1 = box
    cands = []
    for s in edges:
        for t in ts:
            sx = s["start"][0] + (s["end"][0] - s["start"][0]) * t
            sz = s["start"][1] + (s["end"][1] - s["start"][1]) * t
            for side in sides:
                cands.append((((sx - near[0]) ** 2 + (sz - near[1]) ** 2) if near else 0, s["id"], t, side))
    cands.sort(key=lambda c: c[0])
    last = None
    tried = 0
    for _, eid, t, side in cands:
        d = post("/building", {"prefab": prefab, "edge": eid, "t": t, "side": side, "dryRun": True})
        tried += 1
        last = d
        p = d.get("pos")
        if d.get("errors") == 0 and not d.get("error") and p and x0 <= p[0] <= x1 and z0 <= p[1] <= z1:
            if DRY:
                return show(f"{label} (dry)", d)
            r = building(prefab, edge=eid, t=t, side=side)
            spent_add(r)
            show(label, r)
            return r
    show(f"{label} NO SPOT ({tried} tried)", last or {})
    return None


def placed(prefab, x, z, radius=300):
    d = get("/buildings", kind="all", x=x, z=z, radius=radius)
    return [b for b in d.get("buildings") or [] if b.get("prefab") == prefab]


def prom_edges(z0=-262, z1=122):
    return segs(PROM_X, (z0 + z1) / 2, 230, on_line_x(PROM_X, z0, z1))


if __name__ == "__main__":
    out = {}
    if MODE == "core":
        with paused(DEPT):
            # 1. WaterPark01 on the north shore corner (findspot rank 1 now; t=0.324 right -> (-708, 87.5))
            if placed("WaterPark01", -708, 87):
                print("WaterPark01 already placed")
            else:
                north = segs(-700, 120, 120, on_line_z(120, -760, -600))
                place_in_box("WaterPark01", "WaterPark01", north, (-760, -690, 60, 100), near=(-708, 120), sides=("right",))
            # 2. IceflakeArena01 signature (unique)
            sig = get("/prefabs", category="signature", filter="IceflakeArena01").get("prefabs") or [{}]
            if sig[0].get("placed"):
                print("IceflakeArena01 already placed - skipped")
            else:
                north = segs(-916, 120, 140, on_line_z(120, -1060, -760))
                place_in_box("IceflakeArena01", "IceflakeArena01", north, (-960, -860, 30, 60), near=(-916, 120), sides=("right",))
    elif MODE == "promenade":
        with paused(DEPT):
            pts = [[PROM_X, -260], [PROM_X, -75], [PROM_X, 120]]
            d = show("promenade (dry)", road("Pedestrian Street", pts, dryRun=True))
            if d.get("errors") == 0 and not d.get("error"):
                if not DRY:
                    r = show("promenade", road("Pedestrian Street", pts))
                    spent_add(r)
            else:
                # fallback: shift west and build segment by segment
                for a, b in (([-650, -260], [-650, -75]), ([-650, -75], [-650, 120])):
                    d = show(f"promenade x=-650 {a[1]}..{b[1]} (dry)", road("Pedestrian Street", [a, b], dryRun=True))
                    if d.get("errors") == 0 and not DRY:
                        spent_add(show("promenade x=-650", road("Pedestrian Street", [a, b])))
        print("promenade edges", [(s["id"], s["prefab"], s["start"], s["end"]) for s in prom_edges()])
    elif MODE == "parks":
        # 4. cafe between the arena (x<=-820) and the waterpark (x>=-756) on the north road
        with paused(DEPT):
            north = segs(-790, 120, 80, on_line_z(120, -840, -740))
            place_in_box("prom cafe ParkCafe01", "ParkCafe01", north, (-815, -760, 60, 112), near=(-788, 120), sides=("right",))
        # 5. west side of the promenade (building centre must be west of the street: x < -651)
        rows = [
            ("CampfireSite01", (-760, -655, -250, -140), (-643, -200)),
            ("SunkenPlaza01", (-760, -655, -135, -50), (-643, -95)),
            ("CityPark02", (-760, -655, -45, 5), (-643, -20)),
            ("BicyclePark01", (-760, -655, 5, 60), (-643, 30)),
        ]
        for prefab, box, near in rows:
            with paused(DEPT):
                place_in_box(f"prom {prefab}", prefab, prom_edges(), box, near=near)
        # 6. interior park on the north side of the south road (heading east: left = north)
        with paused(DEPT):
            south = segs(-990, -260, 160, on_line_z(-260, -1140, -880))
            place_in_box("prom park CityPark04", "CityPark04", south, (-1073, -900, -250, -100), near=(-990, -260), sides=("left",))
    elif MODE == "buffer":
        # 7. green buffer toward the WWTP on the south strip (south side of the z=-260 road: right)
        with paused(DEPT):
            south = segs(-990, -260, 200, on_line_z(-260, -1140, -840))
            place_in_box("buffer A CityPark11", "CityPark11", south, (-1129, -850, -439, -271), near=(-990, -260), sides=("right",))
            south = segs(-725, -260, 200, on_line_z(-260, -860, -600))
            place_in_box("buffer B DogPark02", "DogPark02", south, (-850, -600, -439, -271), near=(-725, -260), sides=("right",))
    elif MODE == "trees":
        # 8. street trees on the bounding roads (fresh ids: placing/splitting changed some), plus the promenade
        north = segs(-875, 120, 300, on_line_z(120, -1140, -600), filt="Small")
        south = segs(-875, -260, 300, on_line_z(-260, -1140, -600), filt="Small")
        ids = [s["id"] for s in north + south] + [s["id"] for s in prom_edges()]
        print("tree edges", len(ids), ids)
        with paused(DEPT):
            d = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both", "dryRun": True})
            todo = [e["id"] for e in d.get("edges") or [] if e.get("changes")]
            skipped = {e["id"]: e.get("skipped") for e in d.get("edges") or [] if e.get("skipped")}
            show("trees (dry)", d)
            print("skipped", skipped)
            if todo and not DRY:
                r = show("trees", post("/roadupgrade", {"edges": todo, "upgrade": "Trees", "side": "both"}))
                spent_add(r)
                log("trees", {"edges": todo, "skipped": skipped, "cost": r.get("cost")})
    elif MODE == "verify":
        prefabs = {"WaterPark01", "IceflakeArena01", "ParkCafe01", "CampfireSite01", "SunkenPlaza01", "CityPark02",
                   "BicyclePark01", "CityPark04", "CityPark11", "DogPark02"}
        found = []
        for b in get("/buildings", kind="all", x=-860, z=-160, radius=480).get("buildings") or []:
            if b.get("prefab") in prefabs:
                found.append({"id": b["id"], "prefab": b["prefab"], "pos": b["pos"], "roadConnected": b.get("roadConnected")})
        for f in found:
            print(f)
        print("promenade", [(s["id"], s["prefab"], s["start"], s["end"]) for s in prom_edges()])
        print("park services", get("/services", kind="park", brief=1).get("kinds", {}).get("park", {}).get("totals"))
        log("verify", found)
        print("SPENT", load().get("spent"))
