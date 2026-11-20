export const meta = {
  name: 'claude-city-council-round',
  description: 'Claude City council round: 8 department aldermen plan in parallel, the mayor allocates the budget, workers execute approved plans one at a time',
  whenToUse: 'One governing round of Claude City (Cities: Skylines II) after tools/council_brief.py has written logs/council/brief.json',
  phases: [
    { title: 'Plan', detail: 'each alderman assesses its domain and proposes projects (read-only)' },
    { title: 'Allocate', detail: 'mayor ranks proposals within the cash/cash-flow envelope' },
    { title: 'Execute', detail: 'department workers build the approved projects under the build lock' },
  ],
}

const R = args.round
const FIN = args.finance

const API = `
## Tools (live game - the owner's city "Claude City", ~97k people, runs in it)
- HTTP bridge http://127.0.0.1:8787. Python client C:\\soft\\cityskyline\\tools\\cc.py. Use it from Python:
  import sys; sys.path.insert(0, r"C:\\soft\\cityskyline\\tools"); from cc import get, post, paused, road, zone, building, upgrade, place_smart, find_spot, fit, short
  or CLI: cd C:/soft/cityskyline/tools && python -I cc.py get "/findspot?prefab=Hospital01&x=-1600&z=-560&radius=600"
- READ (GET): /city, /happiness, /problems?type=, /hotspots?type=&cell=, /services?kind=garbage|deathcare|healthcare|police|fire|education|post|park|all&brief=1, /coverage?service=Park|Healthcare|Police|FireRescue|Education|PostService&layer=homes, /zonestats, /map?x=&z=&size=&cell= (ASCII map, legend included), /nets?x=&z=&radius=&filter=, /buildings?kind=service|all|destroyed|abandoned&x=&z=&radius=, /prefabs?category=zones|roads|services|buildings|signature&filter=&unlockedOnly=true, /unlockinfo?name=, /findspot?prefab=&x=&z=&radius=&n= (ranked placements; candidates[i].building can be passed to /building), /freeland?x=&z=&radius=&prefab=, /pollution, /groundwater, /tiles, /devtree, /lines, /stops, /roadupgrades, /transitprefabs, /budget, /taxes, /demand (demand per zone with the game's factor breakdown), /jobs (workplaces vs job seekers per education level), /finance (income/expense per source), /stats?names=Population,Unemployed&n=12, /education (eligible vs seats per level, possible classroom extensions), /extensions?owner=<building id> or ?filter=School&onlyBuildable=1 (service building upgrades), /services?kind=electricity|water|telecom|roads (utility and road-maintenance status), /stuck (traffic penalty causes, unreachable buildings, failed paths), /landvalue, /crime, /levels (building levels, waiting for materials), /signatures (signature buildings unlocked/placed), /policies, /modifiers, /fees, /taxes2,
  /requests?kind=prisonerTransport|hearse|garbage|post|maintenance|fire|...|all&onlyFailed=1 (stuck service requests per target), /wind?x=&z= (wind speed + expected turbine output before building; no x/z = grid of owned land), /tourism (attractiveness, tourists, hotel rooms/occupancy, attractions), /deathcare?seconds=30 (measured bodies/day per facility while the game runs), /resources?x=&z=&radius=&resource=fertility|forest|ore|oil|all (natural resource map, best spots, best owned/unowned tiles), /extractors (specialized industry main buildings and their work-area prefabs), /areas?kind=district|extractor (districts with names and policies; extractor work areas with resource amounts). /services now shows upkeep per building and per kind; /prefabs?category=services shows upkeep per prefab; /tiles candidates show price and upkeepIncrease; /services?kind=electricity shows trade and grid connection per building.
- BUILD (Python; every build call takes the cross-process build lock and pauses the game by itself; wrap a batch in \`with paused("<dept>"):\` to hold the lock for the whole batch, keep each batch under ~3 minutes):
  road(prefab, [[x,z],...]) - straight polyline, build long roads segment by segment if "Invalid Shape"; results list builtEdges/newEdgeIds/staleEdgeIds;
  CURVED/ELEVATED roads: post("/road", {"prefab":..,"points":[[x,z],...],"curve":"smooth"}) fits the game's own curves through the points (real arcs: e.g. points on a circle of radius 150 give radius 150; a gentle ±5 m wave over 450 m is practically straight - design visible curves, crescents and S-bends), or {"segments":[{"start":[x,z],"control":[x,z],"end":[x,z]}, {"control":..,"end":..}]} (each control point = the corner of the curve). Elevation: a third value per point [x,z,e] (metres above terrain) or "elevation": e - for overpasses/bridges (ramps <= ~8-10% slope; check geometry.warnings). Result "geometry": spans with turnDeg, minRadius, warnings;
  roundabouts: post("/roundabout", {"at":[x,z] | "node": id, "size":"small|medium|large|xl", "dryRun":True}) (Intersections node is owned; GET /roundabouts lists prefabs); traffic lights / all-way stops: post("/nodeupgrade", {"upgrade":"Traffic Lights"|"All-way Stop", "near":[x,z] | "nodes":[ids], "remove":False}); GET /nodes?x=&z=&radius=&minDegree=3 lists junctions;
  removing roads/tracks: post("/bulldoze", {"edges":[ids]} | {"box":[x0,z0,x1,z1], "nets":"Small Road|Medium Road", "buildings":True, "dryRun":True}) - the dry run reports households, residents, companies and workers that would go (urban renewal phases);
  edge selection without ids for /roadupgrade, /upgrade, /bulldoze: "box":[x0,z0,x1,z1] or "polyline":[[x,z],...] (+"tolerance") with "prefabFilter":"Medium Road"; stale ids are skipped (selection.missing) instead of failing the batch; GET /nets pages (offset/limit, truncated) and shows each segment's upgrades (Trees, Grass...); GET /buildings pages too;
  roadside placement without edge ids: building(prefab, near=[x,z], side="auto") picks the road spot itself;
  zone(zoneName, mode="rect", points=[[x0,z0],[x1,z1]], overwrite=False) or zone(zoneName, mode="road", edges=[ids], depth=1..6, side="both|left|right");
  place_smart(prefab, x, z, radius=400) -> (result, candidate) best validated /findspot spot; building(prefab, edge=id, t=0..1, side="left|right") or building(prefab, x=, z=, angle=);
  fit(label, prefab, (x0,x1,z0,z1), near=(x,z)) - tries all road segments in a box; upgrade(prefab, edges) - replace road type (batches of <=4 edges);
  post("/roadupgrade", {"edges":[ids], "upgrade":"Trees|Grass|Wide Sidewalk|Sound Barrier|Lighting|Bikelane", "side":"both"}) - trees/grass on both sides halve road noise;
  post("/busline", {"stops":[{"x":..,"z":..},...], "color":"#RRGGBB", "stopPrefab":"NA_BusStop01"}, timeout=300) - closed loop; right-hand traffic, so for a two-way route put outbound stops on one side and return stops on the other (x/z offset ~7 m from the road centre picks the side); bus depots exist south (-1450,-870) and north (-1150,4100); line 1 (red) runs along x=-1909;
  post("/bulldoze", {"ids":[...]}); post("/buytile", {"x":..,"z":..}) (~0.1-0.4M per 623 m tile; check /tiles); post("/devtree", {"node": name}) ONLY if your approved proposal lists dev points;
  post("/extension", {"owner": "<building id>", "prefab": "<upgrade name or substring>", "side": "auto"}, timeout=120) - build a service-building upgrade/extension (classrooms, gymnasium, extra garage, city hall wings...); dry run first;
  post("/buildingaction", {"id": .., "action": "outOfService|emptying|historical", "on": true});
  post("/area", {"owner": "<extractor main building id>", "points": [[x,z],...], "dryRun": True}) - redraw a specialized-industry work area (field/forest/mine/oil) after placing its main building ("Agriculture Area Placeholder - Grain", "Forestry Area Placeholder", "Ore Area Placeholder - Ore", ... via building()); corners within the work area's maxRadius (300-400 m) of the building; the result shows the game's resourceAmount. post("/area", {"prefab": "district", "name": .., "points": [...]}) creates a named district (13 exist: GET /areas?kind=district) - district policies via the mayor.
  Every build call accepts "dryRun": True. Results: applied, cost, errors, problems (e.g. "Overlap Existing", "Exceeds City Limits", "Invalid Shape"), conflicts (the entity behind an Overlap Existing: id, prefab, kind) and wouldDemolish (zoned buildings - homes, shops, offices, factories - the build would remove: id, prefab, renters). cc.post sends allowDemolish=False by default, so /building, /road, /upgrade and /roadupgrade REFUSE ("blocked") instead of silently demolishing; pass allowDemolish=True only when demolition is part of your approved plan (urban renewal). /findspot and /freeland treat zoned buildings as obstacles (growables=free shows what a spot would demolish).
- NEVER POST /reload, /speed, /save, /load, /newgame, /ui, /taxes, /taxes2, /budget, /fees, /policy, /loan, /requests/reset (mayor only - put such ideas in a proposal's actions as 'mayor:' items). Never bulldoze other departments' buildings or anything in a PROTECTED zone (exception: an approved Urban Renewal project may demolish everything inside its approved phase box, except protected zones and service buildings it has not listed). Coordinates: x east, z north, metres. Wind blows SW->NE.
- Knowledge: C:\\soft\\cityskyline\\docs\\council\\domains.md (your section + "Citizens & wellbeing" for happiness formulas), C:\\soft\\cityskyline\\docs\\council\\masterplan.json (zones, owners, rules), C:\\soft\\cityskyline\\logs\\council\\brief.json (this round's snapshot: finance, happiness, tickets, services, coverage), C:\\soft\\cityskyline\\logs\\desk\\<department>.json (your complaint tickets with hotspots).
`

