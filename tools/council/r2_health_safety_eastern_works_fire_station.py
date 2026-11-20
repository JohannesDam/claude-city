"""Council round 2 - Health & Safety - eastern-works-fire-station.

The planned site (north strip of tile 358 / 616824:1, south of the z=2820 road) is Economy's: their round-2
Eastern Harvest Valley bought the tile and built IndustrialManufacturingSignature05 1015108:47 on exactly that
spot (x 1222..1375, z 2667..2812) and zoned the rest. /findspot finds no FireStation01 lot inside or around
Eastern Works on owned land.
Replacement site: buy the empty, flat tile 616758:1 (centre 1870,3117; ~$167k) directly EAST of Eastern Works and
put FireStation01 on the east side of the x=1530 Medium Road boundary street (edges 504766:33 .. 1107606:39),
facing west into the estate, centred on z~3095 (middle of Eastern Works). Then the Disaster Response Unit.

Usage: python -I council/r2_health_safety_eastern_works_fire_station.py check|tile|build|dru|verify
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools\council")
from r2_health_safety_common import (get, post, paused, building, show, log, load, footprint, ledger_boxes,  # noqa: E402
                                     overlaps, ext_scan, ext_build, installed, DEPT)

P = "eastern_works_fire_station"
PID = "eastern-works-fire-station"
MODE = sys.argv[1] if len(sys.argv) > 1 else "check"
TILE = {"id": "616758:1", "x": 1870, "z": 3117}
TARGET = (1610, 3095)
RESERVED = [{"project": "eastern-harvest-valley", "department": "economy", "x0": 935, "x1": 1558, "z0": 2182, "z1": 2812,
             "what": "reservedByOthers"}]


def tile_owned():
    t = get("/tiles")
    return any(abs(c[0] - TILE["x"]) < 5 and abs(c[1] - TILE["z"]) < 5 for c in t.get("ownedCentres") or []), t


def candidates(prefab):
    cs = get("/findspot", prefab=prefab, x=TARGET[0], z=TARGET[1], radius=220, n=8).get("candidates") or []
    others = ledger_boxes(PID) + RESERVED
    out = []
    for c in cs:
        b = dict(c["building"])
        b.pop("prefab", None)
        d = post("/building", {"prefab": prefab, **b, "dryRun": True})
        if d.get("errors") or d.get("error") or d.get("wouldDemolish"):
            print(f"  {prefab} {b}: {d.get('problems') or d.get('error')} wd={len(d.get('wouldDemolish') or [])}")
            continue
        box = footprint(d["pos"], d["lot"], d["facing"])
        # the r1 Eastern Works box ends at x 1545, the centre of the x=1530 boundary street: a lot fronting that
        # street from the east touches it by a few metres of sidewalk only
        hit = [h for h in overlaps(box, others) if min(box[1], h["x1"]) - max(box[0], h["x0"]) > 12]
        if hit:
            print(f"  {prefab} {b}: overlaps {[h['project'] for h in hit]}")
            continue
        out.append((c, b, d, box))
    return out


def check():
    owned, t = tile_owned()
    print("tile owned:", owned, [c for c in t.get("candidates") or [] if c["id"] == TILE["id"]])
    for p in ("FireStation01", "FireHouse01"):
        cs = candidates(p)
        for c, b, d, box in cs[:4]:
            print(p, b, d["pos"], d["facing"], box, c.get("score"))


def tile():
    owned, t = tile_owned()
    if owned:
        print("tile already owned")
        return
    cand = [c for c in t.get("candidates") or [] if c["id"] == TILE["id"]]
    print("buying", cand)
    with paused(DEPT):
        r = show("buytile 616758:1", post("/buytile", {"x": TILE["x"], "z": TILE["z"]}))
    print(json.dumps(r)[:600])
    log(P, "tile", {"id": TILE["id"], "result": {k: r.get(k) for k in ("applied", "cost", "price", "error")},
                    "price": cand[0]["price"] if cand else None, "upkeepIncrease": cand[0]["upkeepIncrease"] if cand else None})


def build():
    st = load(P)
    if (st.get("station") or {}).get("id"):
        print("already built", st["station"])
        return
    for prefab in ("FireStation01", "FireHouse01"):
        cs = candidates(prefab)
        if not cs:
            print(prefab, "no clean candidate")
            continue
        c, b, d, box = cs[0]
        with paused(DEPT):
            d2 = show(f"{prefab} (dry)", post("/building", {"prefab": prefab, **b, "dryRun": True}))
            if d2.get("errors") or d2.get("wouldDemolish"):
                continue
            r = show(prefab, building(prefab, **b))
        if r.get("applied"):
            st["station"] = {"prefab": prefab, "id": r.get("placedId"), "pos": d2["pos"], "facing": d2["facing"],
                             "lot": d2["lot"], "box": box, "spec": b, "cost": r.get("cost"),
                             "roadConnected": r.get("roadConnected")}
            log(P, "station", st["station"])
            print(json.dumps(st["station"]))
            return


def dru():
    st = load(P).get("station") or {}
    if st.get("prefab") != "FireStation01":
        print("no FireStation01 - no Disaster Response Unit", st)
        return
    oid = st["id"]
    have, e = installed(oid)
    print(json.dumps(e)[:2500])
    if any("Disaster" in (i.get("prefab") or "") for i in e.get("installed") or []):
        print("DRU already installed")
        return
    c = ext_scan(oid, "Disaster", sides=("back", "left", "right"), offsets=(0, -16, 16, -32, 32))
    if not c:
        print("no clean DRU slot")
        return
    side, off, d = c
    print("DRU slot", side, off, d["pos"], "cost", d.get("cost"))
    with paused(DEPT):
        r = show(f"Disaster Response Unit {side}{off:+d}", ext_build(oid, "Disaster", side, off))
    if r.get("applied"):
        o = [x for x in e.get("offered") or [] if "Disaster" in x["name"]]
        log(P, "dru", {"pos": d["pos"], "facing": d["facing"], "lot": (o[0].get("lot") if o else None), "side": side,
                       "offset": off, "cost": r.get("cost"), "upkeep": (o[0].get("upkeep") if o else None),
                       "name": o[0]["name"] if o else None})
    print(json.dumps(installed(oid)[1].get("installed")))


def verify():
    st = load(P)
    print(json.dumps(st, indent=1)[:1500])
    s = get("/services", kind="fire")["kinds"]["fire"]
    print(json.dumps(s.get("totals"))[:600])
    for b in s["buildings"]:
        if b["id"] == (st.get("station") or {}).get("id") or (b["pos"][0] > 0 and b["pos"][1] > 2400) or b["pos"][0] > -400 and b["pos"][1] > 3000:
            print(json.dumps(b)[:600])
    pr = get("/problems", type="Burned Down")
    print(json.dumps(pr)[:800])
    c = get("/coverage", service="FireRescue", layer="homes")
    print(json.dumps(c.get("summary"))[:600])


if __name__ == "__main__":
    {"check": check, "tile": tile, "build": build, "dru": dru, "verify": verify}[MODE]()
