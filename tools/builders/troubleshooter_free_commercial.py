"""Find free (unbuilt) commercial zone cells near No Customers clusters and rezone them to residential.

No Customers = shops without enough buyers. Where commercial cells are still empty next to struggling shops, turning
them into homes (residential demand is 100) adds customers and stops more competing shops from growing.

Method: for each road edge in the box, dry-run POST /zone mode=road (depth 6) per side with overwrite=True; the game's
zone preview reports cellsFromZone (what the cells are now) and occupiedCellsRezoned (cells under buildings). A side
whose only changed cells are free commercial cells (occupied 0, no other zone types) is rezoned for real. Nothing
occupied is ever touched.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_free_commercial.py --box x0 z0 x1 z1
     [--zone "NA Residential Medium"] [--apply]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused  # noqa: E402

COMMERCIAL = ("Commercial",)


def main():
    a = sys.argv
    box = [float(v) for v in a[a.index("--box") + 1:a.index("--box") + 5]]
    zone = a[a.index("--zone") + 1] if "--zone" in a else "NA Residential Medium"
    apply = "--apply" in a
    x0, z0, x1, z1 = box
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    r = max(x1 - x0, z1 - z0) / 2 + 50
    segs = [s for s in get("/nets", x=cx, z=cz, radius=r, limit=500).get("segments", [])
            if s.get("road") and "Highway" not in s["prefab"]]
    segs = [s for s in segs if x0 <= (s["start"][0] + s["end"][0]) / 2 <= x1 and z0 <= (s["start"][1] + s["end"][1]) / 2 <= z1]
    print("edges in box", len(segs))
    found = []
    with paused("troubleshooter"):
        for s in segs:
            for side in ("left", "right"):
                body = {"zone": zone, "mode": "road", "edges": [s["id"]], "depth": 6, "side": side, "overwrite": True}
                d = post("/zone", dict(body, dryRun=True))
                frm = d.get("cellsFromZone") or {}
                occ = d.get("occupiedCellsRezoned", 0)
                com = sum(v for k, v in frm.items() if any(c in k for c in COMMERCIAL))
                other = sum(v for k, v in frm.items() if not any(c in k for c in COMMERCIAL))
                if com and occ == 0 and other == 0:
                    found.append((s["id"], side, com, s["start"], s["end"]))
                    print("free commercial", s["id"], side, com, frm, s["start"], s["end"], flush=True)
                    if apply:
                        res = post("/zone", body)
                        print("   rezoned", res.get("applied"), res.get("cellsChanged"), res.get("occupiedCellsRezoned"),
                              flush=True)
    print(json.dumps({"zone": zone, "box": box, "free": found, "applied": apply}))


if __name__ == "__main__":
    main()
