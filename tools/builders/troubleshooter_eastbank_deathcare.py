"""Troubleshooter: deathcare for East Bank (builder-south's district; conductor hand-off).

East Bank (6.2k residents, growing) had no deathcare: its dead were collected from Cemetery01 213867:19 and
Crematorium01 189256:661 across the lake, 2.7-3.1 km away. A Crematorium01 (own hearses, no storage limit) on a
clean roadside lot in the empty south-centre of the district, north of z -1450 and away from the east entry.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_eastbank_deathcare.py [--build]
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import paused, place_smart, short  # noqa: E402

BUILD = "--build" in sys.argv
with paused("troubleshooter"):
    r, c = place_smart("Crematorium01", 480, -1241, radius=250, dry=not BUILD, within=[-280, -1440, 840, -330])
print(json.dumps(short(r) if r else c)[:500], json.dumps({k: (c or {}).get(k) for k in ("x", "z", "angle", "road")}))
