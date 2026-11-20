"""Troubleshooter: one-line deathcare status (dead waiting, rates, facility states) for before/after checks.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_dcwatch.py [seconds]
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import get  # noqa: E402

secs = int(sys.argv[1]) if len(sys.argv) > 1 else 0
if secs:
    m = get("/deathcare", seconds=secs, timeout=secs + 120)
    print("measured", m.get("window", {}).get("date"), m.get("totals"))
d = get("/services", kind="deathcare")["kinds"]["deathcare"]
dem = d["demand"]
print(get("/city").get("date"), "dead", dem["deadCitizens"], "awaiting", dem["deadAwaitingHearse"], "requests", dem["requests"],
      "status", d["totals"]["statusCounts"], "vehicles", d["totals"]["vehicles"])
for b in d["buildings"]:
    v = b["vehicles"]
    proc = b.get("processing") or {}
    meas = proc.get("measured") or (b.get("intake") or {}).get("measured") or {}
    print(f"  {b['id']:12} {b['prefab']:14} {str([round(p) for p in b['pos']]):16} {','.join(b['status']):22} "
          f"hearses {v['inUse']}/{v['usable']:2} bodies {b['bodies']['count']}/{b['bodies']['capacity']} "
          f"perDay {meas.get('bodiesPerDay', meas.get('storedPerDay'))}")
