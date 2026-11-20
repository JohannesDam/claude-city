"""Council round 2 - Education - west-campus.

Curved Small Road lane through the empty HV-line superblock (x -2948..-2684, z 1502..1910), trees + grass,
HighSchool01 west of the lane facing east + Extension Wing + Library (+ optional Sports Field).
Land east of the lane up to the HV line stays free for Parks' campus green.

Usage: python -I council/r2_education_west_campus.py road|school|verify
"""
import json
import os
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, building, find_spot, short  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
MODE = sys.argv[1] if len(sys.argv) > 1 else "dry"
LOG = r"C:\soft\cityskyline\logs\council\r2_education_west_campus.json"

LANE = [[-3009, 1830], [-2930, 1832], [-2850, 1800], [-2805, 1720], [-2790, 1620], [-2789, 1450]]
ALT_WEST = [[-3009, 1720], [-3009, 1940]]
APPROVED_DEMO = {"876542:1", "872030:5", "618769:1"}
BLOCK = (-2948, -2650, 1500, 1910)  # school lot corners must stay inside (x0, x1, z0, z1)
HV = [(-2690, 1950), (-2614, 1833), (-2539, 1716)]


def load():
    try:
        return json.load(open(LOG))
    except Exception:
        return {}


def save(st):
    json.dump(st, open(LOG, "w"), indent=1)


def show(label, r):
    print(f"{label:<30} {json.dumps(short(r))[:900]}", flush=True)
    return r


def do_road():
    st = load()
    pts = LANE
    with paused("education"):
        d = post("/road", {"prefab": "Small Road", "points": pts, "curve": "smooth", "dryRun": True})
        wd = {w["id"] for w in d.get("wouldDemolish") or []}
        show("lane (dry)", d)
        if d.get("errors") != 0 or not wd <= APPROVED_DEMO:
            print("lane dry run not clean or demolishes unapproved buildings:", wd - APPROVED_DEMO, flush=True)
            return
        r = post("/road", {"prefab": "Small Road", "points": pts, "curve": "smooth", "allowDemolish": True})
        show("lane BUILD", r)
        if not r.get("applied"):
            return
        st["lane"] = {k: r.get(k) for k in ("cost", "newEdgeIds", "builtEdges", "staleEdgeIds", "wouldDemolish", "demolished")}
        st["spent"] = st.get("spent", 0) + (r.get("cost") or 0)
        save(st)
    edges = r.get("newEdgeIds") or r.get("builtEdges") or []
    print("new edges", edges, flush=True)
    with paused("education"):
        for up in ("Trees", "Grass"):
            d = post("/roadupgrade", {"edges": edges, "upgrade": up, "side": "both", "dryRun": True})
            show(f"{up} (dry)", d)
            if d.get("errors") in (0, None) and not d.get("error") and not d.get("wouldDemolish"):
                u = show(up, post("/roadupgrade", {"edges": edges, "upgrade": up, "side": "both"}))
                st["spent"] = st.get("spent", 0) + (u.get("cost") or 0)
                st.setdefault("roadupgrades", []).append({"upgrade": up, "cost": u.get("cost"), "applied": u.get("applied")})
    save(st)
    print("SPENT so far", st.get("spent"), flush=True)


def corners_of(c):
    return c.get("corners") or (c.get("building") or {}).get("corners") or []


def inside(corners, box):
    if not corners:
        return False
    xs = [p[0] for p in corners]
    zs = [p[1] for p in corners]
    return box[0] <= min(xs) and max(xs) <= box[1] and box[2] <= min(zs) and max(zs) <= box[3]


def hv_dist(corners):
    import math

    def seg_d(px, pz, a, b):
        ax, az = a
        bx, bz = b
        dx, dz = bx - ax, bz - az
        t = max(0, min(1, ((px - ax) * dx + (pz - az) * dz) / (dx * dx + dz * dz)))
        return math.hypot(px - ax - t * dx, pz - az - t * dz)
    return min(seg_d(p[0], p[1], HV[i], HV[i + 1]) for p in corners for i in range(len(HV) - 1))


