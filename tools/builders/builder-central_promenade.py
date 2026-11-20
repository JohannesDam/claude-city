"""builder-central: Pearl Promenade, first two sections (worklist b1-parks-waterfront-promenade).

The shore strip between the Waterfront streets and the lake is only free in two stretches:
  A (z 120..425): open land x -660..-550 between the office quarter and the water. Curved Pedestrian Street
    from the z=120 street end (-610,120), joining the z=200 street end, along the water to a quiet end at
    (-705,425). The approach to the z=430 street would demolish a home, so it stays a dead end.
  B (z 1045..1450): the 40-70 m strip between the x=-720 shore road and the water. A curved Pedestrian Street
    loop from the shore road at z=1045 along the water and back to the road at z=1450, crossing the pump
    stub at z=1175 (pump lots untouched).
z 450..900: homes and the shallow pump reach the water, so walkers use the x=-720 / x=-809 streets there.
Pedestrian paths cannot be joined to roads through the API (SameNetKind snapping), so the promenade is
built as Pedestrian Street (a road), which connects.

Run: cd C:/soft/cityskyline/tools && python -I builders/builder-central_promenade.py streets|upgrades|nodes [--dry] [--only=A,B,C]
(Pedestrian Street takes no Trees/Lighting/Wide Sidewalk upgrade; the upgrades stage only reports that.)
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short, find_spot, building  # noqa: E402

WHO = "builder-central"
DRY = "--dry" in sys.argv
STAGE = next((a for a in sys.argv[1:] if not a.startswith("--")), "streets")
ONLY = [a.split("=", 1)[1] for a in sys.argv[1:] if a.startswith("--only=")][0].split(",") if any(a.startswith("--only=") for a in sys.argv[1:]) else []
PF = "Pedestrian Street"

SECTIONS = {
    "A": [[-610, 120], [-590, 165], [-555, 215], [-555, 275], [-570, 330], [-625, 378], [-662, 420], [-705, 425]],
    "B": [[-720, 1045], [-692, 1085], [-680, 1125], [-688, 1175], [-676, 1228], [-660, 1285], [-645, 1340],
          [-628, 1398], [-622, 1432], [-650, 1455], [-690, 1455], [-720, 1450]],
    # C (Lakeshore, z 1455..1745): free shore land x -650..-500; branches off B at (-650,1455), quiet end
    "C": [[-650, 1455], [-632, 1500], [-608, 1550], [-585, 1610], [-560, 1665], [-528, 1715], [-505, 1745]],
}

# promenade nodes (park, cafe, playground every ~250-300 m): (label, prefab, near x, near z)
NODES = [
    ("A cafe", "ParkCafe01", -585, 250),
    ("A playground", "Playground05", -600, 360),
    ("A pocket end", "PocketPark05", -690, 410),
    ("B cafe", "ParkCafe01", -690, 1290),
    ("B playground", "Playground05", -700, 1100),
    ("B bikes", "BicyclePark01", -650, 1400),
    ("C pocket", "PocketPark08", -600, 1570),
    ("C green end", "CityPark06", -525, 1715),
]


def stage_streets():
    with paused(WHO):
        for k, pts in SECTIONS.items():
            if ONLY and k not in ONLY:
                continue
            body = {"prefab": PF, "points": pts, "curve": "smooth"}
            d = post("/road", {**body, "dryRun": True})
            if d.get("errors") or d.get("wouldDemolish"):
                print(k, "STOP", short(d))
                continue
            if DRY:
                print(k, "dry ok", d.get("cost"), (d.get("geometry") or {}).get("length"))
                continue
            r = post("/road", body)
            print(k, short(r, keys=("applied", "cost", "errors", "newEdgeIds")), flush=True)


def stage_upgrades():
    with paused(WHO):
        for k, pts in SECTIONS.items():
            for up in ("Trees", "Lighting", "Wide Sidewalk"):
                r = post("/roadupgrade", {"polyline": pts, "tolerance": 12, "prefabFilter": PF, "upgrade": up, "side": "both",
                                          "dryRun": DRY})
                print(k, up, short(r, keys=("applied", "cost", "errors", "problems", "error")),
                      sum(1 for e in r.get("edges", []) if e.get("changes")), "edges", flush=True)


def stage_nodes():
    placed = []
    with paused(WHO):
        for label, prefab, x, z in NODES:
            if ONLY and label.split()[0] not in ONLY:
                continue
            ok = False
            for c in find_spot(prefab, x, z, radius=90, n=8):
                b = dict(c["buildingXZ"])
                b.pop("prefab", None)
                d = post("/building", {"prefab": prefab, **b, "dryRun": True})
                if d.get("errors") or d.get("wouldDemolish"):
                    continue
                if DRY:
                    print(label, "dry ok", c["x"], c["z"], c["road"])
                    ok = True
                    break
                r = building(prefab, **b)
                print(label, prefab, short(r), flush=True)
                if r.get("applied"):
                    placed.append({"label": label, "prefab": prefab, "id": r.get("placedId"), "x": c["x"], "z": c["z"]})
                ok = True
                break
            if not ok:
                print(label, "no spot")
    print(json.dumps(placed))


if __name__ == "__main__":
    {"streets": stage_streets, "upgrades": stage_upgrades, "nodes": stage_nodes}[STAGE]()
