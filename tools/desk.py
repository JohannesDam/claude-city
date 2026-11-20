"""Complaints desk of the Claude City council.

Collects every complaint the city shows (notification icons), turns each complaint type into a ticket,
routes it to the responsible department, and adds what the department needs to act: count, trend since
the previous intake, where it concentrates (GET /hotspots), the service diagnosis (GET /services) and
a playbook. Tickets are written to logs/desk/tickets.json (all) and logs/desk/<department>.json.

  python -I desk.py            # intake + dispatch board
  python -I desk.py --json     # print the tickets as JSON
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cc import get  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
DESK = os.path.join(HERE, "..", "logs", "desk")

DEPARTMENTS = {
    "housing": "Urban planning & housing",
    "economy": "Economy & jobs",
    "utilities": "Utilities (power, water, sewage, garbage, post, telecom)",
    "health": "Health & deathcare",
    "safety": "Safety (police, fire, disasters)",
    "mobility": "Mobility (roads, traffic, transit)",
    "environment": "Environment & beauty",
    "mayor": "Mayor (unclassified)",
}

# complaint icon -> (department, weight per icon, service kind for /services, playbook)
COMPLAINTS = {
    # utilities
    "Garbage Notification": ("utilities", 1.0, "garbage", "Too much garbage waiting. Read /services?kind=garbage diagnosis: failedPathfind or a hotspot >1.5 km from the nearest facility means a facility closer to the hotspot (incinerator/recycling downwind, or landfill at the edge); 'no trucks' means more facilities; efficiency <100% means budget/workers."),
    "Electricity Notification": ("utilities", 3.0, None, "Buildings without power: add production (wind/solar/geothermal), check consumption vs production in /city."),
    "Electricity Not Enough Production Notification": ("utilities", 4.0, None, "City-wide power shortage: add production now."),
    "Electricity Not Enough Connected Notification": ("utilities", 2.0, None, "Power plant not connected to the grid: connect with power lines/roads."),
    "Electricity Bottleneck Notification": ("utilities", 2.0, None, "Grid bottleneck: add transformer/high-voltage line near the hotspot."),
    "Electricity Building Bottleneck Notification": ("utilities", 2.0, None, "Building-level power bottleneck: add transformer/line near the hotspot."),
    "Electricity Transformer Out of Capacity": ("utilities", 2.0, None, "Transformer overloaded: add a second transformer nearby."),
    "Battery Empty": ("utilities", 1.0, None, "Battery drained: production is too low at peak."),
    "Water Notification": ("utilities", 3.0, None, "Buildings without water: pumping capacity or pipes missing near the hotspot."),
    "Water Not Enough Production Notification": ("utilities", 4.0, None, "City-wide water shortage: add pumps on clean groundwater (GET /groundwater) or surface water."),
    "No Water": ("utilities", 3.0, None, "No water at building: check pipes/pumps."),
    "Dirty Water": ("utilities", 4.0, None, "Polluted drinking water makes people sick: move pumps away from polluted ground/outlets (GET /groundwater, /pollution)."),
    "Dirty Water Pump Notification": ("utilities", 4.0, None, "Pump draws polluted water: relocate the pump."),
    "No Groundwater": ("utilities", 2.0, None, "Groundwater pump dry: move it (GET /groundwater)."),
    "Not Enough Groundwater Notification": ("utilities", 2.0, None, "Groundwater running low: spread pumps."),
    "Not Enough Surface Water Notification": ("utilities", 2.0, None, "Surface water pump lacks flow: move to deeper/flowing water."),
    "Sewage Notification": ("utilities", 3.0, None, "Sewage not handled: add outlet/treatment capacity."),
    "Sewage Not Enough Production Notification": ("utilities", 4.0, None, "City-wide sewage shortage: add treatment plant/outlet (downstream, away from pumps)."),
    "Pipeline Not Connected": ("utilities", 2.0, None, "Water building not connected: connect via roads/pipes."),
    "Pipeline Not Connected - Sewage": ("utilities", 2.0, None, "Sewage building not connected."),
    "Powerline Not Connected": ("utilities", 2.0, None, "Power building not connected (high voltage)."),
    "Powerline Not Connected - Low": ("utilities", 2.0, None, "Power building not connected (low voltage)."),
    "Facility Full": ("utilities", 1.5, None, "A facility is full (landfill/cemetery/prison/shelter): the ticket's prefabs show which; build a new one or a processing facility (incinerator/crematorium)."),
    # health
    "Hearse Notification": ("health", 1.5, "deathcare", "Dead waiting for a hearse: /services?kind=deathcare — full cemeteries, no hearses, or too far; crematoria process bodies, cemeteries store them."),
    "Ambulance Notification": ("health", 2.0, "healthcare", "Sick/injured waiting for an ambulance: hospitals/clinics near the hotspot, check vehicles."),
    "No Hospital Supplies": ("health", 2.0, "healthcare", "Hospital lacks supplies."),
    # safety
    "Crime Scene": ("safety", 2.0, "police", "Crime: police station coverage near the hotspot (/coverage?service=Police), police HQ/prison capacity."),
    "Fire Notification": ("safety", 5.0, "fire", "Burning building: fire station coverage near the hotspot (/coverage?service=FireRescue)."),
    "Burned Down": ("safety", 1.0, None, "Ruin after fire: clear it (autopilot clears ruins) and check fire coverage."),
    "Weather Damage": ("safety", 1.0, None, "Storm damage: disaster response (early warning, shelters)."),
    "Weather Destroyed": ("safety", 1.0, None, "Destroyed by weather: clear ruins."),
    "Water Damage": ("safety", 1.0, None, "Flood damage."),
    "Water Destroyed": ("safety", 1.0, None, "Destroyed by flood: clear ruins; don't build in flood zones."),
    "Destroyed": ("safety", 1.0, None, "Destroyed building: clear ruins."),
    "No Emergency Shelter Supplies": ("safety", 1.0, None, "Emergency shelter lacks supplies."),
    "Traffic Accident": ("mobility", 1.0, None, "Accidents: dangerous intersections near the hotspot (traffic lights, roundabouts, fewer crossings on arterials)."),
    # mobility
    "Traffic Bottleneck Notification": ("mobility", 1.5, None, "Congestion: hierarchy (arterial/collector), extra connections, roundabouts, public transport along the corridor."),
    "Dead End": ("mobility", 0.5, None, "Dead-end road: connect it or it is intentional."),
    "No Road Access": ("mobility", 2.0, None, "Building without road access: connect a road."),
    "No Car Access": ("mobility", 3.0, None, "No car access: a building's driveway ends where there is no road (e.g. a wide lot only partly on a road - the round-1 prison could not send vans for a week). Extend the frontage road along the whole lot front, then check the icon is gone."),
    "No Pedestrian Access": ("mobility", 1.0, None, "No pedestrian access: connect paths/roads."),
    "No Cargo Access": ("mobility", 2.0, None, "No cargo access: trucks can't reach (road connection)."),
    "No Bicycle Access": ("mobility", 0.5, None, "No bicycle access."),
    "Pathfind Failed": ("mobility", 2.0, None, "Vehicles/citizens can't find a route: disconnected network or one-way trap near the hotspot."),
    "No Outside Connection": ("mobility", 3.0, None, "No outside connection reachable."),
    "No Train Access": ("mobility", 1.0, None, "Train building not connected to track."),
    "No Track Access": ("mobility", 1.0, None, "No track access."),
    "Track Not Connected": ("mobility", 1.0, None, "Track not connected."),
    "No Vehicles": ("mobility", 1.0, None, "Depot/line without vehicles."),
    "No Fuel Notification": ("economy", 1.0, None, "No fuel."),
    # economy
    "No Customers": ("economy", 1.0, None, "Shops without customers: too much commercial for the population nearby, or poor access; slow down commercial zoning there."),
    "No Inputs": ("economy", 1.0, None, "Industry/commerce lacks inputs: supply chain gap (raw materials, cargo access, outside connections, specialized industry)."),
    "MissingEducatedWorkers": ("economy", 1.0, "education", "Employers lack educated workers: high schools/college/university capacity."),
    "MissingUneducatedWorkers": ("economy", 1.0, None, "Employers lack uneducated workers: more housing near jobs, or fewer low-skill jobs."),
    # housing
    "Rent Too High": ("housing", 1.0, None, "Rent too high: land value outruns incomes; more supply (density/zoning), lower taxes, better jobs."),
    "Condemned": ("housing", 1.0, None, "Condemned building (zone changed/obsolete): leave it or clear it."),
    "Abandoned": ("housing", 1.0, None, "Abandoned building: cause is usually no demand/high rent/missing services; clear and fix the cause."),
    "Abandoned Collapsed": ("housing", 1.0, None, "Collapsed abandoned building: clear ruins."),
    # environment
    "Air Pollution": ("environment", 1.0, None, "Air pollution at homes: polluters upwind (wind SW->NE), move/replace them; trees/parks."),
    "Ground Pollution": ("environment", 1.0, None, "Ground pollution: industry/landfill nearby; affects groundwater and health."),
    "Noise Pollution": ("environment", 0.7, None, "Noise at homes: highways/arterials/industry/turbines next to homes; buffers (parks, offices)."),
    "Sad Face": ("mayor", 0.5, None, "Unhappy citizens: see /happiness factors."),
}
INFO_ONLY = {"Leveling Building", "Building Level Up", "Happy Face", "Followed", "Selected", "ValentineHeart", "Turned Off"}
FACILITY_FULL_ROUTES = (("Landfill", "utilities"), ("Cemetery", "health"), ("Crematorium", "health"), ("Hospital", "health"),
                        ("Prison", "safety"), ("Police", "safety"), ("Shelter", "safety"))


def slug(s):
    return "".join(c.lower() if c.isalnum() else "-" for c in s).strip("-")


def intake(hotspot_limit=4):
    status = get("/status")
    probs = get("/problems").get("problems") or []
    counts = {p["type"]: p["count"] for p in probs}
    os.makedirs(DESK, exist_ok=True)
    hist_path = os.path.join(DESK, "history.jsonl")
    prev = {}
    try:
        with open(hist_path, encoding="utf-8") as f:
            lines = f.read().strip().splitlines()
            if lines:
                prev = json.loads(lines[-1]).get("counts", {})
    except OSError:
        pass

    services = {}
    tickets = []
    for typ, n in sorted(counts.items(), key=lambda kv: -kv[1]):
        if typ in INFO_ONLY:
            continue
        dept, weight, kind, playbook = COMPLAINTS.get(typ, ("mayor", 1.0, None, "Unclassified complaint type: classify it in desk.py."))
        hs = get("/hotspots", type=typ, cell=400, limit=hotspot_limit)
        spots = [{"center": h["center"], "count": h["count"], "top": list((h.get("topPrefabs") or {}).keys())[:3],
                  "nearestFacility": {k: h["nearestFacility"].get(k) for k in ("prefab", "distance", "status", "vehicles")} if h.get("nearestFacility") else None}
                 for h in (hs.get("hotspots") or [])]
        if typ == "Facility Full":
            tops = [t for s in spots for t in s["top"]]
            for key, d in FACILITY_FULL_ROUTES:
                if any(key in t for t in tops):
                    dept = d
                    break
        diag = None
        if kind:
            if kind not in services:
                services[kind] = (get("/services", kind=kind, brief=1).get("kinds") or {}).get(kind) or {}
            diag = services[kind].get("diagnosis")
        delta = n - prev.get(typ, 0) if prev else None
        tickets.append({
            "id": slug(typ), "type": typ, "department": dept, "count": n, "delta": delta,
            "severity": round(n * weight, 1), "hotspots": spots,
            "medianDistanceToFacility": (hs.get("distanceToNearestFacility") or {}).get("median"),
            "diagnosis": diag, "playbook": playbook,
        })
    tickets.sort(key=lambda t: -t["severity"])
    snap = {"time": time.strftime("%Y-%m-%d %H:%M:%S"), "date": status.get("date"), "population": status.get("population"), "counts": counts}
    with open(hist_path, "a", encoding="utf-8") as f:
        f.write(json.dumps(snap) + "\n")
    with open(os.path.join(DESK, "tickets.json"), "w", encoding="utf-8") as f:
        json.dump({"intake": snap, "tickets": tickets}, f, indent=1)
    for d in DEPARTMENTS:
        mine = [t for t in tickets if t["department"] == d]
        with open(os.path.join(DESK, f"{d}.json"), "w", encoding="utf-8") as f:
            json.dump({"department": DEPARTMENTS[d], "intake": snap, "tickets": mine}, f, indent=1)
    return snap, tickets


def board(snap, tickets):
    print(f"Complaints desk - {snap['date']} - population {snap['population']}")
    total = sum(t["severity"] for t in tickets) or 1
    for d, title in DEPARTMENTS.items():
        mine = [t for t in tickets if t["department"] == d]
        if not mine:
            continue
        sev = sum(t["severity"] for t in mine)
        print(f"\n[{d}] {title}  - severity {sev:.0f} ({100 * sev / total:.0f}%)")
        for t in mine:
            trend = "" if t["delta"] is None else f" ({t['delta']:+d})"
            where = "; ".join(f"{s['center'][0]:.0f},{s['center'][1]:.0f}:{s['count']}" for s in t["hotspots"][:3])
            far = f" median {t['medianDistanceToFacility']:.0f} m to facility" if t.get("medianDistanceToFacility") else ""
            print(f"  {t['type']:<40} {t['count']:>4}{trend}  at {where}{far}")
            for line in (t["diagnosis"] or [])[:2]:
                print(f"      - {line}")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    snap, tickets = intake()
    if "--json" in sys.argv:
        print(json.dumps(tickets, indent=1))
    else:
        board(snap, tickets)
