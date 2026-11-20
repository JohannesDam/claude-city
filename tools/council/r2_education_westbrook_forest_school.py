"""Council round 2 - Education - westbrook-forest-school.

HighSchool01 + Extension Wing on the Westbrook west boundary road (woodland edge), facing the road (west).
Frontage road edges already carry Trees on both sides (checked before building).

Usage: python -I council/r2_education_westbrook_forest_school.py build|verify
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, building, find_spot, short, access_check  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "build"
LOG = r"C:\soft\cityskyline\logs\council\r2_education_westbrook_forest_school.json"
SITE = (-5275, -5138, 325, 510)  # approved site box (x0, x1, z0, z1)
OPTIONS = [
    {"edge": "1114782:135", "t": 0.464, "side": "right", "gap": 0.5},
    {"edge": "1113359:65", "t": 0.8, "side": "right", "gap": 0.5},
]


def load():
    try:
        return json.load(open(LOG))
    except Exception:
        return {}


def save(st):
    json.dump(st, open(LOG, "w"), indent=1)


def show(label, r):
    print(f"{label:<30} {json.dumps(short(r))[:700]}", flush=True)
    return r


def inside(c, box, tol=6):
    xs = [p[0] for p in c]
    zs = [p[1] for p in c]
    return box[0] - tol <= min(xs) and max(xs) <= box[1] + tol and box[2] - tol <= min(zs) and max(zs) <= box[3] + tol


def build():
    st = load()
    if not st.get("hs01"):
        opts = list(OPTIONS)
        # current /findspot candidates inside the approved site box as a fallback (ids may have changed)
        for c in find_spot("HighSchool01", -5206.5, 418.4, radius=200, n=8):
            if inside(c["corners"], SITE):
                b = dict(c["building"])
                b.pop("prefab", None)
                opts.append(b)
        with paused("education"):
            for b in opts:
                d = post("/building", {"prefab": "HighSchool01", **b, "dryRun": True})
                print(f"  {b}: errors={d.get('errors')} problems={d.get('problems')} pos={d.get('pos')} "
                      f"wouldDemolish={[w['id'] for w in d.get('wouldDemolish') or []]}", flush=True)
                p = d.get("pos") or [0, 0]
                if d.get("errors") != 0 or d.get("wouldDemolish") or not (SITE[0] <= p[0] <= SITE[1] and SITE[2] <= p[1] <= SITE[3]):
                    continue
                r = show("HS01 BUILD", building("HighSchool01", **b))
                if r.get("applied"):
                    st["hs01"] = {"spot": b, "placedId": r.get("placedId"), "cost": r.get("cost"),
                                  "roadConnected": r.get("roadConnected"), "pos": p}
                    st["spent"] = st.get("spent", 0) + (r.get("cost") or 0)
                break
        save(st)
        if not st.get("hs01"):
            print("no clean HS01 spot - stop", flush=True)
            return
    hid = st["hs01"]["placedId"]
    if not st.get("wing"):
        with paused("education"):
            d = show("Extension Wing (dry)", post("/extension", {"owner": hid, "prefab": "HighSchool01 Extension Wing", "dryRun": True}, timeout=120))
            if d.get("errors") == 0 and not d.get("error"):
                r = post("/extension", {"owner": hid, "prefab": "HighSchool01 Extension Wing"}, timeout=120)
                show("Extension Wing", r)
                if r.get("applied"):
                    st["wing"] = {"id": r.get("upgradeId"), "cost": r.get("cost")}
                    st["spent"] = st.get("spent", 0) + (r.get("cost") or 0)
        save(st)
    # frontage trees: add only where missing
    todo = []
    for s_ in get("/nets", x=-5279, z=420, radius=120, filter="Small Road").get("segments") or []:
        if -5290 <= s_["start"][0] <= -5270 and -5290 <= s_["end"][0] <= -5270 and "Trees" not in (s_.get("upgrades") or {}):
            todo.append(s_["id"])
    print("frontage edges missing Trees:", todo, flush=True)
    if todo:
        with paused("education"):
            d = show("Trees (dry)", post("/roadupgrade", {"edges": todo, "upgrade": "Trees", "side": "both", "dryRun": True}))
            if d.get("errors") in (0, None) and not d.get("wouldDemolish"):
                u = show("Trees", post("/roadupgrade", {"edges": todo, "upgrade": "Trees", "side": "both"}))
                st["spent"] = st.get("spent", 0) + (u.get("cost") or 0)
        save(st)
    print("SPENT", st.get("spent"), flush=True)


def verify():
    st = load()
    print(json.dumps(st, indent=1))
    hid = (st.get("hs01") or {}).get("placedId")
    for lv in get("/education").get("levels", []):
        if lv["level"] == "highschool":
            print("highschool:", {k: lv[k] for k in ("schools", "students", "seats", "eligible", "shortfall")})
    if hid:
        print(json.dumps(get("/extensions", owner=hid).get("installed")))
        for b in get("/buildings", kind="service", x=st["hs01"]["pos"][0], z=st["hs01"]["pos"][1], radius=30).get("buildings", []):
            print(b)
        print("access", access_check([hid]))


if __name__ == "__main__":
    {"build": build, "verify": verify}[MODE]()