const DEPTS = [
  { key: 'housing', title: 'Alderman of Planning & Housing', section: '## 1. Urban planning & housing (also ## 11 Citizens & wellbeing)', zones: 'Downtown, West residential, West frontier (new land x -4680..-4300, z -900..2180, empty), North residential, East lakeside',
    focus: 'Residential demand: low 100, high 100, medium 0. Zone types never used yet: Commercial High, Office High, Old Town, Low Rent, Waterfront (they unlock signature buildings - /unlockinfo). Plan the West frontier as a beautiful garden suburb (parks woven in, a school, shops). Building levels and land value. The abandoned/condemned tickets.' },
  { key: 'economy', title: 'Alderman of Economic Affairs', section: '## 2. Economy & jobs', zones: 'NE industry estate, Office park, Old industry south (upwind - conversion candidate)',
    focus: 'Unemployment is the top happiness penalty (-1.35). Industrial demand 58 but ~0 free industrial cells; office demand 0 with 14% office cells empty; commercial demand ~24. "Leveling Building" icons (~180) mean buildings wait for resource deliveries; "No Inputs" tickets. Specialized industry (natural resources via area tools) and the 9 unused hubs; unused outside rail/ship connections (cargo). New industry only downwind (NE) - consider buying tiles east of the NE estate.' },
  { key: 'utilities', title: 'Alderman of Public Works', section: '## 4. Utilities', zones: 'NE utilities, SE utilities, Water intake, south part of SE lakeside (treatment plants)',
    focus: 'Margins for growth to 150k: electricity, water (groundwater pumps on a thin deposit; clean aquifer north), sewage (3 treatment plants, 880k capacity), garbage (incinerators NE and SE now keep up; both show "Powerline Not Connected"), post (post sorting facility unlocked but not built; 103 post requests failing pathfinding), telecom. Keep polluters downwind and away from the water intake.' },
  { key: 'health_safety', title: 'Alderman of Health & Safety', section: '## 5. Health & deathcare and ## 6. Safety', zones: 'South quarter (health campus); services anywhere they fit outside protected zones',
    focus: 'No prison although its dev node is owned (prisoner transports fail, police stations have no free patrol cars). Hearse tickets, 2 full cemeteries. FireStation01 (disaster unit) unused; 0 emergency shelters (EmergencyShelterNode costs 1 dev point). Healthcare/police/fire coverage gaps (/coverage), crime hotspots.' },
  { key: 'education', title: 'Alderman of Education', section: '## 7. Education & research', zones: 'University quarter (civic), schools anywhere they fit',
    focus: '16 of 21 schools are full and coverage gaps exist (Education none ~2%). Building extensions (classrooms) are not available through the API yet (being built), so plan new elementary/high schools near under-served homes. MedicalUniversity01 still unplaced (496x144 m). Educated workforce for offices.' },
  { key: 'parks', title: 'Alderman of Recreation, Beauty & Environment', section: '## 8. Parks, leisure, tourism & beauty and ## 10. Environment', zones: 'Claude Park, Westpark (both PROTECTED), north part of SE lakeside (promenade site x -1150..-700, z -450..300)',
    focus: 'Make the city an aesthetic pearl: trees/grass on main roads (halves noise - noise hotspots near the highway at (-497,2692)), a lakeside promenade with WaterPark01 on the SE lakeside, beautify downtown, landmarks/signature buildings, tourism. Air pollution penalty -0.42 (/pollution): find the sources near homes.' },
  { key: 'mobility', title: 'Alderman of Transport', section: '## 9. Mobility', zones: 'roads everywhere; coordinate with zone owners for new roads inside their zones',
    focus: 'Traffic penalty -0.8, ~36 bottlenecks (hotspots in logs/desk/mobility.json). Build a real bus network (line 1 exists along x=-1909; design 3-5 more lines connecting homes, jobs and the university quarter), more bus depots if needed, road maintenance depots (maintenance backlog is mostly road wear; only one road depot), roundabouts/hierarchy fixes at the worst hotspots. Train (TrainNode 1 dev point) and cargo rail are strategic options.' },
  { key: 'renewal', title: 'Alderman of Urban Renewal (stadsvernieuwing)', section: '## 1. Urban planning & housing and ## 9. Mobility', zones: 'districts to write off and rebuild: SW medium quarter (x -3900..-2900, z -900..-150), Old industry south (x -3400..-1100, z -1400..-940); any other district you can justify (never the protected parks)',
    focus: 'The owner allows writing off whole districts and rebuilding them better. The city is a monotone grid: 94% Small Road, 0 curved segments. Pick the district where a rebuild gains most (liveability, traffic, pollution, land value) and design its new layout: road hierarchy (Medium/Large collectors, tree-lined boulevards), curved streets and crescents following terrain/shore, squares, parks, schools/services, mixed zoning with demand. Plan it in phases of ~500 m (demolish a phase and rebuild it in the same round; residential demand is high, so people come back). Use the curved-road/roundabout/edge-bulldoze API if /road and /bulldoze support it (check GET /nets and a dry run), otherwise approximate curves with short straight segments.' },
]

