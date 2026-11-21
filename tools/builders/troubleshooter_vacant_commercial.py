"""Convert vacant commercial buildings (no company, no households) into residential zoning.

Why: Pearl Bay had 21 of its 50 commercial buildings empty (commercial demand only exists for petrol), while
residential demand is 100 and several occupied shops show No Customers. Homes on those lots add customers.

Phases (run separately so the game can update zone-cell occupancy in between):
  scan     --box x0 z0 x1 z1            list pure commercial buildings in the box with 0 companies (bulldoze dry run)
  clear    --ids-file f                 re-check each one is still empty, bulldoze it, log to demolitions.jsonl
  rezone   --ids-file f --zone Z        around each cleared site, rezone the largest square (half-size 24..8 m) whose
                                         dry run touches only free cells, to residential (overwrite=True)
Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_vacant_commercial.py scan --box ...
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused  # noqa: E402

DEMOLITIONS = r"C:\soft\cityskyline\logs\council\demolitions.jsonl"


def probe(b):
    x, z = b["pos"]
    r = post("/bulldoze", {"box": [x - 3, z - 3, x + 3, z + 3], "buildings": True, "nets": "none", "dryRun": True})
    return r.get("buildings", {})


def scan(box, kinds=("Commercial",)):
    x0, z0, x1, z1 = box
    bl, off = [], 0
    while True:
        r = get("/buildings", kind="all", x=(x0 + x1) / 2, z=(z0 + z1) / 2,
                radius=max(x1 - x0, z1 - z0) * 0.75, offset=off, limit=300)
        bl += r["buildings"]
        if not r.get("truncated"):
            break
        off += r["returned"]
    com = [b for b in bl if any(k in b["prefab"] for k in kinds) and "Mixed" not in b["prefab"]
           and x0 <= b["pos"][0] <= x1 and z0 <= b["pos"][1] <= z1]
    vac = []
    for i in range(0, len(com), 40):  # short lock blocks
        with paused("troubleshooter"):
            for b in com[i:i + 40]:
                bb = probe(b)
                if bb.get("count") == 1 and bb.get("companies") == 0 and bb.get("households") == 0 \
                        and bb["list"][0]["id"] == b["id"]:
                    b["zoneKind"] = bb["list"][0].get("kind")
                    vac.append(b)
    print(json.dumps({"scanned": len(com), "vacant": vac}))
    return vac


def clear(vac, why):
    done = []
    with paused("troubleshooter"):
        for b in vac:
            bb = probe(b)
            if not (bb.get("count") == 1 and bb.get("companies") == 0 and bb.get("households") == 0):
                print("skip (no longer empty)", b["id"])
                continue
            r = post("/bulldoze", {"ids": [b["id"]]})
            print("bulldozed", b["id"], b["prefab"], b["pos"], r.get("applied"))
            if r.get("applied"):
                done.append(b)
                with open(DEMOLITIONS, "a") as f:
                    f.write(json.dumps({"who": "troubleshooter", "at": get("/city")["date"], "id": b["id"],
                                        "prefab": b["prefab"], "pos": b["pos"], "households": 0, "companies": 0,
                                        "why": why})
                            + "\n")
    return done


def rezone(sites, zone):
    total = 0
    with paused("troubleshooter"):
        for b in sites:
            x, z = b["pos"]
            for h in (24, 20, 16, 12, 8):
                body = {"zone": zone, "mode": "rect", "points": [[x - h, z - h], [x + h, z + h]], "overwrite": True}
                d = post("/zone", dict(body, dryRun=True))
                frm = d.get("cellsFromZone") or {}
                if d.get("cellsChanged") and d.get("occupiedCellsRezoned") == 0 \
                        and all("Commercial" in k or k == "none" or k == zone for k in frm):
                    r = post("/zone", body)
                    total += r.get("cellsChanged") or 0
                    print("rezoned", b["id"], "half", h, r.get("cellsChanged"), r.get("occupiedCellsRezoned"), frm)
                    break
            else:
                print("no clean square", b["id"], b["pos"])
    print("cells rezoned", total)


def main():
    a = sys.argv
    mode = a[1]
    if mode == "scan":
        kinds = tuple(a[a.index("--kinds") + 1].split(",")) if "--kinds" in a else ("Commercial",)
        scan([float(v) for v in a[a.index("--box") + 1:a.index("--box") + 5]], kinds)
        return
    sites = json.load(open(a[a.index("--ids-file") + 1]))
    if isinstance(sites, dict):
        sites = sites["vacant"]
    if mode == "clear":
        why = a[a.index("--why") + 1] if "--why" in a else "vacant (no company, no households); lot rezoned to a zone with demand"
        done = clear(sites, why)
        json.dump(done, open(a[a.index("--ids-file") + 1] + ".cleared.json", "w"))
    elif mode == "rezone":
        rezone(sites, a[a.index("--zone") + 1] if "--zone" in a else "NA Residential Medium")


if __name__ == "__main__":
    main()
