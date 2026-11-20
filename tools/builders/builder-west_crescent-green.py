"""builder-west, item b2-renewal-swq-p2-crescent-green - SWQ renewal phase 2 "Crescent Green", sub-phases 2b and 2c.

Cap 600 households per sub-phase (West Gate's zoning is in: ~2,000 hh, plus North Woods ~2,100 hh in the bank).
2b: "Crescent Way" - a Medium Road with Trees and Grass on a 240 m-radius arc (sagitta 45 m) from the SWQ
    roundabout (-3180,-650) to the Southgate link node (-2900,-650), replacing the straight z=-650 Small Road.
    Clears the z=-650 south row (6 L5 Medium buildings, ~496 hh). Mixed (shops below homes) on its south side.
    The north row (10 buildings, ~379 hh) loses its street and is rebuilt in 2c, in the very next lock.
2c: "Crescent Loop" - a Small Road with Trees from Crescent Way's nodes (-3113.5,-683.5) and (-2966.5,-683.5)
    north around an apex at z=-612, replacing the north row. Mixed on Crescent Way's north side, Medium on the
    loop, a pocket park; the shelter on the spine stays.
Usage: python -I builders/builder-west_crescent-green.py 2b|2c [dry]
"""
import json
import math
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, demolition_cost, find_spot, get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
ITEM = "b2-renewal-swq-p2-crescent-green"
DRY = "dry" in sys.argv[2:]
LOG = r"C:\soft\cityskyline\logs\council\demolitions.jsonl"
SWQ = [-4030, -920, -2826, -310]
CAP = 600

SAG = 45.0
R = (140 ** 2 + SAG ** 2) / (2 * SAG)
ACZ = -650 - SAG + R                     # arc centre z (x = -3040)


def arc_pt(f):
    phi = f * math.asin(140 / R)
    return [round(-3040 + R * math.sin(phi), 1), round(ACZ - R * math.cos(phi), 1)]


CRESCENT_WAY = [arc_pt(f) for f in (-1, -0.5, 0, 0.5, 1)]
LOOP = [CRESCENT_WAY[1], [-3100, -635], [-3040, -612], [-2980, -635], CRESCENT_WAY[3]]
OLD_Z650 = ("1267733:21", "1267726:17", "1267621:19")
SOUTH_ROW = [-3170, -705, -2905, -655]
NORTH_ROW = [-3170, -645, -2905, -595]


def log(what, would):
    if not would:
        return
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": WHO, "prefab": what, "item": ITEM,
                            "ids": [w.get("id") for w in would], "cost": hh_cost(would) if "kind" in would[0] else demolition_cost(would),
                            "note": "renewal sub-phase <= 600 hh; capacity zoned first (West Gate, North Woods)"}) + "\n")


def hh_cost(lst):
    return {"buildings": len(lst), "households": sum(x.get("households") or 0 for x in lst),
            "companies": sum(1 for x in lst if (x.get("workplaces") or 0) > 0 and not (x.get("households") or 0))}


def zoned_buildings(box):
    d = post("/bulldoze", {"box": box, "buildings": True, "dryRun": True})
    lst = (d.get("buildings") or {}).get("list") or []
    return [x for x in lst if x.get("kind", "").startswith("zoned")], [x for x in lst if not x.get("kind", "").startswith("zoned")]


def side_of(s, px, pz, toward):
    (sx, sz), (ex, ez) = s["start"], s["end"]
    mx, mz = (sx + ex) / 2, (sz + ez) / 2
    right = (ez - sz) * (px - mx) - (ex - sx) * (pz - mz) > 0
    if toward:
        return "right" if right else "left"
    return "left" if right else "right"


def edges_on(pts, prefab, tol=6.0):
    """Current edges of a polyline built from pts (midpoint within tol of the sampled curve)."""
    samples = []
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        for i in range(21):
            samples.append((x0 + (x1 - x0) * i / 20, z0 + (z1 - z0) * i / 20))
    cx = sum(p[0] for p in pts) / len(pts)
    cz = sum(p[1] for p in pts) / len(pts)
    out = []
    for s in get("/nets", x=cx, z=cz, radius=220, filter=prefab).get("segments") or []:
        mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
        if s["prefab"] == prefab and min((mx - a) ** 2 + (mz - b) ** 2 for a, b in samples) < (tol + 8) ** 2:
            out.append(s)
    return out


def place(prefab, box, near, radius=150):
    for c in find_spot(prefab, near[0], near[1], radius=radius, n=40, within=SWQ):
        if box[0] <= c["x"] <= box[1] and box[2] <= c["z"] <= box[3]:
            b = dict(c["building"])
            b.pop("prefab", None)
            d = post("/building", {"prefab": prefab, **b, "dryRun": True})
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                r = building(prefab, **b)
                print(f"  {prefab}: {short(r, ('applied', 'cost', 'placedId', 'roadConnected'))} at ({c['x']}, {c['z']})", flush=True)
                return r.get("placedId")
    print(f"  {prefab}: no clean spot in {box}", flush=True)
    return None


