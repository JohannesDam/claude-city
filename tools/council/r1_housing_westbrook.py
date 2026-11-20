"""Council round 1 - Housing - westbrook-garden-suburb.

Westbrook garden suburb on the West frontier (x -4670..-4300, z -870..2120): two lanes (x=-4600 west,
x=-4380 east) around a 124 m park strip (x -4552..-4428), cross streets every 300-400 m, three
connectors through the gaps in Westpark to the existing x=-4030 T-junctions (z=-650, 760, 1720).

Usage: python -I council/r1_housing_westbrook.py dry|A|B|C|D|E|verify
Each mode is one short build batch; results are appended to logs/council/r1_housing_westbrook.json.
"""
import json
import os
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, zone, building, find_spot, segs_in, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
WHO = "housing"
LOG = r"C:\soft\cityskyline\logs\council\r1_housing_westbrook.json"

XW, XE = -4600, -4380                 # west lane, east lane
ZB = [-870, -650, -320, 60, 430, 760, 1120, 1450, 1720, 2120]   # cross-street breakpoints
CONNECT_Z = [-650, 760, 1720]          # connectors to the x=-4030 road
X_OLD = -4030
BOX = (-4600, -4380)                   # accepted building x range
CAP = 2_420_000                        # approved capex 2.2 M + 10 %

KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "snapping", "pos")


def load():
    if os.path.exists(LOG):
        try:
            return json.load(open(LOG, encoding="utf-8"))
        except Exception:
            pass
    return {"spent": 0, "steps": {}}


STATE = load()


def save():
    json.dump(STATE, open(LOG, "w", encoding="utf-8"), indent=1)


def show(label, r):
    s = short(r, keys=KEYS)
    if "snapping" in s:
        s["snapping"] = [(x.get("snappedTo"), x.get("id")) for x in s["snapping"]]
    print(f"{label:<44} {json.dumps(s)}", flush=True)
    return r


def record(label, r):
    STATE["steps"][label] = short(r, keys=KEYS)
    if r.get("applied"):
        STATE["spent"] += r.get("cost") or 0
    save()


def road_step(label, pts, prefab="Small Road"):
    """Dry run, then build; returns the build result or None."""
    if label in STATE["steps"] and STATE["steps"][label].get("applied"):
        print(f"{label:<44} already built", flush=True)
        return STATE["steps"][label]
    d = show(label + " (dry)", road(prefab, pts, dryRun=True))
    if d.get("errors") not in (0, None) or d.get("error"):
        record(label + " FAILED", d)
        return None
    r = show(label, road(prefab, pts))
    record(label, r)
    return r if r.get("applied") else None


def place(label, prefab, x, z, radius):
    """Best /findspot spot inside the frontier strip with no problems; dry run first."""
    if label in STATE["steps"] and STATE["steps"][label].get("applied"):
        print(f"{label:<44} already built", flush=True)
        return STATE["steps"][label]
    last = None
    for c in find_spot(prefab, x, z, radius=radius, n=12):
        cs = c.get("corners") or [[c["x"], c["z"]]]
        if not all(BOX[0] <= p[0] <= BOX[1] for p in cs):
            continue
        if not all(-875 <= p[1] <= 2125 for p in cs):
            continue
        b = dict(c["building"])
        b.pop("prefab", None)
        d = post("/building", {"prefab": prefab, **b, "dryRun": True})
        last = d
        if d.get("errors") == 0 and not d.get("problems") and not d.get("error"):
            if STATE["spent"] + (d.get("cost") or 0) > CAP:
                show(label + " SKIP (capex cap)", d)
                return None
            r = building(prefab, **b)
            show(label, r)
            r["at"] = [c["x"], c["z"]]
            record(label, r)
            STATE["steps"][label]["at"] = [c["x"], c["z"]]
            save()
            return r
    show(label + " NO SPOT", last or {"error": "no candidate inside the strip"})
    record(label + " NO SPOT", last or {"error": "no candidate inside the strip"})
    return None


def lane(x):
    return [(f"lane x={x} z{ZB[i]}..{ZB[i + 1]}", [[x, ZB[i]], [x, ZB[i + 1]]]) for i in range(len(ZB) - 1)]


