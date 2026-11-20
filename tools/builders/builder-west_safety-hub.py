"""builder-west, item b1-health_safety-sw-quarter-safety-hub (interim part, no demolition).

The civic square (PoliceStation03, crematorium with hearse garage) needs the SWQ renewal phase, which is gated
on the Southgate decant. What can be done now without touching homes:
  - Ambulance Depot on clinic 1299060:3 (fixed upgrade, no new lot);
  - EmergencyShelter01 (8x16 m lot) in the free gaps findspot validates inside the quarter, one per
    uncovered cell (all at 0 shelter coverage today).
Westpark (x < -4030) is protected, so candidates west of the x=-4030 road are skipped.
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, find_spot, get, paused, post, short  # noqa: E402

WHO = "builder-west"

# target cell -> preferred findspot candidate (validated in the survey)
SHELTERS = [
    ("cell -3141,-590", (-3196.5, -606.79)),
    ("cell -3541,-590", (-3603.5, -606.79)),
    ("cell -3941,-690", (-4013.5, -716.79)),
    ("cell -3741,-490", (-3823.5, -496.79)),
    ("cell -3141,-390", (-3196.5, -496.79)),
]


def candidate_at(x, z):
    """Re-run findspot right before building (the city changes) and return the candidate at (x, z)."""
    for c in find_spot("EmergencyShelter01", x, z, radius=60, n=20):
        if abs(c["x"] - x) < 3 and abs(c["z"] - z) < 3 and c["x"] > -4025:
            return c
    return None


def main():
    placed = []
    plan = []
    for label, (x, z) in SHELTERS:
        c = candidate_at(x, z)
        if not c:
            print(f"{label}: candidate gone, skipped", flush=True)
            continue
        b = dict(c["building"])
        b.pop("prefab", None)
        d = post("/building", {"prefab": "EmergencyShelter01", **b, "dryRun": True})
        print(f"{label}: dry {short(d)}", flush=True)
        if d.get("errors") == 0 and not d.get("wouldDemolish"):
            plan.append((label, b))

    with paused(WHO):
        r = post("/extension", {"owner": "1299060:3", "prefab": "MedicalClinic01 Ambulance Depot"}, timeout=120)
        print("ambulance depot:", short(r), flush=True)
        for label, b in plan:
            r = building("EmergencyShelter01", **b)
            print(f"{label}: {short(r)}", flush=True)
            if r.get("applied") and r.get("placedId"):
                placed.append(r["placedId"])
    print(json.dumps({"shelters": placed}))


if __name__ == "__main__":
    main()