def stage_2b():
    with paused(WHO):
        zb, other = zoned_buildings(SOUTH_ROW)
        cost = hh_cost(zb)
        print("2b south row:", cost, "others kept:", [(x["prefab"], x["pos"]) for x in other], flush=True)
        if DRY or cost["households"] > CAP or cost["companies"]:
            print("STOP (dry run or over the cap)", flush=True)
            return
        r = post("/bulldoze", {"ids": [x["id"] for x in zb]})
        print("south row removed:", short(r, ("applied", "errors")), flush=True)
        log("SWQ 2b south row (Crescent Way)", zb)
        r = post("/bulldoze", {"edges": list(OLD_Z650)})
        print("old z=-650 street removed:", short(r, ("applied", "errors", "edgeCount")), flush=True)
        # /roadfit: the given arc first, nudged variants if the junction ends need it; no further demolition
        r = post("/roadfit", {"prefab": "Medium Road", "points": CRESCENT_WAY,
                              "fit": {"tolerance": 25, "minRadius": 150, "allowDemolish": False}}, timeout=300)
        ch = r.get("chosen") or next((c for c in r.get("candidates") or [] if c.get("applied")), {})
        print("Crescent Way:", r.get("applied"), "variant", ch.get("variant"), "minR", (ch.get("geometry") or {}).get("minRadius"),
              "demolition", ch.get("demolition"), "errors", ch.get("errors"), flush=True)
        if not r.get("applied"):
            print("STOP: Crescent Way did not build:", [(c.get("variant"), c.get("problems")) for c in r.get("candidates") or []], flush=True)
            return
        time.sleep(1)
        for upg in ("Trees", "Grass"):
            t = post("/roadupgrade", {"polyline": CRESCENT_WAY, "tolerance": 10, "prefabFilter": "Medium Road", "upgrade": upg, "side": "both"})
            print(f"  {upg}: {short(t, ('applied', 'cost', 'errors'))}", flush=True)
        cells = 0
        for s in edges_on(CRESCENT_WAY, "Medium Road"):
            z = zone("NA Residential Mixed", mode="road", edges=[s["id"]], depth=4, side=side_of(s, -3040, -900, True), overwrite=False)
            cells += z.get("cellsChanged") or 0
        print("Mixed cells on the south side:", cells, flush=True)


def stage_2c():
    out = {}
    with paused(WHO):
        zb, other = zoned_buildings(NORTH_ROW)
        cost = hh_cost(zb)
        print("2c north row:", cost, "others kept:", [(x["id"], x["prefab"], x["pos"]) for x in other], flush=True)
        d = post("/road", {"prefab": "Small Road", "points": LOOP, "curve": "smooth", "dryRun": True, "allowDemolish": True})
        g = d.get("geometry") or {}
        print("Crescent Loop dry:", short(d, ("errors", "problems", "cost")), "minR", g.get("minRadius"),
              demolition_cost(d.get("wouldDemolish")), flush=True)
        if DRY or cost["households"] > CAP or cost["companies"]:
            print("STOP (dry run or over the cap)", flush=True)
            return
        r = post("/bulldoze", {"ids": [x["id"] for x in zb]})
        print("north row removed:", short(r, ("applied", "errors")), flush=True)
        log("SWQ 2c north row (Crescent Loop)", zb)
        r = post("/road", {"prefab": "Small Road", "points": LOOP, "curve": "smooth", "allowDemolish": True})
        print("Crescent Loop:", short(r, ("applied", "cost", "errors")), flush=True)
        time.sleep(1)
        t = post("/roadupgrade", {"polyline": LOOP, "tolerance": 10, "prefabFilter": "Small Road", "upgrade": "Trees", "side": "both"})
        print(f"  Trees: {short(t, ('applied', 'cost', 'errors'))}", flush=True)
        p = post("/place", {"prefab": "PocketPark05", "near": [-3040, -655], "within": [-3100, -680, -2980, -625],
                            "radius": 80, "maxHouseholds": 0, "maxCompanies": 0})
        out["pocket"] = p.get("placedId")
        print("  PocketPark05:", short(p, ("applied", "placedId", "errors")), flush=True)
        cells = {}
        for s in edges_on(CRESCENT_WAY, "Medium Road"):
            z = zone("NA Residential Mixed", mode="road", edges=[s["id"]], depth=4, side=side_of(s, -3040, -500, True), overwrite=False)
            cells["mixed"] = cells.get("mixed", 0) + (z.get("cellsChanged") or 0)
        for s in edges_on(LOOP, "Small Road"):
            z = zone("NA Residential Medium", mode="road", edges=[s["id"]], depth=4, side="both", overwrite=False)
            cells["medium"] = cells.get("medium", 0) + (z.get("cellsChanged") or 0)
        print("zoned cells:", cells, "capacity ~", round(cells.get("mixed", 0) * 2.5 + cells.get("medium", 0) * 2.0), flush=True)
    print(json.dumps(out))


if __name__ == "__main__":
    {"2b": stage_2b, "2c": stage_2c}[sys.argv[1]]()
