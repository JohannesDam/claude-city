"""Council round 1 - Economic Affairs - avenue-arcades (smaller variant).

Planned: two pedestrian streets through the empty Office-park block x -2129..-1909, z -100..290 and
NA Commercial High on it. Since planning, Education's south campus took most of that block this round
(ElementarySchool01 at (-2040,-60), HighSchool01 at (-2056,76) with lot z -12..164, and a Small Road link
along x=-2129 from z -100 to 200). The z=60 street is therefore impossible. What is left is the north part
x -2121..-1965, z 167..270. A z=200 street right through to the avenue node (-1909,200) would clip the lot
of office 994689:7 (lot z 158..198), so the street stops at x=-1975 instead (no demolition).

Usage: python -I council/r1_economy_avenue_arcades.py check|build|zone|unzone_backrow|trees|verify
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, zone, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "check"
DEPT = "economy"
KEYS = ("applied", "cost", "errors", "problems", "error", "warning", "snapping")
LOG = r"C:\soft\cityskyline\logs\council\r1_economy_avenue_arcades.json"
STREET = [[-2129, 200], [-1975, 200]]
ZONE_RECT = [[-2125, 164], [-1965, 268]]


def show(label, r):
    print(f"{label:<34} {json.dumps(short(r or {}, keys=KEYS))}", flush=True)
    return r


def logged():
    try:
        return json.load(open(LOG))
    except Exception:
        return {}


def log(key, val):
    d = logged()
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


def street_edges():
    out = []
    for s in get("/nets", x=-2052, z=200, radius=100, filter="Pedestrian").get("segments") or []:
        if abs(s["start"][1] - 200) < 3 and abs(s["end"][1] - 200) < 3:
            out.append(s["id"])
    return out


if __name__ == "__main__":
    if MODE == "check":
        b = get("/buildings", kind="all", x=-2045, z=215, radius=80)
        print("buildings in north part:", [(e["prefab"], e["pos"]) for e in b.get("buildings", [])])
        show("street dry", post("/road", {"prefab": "Pedestrian Street", "points": STREET, "dryRun": True}))
    elif MODE == "build":
        with paused(DEPT):
            d = show("street dry", road("Pedestrian Street", STREET, dryRun=True))
            if d.get("errors") == 0 and not d.get("error"):
                r = show("street", road("Pedestrian Street", STREET))
                log("street", r)
    elif MODE == "zone":
        with paused(DEPT):
            d = show("zone dry", zone("NA Commercial High", mode="rect", points=ZONE_RECT, overwrite=False, dryRun=True))
            if not d.get("error") and not d.get("errors"):
                r = show("zone", zone("NA Commercial High", mode="rect", points=ZONE_RECT, overwrite=False))
                log("zone", r)
    elif MODE == "unzone_backrow":
        # the rect also caught the unused 6th row (z 264..272) behind the z=320 office lots; reset it
        with paused(DEPT):
            P = [[-2135, 265], [-1960, 271]]
            d = show("unzone dry", zone("Unzoned", mode="rect", points=P, overwrite=True, dryRun=True))
            if d.get("errors") == 0:
                log("unzone_backrow", show("unzone", zone("Unzoned", mode="rect", points=P, overwrite=True)))
    elif MODE == "trees":
        # Pedestrian Street does not support Trees/Grass ("upgrade not supported by this road")
        ids = street_edges()
        print("pedestrian edges:", ids)
        with paused(DEPT):
            d = show("trees dry", post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both", "dryRun": True}))
            if not d.get("error") and not d.get("errors"):
                r = show("trees", post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"}))
                log("trees", {"edges": ids, "result": r})
    elif MODE == "verify":
        zs = get("/zonestats")
        print("zonestats:", [z for z in zs.get("zones", []) if "Commercial" in z["zone"]])
        print("demand:", get("/city").get("demand"))
        print("pedestrian edges:", street_edges())
        print(get("/map", x=-2040, z=215, size=200, cell=8).get("grid"))
        b = get("/buildings", kind="all", x=-1941, z=200, radius=45)
        print("avenue offices still there:", [(e["id"], e["prefab"]) for e in b.get("buildings", [])])
        print("unlock:", json.dumps(get("/unlockinfo", name="NA_CommercialHighSignature01"))[:600])
