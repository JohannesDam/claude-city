"""Council round 2 - Mobility - bus-capacity-and-new-lines.

Finding at execution time: the south BusDepot01 237220:149 and its Extra Garage 183749:425 no longer exist
(burned down - safety desk 'Burned Down' hotspots at (-1444,-825) and (-1448,-761) - and cleared). The north
depot 901427:89 (capacity 25 + 2 x 10 = 45) carries every bus in the city: lines 1/4/5/2/3 hold 12+10+12+9+2 = 45.
So step 1 (switch the garage back on) is impossible; rebuilding BusDepot01 (150k) exceeds this project's capex
(80k +10%) and becomes a mayor item (validated site from a dry run below).

Within budget: a third Extra Garage on the north depot (+10 buses, 75k) and line 6 Westbrook. Line 7 East
Lakeshore is only dry-run (stops validated) and deferred until depot capacity is back, so it does not take
buses from line 3.

Line 6 design (no U-turns): northbound on x=-4380 (east side) z -250/150/520 -> z=760 connector east ->
transfer at line 2's northbound stop 204537:17 (-3393.7,1037.5) -> north on x=-3400 -> z=1720 connector west ->
southbound on x=-4600 (west side) z 1400/1000/520/150/-250 -> back to the start.

Usage: python -I council/r2_mobility_bus_capacity.py dry|apply|verify
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\r2_mobility_bus_capacity_and_new_lines.json"
STOP = "NA_BusStop01"
NORTH_DEPOT = "901427:89"

LINE6 = dict(color="#00ACC1", stops=[
    {"x": -4373, "z": -250}, {"x": -4373, "z": 150}, {"x": -4373, "z": 520},
    "204537:17",  # line 2 northbound stop (-3393.7,1037.5): transfer
    {"x": -4607, "z": 1400}, {"x": -4607, "z": 1000}, {"x": -4607, "z": 520},
    {"x": -4607, "z": 150}, {"x": -4607, "z": -250},
])
LINE7 = dict(color="#6D4C41", stops=[
    {"x": -673, "z": 1700}, {"x": -673, "z": 2078}, {"x": -673, "z": 2400}, {"x": -673, "z": 2700},
    {"x": -687, "z": 2700}, {"x": -687, "z": 2400}, {"x": -687, "z": 2078}, {"x": -687, "z": 1700},
    "360229:25",  # line 4 stop (-1475.3,2242.5)
    "200099:3",   # line 4 stop (-1462.7,2242.5)
])


def load():
    try:
        with open(LOG, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def save(d):
    with open(LOG, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=1)


def line_brief(r):
    out = {k: r.get(k) for k in ("lineId", "error", "warning") if r.get(k)}
    out["stops"] = [{k: s.get(k) for k in ("index", "stopId", "reused", "new", "pos", "edge", "side") if k in s} for s in r.get("stops", [])]
    out["newStops"] = [short(s) for s in r.get("newStops", [])]
    ln = r.get("line")
    out["line"] = short(ln, ("applied", "errors", "warnings", "cost", "lineId", "number", "error")) if isinstance(ln, dict) else ln
    return out


def lines_now():
    return [{k: l.get(k) for k in ("id", "number", "complete", "flags", "waitingPassengers", "vehicles", "length", "duration")}
            for l in get("/lines").get("lines", [])]


log = load()

if MODE == "dry":
    log["southDepot"] = {"extensions": get("/extensions", owner="237220:149"), "garage": get("/extensions", owner="183749:425")}
    print("south depot:", log["southDepot"])
    ext = get("/extensions", owner=NORTH_DEPOT)
    print("north depot combined:", ext.get("combined"))
    with paused("mobility"):
        r = post("/extension", {"owner": NORTH_DEPOT, "prefab": "Extra Garage", "side": "auto", "dryRun": True}, timeout=120)
        print("garage dry:", json.dumps(r)[:800])
        log["garageDry"] = r
        r6 = post("/busline", {**LINE6, "stopPrefab": STOP, "dryRun": True}, timeout=300)
        print("line6 dry:", json.dumps(line_brief(r6))[:2500])
        log["line6Dry"] = line_brief(r6)
        r7 = post("/busline", {**LINE7, "stopPrefab": STOP, "dryRun": True}, timeout=300)
        print("line7 dry:", json.dumps(line_brief(r7))[:2500])
        log["line7Dry"] = line_brief(r7)
        # validated site for rebuilding the burned-down south depot (mayor item)
        cands = get("/findspot", prefab="BusDepot01", x=-1444, z=-826, radius=300, n=3).get("candidates") or []
        if cands:
            b = dict(cands[0]["building"]); b.pop("prefab", None)
            rd = post("/building", {"prefab": "BusDepot01", **b, "dryRun": True})
            log["southDepotRebuildDry"] = {"spot": cands[0]["buildingXZ"], "building": b, "result": short(rd)}
            print("south depot rebuild dry:", log["southDepotRebuildDry"])
    save(log)

elif MODE == "apply":
    with paused("mobility"):
        r = post("/extension", {"owner": NORTH_DEPOT, "prefab": "Extra Garage", "side": "auto"}, timeout=120)
        print("garage:", json.dumps(r)[:800])
        log["garage"] = r
    with paused("mobility"):
        r6 = post("/busline", {**LINE6, "stopPrefab": STOP}, timeout=300)
        print("line6:", json.dumps(line_brief(r6))[:2500])
        log["line6"] = line_brief(r6)
    save(log)

elif MODE == "verify":
    print(json.dumps(get("/extensions", owner=NORTH_DEPOT).get("installed"), indent=0))
    print(json.dumps(get("/extensions", owner=NORTH_DEPOT).get("combined")))
    for l in lines_now():
        print(l)
    log["verify"] = {"lines": lines_now(), "depot": get("/extensions", owner=NORTH_DEPOT).get("combined")}
    save(log)
