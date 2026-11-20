export const meta = {
  name: 'claude-city-advisory-board',
  description: 'Claude City advisory board: 9 aldermen assess their domain read-only and spread goal-level work items over the districts; the mayor script ranks them within the budget and assigns each to a builder. Produces the next worklist; does not build.',
  whenToUse: 'When the builders\' current worklist batch is half done (tools/worklist.py status says "refill"); write logs/council/brief.json and logs/council/opportunities.json first',
  phases: [
    { title: 'Assess', detail: 'each alderman assesses its domain and proposes district-level work items (read-only)' },
    { title: 'Allocate', detail: 'mayor script ranks items within the envelope and assigns builders from the district register' },
  ],
}

const B = args.batch
const FIN = args.finance
const ROOT = 'C:\\soft\\cityskyline'

const SOURCES = `
## What to read (all read-only)
- ${ROOT}\\docs\\council\\builder_handbook.md - how builders work, quality rules, and the full API list (GET endpoints you may use).
- ${ROOT}\\docs\\council\\districts.json - the district register: owner builder, character, phase, bbox. Spread your items over these districts.
- ${ROOT}\\docs\\council\\masterplan.json (vision, rules, zones) and your section of ${ROOT}\\docs\\council\\domains.md.
- ${ROOT}\\logs\\council\\brief.json (finance, happiness, tickets, services, coverage) and ${ROOT}\\logs\\council\\opportunities.json (unused unlocks, transport modes, outside connections, dev points, zoning mix).
- ${ROOT}\\logs\\council\\worklist.json - items still open or claimed by builders: do NOT propose duplicates; build on them.
- ${ROOT}\\logs\\council\\followups.json, ${ROOT}\\logs\\council\\sites.jsonl (land already used), ${ROOT}\\logs\\desk\\<domain>.json (complaint tickets with hotspots).
- Live game via GET only: cd C:/soft/cityskyline/tools && python -I cc.py GET "/districts?detail=1" (per district residents, happiness, workplaces, buildings by zone/service), /city, /coverage, /map, /findspot, /freeland, /tiles, /resources, /areainfo ...
NEVER POST anything, never run build scripts, no dry runs. Coordinates: x east, z north, metres. Wind SW -> NE.`

const DEPTS = [
  { key: 'housing', title: 'Alderman of Planning & Housing', section: '## 1 and ## 11',
    focus: 'Homes for the demand (residential demand per density in /city), density mix (62% low-density now: new cores near transit should be medium/high and mixed), building levels, land value, abandoned buildings. Which districts take the next 30k residents, with which street layout and character?' },
  { key: 'economy', title: 'Alderman of Economic Affairs', section: '## 2',
    focus: 'Jobs per education level (/jobs; 8.7k highly educated underemployed, office demand low), industrial/commercial/office demand and free cells, specialized industry on natural resources (/resources, /extractors), deliveries ("Leveling Building", "No Inputs"), trade.' },
  { key: 'utilities', title: 'Alderman of Public Works', section: '## 4',
    focus: 'Margins for the next 50k residents: electricity, water, sewage, garbage, post, telecom. Clean energy (hydro now unlocked), polluters downwind, keep the water intake clean.' },
  { key: 'health_safety', title: 'Alderman of Health & Safety', section: '## 5 and ## 6',
    focus: 'Deathcare capacity (deaths per day vs processing, /deathcare), healthcare (disease control centre unlocked), police/fire coverage gaps per district, crime hotspots, prison capacity, emergency shelters.' },
  { key: 'education', title: 'Alderman of Education', section: '## 7',
    focus: 'Seats vs eligible per level (/education), coverage gaps per district, extensions, university/research; schools belong in every residential district.' },
  { key: 'parks', title: 'Alderman of Recreation, Beauty & Environment', section: '## 8 and ## 10',
    focus: 'Make every district beautiful: park coverage per district, trees on arterials, noise and air pollution near homes, promenades, landmarks/signature buildings, tourism (hotels are 100% full - hotel capacity caps tourism).' },
  { key: 'mobility', title: 'Alderman of Transport', section: '## 9',
    focus: 'Road hierarchy (ring + radial arterials, roundabouts at the worst junctions, /stuck, traffic hotspots) and PUBLIC TRANSPORT BEYOND BUSES: passenger rail (stations unlocked, 0 built), trams (depot unlocked, 0 built), subway (dev node bought), taxis, bus stations. Which districts get which mode, along which corridors?' },
  { key: 'renewal', title: 'Alderman of Urban Renewal (stadsvernieuwing)', section: '## 1 and ## 9',
    focus: 'Districts in phase "renewal" in the register (Southwest Quarter, Old Southern Works) and any monotone grid worth rebuilding: the next ~500 m phase with its new layout (hierarchy, curved streets, local centre, parks), what it demolishes (households/jobs) and what it gives back.' },
  { key: 'growth', title: 'Alderman of Growth, Land & Trade (gebiedsontwikkeling)', section: '## 2, ## 3 and ## 9',
    focus: 'New land and new kinds of city: tile purchases (/tiles: 52 adjacent candidates, 32 of them gaps inside our area), new districts with a character and a builder, trade and the unused outside connections (2 ship, 2 airplane - harbour and airport dev nodes now bought, cargo terminals), new unlocks and signature buildings (logs/council/opportunities.json), remaining dev points (13: international airport needs 8). Propose new districts as district "NEW:<name>" with newDistrict filled in.' },
]
if (args.focus) for (const d of DEPTS) if (args.focus[d.key]) d.focus = args.focus[d.key]

