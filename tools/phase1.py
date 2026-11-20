"""Phase 1 of Claude City on Corral Riches: start the city, lay the first grid, zone it and
connect power, water and sewage. Coordinates come from the bridge's text map of the start area
(highway stub ends at (-1138, 846); lake shore at x~-672 (z 900-1000) and x~-516 (z 200))."""
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import time


from cc import building, edge_near, get, post, road, short, zone

sys.stdout.reconfigure(encoding="utf-8")


def step(label, res):
    print(f"{label:<38} {short(res)}")
    return res


st = get("/status")
if st.get("mode") != "Game":
    print(post("/newgame", {"map": "Corral Riches", "cityName": "Claude City"}))
    for _ in range(80):
        time.sleep(3)
        st = get("/status")
        if st.get("mode") == "Game" and not st.get("loading"):
            break
    time.sleep(5)
print(post("/ui", {"action": "notutorials"}))
print(post("/ui", {"action": "resume"}))
print(post("/speed", {"speed": 1}))
time.sleep(3)
print(get("/status"))

# spine and first grid
step("spine (Medium Road)", road("Medium Road", [[-1138.4, 846.4], [-1140, 100]]))
for z in (760, 650, 540, 430):
    step(f"street z={z}", road("Small Road", [[-1469, z], [-1139, z], [-809, z]]))
for x in (-1469, -809):
    step(f"street x={x}", road("Small Road", [[x, 760], [x, 650], [x, 540], [x, 430]]))

# zoning
step("residential west", zone("NA Residential Low", mode="rect", points=[[-1600, 420], [-1200, 775]]))
step("residential east", zone("NA Residential Low", mode="rect", points=[[-1080, 420], [-700, 775]]))
step("commercial along spine", zone("NA Commercial Low", mode="rect", points=[[-1200, 420], [-1080, 860]]))

# water: access road to the north-east shore and a pump between road and water
step("water access road", road("Small Road", [[-1137, 900], [-720, 900]]))
step("shore road", road("Small Road", [[-720, 850], [-720, 900], [-720, 990]]))
step("water pump", building("WaterPumpingStation01", edge=edge_near(-720, 945, "Small"), t=0.5, side="right"))

# power: wind turbine next to the spine, north of the water road
step("wind turbine", building("WindTurbine01", edge=edge_near(-1137, 960, "Medium"), t=0.35, side="left"))

# sewage: road to the south shore, outlet at its end
step("sewage road", road("Small Road", [[-1140, 200], [-540, 200]]))
step("sewage outlet", building("SewageOutlet01", x=-523, z=200, angle=-90))

# industry, away from homes
step("industry road west", road("Small Road", [[-1140, 200], [-1469, 200]]))
step("industry west", zone("Industrial Manufacturing", mode="rect", points=[[-1469, 140], [-1180, 262]]))
step("industry east", zone("Industrial Manufacturing", mode="rect", points=[[-1100, 140], [-640, 262]]))

print(post("/save", {"name": "Claude City"}))
print(post("/speed", {"speed": 3}))
print(get("/status"))
