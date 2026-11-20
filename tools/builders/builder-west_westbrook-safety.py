"""builder-west, item b2-health_safety-westbrook-police-north-south-shelters.

North: PoliceStation03 at the clean findspot lot (-4475,1418); LargeEmergencyShelter01 at (-4513,1084) with a
pocket park. South: PoliceStation03 via make_room (<= 60 households) near the school-street civic cluster,
inside the Westbrook bbox (Westpark x > -4298 excluded). Small shelters for the uncovered village cells.
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import building, find_spot, make_room, paused, post, short  # noqa: E402

WHO = "builder-west"
WB = [-5290, -920, -4310, 2170]


def at(prefab, x, z, tol=4, radius=60):
    for c in find_spot(prefab, x, z, radius=radius, n=20, within=WB):
        if abs(c["x"] - x) <= tol and abs(c["z"] - z) <= tol:
            b = dict(c["building"])
            b.pop("prefab", None)
            d = post("/building", {"prefab": prefab, **b, "dryRun": True})
            if d.get("errors") == 0 and not d.get("wouldDemolish"):
                r = building(prefab, **b)
                print(f"{prefab}: {short(r, ('applied', 'cost', 'placedId', 'roadConnected'))} at ({c['x']}, {c['z']})", flush=True)
                return r.get("placedId")
    print(f"{prefab}: no clean lot at ({x}, {z})", flush=True)
    return None


def near(prefab, x, z, radius=150):
    for c in find_spot(prefab, x, z, radius=radius, n=20, within=WB):
        b = dict(c["building"])
        b.pop("prefab", None)
        d = post("/building", {"prefab": prefab, **b, "dryRun": True})
        if d.get("errors") == 0 and not d.get("wouldDemolish"):
            r = building(prefab, **b)
            print(f"{prefab}: {short(r, ('applied', 'cost', 'placedId', 'roadConnected'))} at ({c['x']}, {c['z']})", flush=True)
            return r.get("placedId")
    print(f"{prefab}: no clean lot near ({x}, {z})", flush=True)
    return None


def main():
    out = {}
    with paused(WHO):
        out["police_north"] = at("PoliceStation03", -4474.9, 1417.5)
        out["large_shelter"] = at("LargeEmergencyShelter01", -4513.01, 1083.5)
        out["shelter_park"] = near("PocketPark05", -4513, 1084, radius=90)
        r = make_room("PoliceStation03", -4500, -420, radius=350, within=WB, log_as=WHO)
        print("police_south:", r.get("mode"), r.get("cost"), short(r.get("result") or {}, ("applied", "cost", "placedId", "roadConnected")), flush=True)
        out["police_south"] = (r.get("result") or {}).get("placedId")
        out["shelter_village"] = at("EmergencyShelter01", -4857.67, 242.63)
        out["shelter_civic"] = at("EmergencyShelter01", -4411.21, 446.5)
        out["shelter_north_crescent"] = near("EmergencyShelter01", -4760, 680, radius=160)
    print(json.dumps(out))


if __name__ == "__main__":
    main()
