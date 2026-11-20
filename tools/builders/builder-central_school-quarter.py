"""builder-central: Downtown school quarter (worklist b1-education-edu-downtown-school-quarter).

Two compact ElementarySchool03 and one HighSchool03 on the vacant strip north of the z=1230 street
(x -2120..-1368, z 1238..1290; zoned low-density but never grown), each with a schoolyard park, then
street trees on the school frontages. No demolition: every placement is dry-run first and skipped when
the game reports wouldDemolish or errors.

Run: cd C:/soft/cityskyline/tools && python -I builders/builder-central_school-quarter.py [--dry]
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused, short  # noqa: E402

WHO = "builder-central"
DRY = "--dry" in sys.argv

# (label, prefab, x, z, angle) - positions from /findspot (front on the named street)
PLACEMENTS = [
    ("HS03 central", "HighSchool03", -1580.93, 1262.5, -180),           # faces the z=1230 street
    ("HS03 schoolyard park", "CityPark06", -1497.5, 1265.36, 90),       # between HS03 and the x=-1469 street
    ("ES03 east", "ElementarySchool03", -1428.5, 1265.36, -90),         # faces the x=-1469 street
    ("ES03 east playground", "Playground03", -1380.23, 1250.5, -180),   # faces the z=1230 street
    ("ES03 west", "ElementarySchool03", -2088.5, 1265.36, -90),         # faces the x=-2134 street
    ("ES03 west playground", "Playground04", -2040.23, 1254.5, -180),   # faces the z=1230 street
]

# school frontages that get street trees on both sides (polyline selection; edge ids change on upgrade)
TREE_STREETS = [
    ("z=1230 street east", [[-1689, 1230], [-1322, 1230]]),
    ("x=-1469 street", [[-1469, 1175], [-1469, 1340]]),
    ("z=1230 street west", [[-2134, 1230], [-1950, 1230]]),
    ("x=-2134 street", [[-2134, 1230], [-2134, 1340]]),
]


def main():
    placed = []
    total = 0
    with paused(WHO):
        for label, prefab, x, z, angle in PLACEMENTS:
            body = {"prefab": prefab, "x": x, "z": z, "angle": angle}
            d = post("/building", {**body, "dryRun": True})
            if d.get("errors") or d.get("wouldDemolish"):
                print(f"{label:<24} SKIP {short(d)}", flush=True)
                continue
            if DRY:
                print(f"{label:<24} dry ok cost={d.get('cost')}", flush=True)
                continue
            r = post("/building", body)
            print(f"{label:<24} {short(r)}", flush=True)
            if r.get("applied"):
                placed.append({"label": label, "prefab": prefab, "id": r.get("placedId"), "x": x, "z": z})
                total += r.get("cost") or 0
        for label, line in TREE_STREETS:
            body = {"polyline": line, "tolerance": 6, "prefabFilter": "Small Road", "upgrade": "Trees", "side": "both"}
            r = post("/roadupgrade", {**body, "dryRun": DRY})
            print(f"trees {label:<20} {short(r, keys=('applied', 'cost', 'edges', 'upgraded', 'errors', 'skipped', 'error'))}", flush=True)
            if not DRY and r.get("applied"):
                total += r.get("cost") or 0
    print(json.dumps({"placed": placed, "cost": total}, indent=1))


if __name__ == "__main__":
    main()
