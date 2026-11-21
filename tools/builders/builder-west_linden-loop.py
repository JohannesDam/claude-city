"""builder-west, jobs priority 2033-01: "Linden Loop" office quarter on the free owned land in north-east Westside
(x -2790..-2510, z 1500..1830; bounded by the curved x=-2790 street on the west, the z=1450 homes on the south
and the high-voltage line (-2446,1572)-(-2539,1716)-(-2614,1833) on the north-east, which roads must not cross).

  - Linden Loop: Medium Road with Trees, a hairpin from the x=-2790 street at z=1540, east to a 60 m semicircle
    (apex x=-2565) and back west at z=1660. The legs are 120 m apart, so both inner sides take 6 cells.
  - PocketPark in the apex before zoning (new zoning grows within a minute).
  - Linden Close: Small Road with Trees, a cul-de-sac from the north leg into the triangle below the power line.
  - Zoning (overwrite=False): Office High on the outer sides, Office Low on the inner sides and the close.
Usage: python -I builders/builder-west_linden-loop.py [dry]
"""
import json
import math
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[1:]
K = 0.5523 * 60
LOOP = [{"start": [-2790, 1540], "end": [-2625, 1540]},
        {"control1": [-2625 + K, 1540], "control2": [-2565, 1600 - K], "end": [-2565, 1600]},
        {"control1": [-2565, 1600 + K], "control2": [-2625 + K, 1660], "end": [-2625, 1660]},
        {"end": [-2796, 1660]}]
CLOSE = [[-2700, 1660], [-2702, 1730], [-2712, 1795]]
INNER = (-2650, 1600)


def loop_edges():
    out = []
    for s in get("/nets", x=-2670, z=1600, radius=160, filter="Medium Road").get("segments") or []:
        (sx, sz), (ex, ez) = s["start"], s["end"]
        mx, mz = (sx + ex) / 2, (sz + ez) / 2
        if -2788 < mx < -2550 and 1530 < mz < 1670:
            out.append(s)
    return out


def right_has(s, px, pz):
    (sx, sz), (ex, ez) = s["start"], s["end"]
    mx, mz = (sx + ex) / 2, (sz + ez) / 2
    return (ez - sz) * (px - mx) - (ex - sx) * (pz - mz) > 0


def main():
    d = post("/road", {"prefab": "Medium Road", "segments": LOOP, "dryRun": True})
    print("loop dry:", short(d, ("cost", "errors", "problems")), "minR", (d.get("geometry") or {}).get("minRadius"),
          "demolish", len(d.get("wouldDemolish") or []), flush=True)
    if DRY or d.get("errors") or d.get("wouldDemolish"):
        return
    cells = {}
    with paused(WHO):
        r = post("/road", {"prefab": "Medium Road", "segments": LOOP})
        print("loop:", short(r, ("applied", "cost", "errors")), flush=True)
        if not r.get("applied"):
            return
        time.sleep(1)
        ids = [s["id"] for s in loop_edges()]
        t = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"})
        print("trees:", len(ids), short(t, ("applied", "cost", "errors")), flush=True)
        p = post("/place", {"prefab": "PocketPark08", "near": [-2600, 1600], "within": [-2622, 1565, -2577, 1635],
                            "radius": 40, "maxHouseholds": 0, "maxCompanies": 0}, timeout=300)
        print("park:", p.get("applied"), p.get("placedId"), (p.get("chosen") or {}).get("x"), (p.get("chosen") or {}).get("z"), p.get("error") or "", flush=True)
        c = post("/roadfit", {"prefab": "Small Road", "points": CLOSE, "fit": {"tolerance": 10, "minRadius": 60}})
        print("close:", c.get("applied"), (c.get("chosen") or {}).get("variant"), flush=True)
        if c.get("applied"):
            time.sleep(1)
            cl = [s["id"] for s in get("/nets", x=-2705, z=1730, radius=75, filter="Small Road").get("segments") or []
                  if -2725 < (s["start"][0] + s["end"][0]) / 2 < -2690 and (s["start"][1] + s["end"][1]) / 2 > 1668]
            t = post("/roadupgrade", {"edges": cl, "upgrade": "Trees", "side": "both"})
            print("close trees:", cl, short(t, ("applied", "errors")), flush=True)
        for s in loop_edges():
            inner = "right" if right_has(s, *INNER) else "left"
            outer = "left" if inner == "right" else "right"
            for name, side in (("Office High", outer), ("Office Low", inner)):
                z = zone(name, mode="road", edges=[s["id"]], depth=6, side=side, overwrite=False)
                cells[name] = cells.get(name, 0) + (z.get("cellsChanged") or 0)
        if c.get("applied"):
            cl = [s["id"] for s in get("/nets", x=-2705, z=1730, radius=75, filter="Small Road").get("segments") or []
                  if -2725 < (s["start"][0] + s["end"][0]) / 2 < -2690 and (s["start"][1] + s["end"][1]) / 2 > 1668]
            z = zone("Office Low", mode="road", edges=cl, depth=6, side="both", overwrite=False)
            cells["Office Low"] = cells.get("Office Low", 0) + (z.get("cellsChanged") or 0)
        print("zoned:", cells, flush=True)
    print(json.dumps({"cells": cells}))


if __name__ == "__main__":
    main()
