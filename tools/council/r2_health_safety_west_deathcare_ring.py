"""Council round 2 - Health & Safety - west-deathcare-ring.

Three Crematorium01 where the dead are (SW quarter, NW West residential, Westbrook-North), each with a Hearse
Garage + Additional Incinerator; Hearse Garage + Additional Incinerator on the busy crematorium 456598:5;
Additional Burial Lots as a buffer on the full Cemetery02s.

Approved demolition (only these ids): SW 1116325:5, 1509449:5, 1365507:1, 389870:147, 1365425:5;
NW 321653:81, 1043885:13, 898209:39, 839161:7. Extensions are placed only where a proxy dry run shows that they
replace no zoned building (/extension itself does not report wouldDemolish).

Usage: python -I council/r2_health_safety_west_deathcare_ring.py crem|ext|buffer|verify|district
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools\council")
from r2_health_safety_common import (get, post, paused, building, show, log, load, footprint, ledger,  # noqa: E402
                                     ledger_boxes, overlaps, exists, ext_scan, ext_build, installed, DEPT)

P = "west_deathcare_ring"
PID = "west-deathcare-ring"
MODE = sys.argv[1] if len(sys.argv) > 1 else "check"

CREM = [
    ("sw", dict(edge="1299095:3", t=0.69, side="right", gap=0.5), (-3699.1, -601.5),
     {"1116325:5", "1509449:5", "1365507:1", "389870:147", "1365425:5"}),
    ("nw", dict(edge="819259:23", t=0.357, side="left", gap=0.5), (-3668.5, 1249.6),
     {"321653:81", "1043885:13", "898209:39", "839161:7"}),
    ("westbrook", dict(edge="749588:95", t=0.862, side="left", gap=0.5), (-4501.2, 1168.5), set()),
]
FALLBACK = {
    "sw": [dict(edge="1218703:9", t=0.526, side="left", gap=0.5)],
    "westbrook": [dict(edge="742203:155", t=0.138, side="right", gap=0.5)],
}
WHERE = {"sw": "SW quarter", "nw": "NW West residential", "westbrook": "Westbrook-North"}


def place_crem(key, spec, exp, allowed):
    d = show(f"Crematorium01 {key} (dry)", post("/building", {"prefab": "Crematorium01", **spec, "dryRun": True}))
    if d.get("error") or d.get("errors"):
        return None, d
    wd = {w["id"] for w in d.get("wouldDemolish") or []}
    if not wd <= allowed:
        print(f"  -> skip: would demolish {sorted(wd - allowed)} beyond the approved ids", flush=True)
        return None, d
    if wd and wd != allowed:
        print(f"  note: demolishes only {sorted(wd)} (subset of approved)", flush=True)
    r = show(f"Crematorium01 {key}", building("Crematorium01", **spec, allowDemolish=bool(wd)))
    return (r if r.get("applied") else None), d


def crem():
    st = load(P).get("crem") or {}
    others = ledger_boxes(PID)
    with paused(DEPT):
        for key, spec, exp, allowed in CREM:
            if st.get(key, {}).get("id"):
                print(f"{key}: already built {st[key]['id']}")
                continue
            done = None
            for s in [spec] + FALLBACK.get(key, []):
                d = post("/building", {"prefab": "Crematorium01", **s, "dryRun": True})
                if d.get("pos"):
                    box = footprint(d["pos"], d["lot"], d["facing"])
                    # housing's Westbrook box is the whole suburb and bus line 6 is a route box: not land use
                    hit = [h for h in overlaps(box, others) if h["project"] not in ("westbrook", "bus-capacity-and-new-lines")]
                    if hit:
                        print(f"{key}: {s['edge']} overlaps ledger sites {[h['project'] + ':' + h['what'][:40] for h in hit]}")
                        continue
                r, d = place_crem(key, s, exp, allowed if s is spec else set())
                if r:
                    done = {"id": r.get("placedId"), "pos": r.get("pos") or d.get("pos"), "facing": d.get("facing"),
                            "lot": d.get("lot"), "cost": r.get("cost"), "roadConnected": r.get("roadConnected"),
                            "demolished": sorted({w["id"] for w in d.get("wouldDemolish") or []}), "spec": s}
                    break
            st[key] = done or {}
            log(P, "crem", st)
    print(json.dumps(st, indent=1))


def ext():
    st = load(P).get("crem") or {}
    ex = load(P).get("ext") or {}
    owners = [(k, v["id"]) for k, v in st.items() if v.get("id")] + [("old_456598", "456598:5")]
    plan = {}
    for key, oid in owners:  # scan outside the batch (dry runs only)
        have, _ = installed(oid)
        if any("Hearse Garage" in (h or "") for h in have):
            print(f"{key}: Hearse Garage already installed")
            continue
        c = ext_scan(oid, "Hearse Garage")
        print(f"{key} {oid}: garage slot {c[0] + str(c[1]) if c else None} {c[2]['pos'] if c else ''}", flush=True)
        plan[key] = c
    with paused(DEPT):
        for key, oid in owners:
            have, _ = installed(oid)
            res = ex.get(key) or {}
            c = plan.get(key)
            if c and not any("Hearse Garage" in (h or "") for h in have):
                side, off, d = c
                r = show(f"{key} Hearse Garage {side}{off:+d}", ext_build(oid, "Hearse Garage", side, off))
                if r.get("applied"):
                    res["garage"] = {"pos": d["pos"], "facing": d["facing"], "lot": [4, 6], "side": side,
                                     "offset": off, "cost": r.get("cost"), "id": r.get("placedId") or r.get("id")}
            if not any("Incinerator" in (h or "") for h in have):
                d = post("/extension", {"owner": oid, "prefab": "Additional Incinerator", "dryRun": True}, timeout=120)
                if d.get("errors") == 0:
                    r = show(f"{key} Additional Incinerator", ext_build(oid, "Additional Incinerator"))
                    if r.get("applied"):
                        res["incinerator"] = {"cost": r.get("cost")}
                else:
                    show(f"{key} Additional Incinerator (dry)", d)
            ex[key] = res
            log(P, "ext", ex)
    for key, oid in owners:
        print(key, oid, installed(oid)[0])


BUFFER = [("403759:183", ("front", "back", "right", "left"), ()),
          ("278811:85", ("back", "left", "right", "front"), ()),
          ("267744:17", ("back", "left", "right", "front"), ()),
          ("1154205:25", ("right", "back", "front"), ("left",))]  # never left: Westpark


def buffer():
    bf = load(P).get("buffer") or {}
    plan = {}
    for oid, sides, forbid in BUFFER:
        if bf.get(oid):
            print(f"{oid}: burial lot already built")
            continue
        c = ext_scan(oid, "Additional Burial Lot", sides=sides, offsets=(0, -8, 8, -16, 16), forbid=forbid)
        print(f"{oid}: burial lot slot {c[0] + str(c[1]) if c else None} {c[2]['pos'] if c else ''}", flush=True)
        plan[oid] = c
    with paused(DEPT):
        for oid, c in plan.items():
            if not c:
                continue
            side, off, d = c
            r = show(f"{oid} Additional Burial Lot {side}{off:+d}", ext_build(oid, "Additional Burial Lot", side, off))
            if r.get("applied"):
                bf[oid] = {"pos": d["pos"], "facing": d["facing"], "lot": [6, 6], "side": side, "offset": off,
                           "cost": r.get("cost"), "id": r.get("placedId") or r.get("id")}
                log(P, "buffer", bf)
    print(json.dumps(bf, indent=1))


def verify():
    st = load(P).get("crem") or {}
    s = get("/services", kind="deathcare")["kinds"]["deathcare"]
    print(json.dumps(s["demand"]["throughput"])[:900])
    print("deadAwaitingHearse", s["demand"]["deadAwaitingHearse"], "requests", s["demand"]["requests"])
    ids = {v["id"] for v in st.values() if v.get("id")} | {"456598:5"}
    for b in s["buildings"]:
        if b["id"] in ids or b["type"] == "cemetery":
            print(b["id"], b["prefab"], b["pos"], b["status"], b["vehicles"], b["bodies"],
                  (b.get("processing") or {}).get("rate"), (b.get("processing") or {}).get("effectiveCapacity"))
    for k, v in st.items():
        if v.get("id"):
            bs = [b for b in get("/buildings", kind="service", x=v["pos"][0], z=v["pos"][1], radius=30).get("buildings") or []
                  if b["id"] == v["id"]]
            print(k, v["id"], bs[0] if bs else "MISSING")
    if len(sys.argv) > 2:
        sec = int(sys.argv[2])
        d = get("/deathcare", seconds=sec, timeout=sec + 120)
        print(json.dumps({k: v for k, v in d.items() if k != "facilities"})[:1500])
        for f in d.get("facilities") or []:
            if f.get("id") in ids or (f.get("bodiesPerDay") or f.get("storedPerDay")):
                print("  ", json.dumps(f)[:300])


def district():
    a = get("/areas", kind="district")
    for d in a.get("districts") or a.get("areas") or []:
        print(json.dumps(d)[:400])
    b = [x for x in get("/buildings", kind="service", x=-2795, z=-1061, radius=40).get("buildings") or []]
    print(b)
    r = get("/requests", kind="hearse", onlyFailed=1)
    print(json.dumps(r)[:2500])


if __name__ == "__main__":
    {"crem": crem, "ext": ext, "buffer": buffer, "verify": verify, "district": district}[MODE]()
