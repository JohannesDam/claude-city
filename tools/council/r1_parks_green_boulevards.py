"""Council round 1 - Recreation, Beauty & Environment - green-boulevards.

Street trees, then grass, on both sides of every Medium Road segment within 4.2 km of (-2000, 1200);
the x=-1909 downtown avenue first, then the north (z > 2800), then the rest. Chunks of 10 edges, each in its
own short paused batch; segment ids are re-read before every chunk (upgrades can re-create edges).
Grass removes bike lanes and wide sidewalks (its unset flags), so edges that already have either keep them
and get no grass. Edges whose upgrade fails validation are retried one by one and skipped if they still fail.

Usage: python -I "council/r1_Recreation, Beauty & Environment_green-boulevards.py" baseline|trees|grass|ramps|verify [--dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
DEPT = "Recreation, Beauty & Environment"
MODE = sys.argv[1] if len(sys.argv) > 1 else "verify"
DRY = "--dry" in sys.argv
LOG = r"C:\soft\cityskyline\logs\council\r1_recreation_green_boulevards.json"
CAP = 27500  # approved capex 25k + 10 %
RAMPS = ["395359:1", "395337:1", "395334:1", "395350:1"]
KEEP = ("WideSidewalk", "SecondaryLane")


def load():
    try:
        return json.load(open(LOG))
    except Exception:
        return {}


def save(d):
    json.dump(d, open(LOG, "w"), indent=1)


def key(s):
    return (round(s["start"][0]), round(s["start"][1]), round(s["end"][0]), round(s["end"][1]))


def prio(s):
    if abs(s["start"][0] + 1909) < 2 or abs(s["end"][0] + 1909) < 2:
        return (0, -max(s["start"][1], s["end"][1]))
    if min(s["start"][1], s["end"][1]) > 2800:
        return (1, 0)
    return (2, 0)


def medium():
    segs = get("/nets", x=-2000, z=1200, radius=4200, filter="Medium").get("segments") or []
    segs = [s for s in segs if s.get("prefab") == "Medium Road"]
    return sorted(segs, key=prio)


def noise_avenue():
    d = get("/pollution", type="noise", x=-1909, z=1250, size=800, cell=40)
    rows = (d.get("grid") or "").splitlines()
    return {"max": d.get("max"), "grid": rows}


def run(upgrade):
    d = load()
    st = d.setdefault(upgrade, {"done": [], "failed": {}, "kept": [], "cost": 0, "edges": 0})
    spent_total = d.get("spent", 0)
    seen = set(tuple(k) for k in st["done"]) | set(tuple(json.loads(k)) for k in st["failed"]) | set(tuple(k) for k in st["kept"])
    while True:
        todo = [s for s in medium() if key(s) not in seen][:10]
        if not todo:
            break
        ids = [s["id"] for s in todo]
        bykey = {s["id"]: key(s) for s in todo}
        with paused(DEPT):
            dr = post("/roadupgrade", {"edges": ids, "upgrade": upgrade, "side": "both", "dryRun": True})
            if dr.get("error"):
                print("dry error", dr.get("error"))
                for s in todo:
                    seen.add(key(s))
                    st["failed"][json.dumps(key(s))] = dr.get("error")
                continue
            apply_ids = []
            for e in dr.get("edges") or []:
                k = bykey.get(e["id"])
                before = json.dumps(e.get("before") or {})
                if upgrade == "Grass" and any(f in before for f in KEEP):
                    st["kept"].append(k)
                    seen.add(k)
                    continue
                if e.get("changes"):
                    apply_ids.append(e["id"])
                else:
                    st["done"].append(k)  # already has it / not supported
                    if e.get("skipped") and "already" not in e["skipped"]:
                        st["failed"][json.dumps(k)] = e["skipped"]
                    seen.add(k)
            groups = [apply_ids] if dr.get("errors") == 0 else [[i] for i in apply_ids]
            for g in groups:
                if not g:
                    continue
                if len(groups) > 1:
                    t = post("/roadupgrade", {"edges": g, "upgrade": upgrade, "side": "both", "dryRun": True})
                    if t.get("errors") or t.get("error"):
                        for i in g:
                            st["failed"][json.dumps(bykey[i])] = (t.get("problems") or [t.get("error")])
                            seen.add(bykey[i])
                        continue
                cost = (dr.get("cost") or 0) if len(groups) == 1 else (t.get("cost") or 0)
                if spent_total + cost > CAP:
                    print("capex cap reached - stopping")
                    if not DRY:
                        save(d)
                    return
                if DRY:
                    r = {"applied": False, "cost": cost, "errors": 0}
                else:
                    r = post("/roadupgrade", {"edges": g, "upgrade": upgrade, "side": "both"})
                for i in g:
                    seen.add(bykey[i])
                    if r.get("applied") or DRY:
                        st["done"].append(bykey[i])
                    else:
                        st["failed"][json.dumps(bykey[i])] = r.get("problems") or r.get("error")
                if r.get("applied"):
                    st["cost"] += r.get("cost") or 0
                    st["edges"] += len(g)
                    spent_total += r.get("cost") or 0
                print(f"{upgrade} {len(g):>2} edges  {json.dumps(short(r))}", flush=True)
        if not DRY:
            d["spent"] = spent_total
            save(d)
    if not DRY:
        d["spent"] = spent_total
        save(d)
    print(f"{upgrade}: applied {st['edges']} edges, cost {st['cost']}, kept {len(st['kept'])}, failed {len(st['failed'])}")
    for k, v in list(st["failed"].items())[:20]:
        print("  failed", k, v)


if __name__ == "__main__":
    if MODE == "baseline":
        segs = medium()
        print("medium segments", len(segs), "km", round(sum(s.get("length", 0) for s in segs) / 1000, 2))
        print("avenue", [s["id"] for s in segs if prio(s)[0] == 0])
        d = load()
        d["baseline"] = {"segments": len(segs), "noise": noise_avenue()}
        save(d)
        print("\n".join(d["baseline"]["noise"]["grid"]))
    elif MODE in ("trees", "grass"):
        run("Trees" if MODE == "trees" else "Grass")
    elif MODE == "ramps":
        with paused(DEPT):
            r = post("/roadupgrade", {"edges": RAMPS, "upgrade": "Trees", "side": "both", "dryRun": True})
            print("ramps (dry)", short(r), [(e["id"], e.get("prefab"), e.get("skipped")) for e in r.get("edges") or []], r.get("error"))
            ok = [e["id"] for e in r.get("edges") or [] if e.get("changes")]
            if ok and r.get("errors") == 0 and not DRY:
                print("ramps", short(post("/roadupgrade", {"edges": ok, "upgrade": "Trees", "side": "both"})))
    elif MODE == "verify":
        d = load()
        for u in ("Trees", "Grass"):
            st = d.get(u, {})
            print(u, "edges", st.get("edges"), "cost", st.get("cost"), "kept", len(st.get("kept", [])), "failed", len(st.get("failed", {})))
        n = noise_avenue()
        d["after"] = {"noise": n}
        save(d)
        print("before max", d.get("baseline", {}).get("noise", {}).get("max"), "after max", n["max"])
        for a, b in zip(d.get("baseline", {}).get("noise", {}).get("grid", []), n["grid"]):
            print(f"{a:<26} | {b}")
        print("SPENT", d.get("spent"))
