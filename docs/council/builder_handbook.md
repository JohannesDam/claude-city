# Builder handbook

For the long-lived builder agents of Claude City (district builders and the troubleshooter). Read it once at
the start of your shift; it replaces the per-round worker prompt of the old council. Repository root:
`C:\soft\cityskyline`. Structure and roles: [docs/ORCHESTRATION.md](../ORCHESTRATION.md).

## Your shift

1. Read: this handbook, `docs/council/masterplan.json` (vision, rules, zones), `docs/council/districts.json`
   (who owns which district, character, phase) and the handbook section of `docs/council/domains.md` for the
   domain of your current item.
2. Loop until the worklist has nothing for you:
   - `python -I tools/worklist.py next <you>`: claims your highest-priority open item (items for your
     districts, plus unassigned items inside them). `python -I tools/worklist.py mine <you>` shows your queue.
   - Plan it WITHOUT the lock: survey with GETs (map, findspot, freeland, coverage, nets, districts), dry runs
     are fine too.
   - Build it in short locked batches: `with paused("<you>"):` around a coherent change (a road + its zoning +
     its trees), seconds to a minute or two, never your thinking time. Other builders queue behind you.
   - Verify (see "Done means working" below), then
     `python -I tools/worklist.py done <id> --spent <$> --note "<what/where>"` (or `fail <id> --note "<why>"`,
     or `defer <id> --note "<why>"` when it must wait). Append your sites to `logs/council/sites.jsonl`.
3. When your queue is empty, do the standing list (below) inside your districts and then report back to the
   conductor with a short summary (built, spent, follow-ups, interface gaps).
4. If your context gets long, write a handover note to `logs/council/handover/<you>.md` (what is in progress,
   ids, lessons) before you stop, so the next builder can pick it up.

## The lock ("busy tone")

- The mod holds a first-come-first-served build lock. `cc.paused(who)` queues for it, pauses the game, and
  releases it (resuming the game only when nobody else waits). `GET /lock` shows who builds and who waits.
- Think outside the lock, check at build time: the city changes while you plan, so every build call
  re-validates (overlap, demolition refusal). A stale plan fails safely; re-survey and adapt.
- Build calls without the lock get HTTP 423 "busy" while someone else builds; `cc.post` handles the lock
  for you. Never call `/speed`, `/reload`, `/save`, `/load` yourself.

## Ownership

- You build only inside your districts (bbox in `districts.json`) and on worklist items assigned to you.
  City-wide items carry a location; they go to the owner of that district or to the troubleshooter.
- Boundary roads and arterials belong to the item that names them; connect to existing stubs only.
- Protected zones (parks, landmarks, leisure quarter): never build or bulldoze there unless the item says so.
- Never bulldoze another district's buildings. Urban renewal items may demolish inside their approved phase
  box (pass `allowDemolish=True` only there). To fit a big building in your own district, use `make_room`
  (see "Big buildings" below), which prices and logs what it removes.

## Quality rules (the owner's bar: "an aesthetic pearl where people and city live in synergy")

- No more monotone grids of Small Road. Every new area gets a hierarchy: an arterial (Large/Medium Road,
  with trees) as spine, collector streets, and quiet curved residential streets (crescents, loops, short
  cul-de-sacs). Use `curve:"smooth"` or `segments` with control points; a ±5 m wiggle over 450 m is straight.
- Give each district its character (see `districts.json`): garden suburb, dense core with boulevards,
  work quarter with green buffers, campus, waterfront.
- Trees and grass on arterials and next to homes (`/roadupgrade` Trees/Grass, both sides).
- Polluters NE / downwind of homes (wind SW -> NE). Buffers (parks, trees) between homes and industry or
  highways.
- Every residential block within park coverage; schools, clinics, post and shops within walking distance.
- Zone the density that has demand (`GET /city` demand). The city is 62% low-density homes and 3% high
  density: new cores near transit should be dense (medium/high, mixed use).
- Transit is part of every plan: bus first, tram/train/subway where density allows. Tram line T1 runs on
  x=-1909 (depot TramDepot01 at (-2068,-500)); tram track prefabs are in `/prefabs?category=nets`; trams and
  trains cannot reverse at a dead-end track, so every line end needs a loop. `/lines` can say complete while a
  segment fails pathfinding: check `/problems` for Pathfind Failed / Track Not Connected after building a line.

## Done means working

- `roadConnected` only checks the lot's front centre. After the game has run a moment, call
  `cc.access_check([ids])`: any "No Car Access"/"No Pedestrian Access" means the driveway ends where there is
  no road - extend the frontage road along the whole lot front.