def lane_south_edge():
    """Current id of the straight lane segment (-2790,1620) -> (-2789,1450) (ids change after builds)."""
    for s_ in get("/nets", x=-2789.5, z=1535, radius=90, filter="Small Road").get("segments") or []:
        ends = {tuple(round(v) for v in s_["start"]), tuple(round(v) for v in s_["end"])}
        if ends == {(-2790, 1620), (-2789, 1450)}:
            return s_["id"]
    return None


def west_side():
    """Intended lot: west of the lane facing east onto it (right side of the southbound straight segment).
    /findspot only tries gap 0.5, where the 176 m lot clips the curve; a 3-4 m setback validates."""
    e = lane_south_edge()
    print("lane south edge", e, flush=True)
    if not e:
        return None
    for t, gap in ((0.1, 3.5), (0.1, 4.0), (0.05, 3.5), (0.0, 4.0)):
        b = {"edge": e, "t": t, "side": "right", "gap": gap}
        d = post("/building", {"prefab": "HighSchool01", **b, "dryRun": True})
        print(f"  west t={t} gap={gap}: errors={d.get('errors')} problems={d.get('problems')} pos={d.get('pos')} "
              f"wouldDemolish={[w['id'] for w in d.get('wouldDemolish') or []]}", flush=True)
        p = d.get("pos") or [0, 0]
        if d.get("errors") == 0 and not d.get("wouldDemolish") and -2948 + 64 <= p[0] <= -2650 - 64 and 1500 + 88 <= p[1] <= 1910 - 88:
            r = building("HighSchool01", **b)
            show("HS01 west BUILD", r)
            if r.get("applied"):
                return {"cand": {**b, "pos": p, "mode": "west-side setback"},
                        "result": {k: r.get(k) for k in ("placedId", "cost", "roadConnected", "corners", "pos")}}
            return None
    return None


def do_school():
    st = load()
    if not st.get("hs01"):
        with paused("education"):
            chosen = west_side()
        if chosen:
            st["spent"] = st.get("spent", 0) + (chosen["result"].get("cost") or 0)
        cands = [] if chosen else find_spot("HighSchool01", -2880, 1640, radius=220, n=12)
        print(len(cands), "fallback candidates", flush=True)
        with paused("education"):
            for c in cands:
                cor = corners_of(c)
                b = dict(c["building"])
                b.pop("prefab", None)
                ok_box = inside(cor, BLOCK)
                hvd = hv_dist(cor) if cor else None
                print(f"  cand score={c.get('score')} b={json.dumps(b)[:200]} box={ok_box} hv={hvd and round(hvd)}", flush=True)
                if not ok_box or (hvd is not None and hvd < 40):
                    continue
                d = post("/building", {"prefab": "HighSchool01", **b, "dryRun": True})
                show("  HS01 (dry)", d)
                if d.get("errors") != 0 or d.get("wouldDemolish"):
                    continue
                r = building("HighSchool01", **b)
                show("HS01 BUILD", r)
                if r.get("applied"):
                    chosen = {"cand": c, "result": {k: r.get(k) for k in ("placedId", "cost", "roadConnected", "corners")}}
                    st["spent"] = st.get("spent", 0) + (r.get("cost") or 0)
                break
        if not chosen:
            print("no HS01 spot - stop", flush=True)
            save(st)
            return
        st["hs01"] = chosen
        save(st)
    hid = st["hs01"]["result"]["placedId"]
    print("HS01 id", hid, flush=True)
    with paused("education"):
        for name in ("HighSchool01 Extension Wing", "HighSchool01 Library"):
            if st.get("ext", {}).get(name):
                continue
            d = post("/extension", {"owner": hid, "prefab": name, "dryRun": True}, timeout=120)
            show(f"{name} (dry)", d)
            if d.get("errors") == 0 and not d.get("error"):
                r = post("/extension", {"owner": hid, "prefab": name}, timeout=120)
                show(name, r)
                if r.get("applied"):
                    st.setdefault("ext", {})[name] = {"id": r.get("upgradeId"), "cost": r.get("cost")}
                    st["spent"] = st.get("spent", 0) + (r.get("cost") or 0)
    save(st)
    # optional sports field: only if the slot is clean AND no zoned building lies under its 26x20 lot
    if not st.get("ext", {}).get("HighSchool01 Sports Field"):
        with paused("education"):
            for side in ("auto", "back", "left", "right"):
                d = post("/extension", {"owner": hid, "prefab": "HighSchool01 Sports Field", "side": side, "dryRun": True}, timeout=120)
                show(f"Sports Field {side} (dry)", d)
                if d.get("errors") != 0 or d.get("error"):
                    continue
                pos = d.get("pos")
                fac = d.get("facing") or [0, 1]
                import math
                ang = round(math.degrees(math.atan2(fac[0], fac[1])))
                p = post("/building", {"prefab": "HighSchool01 Sports Field", "x": pos[0], "z": pos[1], "angle": ang, "dryRun": True})
                print(f"    probe angle={ang} wouldDemolish={[(w['id'], w['prefab']) for w in p.get('wouldDemolish') or []]} err={p.get('error')}", flush=True)
                if p.get("wouldDemolish") or p.get("error"):
                    continue
                lot_ok = pos[0] <= -2650 and BLOCK[2] <= pos[1] <= BLOCK[3]
                if not lot_ok:
                    print("    sports field centre outside the block - skip", flush=True)
                    continue
                r = post("/extension", {"owner": hid, "prefab": "HighSchool01 Sports Field", "side": d.get("slot") or side}, timeout=120)
                show("Sports Field", r)
                if r.get("applied"):
                    st.setdefault("ext", {})["HighSchool01 Sports Field"] = {"id": r.get("upgradeId"), "cost": r.get("cost"), "pos": pos}
                    st["spent"] = st.get("spent", 0) + (r.get("cost") or 0)
                break
    save(st)
    print("SPENT", st.get("spent"), flush=True)


