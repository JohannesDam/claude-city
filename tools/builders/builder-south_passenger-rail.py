"""builder-south / b1-mobility-passenger-rail-south: South Gateway passenger station.

The straight branch track between node 583664:173 (-2644.5,-2110) and 367289:1 (-2387.5,-1877.3) heads 42 deg
towards the cargo terminal. North-west of it, between the branch and the yard connector, the freight road
(Medium, dead end at (-2444,-1821)) is extended south-west parallel to the branch as 'Station Road'; the
station (TrainStation03, 200 x 40 m) fronts on it with its tracks towards the branch. Its track ends are read
from the 'No Train Access' / 'Track Not Connected' icons after placement and joined to the branch.
Stages: road-station | link
"""
import json
import sys

sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, short, find_spot, building

WHO = "builder-south"
STATION_ROAD = [[-2444, -1821], [-2461.5, -1855.3], [-2501.9, -1900.0], [-2561.2, -1953.8]]

stage = sys.argv[1] if len(sys.argv) > 1 else ""
if stage == "road-station":
    with paused(WHO):
        r = road("Medium Road", STATION_ROAD, curve="smooth")
        print("Station Road", short(r), r.get("newEdgeIds"))
        post("/roadupgrade", {"polyline": STATION_ROAD, "tolerance": 12, "prefabFilter": "Medium Road", "upgrade": "Trees", "side": "both"})
        for c in find_spot("TrainStation03", -2490, -1915, radius=120, n=10):
            # lot must lie between the road and the branch (south-east side of the road)
            b = dict(c["building"]); b.pop("prefab", None)
            d = post("/building", {"prefab": "TrainStation03", **b, "dryRun": True})
            print("cand", c["x"], c["z"], c["angle"], c["side"], d.get("errors"), d.get("problems"))
            if d.get("errors") == 0 and not d.get("wouldDemolish") and c["angle"] < 0:
                s = building("TrainStation03", **b)
                print("TrainStation03", short(s), c["corners"])
                break

# station link to the network: Double Train Track S-curve (min radius ~206 m) from the station's south-west track
# connection point (-2540.1,-1986) into the branch at (-2606.8,-2076.2) (built with segments/control points).
# bus link 'Rail Link': station -> freight road -> Old Southern Works -> Freight Avenue -> x=-1909 (T1 tram, Office Park)
RAIL_LINK_BUS = [
    {"x": -2477, "z": -1883},     # Station Road, north-east bound (station side)
    {"x": -1805, "z": -820},      # Freight Avenue northbound
    {"x": -1903, "z": -200},      # x=-1909 northbound (shared with line 8)
    {"x": -1915, "z": -200},      # x=-1909 southbound (shared with line 8)
    {"x": -1819, "z": -820},      # Freight Avenue southbound
    {"x": -2486.4, "z": -1872.5}, # Station Road, south-west bound
]
if stage == "bus":
    with paused(WHO):
        r = post("/busline", {"stops": RAIL_LINK_BUS, "color": "#5E35B1", "stopPrefab": "NA_BusStop01"}, timeout=300)
    print(json.dumps(r.get("stops"))[:900], (r.get("line") or {}).get("lineId"), r.get("error"))
