"""Council round 1 - Education - south-campus.

Downtown-South School Campus: link road + ElementarySchool01 + HighSchool01 on the empty office-park
block (x -2121..-1963, z -92..268) and 1-2 infill ElementarySchool02 on the downtown strip
(x -1864..-1704, z 374..421).

Usage: python -I council/r1_education_south_campus.py dry|build|verify
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, building, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"

LINK = [[-2129, -100], [-2129, 60], [-2129, 200]]
ES01 = dict(edge="302816:1", t=0.789, side="right")
HS01 = dict(x=-2056.5, z=76, angle=-90)
ES02A = dict(edge="303215:1", t=0.842, side="left")
ES02B = dict(x=-1825.5, z=397.5, angle=0)
HS_BOX = (-2122, -1963, -24, 268)  # corners must stay inside (x0, x1, z0, z1)

spent = 0


def show(label, r):
    print(f"{label:<34} {json.dumps(short(r))}", flush=True)
    return r


def dry_all():
    show("road link (dry)", road("Small Road", LINK, dryRun=True))
    show("ES01 (dry)", building("ElementarySchool01", **ES01, dryRun=True))
    show("HS01 (dry)", building("HighSchool01", **HS01, dryRun=True))
    show("ES02 A (dry)", building("ElementarySchool02", **ES02A, dryRun=True))
    show("ES02 B (dry)", building("ElementarySchool02", **ES02B, dryRun=True))


def corners_ok(c, box):
    xs = [p[0] for p in c]
    zs = [p[1] for p in c]
    return box[0] <= min(xs) and max(xs) <= box[1] and box[2] <= min(zs) and max(zs) <= box[3]


def build_step(label, fn, *a, **kw):
    """dry run, then real; returns the real result or None."""
    global spent
    d = show(label + " (dry)", fn(*a, **kw, dryRun=True))
    if d.get("errors") not in (0, None) or d.get("error"):
        return None
    r = show(label, fn(*a, **kw))
    if r.get("applied"):
        spent += r.get("cost") or 0
        return r
    return None


def build_all():
    out = {}
    with paused("education"):
        out["road"] = build_step("road link", road, "Small Road", LINK)
        out["es01"] = build_step("ES01 office-park", building, "ElementarySchool01", **ES01)
        out["hs01"] = build_step("HS01 office-park", building, "HighSchool01", **HS01)
    if out["hs01"] is None:
        cands = get("/findspot", prefab="HighSchool01", x=-2056, z=76, radius=80, n=8).get("candidates") or []
        for c in cands:
            b = dict(c["building"])
            corners = c.get("corners") or c.get("lot") or []
            print("HS01 candidate", json.dumps({k: c.get(k) for k in ("rank", "score", "corners")})[:300])
            if corners and not corners_ok(corners, HS_BOX):
                continue
            b.pop("prefab", None)
            with paused("education"):
                out["hs01"] = build_step("HS01 findspot", building, "HighSchool01", **b)
            if out["hs01"]:
                break
    with paused("education"):
        out["es02a"] = build_step("ES02 A downtown", building, "ElementarySchool02", **ES02A)
        out["es02b"] = build_step("ES02 B downtown", building, "ElementarySchool02", **ES02B)
    print("SPENT", spent)
    json.dump({k: v for k, v in out.items()}, open(r"C:\soft\cityskyline\logs\council\r1_education_south_campus.json", "w"), indent=1)
    return out


if __name__ == "__main__":
    if MODE == "dry":
        with paused("education"):
            dry_all()
    elif MODE == "build":
        build_all()


LINK_EDGES = ["1551976:3", "1551977:3"]


def trees():
    with paused("education"):
        d = show("trees link (dry)", post("/roadupgrade", {"edges": LINK_EDGES, "upgrade": "Trees", "side": "both", "dryRun": True}))
        if d.get("errors") in (0, None) and not d.get("error"):
            show("trees link", post("/roadupgrade", {"edges": LINK_EDGES, "upgrade": "Trees", "side": "both"}))


if __name__ == "__main__" and MODE == "trees":
    trees()