def lane_edges():
    pts = {tuple(p) for p in LANE}
    out = []
    for s_ in get("/nets", x=-2870, z=1640, radius=260, filter="Small Road").get("segments") or []:
        a = tuple(round(v) for v in s_["start"])
        b = tuple(round(v) for v in s_["end"])
        if a in pts and b in pts:
            out.append(s_)
    return out


def do_grass():
    """Trees + Grass on every lane segment (the Trees upgrade renewed some ids, so Grass missed two)."""
    st = load()
    for up in ("Trees", "Grass"):
        todo = [s_["id"] for s_ in lane_edges() if up not in (s_.get("upgrades") or {})]
        print(up, "missing on", todo, flush=True)
        if not todo:
            continue
        with paused("education"):
            d = post("/roadupgrade", {"edges": todo, "upgrade": up, "side": "both", "dryRun": True})
            show(f"{up} fix (dry)", d)
            if d.get("errors") in (0, None) and not d.get("error") and not d.get("wouldDemolish"):
                u = show(f"{up} fix", post("/roadupgrade", {"edges": todo, "upgrade": up, "side": "both"}))
                st["spent"] = st.get("spent", 0) + (u.get("cost") or 0)
    save(st)
    for s_ in lane_edges():
        print(" ", s_["id"], s_["start"], s_["end"], s_.get("upgrades"))


def verify():
    st = load()
    print(json.dumps(st, indent=1)[:3000])
    for lv in get("/education").get("levels", []):
        if lv["level"] == "highschool":
            print("highschool:", {k: lv[k] for k in ("schools", "students", "seats", "eligible", "shortfall")})
    hid = (st.get("hs01") or {}).get("result", {}).get("placedId")
    if hid:
        print(json.dumps(get("/extensions", owner=hid).get("installed"), indent=0))
        for s_ in get("/services", kind="education").get("buildings", []) or []:
            if s_.get("id") == hid:
                print(json.dumps(s_)[:800])


if __name__ == "__main__":
    {"road": do_road, "grass": do_grass, "school": do_school, "verify": verify}[MODE]()
