"""builder-northeast - b1-education-edu-knowledge-line-tram: Knowledge Line (tram K).

Branches from T1's north turning loop in Northfield (z=2820 / x=-1689 / z=2930 block, track by builder-central)
north along the x=-1909 avenue to University Hill, where it runs round the campus block
x=-1909 -> z=4250 -> x=-1469 -> z=3700 (University, City Hall, Police HQ, Hospital, Server Farm, offices).
Both ends are loops, so trams never need to reverse. Trams come from the shared TramDepot01 1421663:33 via T1.
Track = "Double Tram Track" road upgrade (keeps Trees/Grass). Stops are NA_TramStop01 ~10 m off the centreline.

Usage: python -I builders/builder-northeast_knowledge_line.py track-dry|track|line-dry|line|trees
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
WHO = "builder-northeast"
MODE = sys.argv[1] if len(sys.argv) > 1 else "track-dry"
LOG = r"C:\soft\cityskyline\logs\council\builder-northeast_knowledge_line.json"
TRACKS = [
    ("trunk x=-1909", [[-1909, 2930], [-1909, 3700]]),
    ("loop x=-1909", [[-1909, 3700], [-1909, 4250]]),
    ("loop z=4250", [[-1909, 4250], [-1469, 4250]]),
    ("loop x=-1469", [[-1469, 4250], [-1469, 3700]]),
    ("loop z=3700", [[-1469, 3700], [-1909, 3700]]),
]
# stops: northbound on the east side of x=-1909, clockwise round the campus loop (north on x=-1909, east on
# z=4250, south on x=-1469, west on z=3700), southbound on the west side of x=-1909, and one stop on T1's
# block loop: the path from there back to the first stop runs round that loop, so trams turn there.
STOPS = [(-1899, 3120), (-1899, 3420),                          # Northfield north, University Hill gate
         (-1899, 3880), (-1899, 4130),                          # Research Park, Welfare / City Hall west
         (-1700, 4240), (-1540, 4240),                          # City Hall, University quad
         (-1459, 4030), (-1459, 3810),                          # Police HQ / park, Hospital
         (-1600, 3690), (-1830, 3690),                          # Server Farm, Software Park offices
         (-1919, 3420), (-1919, 3120),                          # back south
         (-1699, 2875)]                                         # on T1's block loop (x=-1689): turn + transfer


def log(key, val):
    try:
        d = json.load(open(LOG))
    except Exception:
        d = {}
    d[key] = val
    json.dump(d, open(LOG, "w"), indent=1)


if __name__ == "__main__":
    if MODE in ("track-dry", "track"):
        dry = MODE == "track-dry"
        res = {}
        with paused(WHO):
            for label, line in TRACKS:
                for pf in ("Small Road", "Medium Road"):
                    r = post("/roadupgrade", {"polyline": line, "tolerance": 6, "prefabFilter": pf,
                                              "upgrade": "Double Tram Track", "side": "both", "dryRun": dry})
                    n = len(r.get("edges") or [])
                    if n or r.get("error"):
                        print(f"{label:<16} {pf:<12} edges={n} cost={r.get('cost')} err={r.get('errors')} "
                              f"{r.get('error') or ''} demolish={r.get('wouldDemolish')}", flush=True)
                    res[f"{label} {pf}"] = {k: r.get(k) for k in ("applied", "cost", "errors")}
        if not dry:
            log("track", res)
    elif MODE in ("line-dry", "line"):
        body = {"type": "tram", "stopPrefab": "NA_TramStop01", "stops": [{"x": x, "z": z} for x, z in STOPS],
                "color": "#3949AB"}
        if MODE == "line-dry":
            r = post("/busline", {**body, "dryRun": True}, timeout=300)
        else:
            with paused(WHO):
                r = post("/busline", body, timeout=300)
            log("line", r)
        print(json.dumps({k: r.get(k) for k in ("lineId", "error", "warnings")}),
              [(s.get("index"), s.get("side"), s.get("edge"), s.get("reused"), s.get("error")) for s in r.get("stops") or []])
