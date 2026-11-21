"""builder-west, b2-housing-northlake-new-town-p1 phase 1b (conductor order 2033-01-09): Northlake civic buildings.

  - Civic Way: Medium Road with Trees, a curved collector from Park Lane's dead end (-3260,2340) south and east
    through the empty block SW of the square, ending as a cul-de-sac at (-3100,2210). Every tie-in to the avenue
    would remove 72-154 households of new Mixed blocks, so it stays a dead end for now.
  - /place on its frontage (before zoning): ElementarySchool03, MedicalClinic02, PoliceStation02, FireHouse01,
    PostOffice02; BusStation02 at Northlake Square.
  - Then NA Residential Medium on the rest of the Civic Way frontage (overwrite=False).
Usage: python -I builders/builder-west_northlake-civic.py [dry]
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[1:]
CIVIC_WAY = [[-3260, 2340], [-3275, 2270], [-3220, 2212], [-3130, 2200], [-3100, 2210]]
BLOCK = [-3410, 2100, -3085, 2335]
SQUARE = [-3130, 2400]
CIVIC = [("ElementarySchool03", [-3220, 2240]), ("MedicalClinic02", [-3150, 2230]), ("PoliceStation02", [-3270, 2290]),
         ("FireHouse01", [-3160, 2180]), ("PostOffice02", [-3250, 2190])]


def place(prefab, near, within, radius=160):
    body = {"prefab": prefab, "near": near, "within": within, "radius": radius, "maxHouseholds": 0, "maxCompanies": 0}
    r = post("/place", {**body, "dryRun": DRY}, timeout=300)
    c = r.get("chosen") or {}
    print(f"  {prefab}: applied {r.get('applied')} id {r.get('placedId')} at {c.get('x')},{c.get('z')} "
          f"road {c.get('roadConnected')} {r.get('error') or ''}", flush=True)
    return r


def civic_way_edges():
    out = []
    for s in get("/nets", x=-3190, z=2260, radius=140, filter="Medium Road").get("segments") or []:
        mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
        if -3290 < mx < -3090 and 2190 < mz < 2340:
            out.append(s["id"])
    return out


def main():
    body = {"prefab": "Medium Road", "points": CIVIC_WAY, "fit": {"tolerance": 15, "minRadius": 60}}
    d = post("/roadfit", {**body, "dryRun": True})
    c = d.get("chosen") or {}
    print("Civic Way dry:", c.get("variant"), (c.get("geometry") or {}).get("minRadius"), c.get("demolition"), flush=True)
    if DRY:
        place("BusStation02", SQUARE, [-3230, 2300, -3030, 2500], 110)
        return
    if not c or (c.get("demolition") or {}).get("buildings"):
        return
    placed = {}
    with paused(WHO):
        r = post("/roadfit", body)
        print("Civic Way:", r.get("applied"), (r.get("chosen") or {}).get("variant"), flush=True)
        if not r.get("applied"):
            return
        time.sleep(1)
        t = post("/roadupgrade", {"edges": civic_way_edges(), "upgrade": "Trees", "side": "both"})
        print("trees:", short(t, ("applied", "cost", "errors")), flush=True)
        for prefab, near in CIVIC:
            p = place(prefab, near, BLOCK)
            if p.get("applied"):
                placed[prefab] = p.get("placedId")
        p = place("BusStation02", SQUARE, [-3230, 2300, -3030, 2500], 110)
        if p.get("applied"):
            placed["BusStation02"] = p.get("placedId")
        time.sleep(1)
        cells = 0
        for e in civic_way_edges():
            z = zone("NA Residential Medium", mode="road", edges=[e], depth=6, side="both", overwrite=False)
            cells += z.get("cellsChanged") or 0
        print("zoned NA Residential Medium:", cells, flush=True)
    print(json.dumps({"placed": placed}))


if __name__ == "__main__":
    main()
