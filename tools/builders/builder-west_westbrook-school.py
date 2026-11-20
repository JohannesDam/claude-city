"""builder-west, item b1-education-edu-westbrook-south-school.

South Westbrook has no school (only ES01 1335464:57 in the north, full). The free strip between the two
tree-lined lanes x=-4600 and x=-4380 is landlocked south of the z=-320 cross street; the village square
(SunkenPlaza01 405889:231) closes it at z~-565.

Plan:
  1. "School Lane": a gently curved Small Road cul-de-sac from the z=-320 street (node x=-4485.3) south to
     z~-535, Trees both sides.
  2. ElementarySchool03 (1,500 seats, 48x64 m) on the corner west of the lane, facing the z=-320 street.
  3. Playground02 as schoolyard on the other corner, east of the lane.
  4. NA Residential Medium Row along the rest of the lane (garden-suburb row houses).
Usage: python -I builders/builder-west_westbrook-school.py [dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, find_spot, get, paused, post, short, zone  # noqa: E402

WHO = "builder-west"
DRY = "dry" in sys.argv[1:]
LANE = {"prefab": "Small Road",
        "segments": [{"start": [-4485.3, -320], "control": [-4500, -430], "end": [-4492, -535]}]}


def pick(prefab, x, z, box, radius=120, n=30):
    """First findspot candidate whose centre lies in box (x0, x1, z0, z1)."""
    for c in find_spot(prefab, x, z, radius=radius, n=n):
        if box[0] <= c["x"] <= box[1] and box[2] <= c["z"] <= box[3]:
            return c
    return None


def place(prefab, c):
    b = dict(c["building"])
    b.pop("prefab", None)
    d = post("/building", {"prefab": prefab, **b, "dryRun": True})
    if d.get("errors") or d.get("wouldDemolish"):
        print(f"{prefab}: dry failed {short(d)}", flush=True)
        return None
    if DRY:
        print(f"{prefab}: dry ok at ({c['x']}, {c['z']})", flush=True)
        return None
    r = building(prefab, **b)
    print(f"{prefab}: {short(r)} at ({c['x']}, {c['z']})", flush=True)
    return r


def main():
    d = post("/road", {**LANE, "dryRun": True})
    print("lane dry:", short(d), (d.get("geometry") or {}).get("length"), flush=True)
    if d.get("errors") or d.get("wouldDemolish") or DRY:
        return
    out = {}
    with paused(WHO):
        r = post("/road", LANE)
        print("lane:", short(r), flush=True)
        edges = r.get("newEdgeIds") or r.get("builtEdges") or []
        out["lane"] = edges
        t = post("/roadupgrade", {"polyline": [[-4485.3, -322], [-4497, -430], [-4492, -533]], "tolerance": 6,
                                  "prefabFilter": "Small Road", "upgrade": "Trees", "side": "both"})
        print("trees:", short(t), flush=True)

    # re-read the lane edge ids (the tree upgrade renews them)
    lane_edges = [s["id"] for s in get("/nets", x=-4492, z=-430, radius=110, filter="Small Road").get("segments") or []
                  if min(s["start"][1], s["end"][1]) < -330 and -4515 < (s["start"][0] + s["end"][0]) / 2 < -4470]
    print("lane edges now:", lane_edges, flush=True)

    with paused(WHO):
        c = pick("ElementarySchool03", -4520, -365, (-4552, -4500, -400, -340))
        out["school"] = place("ElementarySchool03", c) if c else print("no ES03 spot west of the lane")
        c = pick("Playground02", -4455, -350, (-4478, -4430, -400, -330))
        out["yard"] = place("Playground02", c) if c else print("no Playground02 spot east of the lane")
        if lane_edges:
            z = zone("NA Residential Medium Row", mode="road", edges=lane_edges, depth=4, side="both")
            print("zone:", short(z), flush=True)
    print(json.dumps({k: (v.get("placedId") if isinstance(v, dict) else v) for k, v in out.items()}))


if __name__ == "__main__":
    main()
