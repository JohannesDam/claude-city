"""Troubleshooter: fire house for south Westbrook (worklist item b1-ts-westbrook-fire).

Westbrook has one FireHouse01 (201369:191 at -4408,480); south Westbrook around (-4600,-250) had a fire, a
burned-down house and accidents with the nearest engine 1.3 km away by road. A FireHouse01 (4 engines) on a
clean roadside lot near (-4600,-300), inside the Westbrook bbox.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_westbrook_fire.py [--build]
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import paused, place_smart, short  # noqa: E402

BUILD = "--build" in sys.argv
with paused("troubleshooter"):
    r, c = place_smart("FireHouse01", -4600, -300, radius=500, dry=not BUILD, within=[-5290, -920, -4310, 2170])
print(json.dumps(short(r) if r else c)[:600], json.dumps({k: (c or {}).get(k) for k in ("x", "z", "road")}))