const ITEMS = {
  type: 'object',
  properties: {
    department: { type: 'string' },
    assessment: { type: 'string', description: 'state of your domain in a few sentences, with numbers, per district where it matters' },
    items: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id: { type: 'string', description: 'short slug, unique within your department' },
          title: { type: 'string' },
          kind: { type: 'string', enum: ['fix', 'growth', 'beauty', 'strategic', 'renewal', 'acquisition', 'transport'] },
          goal: { type: 'string', description: 'the measurable outcome the builder must reach (e.g. "+100 bodies/day deathcare capacity south of z -500", "tram line Downtown-Office Park running")' },
          why: { type: 'string', description: 'evidence: numbers, tickets, coverage, demand' },
          district: { type: 'string', description: 'a district name from districts.json, "NEW:<name>" for a new district, or "citywide" (then give a location in where)' },
          where: { type: 'string', description: 'location hint: bbox or point; the builder chooses the exact site at build time' },
          steps: { type: 'string', description: 'outline a builder can follow: prefabs (names from /prefabs), order, layout intent (curves, hierarchy, trees); not exact coordinates unless they matter' },
          capex: { type: 'number' },
          upkeepPerMonth: { type: 'number' },
          impact: { type: 'integer', minimum: 1, maximum: 5 },
          urgency: { type: 'integer', minimum: 1, maximum: 5 },
          x: { type: 'number', description: 'representative x of the location (for assigning a builder)' },
          z: { type: 'number', description: 'representative z' },
          newDistrict: {
            type: 'object', description: 'only for district NEW:<name>',
            properties: { name: { type: 'string' }, bbox: { type: 'array', items: { type: 'number' } }, character: { type: 'string' }, tiles: { type: 'array', items: { type: 'array', items: { type: 'number' } }, description: 'tile centres [x,z] to buy' } },
            required: ['name', 'bbox', 'character'],
          },
        },
        required: ['id', 'title', 'kind', 'goal', 'why', 'district', 'where', 'steps', 'capex', 'upkeepPerMonth', 'impact', 'urgency', 'x', 'z'],
      },
    },
    noteToConductor: { type: 'string', description: 'policies, taxes, budgets, dev points or anything only the conductor may do' },
  },
  required: ['department', 'assessment', 'items'],
}

phase('Assess')
log(`Batch ${B}: envelope $${(FIN.envelope / 1e6).toFixed(1)}M, new upkeep cap $${(FIN.upkeepCap / 1e6).toFixed(1)}M/month`)

