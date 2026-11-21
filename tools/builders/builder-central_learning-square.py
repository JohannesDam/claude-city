"""builder-central: Learning Square (b2-education-b2-edu-downtown-learning-square) + Downtown school seats.

Elementary seats are short city-wide (2033-01: 31.1k eligible vs 28.8k seats, 29 of 30 schools full).
Stages:
  ext    - free-slot extensions on existing schools in my districts (ES02 Classroom Extension Building +100 seats,
           ES03 Extension Floors), side "auto" = slots that remove nothing.
  es     - ElementarySchool03 in Downtown south-west via POST /place all:True (least demolition, make_room-style
           limits: low homes only, no companies), logged to logs/council/demolitions.jsonl.
  hs     - HighSchool03 the same way (city-wide high school has a surplus, so only with small demolition).
  floors - ES03/HS03 Extension Floors on the new schools.

usage: python -I builders/builder-central_learning-square.py ext|es|hs|floors [dry]
"""
import json
import sys
import time

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, get, paused, demolition_cost  # noqa: E402

WHO = "builder-central"
ITEM = "b2-education-b2-edu-downtown-learning-square"
STAGE = sys.argv[1] if len(sys.argv) > 1 else "ext"
DRY = "dry" in sys.argv[2:]
# Downtown south-west (phase box of the item, widened inside Downtown so /place can choose)
SW = [-2500, 450, -2150, 1100]
NEAR = [-2365, 760]

EXT = [  # (owner, prefab) - schools inside or between my districts with a free slot that removes nothing
    ("1537254:39", "ElementarySchool02 Classroom Extension Building"),  # (-1751,398) Downtown/Office Park edge
    ("442536:7", "ElementarySchool02 Classroom Extension Building"),    # (-1826,398)
    ("440047:1", "ElementarySchool02 Classroom Extension Building"),    # (-1579,1088) Downtown
    ("440048:1", "ElementarySchool02 Classroom Extension Building"),    # (-2019,1798) Downtown
    ("440046:1", "ElementarySchool02 Classroom Extension Building"),    # (-1103,358) Waterfront edge
    ("506425:419", "ElementarySchool03 Extension Floors"),              # Lakeshore Bay School
]


def log_demolition(prefab, res, chosen):
    would = chosen.get("demolition") or chosen.get("wouldDemolish") or []
    if isinstance(would, dict):
        would = would.get("list") or would.get("buildings") or []
    with open(r"C:\soft\cityskyline\logs\council\demolitions.jsonl", "a", encoding="utf-8") as f:
        f.write(json.dumps({"at": time.strftime("%Y-%m-%d %H:%M"), "by": WHO, "item": ITEM, "prefab": prefab,
                            "placedId": res.get("placedId"), "pos": [chosen.get("x"), chosen.get("z")],
                            "demolition": chosen.get("demolition")}) + "\n")


def place(prefab, max_hh):
    body = {"prefab": prefab, "near": NEAR, "within": SW, "radius": 450, "maxHouseholds": max_hh,
            "maxCompanies": 0, "all": True, "dryRun": DRY}
    if DRY:
        r = post("/place", body, timeout=600)
    else:
        with paused(WHO):
            r = post("/place", body, timeout=600)
    c = r.get("chosen") or {}
    print(json.dumps({k: r.get(k) for k in ("applied", "placedId", "error", "hint")}))
    print("chosen", json.dumps({k: c.get(k) for k in ("x", "z", "angle", "cost", "errors", "demolition",
                                                       "roadConnected", "corners")})[:1500])
    for cand in (r.get("candidates") or [])[:12]:
        print("  cand", json.dumps({k: cand.get(k) for k in ("x", "z", "errors", "demolition", "roadConnected")})[:300])
    if not DRY and r.get("placedId"):
        log_demolition(prefab, r, c)
    return r


if STAGE == "ext":
    for owner, prefab in EXT:
        body = {"owner": owner, "prefab": prefab, "side": "auto", "dryRun": True}
        d = post("/extension", body, timeout=120)
        ok = d.get("errors") == 0 and not d.get("blocked") and not d.get("wouldDemolish")
        print(owner, prefab, "dry", json.dumps({k: d.get(k) for k in ("errors", "blocked", "wouldDemolish", "cost", "side", "problems")})[:400])
        if ok and not DRY:
            with paused(WHO):
                r = post("/extension", {**body, "dryRun": False}, timeout=120)
            print("   built", json.dumps({k: r.get(k) for k in ("applied", "placedId", "errors", "cost", "side")})[:300])
elif STAGE == "es":
    place("ElementarySchool03", 14)
elif STAGE == "hs":
    place("HighSchool03", 10)
elif STAGE == "floors":
    args = [a for a in sys.argv[2:] if a != "dry"]  # owner prefab owner prefab ...
    for owner, prefab in zip(args[0::2], args[1::2]):
        body = {"owner": owner, "prefab": prefab, "side": "auto", "dryRun": DRY}
        if DRY:
            r = post("/extension", body, timeout=120)
        else:
            with paused(WHO):
                r = post("/extension", body, timeout=120)
        print(owner, prefab, json.dumps({k: r.get(k) for k in ("applied", "errors", "blocked", "wouldDemolish", "cost")})[:400])