if __name__ == "__main__":
    t0 = time.time()
    if MODE == "dry":
        with paused(WHO):
            for z in CONNECT_Z:
                show(f"connector z={z} (dry)", road("Small Road", [[XE, z], [X_OLD, z]], dryRun=True))
            show("east lane z-870..-650 (dry)", road("Small Road", [[XE, -870], [XE, -650]], dryRun=True))
            show("west lane z-870..-650 (dry)", road("Small Road", [[XW, -870], [XW, -650]], dryRun=True))
            show("cross z=-870 (dry)", road("Small Road", [[XW, -870], [XE, -870]], dryRun=True))
    elif MODE == "A":
        conn = {int(k): v for k, v in (json.loads(sys.argv[2]) if len(sys.argv) > 2 else {}).items()}
        with paused(WHO):
            for z in CONNECT_Z:
                pts = conn.get(z) or [[XE, z], [X_OLD, z]]
                road_step(f"connector z={z}", pts)
            for label, pts in lane(XE):
                road_step("east " + label, pts)
    elif MODE == "B":
        with paused(WHO):
            for label, pts in lane(XW):
                road_step("west " + label, pts)
            for z in ZB:
                road_step(f"cross z={z}", [[XW, z], [XE, z]])
    elif MODE == "C":
        todo = [
            ("SunkenPlaza01", -4490, -610, 80),
            ("NA_MixedSignature01", -4490, -700, 120),
            ("HighSchool02", -4440, -130, 150),
            ("DogPark02", -4545, 245, 150),
            ("CityPark11", -4545, 595, 150),
            ("NA_ResidentialMediumSignature01", -4490, 800, 120),
            ("ElementarySchool01", -4420, 940, 200),
            ("CityPark03", -4545, 1285, 150),
            ("ParkCafe01", -4490, 1680, 100),
            ("CityPark04", -4530, 1920, 200),
            ("NA_ResidentialLowSignature01", -4440, 2040, 200),
        ]
        only = sys.argv[2].split(",") if len(sys.argv) > 2 else None
        for prefab, x, z, r in todo:
            if only and prefab not in only:
                continue
            with paused(WHO):  # one placement per lock hold: other crews can slot in between
                place(f"place {prefab}", prefab, x, z, r)
    elif MODE == "D":
        rects = [
            ("NA Residential Medium", [[-4660, -862], [-4608, -320]]),
            ("NA Residential Medium", [[-4372, -320], [-4320, 1712]]),
            ("NA Residential Low", [[-4660, -320], [-4608, 2112]]),
            ("NA Residential Low", [[-4372, 1728], [-4320, 2112]]),
            ("NA Residential Medium Row", [[-4592, -862], [-4552, 2112]]),
            ("NA Residential Medium Row", [[-4428, -862], [-4388, 430]]),
            ("NA Residential Medium Row", [[-4428, 770], [-4388, 2112]]),
            # commercial demand is 0 this round (was 34 when planned): the SE Commercial High strip
            # [[-4372,-862],[-4320,-658]] stays reserved (unzoned) until commercial demand returns, and the
            # north square gets Mixed Old Town (ground-floor shops, driven by residential demand 100)
            # instead of NA Commercial Low.
            ("Residential LowRent", [[-4372, -642], [-4320, -320]]),
            ("NA Residential Mixed Old Town", [[-4552, -700], [-4428, -600]]),
            ("NA Residential Mixed Old Town", [[-4552, 710], [-4428, 810]]),
            ("NA Residential Mixed Old Town", [[-4552, 1670], [-4428, 1770]]),
        ]
        for name, pts in rects:
            assert max(p[0] for p in pts) <= -4300, "never zone in Westpark"
        with paused(WHO):
            for name, pts in rects:
                label = f"zone {name} {pts}"
                if label in STATE["steps"] and STATE["steps"][label].get("applied"):
                    continue
                d = show(label + " (dry)", zone(name, mode="rect", points=pts, overwrite=False, dryRun=True))
                if d.get("errors") not in (0, None) or d.get("error"):
                    record(label + " FAILED", d)
                    continue
                r = show(label, zone(name, mode="rect", points=pts, overwrite=False))
                record(label, r)
    elif MODE == "commercial":
        # the reserved SE strip, zoned once commercial demand is back above 0
        dem = get("/city").get("demand") or {}
        print("commercial demand", dem.get("commercial"), flush=True)
        if (dem.get("commercial") or 0) > 0:
            pts = [[-4372, -862], [-4320, -658]]
            label = f"zone NA Commercial High {pts}"
            with paused(WHO):
                d = show(label + " (dry)", zone("NA Commercial High", mode="rect", points=pts, overwrite=False, dryRun=True))
                if d.get("errors") in (0, None) and not d.get("error"):
                    r = show(label, zone("NA Commercial High", mode="rect", points=pts, overwrite=False))
                    record(label, r)
    elif MODE == "E":
        # /nets caps a query at 200 segments, so segs_in() over the whole suburb misses edges: scan windows
        seen = {}
        for wz in range(-870, 2200, 200):
            for wx in (-4490, -4200):
                for s in get("/nets", x=wx, z=wz, radius=230, filter="Road").get("segments") or []:
                    mx, mz = (s["start"][0] + s["end"][0]) / 2, (s["start"][1] + s["end"][1]) / 2
                    if -4610 <= mx <= -4035 and -880 <= mz <= 2130 and (s.get("prefab") or "").startswith("Small Road"):
                        seen[s["id"]] = s
        ids = sorted(seen)
        print("total length", round(sum(s["length"] for s in seen.values())), flush=True)
        print("new edges", len(ids), flush=True)
        STATE["edges"] = ids
        save()
        for i in range(0, len(ids), 10):
            batch = ids[i:i + 10]
            with paused(WHO):
                d = show(f"trees {i}", post("/roadupgrade", {"edges": batch, "upgrade": "Trees", "side": "both", "dryRun": True}))
                if d.get("errors") in (0, None) and not d.get("error"):
                    r = show(f"trees {i}", post("/roadupgrade", {"edges": batch, "upgrade": "Trees", "side": "both"}))
                    record(f"trees {i}", r)
    elif MODE == "verify":
        print(json.dumps(get("/zonestats"), indent=0)[:3000])
        print(json.dumps(get("/city").get("demand")))
        m = get("/map", x=-4490, z=640, size=3200, cell=50)
        print(m.get("grid"))
    print("SPENT so far", STATE["spent"], f"({time.time() - t0:.0f} s)")