- Lines: complete and with vehicles (`GET /lines`). Coverage improved (`GET /coverage`). Zones growing
  (`GET /districts`).
- The game silently demolishes zoned buildings under a new lot or road; the API reports them
  (`wouldDemolish`) and refuses by default - read it in every dry run.
- Edge ids change when neighbouring roads or buildings are built: prefer box/polyline selection, or re-read
  ids right before each call; placing a building beside a road renews that road's ids too. `/roadupgrade`
  and `/upgrade` return `edgeIdsAfter` (current ids in request order) and `edgeMap` - use those for the next call.
- `/extension` reports `wouldDemolish` and refuses (`blocked`) like `/building`; `side:"auto"` prefers slots
  that remove nothing. `/building` results include the lot `corners`, and dry runs report `roadConnected`.
  `/findspot` tries setbacks 0.5/2/3.5 m.

## Big buildings: find land, buy land, or make room

The owner noticed builders giving up on big buildings that "don't fit". Don't give up, and don't settle for a
bad site far away from where the building is needed. In this order:

1. A clean site near where it is needed: `place_smart(prefab, x, z, radius=500..800)`; `/freeland` to see
   empty land.
2. Buy land: an adjacent tile costs $0.05-0.5M (`GET /tiles` candidates with price and upkeepIncrease), when
   the building still serves its purpose there (coverage radius, access).
3. Make room: `make_room(prefab, x, z, radius=500, dry=True)` lists sites that replace zoned buildings,
   priced by what they cost: households, companies and tax revenue per month (industry and offices pay
   ~$20-25k per building per month, homes ~$90 per household). Without `dry` it builds the cheapest site
   within the limits (default <= 60 households, <= 4 companies, <= 0.1% of monthly income) and logs it to
   `logs/council/demolitions.jsonl`.
   - Clearing homes is acceptable when residential demand is high (people move into new homes elsewhere);
     prefer abandoned/condemned buildings and low-level homes. Avoid workplaces; never service buildings,
     signature buildings or protected parks.
   - Bigger clearances (above the limits) need the conductor: describe the options in your report.
   - Pair a clearance with new homes or zoning elsewhere in your district when you can.

## Money

- The worklist item carries a budget. Stay within it (+10%). Bigger needs: `defer` with a note; the
  conductor decides. Tile purchases are cheap (0.05-0.5M); their upkeep is what counts.

## Standing list (when your queue is empty)

1. Zone land with demand in your districts (curved streets first, then zoning).
2. Trees/grass on arterials and residential streets that have none.
3. Park coverage gaps, missing local services (school, clinic, post, police) - check `/coverage`.
4. Fix problems in your districts (`GET /problems`, `logs/desk/*.json` tickets with your coordinates).

## Tools

Python client: `C:\soft\cityskyline\tools\cc.py`.

```python
import sys; sys.path.insert(0, r"C:\soft\cityskyline\tools")
from cc import get, post, paused, road, zone, building, upgrade, place_smart, find_spot, fit, short, access_check, make_room, demolition_cost
```

CLI: `cd C:/soft/cityskyline/tools && python -I cc.py GET "/findspot?prefab=Hospital01&x=-1600&z=-560&radius=600"`.
Write longer builds as scripts in `tools/builders/<you>_<item>.py` and run them with
`cd C:/soft/cityskyline/tools && python -I builders/<file>.py` (Bash timeout 600000).

### Read (GET)

/city, /districts (per district: residents, households, happiness, workplaces, buildings by zone and service),
/happiness, /problems?type=, /hotspots?type=&cell=, /services?kind=garbage|deathcare|healthcare|police|fire|education|post|park|electricity|water|telecom|roads|all&brief=1,
/coverage?service=Park|Healthcare|Police|FireRescue|Education|PostService&layer=homes, /zonestats,
/map?x=&z=&size=&cell= (ASCII map with legend), /nets?x=&z=&radius=&filter= (paged), /nodes?x=&z=&radius=&minDegree=3,
/buildings?kind=service|all|destroyed|abandoned&x=&z=&radius= (paged), /prefabs?category=zones|roads|services|buildings|signature&filter=&unlockedOnly=true,
/unlockinfo?name=, /findspot?prefab=&x=&z=&radius=&n= (ranked placements; candidates[i].building can be passed to /building),
/freeland?x=&z=&radius=&prefab=, /pollution, /groundwater, /tiles (candidates with price and upkeepIncrease), /devtree,
/lines, /stops, /roadupgrades, /transitprefabs, /budget, /taxes, /demand, /jobs, /finance, /stats?names=&n=,
/education, /extensions?owner=<id> or ?filter=School&onlyBuildable=1, /stuck, /landvalue, /crime, /levels,
/signatures, /policies, /modifiers, /fees, /requests?kind=...&onlyFailed=1, /wind?x=&z=, /tourism,
/deathcare?seconds=30, /resources?x=&z=&radius=&resource=fertility|forest|ore|oil|all, /extractors,
/areas?kind=district|extractor, /areainfo (outside connections: ship, airplane, train, road), /lock.
`python -I tools/opportunities.py` lists unused unlocks, transport modes and outside connections.

