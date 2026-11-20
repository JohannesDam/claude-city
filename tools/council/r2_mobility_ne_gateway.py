"""Council round 2 - Mobility - ne-gateway-interchange.

Grade-separate the at-grade crossing of z=3040 with the NE trunk (nodes 287222:1 / 287226:1, both signalised).
Clearance test (dry run on a parallel line at z=3130): 6 m -> 'Not Enough Clearance', 8 m and 9 m pass.
Large Road: maxSlope 0.2, elevatedLength 80 m.

Phase A (no park land beyond the existing z=3040 road footprint):
  R1 off-ramp NE-bound -> z=2930 street (dead end at x=-500), R2 on-ramp z=3150 street (dead end at x=-500) ->
  NE-bound, merging at a shallow angle at node 287219:1 (-486.7,3370.2);
  bulldoze 567603:59, 569777:53, 570552:9, 570565:13 (x -855.6..-578.7; nothing zoned fronts them);
  Medium 570807:17 (x -578.7..-444) is KEPT: industry lots front it and bulldozing it would delete their zone
  blocks, so the overpass lands on its node 547292:43 at (-578.7,3040) instead of at x=-444;
  overpass Large Road [-855.6,3040,0] -> [-700,3040,8] -> [-640,3040,8] -> [-578.7,3040,0] (5.1% / 13%),
  pillars at x=-700 and x=-640, outside the highway footprint x -688..-648.
Phase B (R3/R4 west ramps inside PROTECTED Claude Park) is NOT built: needs Parks' consent.
Phase C: buy tile (0,4363), widen the 2-lane trunk edges up to the NE interchange split (275..291, 4495..4506).

Usage: python -I council/r2_mobility_ne_gateway.py dry|phaseA|phaseC|verify
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short, upgrade  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\r2_mobility_ne_gateway_interchange.json"
K = ("applied", "cost", "errors", "problems", "conflicts", "wouldDemolish", "blocked", "error", "newEdgeIds", "staleEdgeIds")
RAMP = "Highway Oneway - 1 lane"

R1 = {"a": dict(points=[[-735.3, 2865.6], [-697, 2912], [-637, 2930], [-500, 2930]], curve="smooth", startDir=[0.39, 0.92], endDir=[1, 0]),
      "b": dict(points=[[-735.3, 2865.6], [-680, 2900], [-600, 2925], [-500, 2930]], curve="smooth")}
R2 = {"a": dict(points=[[-500, 3150], [-524, 3180], [-522, 3262], [-497, 3330], [-486.7, 3370.2]], curve="smooth", startDir=[-0.7, 0.71], endDir=[0.476, 0.880]),
      "c": dict(points=[[-500, 3150], [-530, 3170], [-534, 3250], [-505, 3325], [-486.7, 3370.2]], curve="smooth", startDir=[-0.83, 0.55], endDir=[0.476, 0.880]),
      "b": dict(points=[[-500, 3150], [-530, 3172], [-575.9, 3205.2]], curve="smooth", endDir=[0.442, 0.897])}
CROSSING = ["567603:59", "569777:53", "570552:9", "570565:13"]
OVERPASS = {8: [[-855.6, 3040, 0], [-700, 3040, 8], [-640, 3040, 8], [-578.7, 3040, 0]],
            9: [[-855.6, 3040, 0], [-702, 3040, 9], [-638, 3040, 9], [-578.7, 3040, 0]]}
# at-grade pieces to restore if the overpass cannot be built
RESTORE = [("Medium Road", [[-855.6, 3040], [-759.5, 3040]]), ("Large Road", [[-759.5, 3040], [-676.9, 3045.4]]),
           ("Medium Road", [[-676.9, 3045.4], [-658.8, 3036.9]]), ("Large Road", [[-658.8, 3036.9], [-578.7, 3040]])]
TRUNK_NE = ["395311:1", "395518:1", "395514:1", "395511:1"]   # NE-bound 2-lane edges up to the split
TRUNK_SW = ["395512:1", "395513:1", "395516:1", "395314:1"]   # SW-bound 2-lane edges from the merge


def load():
    try:
        with open(LOG, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def save(d):
    with open(LOG, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=1)


def s(r):
    out = {k: r.get(k) for k in K if r.get(k) not in (None, [], {})}
    g = r.get("geometry")
    if g:
        out["geo"] = {k: g.get(k) for k in ("length", "minRadius", "maxSlope", "warnings") if g.get(k) is not None}
    return out


def ramp(variant, spec, dry):
    return post("/road", {"prefab": RAMP, **spec, "dryRun": dry})


log = load()

if MODE == "dry":
    with paused("mobility"):
        for name, specs in (("R1", R1), ("R2", R2)):
            for v, spec in specs.items():
                r = ramp(v, spec, True)
                log[f"{name}{v}Dry"] = s(r)
                print(name, v, s(r))
        r = post("/bulldoze", {"edges": CROSSING, "dryRun": True})
        log["bulldozeDry"] = r
        print("bulldoze dry:", json.dumps(r)[:900])
        for nm, line in (("z2930", [[-500, 2930], [-290, 2930]]), ("z3150", [[-500, 3150], [-290, 3150]])):
            r = post("/upgrade", {"prefab": "Medium Road", "polyline": line, "prefabFilter": "Small Road", "dryRun": True})
            log[f"{nm}UpgradeDry"] = {**s(r), "edgeIds": r.get("edgeIds"), "selection": r.get("selection")}
            print(nm, log[f"{nm}UpgradeDry"])
    save(log)

elif MODE == "phaseA":
    r1v = sys.argv[2] if len(sys.argv) > 2 else "a"
    r2v = sys.argv[3] if len(sys.argv) > 3 else "a"
    res = {}
    with paused("mobility"):
        t0 = time.time()
        # confirm the crossing edges are still the ones planned
        segs = {x["id"]: x for x in get("/nets", x=-717, z=3040, radius=150).get("segments", [])}
        missing = [e for e in CROSSING if e not in segs]
        res["missingBefore"] = missing
        if missing:
            print("crossing edges changed, stop:", missing)
        else:
            res["R1"] = s(ramp(r1v, R1[r1v], False)); print("R1", res["R1"])
            res["R2"] = s(ramp(r2v, R2[r2v], False)); print("R2", res["R2"])
            res["bulldoze"] = short(post("/bulldoze", {"edges": CROSSING}), ("applied", "cost", "errors", "problems", "error", "selection"))
            print("bulldoze", res["bulldoze"])
            time.sleep(1.0)
            built = None
            for h in (8, 9):
                d = post("/road", {"prefab": "Large Road", "points": OVERPASS[h], "dryRun": True})
                res[f"overpass{h}Dry"] = s(d); print("overpass dry", h, s(d))
                if d.get("errors") == 0 and not d.get("wouldDemolish"):
                    r = post("/road", {"prefab": "Large Road", "points": OVERPASS[h]})
                    res["overpass"] = {**s(r), "deck": h, "builtEdges": r.get("builtEdges")}
                    print("overpass", h, s(r))
                    if r.get("applied"):
                        built = h
                        break
            if built is None:
                print("overpass failed - restoring the at-grade crossing")
                res["restore"] = [s(post("/road", {"prefab": p, "points": pts})) for p, pts in RESTORE]
                print(res["restore"])
            print(f"batch {time.time() - t0:.0f} s")
    log["phaseA"] = res
    save(log)

elif MODE == "phaseA2":
    # second attempt after the Large deck hit the OldCastle01 lot (828027:27) beside x -858..-828: a MEDIUM Road
    # overpass (24 m, the same width as today's road beside the castle). The first attempt restored the
    # at-grade crossing with new ids; R1/R2 are already built.
    crossing = ["465097:9", "465101:9", "465568:11", "569777:63", "769032:51"]
    res = {}
    with paused("mobility"):
        t0 = time.time()
        segs = {x["id"]: x for x in get("/nets", x=-717, z=3040, radius=160).get("segments", [])}
        missing = [e for e in crossing if e not in segs]
        res["missingBefore"] = missing
        if missing:
            print("crossing edges changed, stop:", missing)
        else:
            res["bulldoze"] = short(post("/bulldoze", {"edges": crossing}), ("applied", "cost", "errors", "problems", "error", "selection"))
            print("bulldoze", res["bulldoze"])
            time.sleep(1.0)
            built = None
            for h in (8, 9):
                d = post("/road", {"prefab": "Medium Road", "points": OVERPASS[h], "dryRun": True})
                res[f"overpass{h}Dry"] = s(d); print("overpass dry", h, s(d))
                if d.get("errors") == 0 and not d.get("wouldDemolish"):
                    r = post("/road", {"prefab": "Medium Road", "points": OVERPASS[h]})
                    res["overpass"] = {**s(r), "deck": h, "prefab": "Medium Road", "builtEdges": r.get("builtEdges")}
                    print("overpass", h, s(r))
                    if r.get("applied"):
                        built = h
                        break
            if built is None:
                print("overpass failed - restoring the at-grade crossing")
                res["restore"] = [s(post("/road", {"prefab": p, "points": pts})) for p, pts in RESTORE]
                print(res["restore"])
            print(f"batch {time.time() - t0:.0f} s")
    log["phaseA2"] = res
    if res.get("overpass", {}).get("applied"):
        log.setdefault("phaseA", {})["overpass"] = res["overpass"]
    save(log)

elif MODE == "trees":
    # Trees on both sides of the at-grade/ramp parts of the new overpass (elevated parts may refuse)
    ids = [e["id"] for e in (log.get("phaseA", {}).get("overpass", {}).get("builtEdges") or [])]
    with paused("mobility"):
        r = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both", "dryRun": True})
        print("trees dry", s(r), r.get("skipped") or r.get("selection"))
        if r.get("errors") == 0 and not r.get("wouldDemolish"):
            r = post("/roadupgrade", {"edges": ids, "upgrade": "Trees", "side": "both"})
            print("trees", s(r))
        log["overpassTrees"] = s(r)
    save(log)

elif MODE == "streets":
    # z=2930 / z=3150 industrial streets that now carry the ramps: Medium only where nothing is demolished
    out = {}
    with paused("mobility"):
        for nm, line in (("z2930", [[-500, 2930], [-290, 2930]]), ("z3150", [[-500, 3150], [-290, 3150]])):
            d = post("/upgrade", {"prefab": "Medium Road", "polyline": line, "prefabFilter": "Small Road", "dryRun": True})
            print(nm, "dry", s(d), d.get("edgeIds"))
            if d.get("errors") == 0 and not d.get("wouldDemolish") and not d.get("blocked"):
                r = post("/upgrade", {"prefab": "Medium Road", "polyline": line, "prefabFilter": "Small Road"})
                out[nm] = {**s(r), "edgeIds": d.get("edgeIds")}
            else:
                out[nm] = {"skipped": s(d)}
            print(nm, out[nm])
    log["streets"] = out
    save(log)

elif MODE == "phaseC":
    res = {}
    t = [c for c in get("/tiles").get("candidates", []) if c.get("centre") == [0, 4363]]
    res["tileBefore"] = t
    print("tile:", t)
    with paused("mobility"):
        if t:
            r = post("/buytile", {"x": 0, "z": 4363})
            res["buytile"] = r
            print("buytile:", json.dumps(r)[:600])
        for nm, batch in (("NE", TRUNK_NE), ("SW", TRUNK_SW)):
            d = upgrade("Highway Oneway - 3 lanes", batch, dryRun=True)
            print(nm, "dry", s(d))
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                r = upgrade("Highway Oneway - 3 lanes", batch)
                res[nm] = {**s(r), "edgeIds": d.get("edgeIds")}
            else:
                res[nm] = {"skipped": s(d)}
            print(nm, res[nm])
    log["phaseC"] = res
    save(log)

elif MODE == "verify":
    v = {}
    v["nodes"] = get("/nodes", x=-668, z=3041, radius=40, minDegree=3).get("nodes")
    v["nets"] = [(x["id"], x["prefab"], x["start"], x["end"], x.get("elevation")) for x in get("/nets", x=-680, z=3040, radius=140).get("segments", [])]
    v["lines"] = [{k: l.get(k) for k in ("id", "number", "complete", "flags", "waitingPassengers", "vehicles", "length", "duration")} for l in get("/lines").get("lines", [])]
    v["hotspots"] = get("/hotspots", type="Traffic", cell=300).get("hotspots", [])[:12]
    v["trunk"] = [(x["id"], x["prefab"]) for x in get("/nets", x=100, z=4250, radius=280, filter="Highway").get("segments", [])]
    for k, val in v.items():
        print(k, json.dumps(val)[:1500])
    log["verify"] = v
    save(log)
