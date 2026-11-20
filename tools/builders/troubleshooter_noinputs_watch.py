"""Troubleshooter: track the No Inputs trend (count, buy cost of Stone/Ore, where inputs come from, cargo per mode).
Appends one JSON line per run to logs/council/noinputs_watch.jsonl and prints it.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_noinputs_watch.py
"""
import json
import os
import statistics
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import get  # noqa: E402

LOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "logs", "council", "noinputs_watch.jsonl")
c = get("/companies", problems=1)
costs = {}
src = {}
for x in c.get("companies", []):
    for i in x.get("inputs", []):
        if i.get("buyCost") is not None:
            costs.setdefault(i["resource"], []).append(i["buyCost"])
    k = (x.get("lastPurchaseFrom") or {}).get("kind")
    src[k] = src.get(k, 0) + 1
f = get("/freight")["summary"]["cargoStatistics"]
row = {"date": get("/city").get("date"), "noInputs": c.get("withNoInputs"),
       "buyCostMedian": {r: round(statistics.median(v), 2) for r, v in costs.items()},
       "listedLastPurchaseFrom": src, "cityLastPurchaseFrom": c.get("lastInputPurchaseFrom"),
       "cargo": {k.replace("CargoCount", ""): v for k, v in f.items()}}
print(json.dumps(row))
with open(LOG, "a", encoding="utf-8") as fh:
    fh.write(json.dumps(row) + "\n")
