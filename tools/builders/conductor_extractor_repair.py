"""Repair extractor hubs that were placed without their attached extractor building (so no company could
ever rent them and they extracted 0). For each hub: remember its work-area polygon, bulldoze the hub, place
the same placeholder again (the API now attaches the extractor building like the vanilla tool), then redraw
the work area on the new hub.

usage: python -I builders/conductor_extractor_repair.py <hub id> [<hub id> ...] [--all] [--dry]
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from cc import get, paused, post  # noqa: E402


def hubs():
    return [a for a in get("/areas", kind="extractor").get("areas") or [] if a.get("ownerProblem")]


def repair(a, dry=False):
    hub, prefab = a["owner"], a["ownerPrefab"]
    x, z = a["ownerPos"]
    angle = a.get("ownerAngle") or 0
    poly = a["polygon"]
    out = {"hub": hub, "prefab": prefab}
    with paused("conductor extractor repair"):
        if dry:
            out["note"] = "dry: would bulldoze and rebuild"
            return out
        b = post("/bulldoze", {"ids": [hub]})
        out["bulldoze"] = {k: b.get(k) for k in ("applied", "removed", "error")}
        d = post("/building", {"prefab": prefab, "x": x, "z": z, "angle": angle, "dryRun": True})
        out["dry"] = {k: d.get(k) for k in ("errors", "problems", "attachment", "attachmentWarning")}
        if d.get("errors") != 0:
            return out
        r = post("/building", {"prefab": prefab, "x": x, "z": z, "angle": angle})
        out["build"] = {k: r.get(k) for k in ("applied", "placedId", "extractorBuilding", "errors")}
        new = r.get("placedId")
        if not new:
            return out
        ad = post("/area", {"owner": new, "points": poly, "dryRun": True})
        out["areaDry"] = {k: ad.get(k) for k in ("errors", "problems", "resourceAmount", "error")}
        if ad.get("errors") == 0:
            ar = post("/area", {"owner": new, "points": poly})
            out["area"] = {k: ar.get(k) for k in ("applied", "resourceAmount", "errors", "error")}
    return out


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    todo = hubs()
    if "--all" not in sys.argv:
        todo = [a for a in todo if a["owner"] in args]
    for a in todo:
        print(json.dumps(repair(a, dry="--dry" in sys.argv), default=str)[:900], flush=True)


if __name__ == "__main__":
    main()
