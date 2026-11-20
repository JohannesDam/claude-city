"""Council round 2 - Health & Safety - westbrook-civic-strip.

FireHouse01, PoliceStation02 and MedicalClinic02 on Housing's reserved civic strip (x -4428..-4388, z 430..760),
left (west) side of the x=-4380 lane, all 5x5 lots facing east onto the lane. PostOffice02 (step 4) is only
built with Utilities' agreement: none was given this round, so its lot (z ~664..706) stays free.

Usage: python -I council/r2_health_safety_westbrook_civic_strip.py base|build|verify
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools\council")
from r2_health_safety_common import (get, post, paused, building, show, log, load, footprint, ledger_boxes,  # noqa: E402
                                     overlaps, DEPT)

P = "westbrook_civic_strip"
PID = "westbrook-civic-strip"
MODE = sys.argv[1] if len(sys.argv) > 1 else "base"
STRIP = (-4428, -4388, 430, 760)
PLAN = [
    ("FireHouse01", dict(edge="1200136:91", t=0.30, side="left", gap=0.5), 480),
    ("PoliceStation02", dict(edge="1200136:91", t=0.619, side="left", gap=0.5), 532),
    ("MedicalClinic02", dict(edge="563535:209", t=0.238, side="left", gap=0.5), 634),
]
WEST = -4300  # Westbrook is x < -4300


def westbrook_cells(service):
    c = get("/coverage", service=service, layer="homes")
    worst = [w for w in c.get("worstResidentialCells") or [] if w["center"][0] < WEST]
    return {"summary": c.get("summary"), "westbrookWorst": [(w["center"], w["households"], w["avgCoverage"]) for w in worst[:8]]}


def base():
    out = {s: westbrook_cells(s) for s in ("Healthcare", "Police", "FireRescue")}
    for s, v in out.items():
        print(s, json.dumps(v)[:900])
    log(P, "baseline", out)


def inside(box):
    x0, x1, z0, z1 = STRIP
    return box[0] >= x0 - 1 and box[1] <= x1 + 1 and box[2] >= z0 - 1 and box[3] <= z1 + 1


def spec_for(prefab, spec, zt):
    """The planned spec when its dry run is clean and inside the strip, else the nearest clean /findspot
    candidate inside the strip."""
    d = post("/building", {"prefab": prefab, **spec, "dryRun": True})
    if d.get("errors") == 0 and not d.get("wouldDemolish") and inside(footprint(d["pos"], d["lot"], d["facing"])):
        return spec, d
    show(f"{prefab} planned (dry)", d)
    for c in get("/findspot", prefab=prefab, x=-4408, z=zt, radius=30, n=8).get("candidates") or []:
        b = dict(c["building"])
        b.pop("prefab", None)
        d = post("/building", {"prefab": prefab, **b, "dryRun": True})
        if d.get("errors") == 0 and not d.get("wouldDemolish") and inside(footprint(d["pos"], d["lot"], d["facing"])):
            return b, d
    return None, d


def build():
    st = load(P).get("built") or {}
    others = [o for o in ledger_boxes(PID) if o["project"] not in ("westbrook", "bus-capacity-and-new-lines")]
    with paused(DEPT):
        for prefab, spec, zt in PLAN:
            if (st.get(prefab) or {}).get("id"):
                print(prefab, "already built", st[prefab]["id"])
                continue
            s, d = spec_for(prefab, spec, zt)
            if not s:
                show(f"{prefab}: no clean spot in the strip", d)
                continue
            box = footprint(d["pos"], d["lot"], d["facing"])
            if overlaps(box, others):
                print(prefab, "overlaps ledger", overlaps(box, others))
                continue
            show(f"{prefab} (dry)", d)
            r = show(prefab, building(prefab, **s))
            if r.get("applied"):
                st[prefab] = {"id": r.get("placedId"), "pos": d["pos"], "facing": d["facing"], "lot": d["lot"],
                              "box": box, "spec": s, "cost": r.get("cost"), "roadConnected": r.get("roadConnected")}
                log(P, "built", st)
    print(json.dumps(st, indent=1))


def verify():
    st = load(P)
    ids = {v["id"] for v in (st.get("built") or {}).values()}
    for kind in ("fire", "police", "healthcare"):
        s = get("/services", kind=kind)["kinds"][kind]
        for b in s["buildings"]:
            if b["id"] in ids:
                print(kind, json.dumps({k: b.get(k) for k in ("id", "prefab", "pos", "status", "vehicles", "efficiency", "workers")}))
    for bid in ids:
        v = [x for x in (st.get("built") or {}).values() if x["id"] == bid][0]
        bs = [b for b in get("/buildings", kind="service", x=v["pos"][0], z=v["pos"][1], radius=20).get("buildings") or [] if b["id"] == bid]
        print(bid, bs[0] if bs else "MISSING")
    print("baseline:", json.dumps(st.get("baseline"))[:1500])
    for s in ("Healthcare", "Police", "FireRescue"):
        print("now", s, json.dumps(westbrook_cells(s))[:900])


if __name__ == "__main__":
    {"base": base, "build": build, "verify": verify}[MODE]()
