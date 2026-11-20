"""Troubleshooter: school seat sweep (worklist item b2-education-b2-edu-seat-sweep).

All clean seat extensions on existing schools: /extension with side 'auto' and no allowDemolish (the API now
refuses slots that would demolish and prefers slots that remove nothing). Skipped slots are reported.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_seat_sweep.py [--build]
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import get, paused, post  # noqa: E402

BUILD = "--build" in sys.argv
ES2, HS2 = "ElementarySchool02 Classroom Extension Building", "HighSchool02 Classroom Extension Building"
JOBS = [(o, ES2) for o in ("189068:55", "280991:43", "1876768:3", "1537254:39", "292455:45", "442536:7",
                           "440047:1", "440048:1", "440046:1")]
JOBS += [("353936:21", "ElementarySchool03 Extension Floors"), ("381515:7", "ElementarySchool03 Extension Floors"),
         ("1510166:11", "ElementarySchool01 Extension Wing")]
JOBS += [(o, HS2) for o in ("525823:31", "1341397:157", "1148081:25", "593974:83", "158054:1")]
JOBS += [("447338:67", "HighSchool03 Extension Floors")]

out = []
for owner, prefab in JOBS:
    with paused("troubleshooter"):
        d = post("/extension", {"owner": owner, "prefab": prefab, "side": "auto", "dryRun": True}, timeout=120)
        row = {"owner": owner, "prefab": prefab, "slot": d.get("slot"), "errors": d.get("errors"),
               "problems": d.get("problems"), "wouldDemolish": d.get("wouldDemolish"), "blocked": d.get("blocked"),
               "error": d.get("error"), "cost": d.get("cost")}
        ok = d.get("errors") == 0 and not d.get("wouldDemolish") and not d.get("blocked") and not d.get("error")
        if ok and BUILD:
            r = post("/extension", {"owner": owner, "prefab": prefab, "side": "auto"}, timeout=120)
            time.sleep(1.0)
            row.update(applied=r.get("applied"), placedId=r.get("placedId"), builtErrors=r.get("errors"),
                       builtWouldDemolish=r.get("wouldDemolish"))
    print(json.dumps(row)[:400])
    out.append(row)
built = [r for r in out if r.get("applied")]
print("built", len(built), "of", len(out), "cost", sum(r.get("cost") or 0 for r in built))
e = get("/education")
for lv in e["levels"][:2]:
    print(lv["level"], "seats", lv["seats"], "eligible", lv["eligible"], "avail", lv["availabilityPct"], "short", lv["shortfall"])
