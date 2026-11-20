"""Shared helpers for the council round 2 Economic Affairs scripts (logging, spend tracking, site ledger)."""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
DEPT = "economy"
LEDGER = r"C:\soft\cityskyline\logs\council\sites.jsonl"
KEYS = ("applied", "cost", "errors", "problems", "placedId", "roadConnected", "error", "warning", "blocked",
        "wouldDemolish", "conflicts", "pos", "angle", "bought", "newEdgeIds", "resourceAmount", "cells")


class Log:
    def __init__(self, path):
        self.path = path

    def load(self):
        try:
            with open(self.path) as f:
                return json.load(f)
        except Exception:
            return {}

    def put(self, key, val):
        d = self.load()
        d[key] = val
        with open(self.path, "w") as f:
            json.dump(d, f, indent=1)

    def spend(self, amount, what):
        d = self.load()
        d.setdefault("spent", []).append([what, amount or 0])
        with open(self.path, "w") as f:
            json.dump(d, f, indent=1)

    def total(self):
        return sum(a for _, a in self.load().get("spent", []))


def show(label, r, keys=KEYS):
    print(f"{label:<44} {json.dumps(short(r or {}, keys=keys), default=str)[:1500]}", flush=True)
    return r


def ledger_rows():
    rows = []
    with open(LEDGER, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows


def ledger_overlaps(x0, x1, z0, z1, ignore_project=None):
    out = []
    for r in ledger_rows():
        if ignore_project and r.get("project") == ignore_project:
            continue
        if r["x0"] < x1 and r["x1"] > x0 and r["z0"] < z1 and r["z1"] > z0:
            out.append(r)
    return out


def ledger_append(project, x0, x1, z0, z1, what, ids):
    row = {"round": 2, "department": DEPT, "project": project, "x0": x0, "x1": x1, "z0": z0, "z1": z1,
           "what": what, "ids": ids}
    with open(LEDGER, "a", encoding="utf-8") as f:
        f.write(json.dumps(row) + "\n")
    print("ledger +", json.dumps(row))
