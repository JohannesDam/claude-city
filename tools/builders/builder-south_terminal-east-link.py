"""builder-south: Terminal East Link (option O3c of the terminal access study).

Medium Road with Trees from the Gateway Business Park node (-1690,-1885) west to the CargoTrainTerminal01 610973:49
approach road at (-2343,-1730): a second truck entrance, level crossing on the terminal's cargo spur.
usage: python -I builders/builder-south_terminal-east-link.py dry|build
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short  # noqa: E402

PTS = [[-1690, -1885], [-1850, -1905], [-2150, -1870], [-2300, -1805], [-2343, -1730]]
BODY = {"prefab": "Medium Road", "points": PTS, "curve": "smooth"}
KEYS = ("applied", "cost", "errors", "problems", "error", "wouldDemolish", "conflicts", "newEdgeIds")

if (sys.argv[1] if len(sys.argv) > 1 else "dry") == "dry":
    print(json.dumps(short(post("/road", {**BODY, "dryRun": True}), keys=KEYS)))
else:
    with paused("builder-south"):
        d = post("/road", {**BODY, "dryRun": True})
        if d.get("errors") or d.get("wouldDemolish") or d.get("error"):
            print("dry run not clean", json.dumps(short(d, keys=KEYS)))
            sys.exit(1)
        r = post("/road", BODY)
        print("road", json.dumps(short(r, keys=KEYS)))
        ids = r.get("newEdgeIds") or []
        if ids:
            for k in range(0, len(ids), 4):
                u = post("/roadupgrade", {"edges": ids[k:k + 4], "upgrade": "Trees", "side": "both"})
                print("trees", u.get("applied"), u.get("cost"), u.get("error"), u.get("edgeIdsAfter"))
