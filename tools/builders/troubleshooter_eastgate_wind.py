"""East Gate wind farm: places WindTurbine01s with POST /place (one call each, 0 demolition) inside East Gate,
spread over the given anchor points, as the non-fossil replacement for the stopgap coal plants (the river west of
East Bank is too wide for high-voltage pylons, so East Gate cannot be tied to the main grid).
Run: cd C:/soft/cityskyline/tools && python -I builders/troubleshooter_eastgate_wind.py N
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import post, paused  # noqa: E402

EAST_GATE = [935, -2181, 5298, 312]
ANCHORS = [(4900, -800), (4900, -1000), (5130, -700), (5130, -950), (4230, -1286), (4450, -1286), (4000, -1286),
           (4700, -650), (5130, -1100), (4900, -600), (4650, -900), (4300, -1100), (3700, -1300), (3500, -1350)]


def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 6
    placed = []
    with paused("troubleshooter"):
        for ax, az in ANCHORS:
            if len(placed) >= n:
                break
            r = post("/place", {"prefab": "WindTurbine01", "near": [ax, az], "within": EAST_GATE, "radius": 220,
                                "maxHouseholds": 0, "maxCompanies": 0}, timeout=240)
            pid = r.get("placedId")
            ch = r.get("chosen") or {}
            print(ax, az, "->", pid, ch.get("x"), ch.get("z"), r.get("error") or "", flush=True)
            if pid:
                placed.append({"id": pid, "pos": [ch.get("x"), ch.get("z")]})
    print(json.dumps(placed))


if __name__ == "__main__":
    main()
