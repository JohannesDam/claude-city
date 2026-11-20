"""Troubleshooter: link the SW Quarter to Old Southern Works.

The SW Quarter (5,800+ households) is reachable only from the north: its east edge road (x=-2900) dead-ends at
z=-1010 and the OSW street z=-1110 dead-ends at x=-3000. Services in OSW (crematorium 190045:615 with 10 idle
hearses, others) need a ~3 km detour to reach the SW Quarter, while all hearse hotspots are there.
This joins both dead ends with a short Small Road through empty owned land west of the industrial lots
(no demolition), keeping clear of the industrial lots (x >= -2996) and the police/fire lots.

Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_swq_link.py [--build]
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import paused, post  # noqa: E402

BUILD = "--build" in sys.argv

SEGMENTS = [
    {"start": [-2900, -1010], "control": [-2900, -1030], "end": [-2920, -1030]},
    {"start": [-2920, -1030], "end": [-2994, -1030]},
    {"start": [-2994, -1030], "control": [-3020, -1030], "end": [-3020, -1056]},
    {"start": [-3020, -1056], "end": [-3020, -1086]},
    {"start": [-3020, -1086], "control": [-3020, -1110], "end": [-3000, -1110]},
]


def show(tag, r):
    keep = ("applied", "cost", "errors", "problems", "wouldDemolish", "conflicts", "error", "builtEdges",
            "newEdgeIds", "geometry", "snapping")
    print(tag, json.dumps({k: r.get(k) for k in keep if r.get(k) is not None})[:2500])


with paused("troubleshooter"):
    r = post("/road", {"prefab": "Small Road", "segments": SEGMENTS, "dryRun": True})
    show("dry", r)
    if BUILD and r.get("errors", 1) == 0 and not r.get("wouldDemolish") and not r.get("problems"):
        r = post("/road", {"prefab": "Small Road", "segments": SEGMENTS})
        show("build", r)