// per-round focus from the mayor overrides the defaults above
if (args.focus) for (const d of DEPTS) if (args.focus[d.key]) d.focus = args.focus[d.key]

const PROPOSALS = {
  type: 'object',
  properties: {
    department: { type: 'string' },
    assessment: { type: 'string', description: 'state of your domain in a few sentences, with numbers' },
    proposals: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id: { type: 'string', description: 'short slug, unique within your department' },
          title: { type: 'string' },
          kind: { type: 'string', enum: ['fix', 'growth', 'beauty', 'strategic'] },
          why: { type: 'string', description: 'evidence: tickets, KPIs, coverage, demand' },
          where: { type: 'string', description: 'coordinates / bbox / masterplan zone' },
          actions: { type: 'string', description: 'concrete steps a worker can execute: prefabs, coordinates, endpoints, order' },
          capex: { type: 'number', description: 'estimated construction cost in $ (buildings: /prefabs cost; tiles; roads)' },
          upkeepPerMonth: { type: 'number', description: 'estimated added monthly upkeep in $' },
          devPoints: { type: 'number', description: 'development points needed (0 if none)' },
          impact: { type: 'integer', minimum: 1, maximum: 5 },
          urgency: { type: 'integer', minimum: 1, maximum: 5 },
          risks: { type: 'string' },
          sites: {
            type: 'array', description: 'every piece of land this project builds on (lots, new roads, zoned areas) as axis-aligned boxes in metres; [] for non-spatial projects (policies, line changes, upgrades of existing buildings)',
            items: { type: 'object', properties: { x0: { type: 'number' }, x1: { type: 'number' }, z0: { type: 'number' }, z1: { type: 'number' }, what: { type: 'string' } }, required: ['x0', 'x1', 'z0', 'z1'] },
          },
        },
        required: ['id', 'title', 'kind', 'why', 'where', 'actions', 'capex', 'upkeepPerMonth', 'devPoints', 'impact', 'urgency', 'sites'],
      },
    },
  },
  required: ['department', 'assessment', 'proposals'],
}

