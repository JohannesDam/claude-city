"""Council round 2 - Education - hs-seats-now.

5 HS02 classroom extensions (+100 seats each) + HighSchool01 Library on both HS01.
Never demolish; skip a school whose sides are all blocked. No Sports Field at 1552809:15.

Usage: python -I council/r2_education_hs_seats_now.py dry|build|verify
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\r2_education_hs_seats_now.json"

HS02 = ["1148081:25", "1341397:157", "593974:83", "525823:31", "158054:1"]
HS01 = ["1551911:3", "1552809:15"]
CLASS = "HighSchool02 Classroom Extension Building"
LIB = "HighSchool01 Library"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "id", "side", "pos", "error", "warning", "blocked",
        "wouldDemolish", "conflicts", "upgradeId", "extensionId")


def s(d):
    return {k: d.get(k) for k in KEYS if d.get(k) not in (None, [], {})}


def clean(d):
    if not isinstance(d, dict) or d.get("error") or d.get("blocked"):
        return False
    if d.get("errors") not in (0, None):
        return False
    probs = json.dumps(d.get("problems") or [])
    if "Overlap Existing" in probs:
        return False
    if d.get("wouldDemolish"):
        return False
    return True


def ext(owner, prefab, side=None, dry=True):
    body = {"owner": owner, "prefab": prefab, "dryRun": dry}
    if side:
        body["side"] = side
    return post("/extension", body, timeout=120)


def probe(prefab, pos):
    """/extension does not report zoned buildings under a placeable upgrade (the game removes them silently);
    a /building dry run of the same lot at the slot position does. Returns the wouldDemolish list."""
    r = post("/building", {"prefab": prefab, "x": pos[0], "z": pos[1], "angle": 0, "dryRun": True})
    return r.get("wouldDemolish") or [], r


def classroom(owner, build):
    seen = set()
    for side in ("auto", "back", "left", "right"):
        d = ext(owner, CLASS, side, True)
        slot = d.get("slot") or side
        print(f"  {owner} classroom side={side} slot={slot} dry: {json.dumps(s(d))[:400]}", flush=True)
        if not clean(d) or slot in seen:
            continue
        seen.add(slot)
        wd, _ = probe(CLASS, d["pos"])
        if wd:
            print(f"    -> slot {slot} at {d['pos']} would demolish {len(wd)} zoned: "
                  f"{[(w['id'], w['prefab'], w.get('renters')) for w in wd]}", flush=True)
            continue
        if not build:
            return {"side": slot, "pos": d["pos"], "dry": s(d)}
        r = ext(owner, CLASS, slot, False)
        print(f"  {owner} classroom slot={slot} BUILD: {json.dumps(s(r))[:600]}", flush=True)
        return {"side": slot, "pos": d["pos"], "result": r}
    print(f"  {owner}: no clean side - skipped", flush=True)
    return None


def library(owner, build):
    d = ext(owner, LIB, None, True)
    print(f"  {owner} library dry: {json.dumps(s(d))[:600]}", flush=True)
    if clean(d) and build:
        r = ext(owner, LIB, None, False)
        print(f"  {owner} library BUILD: {json.dumps(s(r))[:600]}", flush=True)
        return r
    return d if not build else None


def seats():
    for lv in get("/education").get("levels", []):
        if lv["level"] == "highschool":
            print("highschool:", {k: lv[k] for k in ("schools", "students", "seats", "eligible", "shortfall")}, flush=True)
            return lv


def run(build):
    out = {"classroom": {}, "library": {}}
    seats()
    for owner in HS02:
        with paused("education"):
            out["classroom"][owner] = classroom(owner, build)
    with paused("education"):
        for owner in HS01:
            out["library"][owner] = library(owner, build)
    if build:
        spent = 0
        for v in out["classroom"].values():
            if v and v.get("result", {}).get("applied"):
                spent += v["result"].get("cost") or 0
        for v in out["library"].values():
            if v and v.get("applied"):
                spent += v.get("cost") or 0
        out["spent"] = spent
        print("SPENT", spent, flush=True)
        json.dump(out, open(LOG, "w"), indent=1)
    return out


def verify():
    seats()
    for owner in HS02 + HS01:
        d = get("/extensions", owner=owner)
        print(owner, d.get("prefab"), [(i["id"], i["prefab"], i["pos"]) for i in d.get("installed", [])], flush=True)


if __name__ == "__main__":
    if MODE == "dry":
        run(False)
    elif MODE == "build":
        run(True)
        verify()
    elif MODE == "verify":
        verify()