const plans = (await parallel(DEPTS.map(d => () => agent(
  `You are the ${d.title} on Claude City's advisory board (worklist batch ${B}). The owner's brief: the city must become "an aesthetic pearl where people and city live in synergy", think big, and keep renewing - new transport modes, trade, new districts - not the same round of chores.
You do not build. Builders (one per group of districts, plus a troubleshooter) work continuously from a shared worklist; your items go on it. Give GOALS with an outline, not exact coordinates: the city changes while builders work, they pick exact sites at build time.
Spread city-wide needs over the districts in the register: say for each item which district takes it, matching the district's character and free land. Use "NEW:<name>" for a district that does not exist yet (with newDistrict) and "citywide" only for things without a district (give x/z anyway).
Focus: ${d.focus}
Your handbook section in domains.md: ${d.section}.
${SOURCES}

Be efficient (~25 tool calls). Propose 4-8 items, ranked, at least two of them big (a new transport mode, a new district phase, a landmark, a renewal phase). Estimate capex and monthly upkeep honestly (GET /prefabs?category=services shows cost and upkeep). The whole board shares $${(FIN.envelope / 1e6).toFixed(1)}M and $${(FIN.upkeepCap / 1e6).toFixed(1)}M/month of new upkeep in this batch.`,
  { label: `assess:${d.key}`, phase: 'Assess', schema: ITEMS },
)))).map((p, i) => p && { ...p, department: DEPTS[i].key }).filter(Boolean)

phase('Allocate')
const REG = args.register // [{name, owner, bbox}]
const inBox = (x, z, b) => b && x >= b[0] && x <= b[2] && z >= b[1] && z <= b[3]
const ownerFor = it => {
  if (it.district && it.district.startsWith('NEW:')) return null // conductor registers the district first
  const byName = REG.find(r => r.name.toLowerCase() === (it.district || '').toLowerCase())
  if (byName) return byName.owner
  const byLoc = REG.find(r => inBox(it.x, it.z, r.bbox))
  if (byLoc) return byLoc.owner
  return 'troubleshooter'
}
const all = []
for (const p of plans) for (const x of p.items || []) all.push({ ...x, department: p.department })
const prio = x => 3 * x.urgency + 2 * x.impact + (x.kind === 'fix' ? 3 : 0) + (['strategic', 'transport', 'acquisition'].includes(x.kind) ? 1 : 0) - Math.min(4, (x.capex || 0) / 10e6)
all.sort((a, b) => prio(b) - prio(a))
let cash = FIN.envelope, upkeep = FIN.upkeepCap
const perDept = {}, items = [], deferred = []
for (const x of all) {
  const n = perDept[x.department] || 0
  if (n < 6 && (x.capex || 0) <= cash && (x.upkeepPerMonth || 0) <= upkeep) {
    perDept[x.department] = n + 1
    cash -= x.capex || 0; upkeep -= x.upkeepPerMonth || 0
    items.push({
      id: `b${B}-${x.department}-${x.id}`.slice(0, 60), title: x.title, kind: x.kind, goal: x.goal, why: x.why,
      district: x.district, where: x.where, steps: x.steps, budget: x.capex, upkeep: x.upkeepPerMonth,
      priority: Math.round(prio(x) * 10) / 10, department: x.department, owner: ownerFor(x), x: x.x, z: x.z,
      ...(x.newDistrict ? { newDistrict: x.newDistrict } : {}),
    })
  } else deferred.push({ department: x.department, title: x.title, capex: x.capex, why: (x.capex || 0) > cash ? 'budget' : 'department quota' })
}
log(`Worklist batch ${B}: ${items.length}/${all.length} items, capex $${((FIN.envelope - cash) / 1e6).toFixed(1)}M, upkeep $${((FIN.upkeepCap - upkeep) / 1e6).toFixed(2)}M/month`)
const byOwner = {}
for (const x of items) byOwner[x.owner || 'conductor (new district)'] = (byOwner[x.owner || 'conductor (new district)'] || 0) + 1
log(`  per builder: ${Object.entries(byOwner).map(([k, v]) => `${k}=${v}`).join(', ')}`)

return {
  batch: B, finance: FIN, items, deferred,
  assessments: plans.map(p => ({ department: p.department, assessment: p.assessment, noteToConductor: p.noteToConductor || '' })),
}