const RESULTS = {
  type: 'object',
  properties: {
    department: { type: 'string' },
    results: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id: { type: 'string' },
          status: { type: 'string', enum: ['done', 'partial', 'failed', 'skipped'] },
          spent: { type: 'number' },
          summary: { type: 'string' },
        },
        required: ['id', 'status', 'spent', 'summary'],
      },
    },
    followUps: { type: 'array', items: { type: 'string' } },
    interfaceGaps: { type: 'array', items: { type: 'string' }, description: 'things you needed but the API could not do' },
  },
  required: ['department', 'results', 'followUps'],
}

phase('Plan')
log(`Round ${R}: envelope $${(FIN.envelope / 1e6).toFixed(1)}M, new upkeep cap $${(FIN.upkeepCap / 1e6).toFixed(1)}M/month, ${FIN.devPoints} dev points`)

const plans = (await parallel(DEPTS.map(d => () => agent(
  `You are the ${d.title} of Claude City's council (round ${R}). The owner's brief: the city must become "an aesthetic pearl where people and city live in synergy" and the council must think big, not in small chores.
Your zones (masterplan): ${d.zones}.
Focus for this round: ${d.focus}

Do this (READ-ONLY: GET requests and reading files only - no builds, no dry runs, no POST at all):
1. Read your handbook section in C:\\soft\\cityskyline\\docs\\council\\domains.md (${d.section}), the masterplan, the brief (logs/council/brief.json), the follow-ups addressed to you by earlier crews (logs/council/followups.json: key "${d.key}" plus items under other keys that mention your domain), the land already used (logs/council/sites.jsonl) and your tickets (logs/desk/${d.key === 'health_safety' ? 'health.json and logs/desk/safety.json' : d.key === 'parks' ? 'environment.json' : d.key === 'renewal' ? 'housing.json and logs/desk/mobility.json' : d.key + '.json'}).
2. Investigate with GETs (map, coverage, services, findspot/freeland to check that sites really exist). Be efficient (~25 tool calls).
3. Propose 2-5 projects, ranked, at least one of them big/strategic if it is justified. Each must be concrete enough for a worker to execute without guessing (prefab names from /prefabs, coordinates checked against /map or /findspot, order of steps). Estimate capex and monthly upkeep honestly. The whole council shares $${(FIN.envelope / 1e6).toFixed(1)}M and $${(FIN.upkeepCap / 1e6).toFixed(1)}M/month of new upkeep this round, and ${FIN.devPoints} development points.
${API}`,
  { label: `plan:${d.key}`, phase: 'Plan', schema: PROPOSALS },
)))).map((p, i) => p && { ...p, department: DEPTS[i].key }).filter(Boolean)

