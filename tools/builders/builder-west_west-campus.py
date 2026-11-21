"""builder-west, jobs priority 2033-01 (office demand 98, unemployment the biggest happiness penalty):
"West Campus" - Office on the free owned land in the south-west corner of the Southwest Business Park
(free rect x -4670..-4302, z -1558..-1174, no road yet).

  - "Campus Way": Medium Road with Trees from the SWBP south road node (-4245,-1190) west to the campus circle;
  - "Campus Circle": Medium Road with Trees, a ring of radius 110 m around (-4480,-1380);
  - a small park inside the circle (placed before zoning);
  - zoning: Office High on Campus Way and the circle's outer side, Office Low on its inner side.
Roads carry water, sewage and power in this game; the SWBP network has no utility notifications.
Usage: python -I builders/builder-west_west-campus.py [dry]
"""
import json
import math
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import demolition_cost, get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[1:]
CX, CZ, R = -4480.0, -1380.0, 110.0


def cpt(deg):
    t = math.radians(deg)
    return [round(CX + R * math.cos(t), 1), round(CZ + R * math.sin(t), 1)]


CAMPUS_WAY = [[-4245, -1190], [-4360, -1222], [CX, CZ + R]]
ARC_W = [cpt(d) for d in (90, 135, 180, 225, 270)]
ARC_E = [cpt(d) for d in (90, 45, 0, -45, -90)]
SWBP = [-4675, -1558, -3428, -935]


LOG = r"C:\soft\cityskyline\logs\council\demolitions.jsonl"


def build(prefab, name, pts, max_companies=0):
    d = post("/road", {"prefab": prefab, "points": pts, "curve": "smooth", "dryRun": True, "allowDemolish": True})
    g = d.get("geometry") or {}
    print(f"{name} dry: {short(d, ('errors', 'problems', 'cost'))} minR {g.get('minRadius')} demolish {demolition_cost(d.get('wouldDemolish'))}", flush=True)
    cost = demolition_cost(d.get("wouldDemolish"))
    if DRY or d.get("errors") or cost["households"] or cost["companies"] > max_companies:
        return False
    r = post("/road", {"prefab": prefab, "points": pts, "curve": "smooth", "allowDemolish": max_companies > 0})
    print(f"{name}: {short(r, ('applied', 'cost', 'errors'))}", flush=True)
    if d.get("wouldDemolish") and r.get("applied"):
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M:%S"), "by": WHO, "prefab": f"{prefab} ({name})",
                                "item": "jobs priority 2033-01: West Campus office land", "ids": [w.get("id") for w in d["wouldDemolish"]],
                                "cost": cost, "note": "the only junction into the free campus land; every alternative also hits 1-2 offices"}) + "\n")
    return bool(r.get("applied"))


def main():
    with paused(WHO):
        ok = build("Medium Road", "Campus Way", CAMPUS_WAY, max_companies=1)
        ok = build("Medium Road", "Campus Circle west", ARC_W) and ok
        ok = build("Medium Road", "Campus Circle east", ARC_E) and ok
        if DRY or not ok:
            return
        time.sleep(1)
        for pts in (CAMPUS_WAY, ARC_W, ARC_E):
            t = post("/roadupgrade", {"polyline": pts, "tolerance": 10, "prefabFilter": "Medium Road", "upgrade": "Trees", "side": "both"})
            print("  trees:", short(t, ("applied", "cost", "errors")), flush=True)
        p = post("/place", {"prefab": "CityPark02", "near": [CX, CZ], "within": [CX - 95, CZ - 95, CX + 95, CZ + 95],
                            "radius": 100, "maxHouseholds": 0, "maxCompanies": 0}, timeout=300)
        print("  park:", p.get("applied"), (p.get("chosen") or {}).get("x"), (p.get("chosen") or {}).get("z"), p.get("error") or "", flush=True)
        cells = {}
        segs = {s["id"]: s for s in get("/nets", x=-4420, z=-1330, radius=240, filter="Medium Road").get("segments") or []}
        for s in segs.values():
            (sx, sz), (ex, ez) = s["start"], s["end"]
            mx, mz = (sx + ex) / 2, (sz + ez) / 2
            dist = math.hypot(mx - CX, mz - CZ)
            if 95 < dist < 125:                                   # circle
                right_in = (ez - sz) * (CX - mx) - (ex - sx) * (CZ - mz) > 0
                inner, outer = ("right", "left") if right_in else ("left", "right")
                jobs = [("Office High", outer, 5), ("Office Low", inner, 4)]
            elif -4485 < mx < -4240 and -1272 < mz < -1185 and dist > 125:   # Campus Way
                jobs = [("Office High", "both", 5)]
            else:
                continue
            for name, side, depth in jobs:
                z = zone(name, mode="road", edges=[s["id"]], depth=depth, side=side, overwrite=False)
                cells[name] = cells.get(name, 0) + (z.get("cellsChanged") or 0)
        print("zoned cells:", cells, flush=True)
    print(json.dumps({"cells": cells}))


if __name__ == "__main__":
    main()