### Build (every call accepts "dryRun": True)

- `road(prefab, [[x,z],...])` - polyline; long roads segment by segment if "Invalid Shape"; result lists
  builtEdges/newEdgeIds/staleEdgeIds.
- Curved/elevated: `post("/road", {"prefab":..,"points":[[x,z],...],"curve":"smooth"})` fits real arcs through
  the points (points on a circle of radius 150 give radius 150), or
  `{"segments":[{"start":[x,z],"control":[x,z],"end":[x,z]}, {"control":..,"end":..}]}` (control = corner of the
  curve). Elevation: `[x,z,e]` per point or `"elevation": e` for overpasses/bridges (ramps <= ~8-10%; check
  geometry.warnings). Result "geometry": spans with turnDeg, minRadius, warnings.
- Roundabouts: `post("/roundabout", {"at":[x,z] | "node": id, "size":"small|medium|large|xl", "dryRun":True})`.
  Traffic lights / all-way stops: `post("/nodeupgrade", {"upgrade":"Traffic Lights"|"All-way Stop", "near":[x,z] | "nodes":[ids]})`.
- Removing: `post("/bulldoze", {"edges":[ids]} | {"box":[x0,z0,x1,z1], "nets":"Small Road|Medium Road", "buildings":True, "dryRun":True})`
  - the dry run reports households, residents, companies and workers that would go. `post("/bulldoze", {"ids":[...]})`.
- Edge selection without ids for /roadupgrade, /upgrade, /bulldoze: `"box":[x0,z0,x1,z1]` or
  `"polyline":[[x,z],...]` (+`"tolerance"`) with `"prefabFilter":"Medium Road"`; stale ids are skipped.
- Roadside placement: `building(prefab, near=[x,z], side="auto")`; `place_smart(prefab, x, z, radius=400)`;
  `building(prefab, edge=id, t=0..1, side="left|right")` or `building(prefab, x=, z=, angle=)`;
  `fit(label, prefab, (x0,x1,z0,z1), near=(x,z))`.
- Zoning (`overwrite` is False by default since 2032-01: rezoning cells that already carry a zone - and so the
  buildings on them - needs `overwrite=True`, which counts as demolition: renewal phases only; `side` "right" =
  right-hand travelling from the edge's start to its end): `zone(name, mode="rect", points=[[x0,z0],[x1,z1]])` or
  `zone(name, mode="road", edges=[ids], depth=1..6, side="both|left|right")`.
- `upgrade(prefab, edges)` replaces the road type (batches of <= 4 edges).
- `post("/roadupgrade", {"edges":[ids], "upgrade":"Trees|Grass|Wide Sidewalk|Sound Barrier|Lighting|Bikelane", "side":"both"})`.
- Bus lines: `post("/busline", {"stops":[{"x":..,"z":..},...], "color":"#RRGGBB", "stopPrefab":"NA_BusStop01"}, timeout=300)`
  - closed loop, right-hand traffic: outbound stops on one side, return stops on the other (~7 m off the centre).
- `post("/buytile", {"x":..,"z":..})`; `post("/extension", {"owner": id, "prefab": name, "side":"auto"}, timeout=120)`;
  `post("/buildingaction", {"id":.., "action":"outOfService|emptying|historical", "on":True})`.
- Areas: `post("/area", {"owner": "<extractor main building id>", "points": [...], "dryRun": True})` redraws a
  specialized-industry work area (corners within 300-400 m of the building);
  `post("/area", {"prefab":"district", "name":.., "points":[...]})` creates a named district (corners ~10 m
  inside owned tile edges, else "Exceeds City Limits").
- Results: applied, cost, errors, problems ("Overlap Existing", "Exceeds City Limits", "Invalid Shape"),
  conflicts (the entity behind an overlap) and wouldDemolish. `cc.post` sends `allowDemolish=False` by default.

### Never

`/reload`, `/speed`, `/save`, `/load`, `/newgame`, `/ui`, `/taxes`, `/taxes2`, `/budget`, `/fees`, `/policy`,
`/loan`, `/requests/reset`, `/devtree` - conductor only; put such needs in your report.
Coordinates: x east, z north, metres. Wind blows SW -> NE. Outer high-voltage pylons in the NW block roads.