phase('Allocate')
const all = []
for (const p of plans) for (const x of p.proposals || []) all.push({ ...x, department: p.department })
const prio = x => 3 * x.urgency + 2 * x.impact + (x.kind === 'fix' ? 3 : 0) + (x.kind === 'strategic' ? 1 : 0) - Math.min(5, (x.capex || 0) / 5e6)
all.sort((a, b) => prio(b) - prio(a))
let cash = FIN.envelope, upkeep = FIN.upkeepCap, points = FIN.devPoints
const perDept = {}, approved = [], deferred = []
for (const x of all) {
  const n = perDept[x.department] || 0
  const big = (x.capex || 0) > 0.5 * FIN.envelope && x.impact < 4
  if (n < 3 && !big && (x.capex || 0) <= cash && (x.upkeepPerMonth || 0) <= upkeep && (x.devPoints || 0) <= points) {
    approved.push(x); perDept[x.department] = n + 1
    cash -= x.capex || 0; upkeep -= x.upkeepPerMonth || 0; points -= x.devPoints || 0
  } else deferred.push(x)
}
// site reservation: approved projects are ranked by priority; a lower-ranked project whose land overlaps a higher-ranked one
// (other department) must find another site - the mayor tells its crew which land is reserved
const overlap = (a, b) => Math.min(a.x1, b.x1) - Math.max(a.x0, b.x0) > 8 && Math.min(a.z1, b.z1) - Math.max(a.z0, b.z0) > 8
const norm = b => ({ x0: Math.min(b.x0, b.x1), x1: Math.max(b.x0, b.x1), z0: Math.min(b.z0, b.z1), z1: Math.max(b.z0, b.z1), what: b.what })
for (let i = 0; i < approved.length; i++) {
  const x = approved[i]
  x.reservedByOthers = []
  for (let j = 0; j < i; j++) {
    const y = approved[j]
    if (y.department === x.department) continue
    for (const a of (x.sites || []).map(norm)) for (const b of (y.sites || []).map(norm))
      if (overlap(a, b)) x.reservedByOthers.push({ project: y.title, department: y.department, box: b })
  }
  if (x.reservedByOthers.length) log(`  ! site conflict: [${x.department}] ${x.id} overlaps ${[...new Set(x.reservedByOthers.map(r => r.department + ':' + r.project.slice(0, 40)))].join('; ')}`)
}
log(`Approved ${approved.length}/${all.length} proposals; capex $${((FIN.envelope - cash) / 1e6).toFixed(1)}M, upkeep $${((FIN.upkeepCap - upkeep) / 1e6).toFixed(2)}M/month, ${FIN.devPoints - points} dev points`)
for (const x of approved) log(`  + [${x.department}] ${x.title} ($${((x.capex || 0) / 1e6).toFixed(2)}M)`)
for (const x of deferred) log(`  - deferred [${x.department}] ${x.title}`)

phase('Execute')
const order = [...new Set(approved.map(x => x.department))]
const executed = []
for (const dep of order) {
  const d = DEPTS.find(z => z.key === dep) || { title: dep, zones: '', section: '' }
  const mine = approved.filter(x => x.department === dep)
  const r = await agent(
    `You are the worker crew of the ${d.title} of Claude City (council round ${R}). The mayor approved these projects for you (JSON):
${JSON.stringify(mine, null, 1)}

Execute them now in the live game:
- Write one Python script per project (or one for all) at C:\\soft\\cityskyline\\tools\\council\\r${R}_${dep}_<project>.py, run it with: cd C:/soft/cityskyline/tools && python -I council/<file>.py (Bash timeout 600000). Keep every \`with paused("${dep}"):\` batch short; another crew may be waiting for the build lock. Print results with short(...).
- Check every site again right before building (dry runs, /findspot) - the city changed since planning. Adapt sensibly when a step fails (alternative site nearby, smaller variant) but stay within the approved capex (+10%) and the project's intent. Respect masterplan zones (no building in other departments' protected zones).
- Verify afterwards (GET /buildings, /lines, /coverage, /services...) that it works (e.g. roadConnected true; line complete; coverage improved).
- Site ledger: before building read C:\soft\cityskyline\logs\council\sites.jsonl (land used by earlier crews and rounds) and each project's "reservedByOthers" (land of higher-ranked projects of other departments this round - do NOT build there, pick another site). After building, append one JSON line per site you used: {"round": ${R}, "department": "${dep}", "project": "<id>", "x0":..,"x1":..,"z0":..,"z1":.., "what": "<prefab or road/zone>", "ids": [..]}.
- Lessons from round 1: the game silently demolishes zoned buildings under a new lot or road; the API now reports them (wouldDemolish) and refuses by default - read wouldDemolish in every dry run. Edge ids change when neighbouring roads/buildings are built: prefer box/polyline selection or re-read ids right before each call; page /nets with offset/limit.
- Do not touch anything not related to your approved projects.
Report per project: status, money spent, what was built where; follow-ups for the next round; and interface gaps (things you needed but the API could not do).
${API}`,
    { label: `execute:${dep}`, phase: 'Execute', schema: RESULTS },
  )
  if (r) executed.push(r)
  log(`${dep}: ${(r?.results || []).map(z => `${z.id}=${z.status}`).join(', ')}`)
}

return { round: R, finance: FIN, assessments: plans.map(p => ({ department: p.department, assessment: p.assessment })), approved, deferred: deferred.map(x => ({ department: x.department, title: x.title, capex: x.capex, impact: x.impact, urgency: x.urgency })), executed }
