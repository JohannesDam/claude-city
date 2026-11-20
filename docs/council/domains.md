# Claude City Council — domain handbook

Cities: Skylines II, PC/Game Pass build **1.6.4**. Written 2026-11-20 against the running city (game date 2030-01-10, milestone 14 of 20, ~95,500 people).

This is the knowledge base for the city council: a **mayor** who budgets from cash and cash flow, and one **alderman** (department head) per domain with a few workers. Each domain section lists the game mechanics that matter, the metrics and where to read them, the levers a player has, the prefabs in this build with their lock state, rules of thumb, and the API status of every lever. The missing and partial levers are collected in [`gaps.json`](gaps.json) (51 entries, ranked by impact ÷ effort with S = 1, M = 2, L = 3; teammate work in progress listed last). The spatial plan (zones, wind, protected areas) lives in [`masterplan.json`](masterplan.json); this handbook does not repeat it.

**Sources and how much to trust them**
- **IL** — read from `Game.dll` with `tools/ildump/d.sh`. Type, field and method names are exact; formulas are transcribed from IL. Parameter *values* live in prefabs and are not in the DLL; where a value matters it says "prefab value" or "live" (when an endpoint already prints it).
- **Glossary / locale** — the game's own English help text (`Cities2_Data/Content/Game/Locale.cok` → `en-US.loc`: `Glossary.SECTION_CONTENT[...]`, `Policy.DESCRIPTION[...]`, `Notifications.DESCRIPTION[...]`). Quoted in "…".
- **Live** — read-only GETs on the bridge. The city was being played while this was written: numbers are from 19:20 game time unless marked (05:33 / 11:12).
- **[GK]** — general Cities: Skylines II knowledge, not verified in this build. Treat as a hypothesis.

**API status legend:** ✅ endpoint exists · 🟡 partly (read-only, indirect or untested) · ❌ missing · 🚧 teammate work in progress (merged into `Api.cs` but still being built): road upgrades `/roadupgrade` + `/roadupgrades`; transit `/busstop`, `/busline`, `/lines`, `/stops`, `/transitprefabs`; placement `/findspot`, `/freeland`.

## Contents
1. [Urban planning & housing](#1-urban-planning--housing--alderman-of-planning)
2. [Economy & jobs](#2-economy--jobs--alderman-of-economic-affairs)
3. [Finance (the mayor)](#3-finance--the-mayor-with-the-treasurer)
4. [Utilities](#4-utilities--alderman-of-public-works)
5. [Health & deathcare](#5-health--deathcare--alderman-of-health)
6. [Safety](#6-safety--alderman-of-safety)
7. [Education & research](#7-education--research--alderman-of-education)
8. [Parks, leisure, tourism & beauty](#8-parks-leisure-tourism--beauty--alderman-of-recreation--tourism)
9. [Mobility](#9-mobility--alderman-of-transport)
10. [Environment](#10-environment--alderman-of-environment)
11. [Citizens & wellbeing](#11-citizens--wellbeing--alderman-of-social-affairs)
12. [Progression](#12-progression--alderman-of-development-milestones-development-tree-unlocks)
13. [City policies](#13-city-policies--policy-clerk-reports-to-the-mayor)
- [Appendix A — endpoint inventory](#appendix-a--endpoint-inventory)
- [Appendix B — quick wins found while writing this](#appendix-b--quick-wins-found-while-writing-this)

## The council

| # | Domain | Alderman | Workers | Headline metric |
|---|---|---|---|---|
| — | Whole city | **Mayor** | treasurer | cash, monthly balance, happiness |
| 1 | Urban planning & housing | Planning | zoning officer, housing officer, land registrar | residential demand, free zoned cells, Leveling Building icons |
| 2 | Economy & jobs | Economic Affairs | commerce, industry, labour officers | commercial/industrial/office demand, Unemployment, No Inputs/No Customers |
| 3 | Finance | Mayor + Treasurer | tax officer, budget officer | balance, tax income, upkeep, envelope |
| 4 | Utilities | Public Works | power, water & sewage, waste, telecom & post | supply ≥ demand on every network; Garbage factor |
| 5 | Health & deathcare | Health | clinics/hospitals, deathcare | health, sick and dead awaiting transport |
| 6 | Safety | Safety | police chief, fire chief, disaster officer | crime, prisoner transport, fires, shelters |
| 7 | Education & research | Education | schools officer, research officer | seats vs students, education fit of jobs |
| 8 | Parks, leisure, tourism & beauty | Recreation & Tourism | parks keeper, tourism officer, landscaper | park coverage, Entertainment/Leisure, attractiveness |
| 9 | Mobility | Transport | road engineer, transit planner, freight & connections | bottlenecks, TrafficPenalty, passengers |
| 10 | Environment | Environment | pollution officer, water-quality officer | pollution factors, polluted groundwater |
| 11 | Citizens & wellbeing | Social Affairs | — (briefs the council) | happiness, health, move-away reasons |
| 12 | Progression | Development | XP planner, unlock strategist | XP to next milestone, unspent dev points, permits |
| 13 | City policies | Policy clerk | — | active policies and their cost |

## City snapshot (live, game date 2030-01-10 19:20)

| | |
|---|---|
| Population | 95,549 (100,897 incl. move-ins) |
| Happiness / health | 82 / 68 (78 / 62 at 05:33) |
| Money | 80.0 M |
| Monthly income / expenses / balance | 22.24 M / −12.53 M / **+9.71 M** (tax income 20.78 M) |
| Taxes | overall 10 %; residential 8, commercial 10, industrial 10, office 10 |
| Service budgets | Garbage 150 %, Health & Deathcare 125 %, all others 100 % |
| Demand | residential low 100, medium 26, high 100; household 81; commercial 34; industrial 58; office 0; storage 0 |
| Electricity | production 2.23 M, consumption 1.36 M (all fulfilled) |
| Water / sewage | water 638 k capacity vs 406 k; sewage 900 k vs 406 k (at 05:33 sewage was short: 300 k vs 352 k, 160 *Backed up sewer* icons) |
| Milestone | 14 achieved; 87 % to M15; **24 development points unspent** |
| Tiles | 54 of 529 owned; 125 permits available |
| Problems (`/problems`) | Leveling Building 186, Traffic Bottleneck 31, Garbage 25, Noise 18, No Inputs 16, No Customers 13, Hearse 13, Traffic Accident 7, Burned Down 5, Powerline Not Connected 3, Facility Full 2 |
| Worst happiness factors | Unemployment −1.42, TrafficPenalty −0.90, Garbage −0.74 (−2.36 at 05:33), AirPollution −0.41 |
| Best happiness factors | Mail +7.49, Consumption +6.40, Entertainment +6.00 (capped), Healthcare +4.63, Education +3.07 |

---

## 1. Urban planning & housing — Alderman of Planning

**Workers:** zoning officer (what to zone where), housing officer (vacancy, rent, levels, homelessness), land registrar (map tiles, districts).
**Headline metrics:** residential building demand per density, free zoned cells, Leveling Building icons, homelessness.

### Mechanics that matter
- **Zone types in this build** (all unlocked, `/prefabs?category=zones`): Residential Low, Low Waterfront, Medium, Medium Row, High, Mixed (shop on the ground floor), Mixed Old Town — each in an EU and NA theme — plus `Residential LowRent`; Commercial Low/High (EU/NA); Office Low/High; `Industrial Manufacturing` (one industrial zone, includes warehouses). Theme only changes looks. **Specialized industry is not a zone** (see §2).
  - LowRent: "large apartment buildings with small, affordable apartments. Lower rents provide lower tax income" (locale). `ZonePropertiesData.m_IgnoreLandValue` drops the land-value part of rent (inferred user: LowRent).
  - Mixed: rent is split between households and the shop (`EconomyParameterData.m_MixedBuildingCompanyRentPercentage`).
  - Density per zone: `PropertyUtils.GetZoneDensity` → Low / Medium / High; row and mixed count toward medium/high demand (exact mapping [GK]).
  - Rezoning or dezoning under a building condemns it and it is demolished over time (glossary *RemovingandRezoning*).
- **Residential demand (IL `ResidentialDemandSystem/UpdateResidentialDemandJob`).**
  - Household demand (0–200) = min(200, popBonus + Happiness + Homelessness(−) + Taxes + Unemployment + Students + max(FreeSimpleJobs, FreeComplexJobs)), with `popBonus = 20 − smoothstep(0, 20, population/20000)`, `Happiness = m_HappinessEffect·(max(m_MinimumHappiness, avgHappiness) − m_NeutralHappiness)`, `Taxes = TaxEffect·Σ_{5 education levels}(10 − residentialRate(level))/5`, free-workplace terms clamped (simple 0..40, complex 0..20), `Students = StudentEffect·clamp(studyPositions/200, 0, 5)`, `Unemployment = NeutralUnemployment − unemploymentRate`. Each term has asymmetric positive/negative weights (`GetFactorValue`).
  - Building demand per density: `EmptyBuildings(d) = 100·(target·total − free)/(target·total)` with target `FreeResidentialProportion[d]`; LOW = Happiness + Taxes + FreeWorkplaces/2 + Unemployment + EmptyBuildings; MEDIUM adds Students and full FreeWorkplaces; HIGH also adds Homelessness(+). If EmptyBuildings < 0 the density sum is 0. `buildingDemand[d] = clamp(household/2 + EmptyBuildings + sum, 0, 100)`.
  - So **vacancy dominates**: as soon as a density has fewer empty homes than its target, its demand jumps; while unbuilt cells remain, it sits near 0. Only high density gains from homelessness.
  - `DemandFactor` enum (signed factors, readable via `GetLow/Medium/HighDensityDemandFactors`): StorageLevels, UneducatedWorkforce, EducatedWorkforce, CompanyWealth, LocalDemand, Unemployment, FreeWorkplaces, Happiness, Homelessness, TouristDemand, LocalInputs, Taxes, Students, EmptyBuildings, EmptyZones, PoorZoneLocation, PetrolLocalDemand, Warehouses, BuildingDemand.
- **Land value** (`LandValueSystem`, per road edge `Game.Net.LandValue` and per cell `LandValueCell`): baseline + bonuses from health, education, police and telecom coverage, attractiveness, commercial services, bus and tram/subway access (capped by `m_CommonFactorMaxBonus`), minus ground/air/noise pollution (`LandValueParameterData`). Glossary: services raise land value "only if citizens find the services useful"; wealthy households and profitable companies seek high land value, poorer ones low land value.
- **Rent** (IL `PropertyUtils.GetRentPricePerRenter`): `(landValue·LandValueModifier[zone] + RentBase[zone]·level)·lotSize·spaceMultiplier / properties` (no land-value term when `m_IgnoreLandValue`). `RentAdjustSystem` sets `BuildingFlags.HighRentWarning` and the *High Rent* icon when renters cannot afford it.
- **Condition, levels, abandonment** (`BuildingUpkeepSystem`): renters pay rent → the building pays upkeep → leftover raises `BuildingCondition.m_Condition` (else it falls). Level-up when condition ≥ `BuildingUtils.GetLevelingCost`: residential = properties·40·2^(2·level), commercial/industrial = properties·160·2^(2·level) (×4 for buildings that store goods), × city modifier `BuildingLevelingCost`. **In 1.6.4 a level-up also needs a delivered resource** (`ZoneLevelUpResourceData {m_LevelUpResource, m_Level}`, `BuildingUpkeepSystem/LevelUpMaterial`) — that is the *Leveling Building* icon: "The building is waiting for the resource delivery to level up". Which resource per level is a prefab value. Below −`GetAbandonCost` the building levels down or is abandoned (`Game.Buildings.Abandoned`, collapsed later by `DestroyAbandonedSystem`). Higher levels use fewer resources, produce less garbage/pollution and pay more rent/tax (glossary *BuildingLevels*); lower taxes leave more money for leveling.
- **Historical buildings**: `BuildingFlags.Historical` — never level up/down, never abandoned (UI toggle).
- **Zoning suitability** (`ZoneEvaluationUtils.GetScore`, `ZoningEvaluationFactor`: Workplaces, Services, Competitors, Customers, OutsideConnections, Inputs, Pollution, LandValue, Employees) using the per-edge `Game.Net.ResourceAvailability` buffer. This is the "zoning factors" tooltip in the game.
- **Districts**: `Game.Areas.District` area entity with a `Policy` buffer and `DistrictModifier` buffer; buildings and edges get `CurrentDistrict`. "Districts let you assign policies and city services to only be in effect inside the district" (glossary). Service buildings can be restricted to districts (`ServiceDistrict` buffer).
- **Map tiles**: money **and** expansion permits; each purchased tile adds upkeep that grows with tile price and tiles owned (`EconomyParameterData.m_MapTileUpkeepCostMultiplier`, `MapTilePurchaseSystem.CalculateOwnedTilesUpkeep`, expense `MapTileUpkeep`). `MapTilePurchaseSystem.GetFeatureAmount(MapFeature)` reports FertileLand, Forest, Oil, Ore, SurfaceWater, GroundWater, Fish, BuildableLand, Area for a selection.
- **Signature buildings**: unlock through `UnlockRequirement` entries evaluated by requirement systems — `ZoneBuiltRequirementData` (zone/theme/type, minimum squares, count, level), `CitizenRequirementData` (population, happiness), `ProcessingRequirementData` (produced amount), `ObjectBuiltRequirementData`, `TransportRequirementData`, `PrefabUnlockedRequirement`; progress in `UnlockRequirementData.m_Progress`.

### Key metrics & where to read them
| Metric | Read via | Now (19:20) |
|---|---|---|
| Building demand per density, household demand | `/city.demand` ✅ | low 100, medium 26, high 100, household 81 |
| Demand factor breakdown | `ResidentialDemandSystem.Get*DensityDemandFactors` ❌ | — |
| Zoned / occupied / free cells per zone | `/zonestats` ✅ | Res Low 72.2k (0.8 % free), Res Medium 14.3k (0.2 %), Res High 1.6k (0.2 %), Mixed 973 (0 %), Row 441 (0 %) |
| Buildings per zone, abandoned, no road | `/buildings` ✅ | 7,352 buildings, 1 abandoned (05:33) |
| Leveling / high rent / level-up icons | `/problems` ✅ | Leveling Building 186 (214 at 05:33) |
| Land value | `LandValueSystem` ❌ | — |
| Building levels, condition, rent stress | `SpawnableBuildingData.m_Level`, `BuildingCondition`, `HighRentWarning` ❌ | — |
| Homeless households | `CountHouseholdDataSystem`, `StatisticType.HomelessCount` ❌; Homelessness happiness ✅ | factor 0 |
| Tiles and permits | `/tiles` ✅ | 54 owned, 125 permits |
| Signature unlock progress | `UnlockRequirementData.m_Progress` ❌ | — |

### Levers
| Lever | API |
|---|---|
| Zone any type/theme by rectangle or along road edges (depth 1–6, side), dezone | ✅ `/zone` |
| Choose where to zone (suitability) | ❌ (`/zonescore` gap) |
| Buy map tiles | ✅ `/buytile` |
| Tile natural-resource amounts before buying | ❌ |
| Draw districts, assign district policies, restrict services to a district | ❌ |
| Mark building historical | ❌ |
| Bulldoze buildings (not roads) | ✅ `/bulldoze` |
| Road Zones tool (remove/re-add a zoning grid) | ❌ (it is a road-service prefab `RoadZones`; check `/roadupgrade` 🚧) |
| Place signature buildings | 🟡 `/building` (category exists, placement untested) |
| Residential tax (demand + happiness) | ✅ `/taxes` zone level; ❌ per education level |

### Rules of thumb
- Zone a density only when its demand is > 0 **and** its free cells are ~0; empty cells hold that density's demand down (IL).
- Medium demand came back (26) once medium free cells fell to 0.2 % — now zone medium/row/mixed again, preferably near transit and services (land value).
- High density is where homeless households go; LowRent keeps rent down where land value is high.
- Leveling needs a delivered resource: a large *Leveling Building* count means the supply chain or truck access for that resource is the bottleneck, not money. Check *No Inputs* and traffic near those blocks.
- Keep housing away from industry, landfills, highways and airports (noise is wellbeing-only, air/ground are health-only; §11).
- Buy tiles with resources (fertile land, forest, ore, oil, groundwater) before industry needs them; check permits (`/tiles`).

### Live diagnosis
- Residential zones are almost full everywhere (0–0.8 % free); demand low 100 and high 100, medium 26. **Commercial High, Office High, Residential Mixed Old Town, Low Rent and Waterfront have never been zoned**, which also keeps 51 signature buildings locked (their requirements are mostly zone-built counts).
- 186 buildings are stuck waiting for level-up material; 16 *No Inputs* companies — a supply problem, see §2.

---

## 2. Economy & jobs — Alderman of Economic Affairs

**Workers:** commerce officer (shops, hotels, fuel), industry officer (manufacturing, warehouses, specialized industry), labour officer (jobs vs education).
**Headline metrics:** commercial/industrial/office demand, Unemployment factor, *No Customers* / *No Inputs* icons.

### Mechanics that matter
- **Resources** (`Game.Economy.Resource`, 41): raw (extractor) Grain, Vegetables, Wood, Oil, Ore, Coal, Stone, Livestock, Cotton, Fish; office (immaterial) Software, Telecom, Financial, Media; commercial (sold to people) ConvenienceFood, Food, Meals, Paper, Furniture, Vehicles, Lodging, Petrochemicals, Plastics, Electronics, Chemicals, Pharmaceuticals, Beverages, Textiles, Entertainment, Recreation; processed in industry Timber, Metals, Steel, Minerals, Concrete, Machinery, …; plus mail, garbage, money. Chain: raw (specialized industry) → processed (industrial zone) → immaterial (offices) (glossary *Resources*).
- **Companies** (`Game.Companies`): `CompanyData`, `Profitability`, `WorkProvider {m_MaxWorkers}`, `Employee` buffer, `ServiceAvailable`, `ResourceBuyer/Seller/Exporter`, `TradeCost {resource, buy, sell}`, `LodgingProvider`. Recipe `IndustrialProcessData {m_Input1, m_Input2, m_Output, m_WorkPerUnit, m_MaxWorkersPerCell}`. Profit `EconomyUtils.GetCompanyProfitPerDay`; wages `EconomyParameterData.m_Wage0..4` by `WorkplaceComplexity` (Manual, Simple, Complex, Hitech). Company efficiency is a product of `EfficiencyFactor`s (electricity/water fees, missing or sick employees, utilities, garbage, mail, telecom, materials, city modifiers).
- **Worker output depends on happiness** (IL `EconomyUtils.GetWorkerWorkforce`): `((level==0 ? 1 : 2) + 2.5·level) × (0.75 + happiness/200)` → happiness 0 = 75 %, 50 = 100 %, 100 = 125 %.
- **Commercial demand** (`CommercialDemandSystem`): per commercial resource, household + tourist consumption vs what shops can sell; tax effect `−0.05·(rate(resource) − 10)·TaxEffect`; Lodging compares tourists × `m_HotelRoomPercentRequirement` with lodging (TouristDemand factor); Petrochemicals = fuel stations (PetrolLocalDemand).
- **Industrial & office demand** (`IndustrialDemandSystem`): per resource `companyDemand = 50·max(0, baseDemand·resourceDemandRatio) + taxEffect + workforce terms`, tax effect `100·(TaxEffect·−0.05·(rate − 10) + offset)`; workforce terms from employable minus free workplaces for education 0–1 (−10..+15) and 2–4 (−10..+10). Building demand per resource = 50 if companies want in and there is no free property. City modifiers `OfficeSoftwareDemand` and `IndustrialElectronicsDemand` (Server Farm / LHC) raise it. Storage demand `ceil((20·x)^0.75)`.
- **Workplaces & education** (`Game.Companies.Workplaces`, 5 levels Uneducated…HighlyEducated; simple = 0–1, complex = 2–4): "a citizen can work any job at or below their education; prefers matching level, closer, fewer employees" (glossary *WorkplaceAvailability*). Commuters from outside fill jobs (`DemandParameterData.m_CommuterWorkerRatioLimit`). Unemployed citizens accumulate the Unemployment wellbeing penalty (§11). Benefits: `m_UnemploymentBenefit`, `m_Pension`, `m_FamilyAllowance`.
- **Specialized industry** (not a zone): place an **area hub** (the `… Area Placeholder` prefabs: Agriculture Cotton/Grain/Livestock/Vegetable, Forestry, Oil, Ore Coal/Ore/Stone; `PlaceholderBuildingData` swaps it for a real extractor building), then draw the production area polygon inside the hub's radius (`LotData.m_MaxRadius`), editable with the Area Tool (glossary *IndustryHubandArea*, *AreaTool*). `ExtractorAreaData {m_MapFeature, m_RequireNaturalResource, …}`, area `Game.Areas.Extractor`. Resources: `NaturalResourceSystem` cells `{m_Fertility, m_Ore, m_Oil, m_Fish}` (base/used, max 10,000); fertility and fish regenerate, ore and oil deplete. Livestock and **stone need no natural resource**. Geological Research Center → `OreResourceAmount`/`OilResourceAmount`. "Specialized industries using local resources also cause more related industrial manufacturing" (glossary).
- **Trade**: companies buy from the cheapest source — local sellers, warehouses or outside connections (`TradeSystem.GetBestTradePriceAmongTypes`, `TradeCost`); outside costs per mode (`OutsideTradeParameterData` Road/Train/Air/Ship weight and distance multipliers); city modifiers `ImportCost`, `ExportCost`. Cargo rail/ship/air "lowers the cost of shipping goods ... more businesses that are profitable" (glossary). Production stats: `CityProductionStatisticSystem.GetConsumptionProductions/GetProductionChains`, `CountCompanyDataSystem.GetProduction`.
- **Tourism economy**: hotels (Lodging) and tourist spending (`m_TouristConsumptionMultiplier`); details in §8.

### Key metrics & where to read them
| Metric | Read via | Now (19:20) |
|---|---|---|
| Commercial/industrial/office/storage demand | `/city.demand` ✅ | commercial 34, industrial 58, office 0, storage 0 |
| Factor and per-resource demand | `CommercialDemandSystem.GetDemandFactors/GetResourceDemands`, `IndustrialDemandSystem.Get*DemandFactors` ❌ | — |
| Free cells per zone | `/zonestats` ✅ | Office Low 14.1 % free (1,794 cells); Industrial 0.2 %; Commercial Low 0 % |
| Company problems | `/problems` ✅ | No Inputs 16, No Customers 13 |
| Unemployment | `/happiness` (Unemployment) ✅; counts per education ❌ (`CountWorkplacesSystem`, `CountHouseholdDataSystem`) | −1.42 (worst factor) |
| Production / import / export per resource | `CityProductionStatisticSystem`, `TradeSystem` ❌ | — |
| Company profitability | `Game.Companies.Profitability` ❌ | — |
| Natural resources | `NaturalResourceSystem` ❌ | — |

### Levers
| Lever | API |
|---|---|
| Zone commercial/office/industry | ✅ `/zone` |
| Taxes per zone type | ✅ `/taxes` |
| Taxes per resource (commercial/industrial/office) | ❌ (`TaxSystem.Set{Commercial,Industrial,Office}TaxRate(resource, r)`) |
| Specialized industry hub | 🟡 `/building` with `… Area Placeholder` (untested) |
| Production area polygon | ❌ |
| Warehouses / cargo terminals / harbor / airport | ❌ cargo stations locked; ✅ `/devtree` to unlock |
| Server Farm (software demand), Technical University (industry/office efficiency) | ✅ `/building` (Server Farm needs ServerFarmNode 4 pts) |
| Small Business Enthusiast, Advanced Pollution Management | ❌ (policies) |

### Rules of thumb
- Office demand is 0 with 14 % of office cells empty: **stop zoning office**; office needs educated workers and buyers of software/telecom/financial/media.
- Industrial demand 58 with 0.2 % free: zone more Industrial Manufacturing, downwind and away from groundwater pumps and homes.
- Commercial demand rises with households and tourists: Commercial High has never been zoned; put it on arterials (customers) and near hotels.
- Unemployment: industry and commercial give simple jobs, offices complex ones. If the unemployed are under-educated, add simple jobs; if over-educated, add complex jobs [GK reading of the workplace rules]. A `/jobs` endpoint is needed to know which.
- Happier workers produce up to +25 % (IL), so happiness is also an economic lever.
- Specialized industry turns imports into exports and spawns related manufacturing; start with stone or livestock (no resource needed) to test the hub placement [GK + glossary].
- Cargo rail (TrainNode 1 pt) and harbor (2 pts) use connections the map already has.

### Live diagnosis
- **Unemployment is now the worst happiness factor** (−1.42); industrial demand 58 cannot be served (0.2 % free cells) while office cells sit empty.
- *No Inputs* rose from 5 to 16 and *Leveling Building* is 186: materials are not reaching companies/buildings. All freight is by truck; no specialized industry exists; all nine hub prefabs are unlocked and unused.

---

## 3. Finance — the Mayor (with the Treasurer)

**Workers:** tax officer, budget officer.
**Headline metrics:** cash, monthly balance, tax income, service upkeep.

### Mechanics that matter
- **Budget** (`CityServiceBudgetSystem`): `GetTotalIncome`, `GetTotalExpenses`, `GetBalance` (monthly balance: "profit/loss per month at current values", glossary *Budget*), `GetTotalTaxIncome`, `GetMoneyDelta`, `GetIncome(IncomeSource)`, `GetExpense(ExpenseSource)`.
  - `IncomeSource`: TaxResidential, TaxCommercial, TaxIndustrial, TaxOffice, FeeHealthcare, FeeElectricity, FeeEducation, FeeParking, FeePublicTransport, FeeGarbage, FeeWater, GovernmentSubsidy, ExportElectricity, ExportWater.
  - `ExpenseSource`: ServiceUpkeep, LoanInterest, ImportElectricity, ImportWater, ExportSewage, SubsidyResidential/Commercial/Industrial/Office, ImportPoliceService, ImportAmbulanceService, ImportHearseService, ImportFireEngineService, ImportGarbageService, MapTileUpkeep.
- **Taxes** (`TaxSystem`): one array — overall rate, offsets per `TaxAreaType` (Residential, Commercial, Industrial, Office), five residential education-level offsets, and per-resource offsets for commercial/industrial/office. Getters `GetTaxRate(area)`, `GetResidentialTaxRate(level)`, `Get{Commercial,Industrial,Office}TaxRate(resource)`; setters `TaxRate`, `SetTaxRate(area, r)`, `SetResidentialTaxRate(level, r)`, `Set{Commercial,Industrial,Office}TaxRate(resource, r)`; limits in `GetTaxParameterData()`; estimates `GetEstimated*TaxIncome`. Negative rate = subsidy (expense). Residential tax is paid on wages by education level; company tax on monthly profit per resource (glossary *Taxation*).
  - Effects: demand (Taxes factor, neutral at 10 %), happiness (Tax factor neutral at 10 %, ≈ +1.2 happiness per point below 10 on residential; §11), leveling speed. Central Bank raises tax-happiness tolerance (`TaxHappiness` modifier).
- **Service budgets** (`SetServiceBudget(service, %)`, 50–150 %): efficiency = `100·BuildingEfficiencyParameterData.m_ServiceBudgetEfficiencyFactor(budget/100)` (`GetServiceEfficiency`); efficiency scales usable vehicles, processing and city-wide effects (locale on Police & Administration). Upkeep per service from `GetEstimatedServiceBudget`; City Hall lowers base upkeep (`CityServiceBuildingBaseUpkeepCost`).
- **Service fees** (`ServiceFeeSystem`, city buffer `Game.City.ServiceFee`): `PlayerResource` Electricity, Healthcare, BasicEducation, SecondaryEducation, HigherEducation, Garbage, Water, Mail, PublicTransport, FireResponse, Police, Sewage, Parking. `ServiceFeeParameterData` gives default/max/adjustable per fee. A higher utility fee lowers consumption (curve), lowers company/service efficiency and happiness (ElectricityFee/WaterFee factors); the water fee also cuts sewage; education fees raise dropout. `GetFee/SetFee`, `GetServiceFeeIncomeEstimate`. [GK: in the UI only electricity and water fees are adjustable — check `m_Adjustable`.]
- **Loans** (`Game.Tools.LoanSystem`): `CurrentLoan {m_Amount, m_DailyInterestRate, m_DailyPayment}`, `Creditworthiness` (limit from `MilestoneData.m_LoanLimit`), `RequestLoanOffer(amount)`, `ChangeLoan(amount)` (0 repays). Interest % = lerp(min, max, amount/creditworthiness) × `LoanInterest` modifier (City Hall, Central Bank); only interest is paid until repaid (glossary *Loans*).
- **Other money**: milestone rewards (`MilestoneData.m_Reward`, M15 = 375k), map tile upkeep, refunds for bulldozing soon after building (`m_BuildRefundPercentage` [GK on timing]), imported city services (policy), utility import/export.

### Key metrics & where to read them
| Metric | Read via | Now (19:20) |
|---|---|---|
| Cash | `/status`, `/city.money` ✅ | 80.0 M |
| Income / expenses / balance / tax income | `/city` ✅ | 22.24 M / −12.53 M / **+9.71 M per month** / 20.78 M |
| Income and expense per source | `GetIncome/GetExpense` ❌ | — |
| Tax rates | `GET /taxes` ✅ | overall 10; res 8, com 10, ind 10, off 10 |
| Service budgets and upkeep | `GET /budget` ✅ | Health & Deathcare 3.01 M (125 %), Garbage 2.05 M (150 %), Police & Admin 1.50 M, Parks 1.48 M, Communications 1.25 M, Education 1.10 M, Water 0.44 M, Electricity 0.29 M, Fire 0.22 M, Transportation 0.18 M, Roads 0.13 M |
| Fees | ❌ | — |
| Loan, creditworthiness | ❌ | — |

### Levers
| Lever | API |
|---|---|
| Overall and per-zone tax | ✅ `POST /taxes` |
| Residential tax per education level; company tax per resource | ❌ |
| Service budget 50–150 % | ✅ `POST /budget` |
| Service fees | ❌ |
| Loans | ❌ |
| Buy tiles / dev-tree nodes | ✅ |
| Out of Service on idle service buildings (cut upkeep) | ❌ |
| Import City Services (paid outside services) | ❌ (policy) |

### Mayor's cash-flow rules
The council round already computes the finance rule in `tools/council_brief.py` (`finance_rule`); this handbook uses the same numbers:
1. **Reserve** = max(20 M, 2 × monthly expenses) — never spend below it. Today: max(20 M, 25.1 M) = **25.1 M**.
2. **Round envelope** (capital for one council round) = 50 % of cash above the reserve + 1 month of surplus. Today: 0.5 × 54.9 M + 9.7 M ≈ **37.2 M**.
3. **New upkeep cap** = 50 % of the monthly surplus. Today ≈ **4.9 M/month** of new running costs in total.
4. **Austerity** when the monthly balance drops below 2 M: only essential or revenue-positive work.

Further rules derived from the mechanics above:

5. **Budget % is a scalpel**: raise a service budget only when `/services` shows vehicles unusable through efficiency or full processing; lower it (not below 100 %) for services with idle fleets (fire: 37/38 engines idle).
6. **Taxes**: below 10 % costs income but raises demand and happiness (Tax factor); above 10 % does the opposite. Prefer targeted cuts (per education level/resource, once the API allows) over overall cuts.
7. **Spend non-cash currencies first**: 24 unspent dev points and 125 permits are free value.
8. **Loans** only for one-off builds larger than the round envelope; repay as soon as cash allows (interest only, `ChangeLoan(0)`).
9. Every domain request to the mayor states: build cost, monthly upkeep, the metric it moves and by how much.

### Live diagnosis
- Very healthy: +9.7 M/month and 80 M cash; no loan needed. The binding constraints are not money but missing levers (fees, policies, upgrades) and unspent points.
- Health & Deathcare at 125 % with 110 of 122 ambulances idle: candidate to bring back toward 100 % once deathcare hearse shortages are fixed by buildings instead of budget.

---

## 4. Utilities — Alderman of Public Works

**Workers:** power engineer, water & sewage engineer, waste manager, telecom & post officer.
**Headline metrics:** supply ≥ demand on every network; Garbage factor; garbage/post requests that fail pathfinding.

### Cross-cutting: building efficiency
`Game.Buildings.Efficiency` buffer, `BuildingUtils.GetEfficiency` = product of `EfficiencyFactor`s: Destroyed, Abandoned, Disabled, Fire, ServiceBudget, NotEnoughEmployees, SickEmployees, EmployeeHappiness, ElectricitySupply, ElectricityFee, WaterSupply, DirtyWater, SewageHandling, WaterFee, Garbage, Telecom, Mail, MaterialSupply, WindSpeed, WaterDepth, SunIntensity, NaturalResources, city-modifier factors, SpecializationBonus, LackResources, … Penalty sizes in `BuildingEfficiencyParameterData`. For a service building, efficiency scales usable vehicles, processing and coverage, so **a utility failure cascades into every other service**. `/services` already prints `efficiencyFactors` for 8 service kinds — not for power, water or telecom buildings.

### 4.1 Electricity
- **Producers**: `ElectricityProducer {m_Capacity, m_LastProduction}`; fuel plants `PowerPlantData` (coal/gas/nuclear; *No Fuel* when deliveries lag), wind `WindPoweredData` (× WindSpeed factor), solar `SolarPoweredData` (× SunIntensity, cloud penalty), hydro `WaterPoweredData`, geothermal `GroundWaterPoweredData`, incinerator `GarbagePoweredData`, `EmergencyGeneratorData` (diesel upgrade, starts on low battery).
- **Storage**: `BatteryData {m_Capacity, m_PowerOutput}` (Emergency Battery Station, solar Backup Battery); `ElectricityStatisticsSystem.batteryCharge/batteryCapacity`.
- **Network**: max-flow graph (`ElectricityFlowSystem`), edges flagged Bottleneck / BeyondBottleneck / Disconnected; `ElectricityConnectionData {m_Capacity, m_Voltage Low|High}`. Roads carry low-voltage cables; transformer stations (`TransformerData`) join high and low voltage. Icons: Electricity Bottleneck, Not Enough Production, Transformer Out of Capacity, Battery Empty, Powerline Not Connected (high/low).
- **Consumption**: `ElectricityConsumer`; `AdjustElectricityConsumptionSystem` = prefab consumption × renters/employees × temperature curve × fee curve × district *Energy Consumption Awareness* (−5 %).
- **Trade**: `ElectricityTradeSystem.export/import` through the map's power-line connection plus a transformer/plant connected to it; importing "can get expensive" (glossary).
- **Shortage**: ElectricitySupply efficiency factor after a delay + Electricity happiness factor (wellbeing, ramps with `m_CooldownCounter`).

### 4.2 Water & sewage
- **Pumps**: `WaterPumpingStationData {m_Types Groundwater|SurfaceWater, m_Capacity, m_Purification}`; runtime pollution `WaterPumpingStation.m_Pollution`.
- **Groundwater**: `GroundWaterSystem` cells `{m_Amount, m_Polluted, m_Max}` (max 10,000, min usable 500); replenishes and self-purifies slowly. Ground pollution seeps in (`GroundWaterPollutionSystem`) and "if any part of a groundwater deposit becomes affected by ground pollution, all of the water within becomes contaminated" (glossary); it clears after the source is gone.
- **Towers**: no source needed, placeable anywhere, but expensive upkeep per unit of capacity (glossary).
- **Sewage**: `SewageOutletData {m_Capacity, m_Purification}`. A plain outlet dumps into surface water and pollutes pumps downstream; the Wastewater Treatment Plant purifies and returns water to the fresh network, removed pollutants become garbage (glossary).
- **Pipes**: `WaterPipeConnectionData {fresh, sewage, storm capacity}`; roads include pipes; only Small Water / Small Sewage / Combined Small pipes are in the menu. Pollution spreads through pipes (`WaterPipePollutionSystem`, `m_MaxToleratedPollution`).
- **Consumers**: `WaterConsumer`; efficiency factors WaterSupply, DirtyWater, SewageHandling, WaterFee; happiness Water, WaterPollution, Sewage, WaterFee (sewage and water penalties ramp up over a delay, then hit health **and** wellbeing).
- **Trade**: fresh import/export and sewage export need pipes to the map edge (`WaterTradeSystem`). **This map shows no water outside connection** in `/areainfo`.

### 4.3 Garbage
- **Facilities**: `GarbageFacilityData {m_GarbageCapacity, m_VehicleCapacity, m_TransportCapacity, m_ProcessingSpeed, m_IndustrialWasteOnly, m_LongTermStorage}`. Landfill = long-term storage (lot can be enlarged with the Lot Tool, glossary *LotTool*); incinerator = processing + electricity (air pollution); recycling center = processing + sells recovered resources; hazardous waste site (locked) = industrial waste only, lowers industrial ground pollution. **No transfer station prefab exists in this build.**
- **Accumulation**: `GarbageProducer.m_Garbage`; `GarbageAccumulationSystem` from consumption × renters, building level and education balance, homeless garbage, district *Recycling* (GarbageProduction), city *Advanced Pollution Management* (+15 % industrial garbage).
- **Thresholds (live `GarbageParameterData`)**: pickup request at 400, warning icon at 10,000; happiness penalty per home `−min(10, max(0, (garbage − 500)/1000))` on health **and** wellbeing (§11). Efficiency penalty `1 − maxPenalty·saturate((g − warning)/(max − warning))`.
- **Emptying** is a building action (send stored garbage elsewhere) — needed when a landfill is full.

### 4.4 Telecom
- `TelecomFacilityData {m_Range, m_NetworkCapacity, m_PenetrateTerrain}`; `TelecomCoverageSystem` 128² map `{signal, load}`, quality = min(1, 2·signal/(255 + 2·load)); load comes from households + employees, so dense new districts can fall below the baseline. Effects: Telecom happiness (bonus above `m_TelecomBaseline`, penalty below), Telecom efficiency factor on companies, fire/police response time (`m_TelecomResponseTimeModifier`); "boost commercial zone sales, decrease material costs for industrial zones" (glossary). Radio masts also broadcast disaster alarms.

### 4.5 Post
- `PostFacilityData {van, truck, mail capacity, sorting rate}`, `MailBoxData`. Mail types unsorted/local/outgoing; the sorting facility handles intercity mail by truck. Effects: Mail happiness (§11: up to ≈ +15 wellbeing when mail is delivered and backlog small; down to −30 when mail piles up) and Mail efficiency on companies.

### Key metrics & where to read them
| Metric | Read via | Now (19:20) |
|---|---|---|
| Electricity production / consumption / fulfilled | `/city.electricity` ✅ | 2.23 M / 1.36 M / all |
| Battery charge, import/export, per-plant output | `ElectricityStatisticsSystem`, `ElectricityTradeSystem`, `ElectricityProducer` ❌ | — |
| Grid bottlenecks | `/problems` icons 🟡; flow edges ❌ | Powerline Not Connected 3 |
| Water / sewage capacity vs consumption | `/city` ✅ | water 638 k / 406 k; sewage 900 k / 406 k |
| Groundwater amount & pollution | `/groundwater` ✅ | — |
| Pump pollution, outlet purification, surface-water pollution | ❌ | — |
| Garbage production vs processing, storage, trucks, requests, penalised homes | `/services?kind=garbage` ✅, `/hotspots?type=Garbage` ✅ | prod 3.24 M vs processing 7.50 M; storage 29.5 %; 0 waiting, 25 failed pathfinding; 47 homes penalised |
| Post vans, storage, requests | `/services?kind=post` ✅ | 64 waiting, **103 failed pathfinding**; 10/19 buildings out of vans |
| Telecom quality | Telecom factor ✅; map ❌ | +2.38 |
| Fees | ❌ | — |

### Levers
| Lever | API |
|---|---|
| Power plants, turbines, transformers, batteries; pumps, towers, outlets, treatment plants; landfills, incinerators, recycling; radio masts, telecom towers; post offices, sorting facility | ✅ `/building` |
| Power lines (high/low voltage), water/sewage/combined pipes | ✅ `/road` |
| Building upgrades (WWTP Extra Processing Unit / Advanced Filtering, Solar Backup Battery, Diesel Generator, Incinerator/Recycling Storage Extension, Landfill Hazardous Collection Point, Post Van Garage, Mail Storage Extension, Sewage Outlet Extra Settling Tank …) | ❌ (`/building` has no owner; upgrades cannot be attached) |
| Mailboxes (`PostMailbox01`, no BuildingData) | 🟡 untested via `/building` fallback |
| Landfill lot size (Lot Tool) | ❌ |
| Emptying / Out of Service | ❌ |
| Electricity / water / garbage fees | ❌ |
| Budgets | ✅ `/budget` |
| Energy Consumption Awareness, Recycling (district policies), Advanced Pollution Management (city) | ❌ |
| Unlock gas (2), hydro (2), desalination (8), hazardous waste (8), server farm (4) | ✅ `/devtree` |

### Prefabs in this build
- Electricity: WindTurbine01/02/03 (25k/17k/8.5k), SmallCoalPowerPlant01 (100k), GeothermalPowerPlant01 (750k), SolarPowerStation01 (1.5M), EmergencyBatteryStation01 (150k, + Diesel Generator), TransformerStation01/02 (30k); 🔒 GasPowerPlant01 (2 pts), CoalPowerPlant01 (4), NuclearPowerPlant01 (8), Hydroelectric dam (2).
- Water: WaterPumpingStation01 (25k), GroundwaterPumpingStation01 (40k), WaterTower01/02/03, SewageOutlet01 (25k, + Extra Settling Tank), WastewaterTreatmentPlant01 (400k, + Extra Processing Unit 100k, Advanced Filtering 50k); 🔒 DesalinationPlant01 (8 pts).
- Garbage: Landfill01 (70k), IncinerationPlant01 (1M), RecyclingCenter01 (880k); 🔒 HazardousWasteProcessingSite01 (8 pts, 6.36M).
- Telecom: RadioMast01 (25k), TelecomTower01 (125k); 🔒 ServerFarm01 (4 pts), SatelliteUplink01 (8).
- Post: PostOffice01 (250k), PostOffice02 (75k), PostSortingFacility01 (650k).

### Rules of thumb
- Production ≥ 1.2× consumption, with batteries behind wind/solar (wind output follows the WindSpeed factor). Fossil plants downwind of housing and away from groundwater.
- Sewage ≥ 1.1× consumption; treat rather than dump when an outlet is upstream of a pump; pump upstream and on the clean core of a groundwater deposit; keep industry and landfills off deposits (one polluted cell poisons the whole deposit).
- Garbage: processing ≥ production; a facility within ~1.5 km of dense housing; watch failed pathfinding (unreachable segments, one-way traps, district restrictions).
- Post and telecom are "invisible" until they fail: keep vans available and telecom quality above baseline in dense blocks.
- Raising the water fee is a fast stop-gap for water/sewage shortages (less use, less sewage) at a happiness/efficiency cost — once fees are exposed.

### Live diagnosis
- **Sewage** was 15 % undrained at 05:33 (300 k capacity vs 352 k, 160 *Backed up sewer* icons); fixed by 19:20 (900 k). Watch it: the Sewage factor ramps over a delay before it hits health and wellbeing.
- Power is wind-only (41 × WindTurbine01) plus 2 incinerators, no batteries; both incinerators show *Powerline Not Connected*. Production is 1.6× consumption, so the surplus is probably exported — export is not visible.
- Groundwater pumps (3 × at x ≈ −1216, z 1802–2022) sit on the thin western edge of their deposit; the clean core (8–9 of 9) is ~150–250 m east. 34 polluted groundwater cells lie under the northern industry/landfill clusters.
- Garbage improved sharply (factor −2.36 → −0.74; waiting requests 92 → 0). All garbage facilities are in industrial clusters; the dense core around (−1800, 900) has none within 2 km.
- Post: **103 van requests failed pathfinding**; no PostSortingFacility01 although its node is owned.

---

## 5. Health & deathcare — Alderman of Health

**Workers:** clinics & hospitals officer, deathcare officer.
**Headline metrics:** average health, sick awaiting ambulance, dead awaiting hearse, Healthcare and DeathPenalty factors.

### Mechanics that matter
- **Sickness** (IL `SicknessCheckSystem.TryAddHealthProblem`): `t = saturate(0.001·2^(10 − health/10))`, probability = lerp(`HealthEventData` min, max, t) × city modifier `DiseaseProbability`. t: health 100 → 0.001, 70 → 0.008, 50 → 0.032, 30 → 0.128, 0 → 1. **Risk is negligible above ~60 health and rises steeply below ~40.** Event types Disease, Injury, Death; a share needs an ambulance.
- **Health** itself is built in `CitizenHappinessSystem` (§11): healthcare coverage, pollution (air/ground/water), sewage/water shortages, garbage, death in household, homelessness, sickness (halves health), +1 for bicycle users; capped by age (100/90/80, then −10 per year after year 5), so an ageing city has lower average health.
- **Death** (`DeathCheckSystem`): old age curve `HealthcareParameterData.m_DeathRate`; sick/injured die with a probability against their health; hospital `m_TreatmentBonus` lowers it; long trips slow recovery (glossary).
- **Hospitals**: `HospitalData {ambulances, helicopters, m_PatientCapacity, m_TreatmentBonus, m_HealthRange, m_TreatDiseases, m_TreatInjuries}`; *No Hospital Supplies* when supplies run out. Medical University → `HospitalEfficiency` city-wide. Health Research Institute "studies ways to keep citizens healthier"; Disease Control Center (locked) treats disease with intensive care and helicopters.
- **Deathcare**: `DeathcareFacilityData {m_HearseCapacity, m_StorageCapacity, m_ProcessingRate, m_LongTermStorage}`. Cemeteries store bodies permanently, fill up, and are park-like; crematoria process bodies and keep no stock (glossary). A dead body at home gives every household member the DeathPenalty (health and wellbeing) until collected.

### Key metrics & where to read them
| Metric | Read via | Now (19:20) |
|---|---|---|
| Average health | `/city.health` ✅ | 68 (62 at 05:33) |
| Sick, injured, awaiting ambulance, patients | `/services?kind=healthcare` ✅ | 101 sick, 47 awaiting transport, patients 30/1,925, 110/122 ambulances idle |
| Dead awaiting hearse, bodies vs capacity, death rate | `/services?kind=deathcare` ✅ | 233 dead awaiting hearse, bodies 9,667/24,060, 2 cemeteries full, 3 out of hearses, death rate 642 |
| Coverage | `/coverage?service=Healthcare&layer=homes` ✅ | 100 % "good" (11:12) |
| Healthcare / DeathPenalty factors | `/happiness` ✅ | +4.63 (health +8) / −0.03 |
| Hotspots | `/hotspots?type=Hearse|Ambulance` ✅ | Hearse 13 |

### Levers
| Lever | API |
|---|---|
| Clinics, hospitals, cemeteries, crematoria, Health Research Institute | ✅ `/building` |
| Upgrades (Clinic Extension Wing, Cemetery Chapel/Columbarium/Mausoleum/Burial Lot, Crematorium Hearse Garage, Medical University Practice Clinic/Research) | ❌ |
| Empty a full cemetery | ❌ (building action) |
| Budget | ✅ (125 % now) |
| Healthcare fee | ❌ |
| Disease Control Center | ✅ unlock via `/devtree` (8 pts) |
| Import City Services (ambulance, hearse) | ❌ (policy) |

### Prefabs
MedicalClinic01 (60k), MedicalClinic02 (30k), Hospital01 (1.88M), HealthResearchInstitute01 (1M), Cemetery01 (114k), Cemetery02 (42k), Crematorium01 (240k); 🔒 DiseaseControlCenter01 (8 pts).

### Rules of thumb
- Keep average health above 60: below it, sickness rises exponentially (IL).
- Fix pollution near homes and clean water before adding clinics: those are health inputs; clinics only add coverage.
- Hearses scale with the death rate; prefer crematoria in dense areas (no storage to fill); replace or empty full cemeteries.
- Budget above 100 % only when vehicles are unusable through efficiency; idle fleets mean the problem is distance or pathfinding.

### Live diagnosis
- Health is good (68) and ambulances are idle; the deathcare side is the weak spot: 233 bodies awaiting hearses, 2 full Cemetery02s, 3 facilities out of hearses (cluster around (−3374, −308)).

---

## 6. Safety — Alderman of Safety

**Workers:** police chief, fire chief, disaster officer.
**Headline metrics:** Crime factor, patrol/prisoner requests, fires and burned-down buildings, shelter coverage.

### Mechanics that matter
- **Crime accumulation** (IL `CrimeAccumulationSystem`): per building per day `m_CrimeRate × m_CrimePoliceCoverageFactor × 5/(5 + policeCoverage)`, × local, district (Gated Community) and city `CrimeAccumulation` modifiers, capped at `m_MaxCrimeAccumulation`; buildings without road access accumulate nothing. Police coverage 5 → 10 cuts accumulation to 2/3, 5 → 15 to 1/2. Patrol requests are raised when crime is high.
- **Who commits crime** (IL `CrimeCheckSystem`): probability peaks at wellbeing 25 and falls quadratically toward 100 (`t = W ≤ 25 ? W/25 : (100 − W)/75`, squared) × `CrimeProbability` modifier; "low overall well-being and unemployment increase the likelihood" (locale). Recidivism: welfare (`m_WelfareCrimeRecurrenceFactor`), Pre-Release Programs policy, police budget ("lowering it raises the likelihood of returning to crime").
- **Crime factor** for residents: `−min(maxPenalty, (crime − negligible)·multiplier) − CrimeVictim effect` (§11).
- **Police**: `PoliceStationData {patrol cars, helicopters, m_JailCapacity, m_PurposeMask Patrol|Emergency|Intelligence}`; CIB is Intelligence and "lowers crime rate citywide". **Prison**: `PrisonData {vans, m_PrisonerCapacity, prisoner wellbeing/health}`; jailed criminals wait in station jails for a prison van (`PrisonerTransportRequest`) — **without a prison those requests never resolve**.
- **Fire hazard** (IL `EventHelpers.FireHazardData.GetFireHazard`): `prefab hazard × (1 − 0.03·(level − 1)) × zone multiplier × max(0.01, 1 − 0.01·fireCoverage)`, then the district *Fire Safety Sprinkler System* modifier and damage. Higher building levels burn less. Forest fire hazard grows with temperature and dry days; Firewatch Tower supplies `ForestFireHazard`/`ForestFireResponseTime` local modifiers; forest fires need helicopters (glossary). Response time worsens with poor telecom and darkness (`FireConfigurationData`). Damaged buildings self-repair; collapsed ones are cleared (bulldozing speeds it up).
- **Disasters**: tornado, hail storm, lightning, forest fire, flood, tsunami, building collapse (`WeatherPhenomenonData`, `WaterLevelChangeData`). `DangerFlags` StayIndoors/Evacuate/UseTransport drive evacuation (buses from the Large Emergency Shelter). Shelters `EmergencyShelterData {capacity, vehicles}` need supplies; sheltered citizens lose wellbeing. Early Disaster Warning System → `DisasterWarningTime`, `DisasterDamageRate`. Natural disasters are a new-game option; the current value is not readable [it appears enabled: a *Weather Destroyed* icon exists].
- **Traffic accidents** are handled by police + fire + ambulance (§9).

### Key metrics & where to read them
| Metric | Read via | Now (19:20) |
|---|---|---|
| Patrol / emergency / prisoner-transport requests, jails | `/services?kind=police` ✅ | patrol 18 waiting; **prisoner transport 8 waiting, 8 failed**; 9/10 stations "no patrol cars available"; jails 22/280 |
| Police coverage | `/coverage?service=Police` ✅ | 100 % good (11:12) |
| Crime factor | `/happiness` ✅ | −0.01 (−0.41 at 05:33) |
| Crime per building, city crime rate, criminals, prisoners | `CrimeProducer`, `PoliceInfoviewUISystem`, `StatisticType.CrimeRate/CrimeCount` ❌ | — |
| Fire requests, buildings on fire, engines | `/services?kind=fire` ✅ | 6 requests failed pathfinding, 37/38 engines idle |
| Fire hazard map / average | ❌ | — |
| Burned down / collapsed / weather damage | `/problems` ✅ | Burned Down 5, Abandoned Collapsed 1, Weather Destroyed 1 |
| Shelter coverage | `/coverage?service=EmergencyShelter` ✅ | 0 % (no shelters) |
| Active disasters, danger zones, shelter use | ❌ | — |

### Levers
| Lever | API |
|---|---|
| Police stations, HQ, prison, CIB; fire houses, fire station | ✅ `/building` |
| Prison Extra Wing, HQ Extra Jails/Garage/Helipad, Fire Station Disaster Response Unit | ❌ (upgrades) |
| Shelters, firewatch tower, heliport, early warning | ✅ unlock via `/devtree` (1, 1, 2, 8 pts) then `/building` |
| Budgets | ✅ |
| Pre-Release Programs, Gated Community, Sprinklers, City Promotion side effect, Import City Services | ❌ (policies) |
| Police/fire fees | ❌ |

### Prefabs
PoliceStation01 (160k), PoliceStation02 (100k), PoliceStation03 (220k), PoliceHeadquarters01 (2.1M), Prison01 (650k), CentralInvestigationBureau01 (3.2M); FireHouse01 (120k), FireHouse02 (125k), FireStation01 (2.15M); 🔒 FirewatchTower01 (1 pt), FirefightingHelicoptersDepot01 (2), EmergencyShelter01 (1 pt, 35k), LargeEmergencyShelter01 (4), EarlyDisasterWarningSystem01 (8).

### Rules of thumb
- Crime: coverage first (accumulation ∝ 5/(5+coverage)), then wellbeing and jobs (crime peaks at wellbeing 25), then intelligence (CIB).
- A city with jails needs a prison; otherwise stations clog with prisoners and patrol cars stay busy.
- Fire: cover industry well; levelled buildings burn less; firewatch towers near forests.
- Shelters are cheap insurance once disasters are on; they need road access and supplies.
- Failed-pathfinding fire/police requests point to unreachable spots — fix the road network, not the budget.

### Live diagnosis
- **No prison is built although PrisonNode is owned**: 8 prisoner transports fail, 8 stations "waiting for prison van", 9/10 out of patrol cars. Prison01 costs 650k.
- 5 burned-down industrial buildings at (−206, 2852…3076); good fire coverage there, so it is the inherent hazard of low-level industry.
- No emergency shelters (node costs 1 point).

---

## 7. Education & research — Alderman of Education

**Workers:** schools officer, research officer.
**Headline metrics:** seats vs students per level, Education factor, education fit of jobs (§2).

### Mechanics that matter
- **Levels**: citizens have education 0–4 (Uneducated … Highly Educated). Schools: `SchoolData.m_EducationLevel` 1 elementary, 2 high school, 3 college, 4 university; `SchoolData {m_StudentCapacity, m_GraduationModifier, m_StudentWellbeing, m_StudentHealth}`; runtime `School {m_AverageGraduationTime, m_AverageFailProbability}`. Higher levels require the full path (glossary *EducationPaths*). Entry: `EducationParameterData` (enter high school, adults, workers continuing).
- **Graduation** (IL `GraduationSystem.GetGraduationProbability`), with `w = saturate((0.5 + studyWillingness)·wellbeing/75)`: L1 `smoothstep(0, 1, 0.6w + 0.41)`; L2 `0.3·ln(2.6w + 1.1)`; L3 `(90·ln(1.6w + 1) + collegeMod.x)·(1 + collegeMod.y)/100`; L4 `(70w + uniMod.x)·(1 + uniMod.y)/100`; then `p = 1 − (1 − p)/efficiency + m_GraduationModifier`. **Wellbeing and school efficiency drive graduation**; `CollegeGraduation`/`UniversityGraduation` modifiers (research buildings).
- **Dropout** (`GetDropoutProbability`): long commutes, high education fees and low household wealth raise it.
- **Education happiness** goes only to **children** (k = household size if the citizen is a child): `round(sqrt(k)·multiplier·(coverage − neutral))`, live multiplier 3, neutral 5 — coverage, not seats (§11).
- **Research & admin city effects** (locale): Technical University + office/industrial efficiency; Medical University + healthcare efficiency; Radio Telescope + university interest and graduation; LHC + university interest, electronics and software demand; Geological Research Center larger ore/oil deposits; research "needs highly educated people". City Hall lowers service upkeep and imported-service cost and unlocks City Promotion; Central Bank lowers loan interest and improves tax-happiness tolerance; Welfare Office local wellbeing and lower recidivism.

### Key metrics & where to read them
| Metric | Read via | Now (19:20) |
|---|---|---|
| Students vs seats per level, full schools, graduation time, fail probability | `/services?kind=education` ✅ | 15,317 students / 47,200 seats; 16 of 21 schools full; elementary and high school ~97–100 % full (05:33: 5,432/5,600 and 1,599/1,600) |
| Eligible (wanting) students per level | `EducationInfoviewUISystem` ❌ | — |
| Coverage | `/coverage?service=Education&layer=homes` ✅ | 0.6 % of homes uncovered, 3 % low (11:12) |
| Education factor | `/happiness` ✅ | +3.07 |
| Workforce by education | ❌ (`/jobs` gap) | — |

### Levers
| Lever | API |
|---|---|
| Schools, college, universities | ✅ `/building` |
| School upgrades (ElementarySchool02 Classroom Extension 8k, Gymnasium; ElementarySchool01 Playground/Children's Clinic; HighSchool02 Classroom Extension 40k, Sport Park; College/University Library; University Park) | ❌ (upgrades) |
| Education fees | ❌ |
| Research buildings | ✅ unlock via `/devtree` (Geological 4, Radio Telescope 4, LHC 8) then `/building` |
| Budget | ✅ |

### Prefabs
ElementarySchool01 (100k, 18×8), ElementarySchool02 (40k, 9×6), ElementarySchool03 (150k), HighSchool01 (300k), HighSchool02 (150k), HighSchool03 (300k), College01 (750k), University01 (1.5M), TechnicalUniversity01 (1.6M), MedicalUniversity01 (2.4M); 🔒 GeologicalResearchCenter01, RadioTelescope01, LargeHadronCollider01.

### Rules of thumb
- Size schools to eligible students, not to coverage: coverage feeds happiness, seats feed graduation.
- Happy students graduate (w ∝ wellbeing/75); efficient schools graduate (budget, staff, utilities).
- Match education to jobs: uneducated workers for industry, educated for offices; an over-educated workforce with only simple jobs shows up as Unemployment.
- Research buildings pay off only with highly educated workers available.

### Live diagnosis
- For ~95k people the city has 14 small ElementarySchool02 and 4 small HighSchool02: lower levels are full while college and universities have spare seats — children are almost certainly unschooled (eligible counts not readable). The classroom extensions (8k / 40k) are the cheapest capacity but cannot be placed via the API; a large ElementarySchool01/HighSchool01 can.

---

## 8. Parks, leisure, tourism & beauty — Alderman of Recreation & Tourism

**Workers:** parks keeper (parks, maintenance, coverage), tourism officer (attractions, signature buildings, hotels), landscaper (trees, surfaces, road greenery).
**Headline metrics:** Entertainment and Leisure happiness factors, park coverage of homes, city attractiveness and tourists.

### Mechanics that matter
- **Parks** (`Game.Prefabs.ParkData` {m_MaintenancePool, m_AllowHomeless}; `Game.Buildings.Park.m_Maintenance`) lose maintenance and need park maintenance depot vehicles (`MaintenanceRequest`). They provide park coverage (`CoverageService.Park`), which our `/coverage` converts to the Entertainment wellbeing term: `20 * min(1, sqrt(coverage/1.5))` (see `/coverage` note; city modifiers ignored).
- **Leisure** (`Game.Prefabs.LeisureProviderData` {m_Efficiency, m_Resources, m_LeisureType}; `Game.Agents.LeisureType`: Meals, Entertainment, Commercial, CityIndoors, Travel, CityPark, CityBeach, Attractions, Relaxation, Sightseeing). Citizens with free time look for a provider of a type; commercial zones (restaurants, entertainment) and parks/attractions supply it. Formula of the Leisure factor: see §11.
- **Attractiveness of a building** (`AttractionData.m_Attractiveness` → `Game.Buildings.AttractivenessProvider.m_Attractiveness`): `AttractionSystem` applies factors Efficiency, Maintenance, Forest, Beach, Height; the terrain part comes from `TerrainAttractivenessSystem` (`AttractivenessParameterData` m_ForestEffect/m_ForestDistance, m_ShoreEffect/m_ShoreDistance, m_HeightBonus). Glossary: "sea shores, forests, mountains and special features like ruins" raise nearby attractions.
- **City attractiveness (IL `TourismSystem/TourismJob::Execute`)**: `S = Σ_providers a_i² / 10000`; `A = 200 / (1 + e^(−0.3·S)) − 100` (0…100), then the `Attractiveness` city modifier (City Promotion policy: +100%) → `Game.City.Tourism.m_Attractiveness`.
- **Tourists (IL `TourismSystem::GetTargetTourists`)**: `A ≤ 100 → 15·A`; `A > 100 → 1500 + round(100·log10(1 + A − 100))`. Spawning ramps down as current tourists approach 110% of target (`GetSpawnProbability`) and is scaled by weather (`GetWeatherEffect`: temperature, rain, snow ranges in `AttractivenessParameterData`).
  → A few strong attractions (a≈100 each adds 1 to S) push A toward ~80–90 quickly; past A = 100 (only with City Promotion) more attractions add tourists only logarithmically. Hotels (`Game.Companies.LodgingProvider.m_FreeRooms`, commercial zone) cap stays; *TouristNoHotel* is a `MoveAwayReason`.
- **Beauty & greenery.** Forests raise the attractiveness of nearby attractions (terrain factor); grass/trees on roads halve road noise (§9). Plants absorb pollution into `Plant.m_Pollution` and wither (`ObjectPolluteSystem`), but the IL shows no removal from the pollution maps — trees are not a pollution fix (§10). The *Landscaping* budget line exists (upkeep 0 today).
- **Signature buildings**: unique, unlock one by one with milestones/growth; one of each, from both themes; give XP; "many ... have a positive effect on the area surrounding it"; can be placed in any zone (glossary *SignatureBuildings*). Construction cost 0 in `/prefabs`.
- **Landmarks/tourist attractions** "create some noise pollution and are somewhat expensive to upkeep" (glossary).
- **Historical buildings**: a zoned building marked historical no longer levels up, downgrades or gets abandoned (glossary *HistoricalBuildings*; toggle in the building info panel).

### Key metrics & where to read them
| Metric | Read via | Now |
|---|---|---|
| Entertainment / Leisure happiness | `/happiness` ✅ | Entertainment +6.00 (y −4, z 16 = capped), Leisure +1.24 |
| Park coverage of homes | `/coverage?service=Park&layer=homes` ✅ | 100 % of 52,272 households "good", avg bonus 20 = cap (05:33: 31 % uncovered in the west) |
| Park maintenance | `/services?kind=park` ✅ | 60 parks + 4 depots, all ok |
| City attractiveness, tourists, lodging | `Game.City.Tourism`, `StatisticType.TouristCount/TouristIncome/LodgingUsed/LodgingTotal` ❌ | — |
| Attractiveness per provider and its factors | `AttractivenessProvider`, `AttractionSystem` ❌ | — |
| Signature buildings placed vs. available; unlock requirements | `/prefabs?category=signature` 🟡 (lock state only); `/unlockinfo?name=` ✅ (requirements + progress) | 13 unlocked, 51 locked |

### Levers
| Lever | API |
|---|---|
| Place parks, playgrounds, plazas, sports, attractions, signature buildings | ✅ `/building` |
| Park maintenance depot (+ Extra Garage) | ✅ depot; ❌ garage (service upgrade) |
| School/university park & sports sub-buildings (e.g. University01 Park, HighSchool01 Sports Field) | ❌ (service upgrades) |
| Trees / forest brush, surfaces, terraforming | ❌ |
| Road trees and grass | 🚧 `/roadupgrade` |
| City Promotion (attractiveness +100%, crime near attractions +20%), Recreational Use | ❌ (policies, §13) |
| Mark building historical | ❌ |
| Unlock tourist attractions/landmarks | ✅ `/devtree` |

### Prefabs in this build
- Parks (all unlocked): PocketPark01–10 (1.5–7.5k), CityPark01–11, ChirperPark01–04, DogPark01/02, Playground01–05, CampfireSite01/02, BicyclePark01, ParkCafe01, SunkenPlaza01; sports: BasketballCourt01/04, SkatePark01, TennisCourt01, GymPark01, FootballField01, AmericanFootballField01, CommunityPool01; ParkMaintenanceDepot01.
- Tourist attractions (unlocked): WaterPark01 (800k), ObservationTower01 (3.2M), BronzeStatue01 (3.2M), OldCastle01 (4.8M). Nature props: Ruins* (castle, old cemetery, mill, cairns, stone formations; 0–1k).
- Signature, unlocked (13): IceflakeArena01, IndustrialManufacturingSignature05, IndustrialManufacturingSignature09, OfficeLowSignature01–03, NA_ResidentialLowSignature01–03, NA_ResidentialMediumSignature01–03, NA_MixedSignature01. Locked: 51 more (EU/NA commercial, office high, residential high/row/waterfront, mixed, industrial).

### Rules of thumb
- Cover every residential block with park coverage (pocket parks are 1.5k); the wellbeing term saturates at coverage 1.5, so once a block is covered, further parks there add nothing to happiness — spread them, don't stack them.
- A few high-attractiveness buildings near forest/shore/height give most of the tourism (logistic in IL); add hotels (commercial demand) before pushing City Promotion.
- Place every unlocked signature building: free, XP, local bonus.
- Keep park maintenance depots within reach; unmaintained parks lose attractiveness (Maintenance factor).

### Live diagnosis
- Park coverage went from 31 % of households uncovered (05:33, west side) to 100 % (19:20): Entertainment is now capped. New parks only matter for new housing, tourism and land value.
- WaterPark01 is unlocked and has never been built by our scripts. Phase scripts placed OfficeLowSignature01–03 and IndustrialManufacturingSignature05 only: IceflakeArena01, IndustrialManufacturingSignature09 and the NA residential/mixed signatures may still be waiting (verify before building — one of each).


---

## 9. Mobility — Alderman of Transport

**Workers:** road engineer (hierarchy, intersections, lane rules), transit planner (lines, depots, stops), freight & connections officer (cargo, outside connections, parking).
**Headline metrics:** bottleneck icons, TrafficPenalty happiness factor, city average traffic flow, transit passengers.

### Mechanics that matter
- **Roads are also the utility network.** "Most roads come with a built-in utility network for electricity, water, and sewage ... Bridges and highways do not" (locale glossary *Roads*). Zoning cells only appear along roads whose `RoadData.m_Flags` has `EnableZoning` (`Game.Prefabs.RoadFlags`: EnableZoning, SeparatedCarriageways, PreferTrafficLights, DefaultIsForward/Backward, UseHighwayRules, HasStreetLights).
- **Hierarchy.** Alley (8 m) → Small (16 m, 2 lanes) → Medium (24 m, 4 lanes) → Large/XL (32–40 m, 6–8 lanes) → highways (no zoning, no utilities, `UseHighwayRules`). Speed limit = `RoadData.m_SpeedLimit`; "the larger the road, the higher the speed limit and traffic capacity" (glossary *RoadTypes*). Asymmetric and one-way variants exist; lane direction is flipped by rebuilding the same road on top in the opposite direction (glossary *RoadLaneDirections*).
- **Traffic flow** is stored per road edge in `Game.Net.Road` (`m_TrafficFlowDuration0/1`, `m_TrafficFlowDistance0/1`, a float4 per time-of-day slot) and updated by `TrafficFlowSystem` 32×/day; city averages via `TrafficFlowSystem.cityAverageTrafficFlow` / `cityAverageTrafficVolume`. `TrafficBottleneckSystem` places the *Traffic Bottleneck Notification* icons.
- **Intersections** are node composition flags (`Game.Prefabs.CompositionFlags.General`): `TrafficLights`, `RemoveTrafficLights`, `AllWayStop`, `Crosswalk`, `Roundabout`, `LevelCrossing`. Turn rules sit on the edge ends (`CompositionFlags.Side`): `ForbidLeftTurn`, `ForbidRightTurn`, `ForbidStraight`, `AddCrosswalk`/`RemoveCrosswalk`, `ZonesDisabled` (the *Road Zones* tool). Signals: `TrafficLightSystem` + `TrafficLightInitializationSystem`. Glossary: lights help "small but busy intersections, where traffic on one side needs to wait for a long time"; stop signs give one direction more chances; no-turn signs change lane markings.
- **Edge upgrades** (`CompositionFlags.Side`, as listed live by `/roadupgrades`): Grass = PrimaryBeautification (+ middle), Trees = SecondaryBeautification (+ middle), Wide Sidewalk, Sound Barrier, Lighting (`General.Lighting`; `Game.Net.RoadFlags` IsLit/AlwaysLit/LightsOff), Bikelane (one/both sides), Bicycle Traffic Restriction (ForbidSecondary), public-transport lanes (PrimaryLane), Road Zones (ZonesDisabled), tram track (locked).
- **Road noise (IL, `NetPollutionSystem/UpdateNetPollutionJob::CheckUpgrades`).** A road's noise is spread over (left, centre, right). Sound barrier on both sides → ×(0, 0.5, 0); on one side → that side 0, centre ×0.5, other side ×1.5. Beautification (grass/trees, either flag) on both sides → ×0.5 everywhere; on one side → (0.5, 0.75, 1). So **street trees or grass on both sides halve a road's noise**; sound barriers almost remove it at the sides (but push it to the other side if only one side gets one).
- **Wear & maintenance.** `Game.Net.NetCondition.m_Wear`; Road Maintenance Depot vehicles "clear roads of snow, wrecked vehicles, debris and do road repairs ... reduces the likelihood of vehicle accidents and traffic jams" (glossary). Requests are `MaintenanceRequest` (shared with park maintenance).
- **Accidents** (`Game.Events.TrafficAccident`): police, fire and ambulances respond (glossary *TrafficAccidents*). City modifier `HighwayTrafficSafety` is what the High-Speed Highways policy touches (+10% accident probability per its description).
- **Parking**: `Game.Net.ParkingLane`, `Game.Buildings.ParkingFacility` (`ParkingFacilityData.m_ComfortFactor`, `m_GarageMarkerCapacity`), `ParkingParametersData`. Fees: *Roadside Parking Fee* (district policy), *Parking Fee* (lot, building policy).
- **Public transport.** `Game.Prefabs.TransportType`: Bus, Train, Taxi, Tram, Ship, Post, Helicopter, Airplane, Subway, Rocket, Work, Ferry, Bicycle, Car. Lines: `TransportLineData` (default vehicle interval, stop duration, passenger/cargo) and `Game.Routes.TransportLine` (`m_VehicleInterval`, `m_UnbunchingFactor`, `m_TicketPrice`, flags `RequireVehicles`, `NotEnoughVehicles`). Route policies use `Game.Routes.RouteModifierType` {TicketPrice, VehicleInterval} (UI: "Ticket price", "Vehicles"). A depot must exist before vehicles run; a line is a closed loop of stops; stations are optional (glossary *BusLines*, *BusDepots*, *BusStops*). Taxis need a depot; taxi stands make service faster; *Taxi Base Fare* = `CityModifierType.TaxiStartingFee`. "Well-organized public transportation decreases traffic congestion and lowers the overall cost of transportation"; passengers "have more money to use on goods and services" (glossary *PublicTransportation*, *PassengerTransportation*).
- **Cargo.** "Using Trains, Ships and Airplanes for transporting cargo lowers the cost of shipping goods ... more businesses that are profitable" (glossary *CargoTransportation*). Cargo train terminals belong next to industry. The *Cargo Transport* route tool is unlocked by the Train, Harbor or Airport node.
- **Outside connections** (`OutsideConnectionTransferType`: Road, Train, Air, Ship). "The most important Outside Connection is the highway. It allows for new citizens to move in, for tourists to visit the city, and for the import and export of resources, goods, and even some City Services" (glossary). New connection points appear when a road/rail/seaway reaches the map edge on an owned tile.
- **Bicycles**: bike paths and lanes, bicycle parking (locked behind ParkingAreasNode), policies *Urban Cycling Initiative* (more cycling, small health bonus) and *Bicycle Traffic Restriction* (keeps bikes off carriageways).

### Key metrics & where to read them
| Metric | Read via | Now |
|---|---|---|
| Bottleneck icons, clusters | `/problems`, `/hotspots?type=Traffic` ✅ | 31 at 19:20 (51 at 05:33, clustered on Small Road at (−3398, −150) and on the 2-lane map highways at (5015, −1523), (3279, 2536), (−723, 2892)) |
| Traffic penalty on happiness | `/happiness` (TrafficPenalty) ✅ | −0.90 (−0.61 at 05:33) — mostly a connectivity signal, see §11 |
| Per-road congestion, city average flow | `Game.Net.Road` flow fields, `TrafficFlowSystem.cityAverageTrafficFlow` ❌ | — |
| Network inventory | `/areainfo` netSegmentsByPrefab, `/nets` ✅ | Small Road 2006 segments, Medium 97, Large/XL 0 |
| Accidents | `/problems` (Traffic Accident) ✅, `Game.Events.TrafficAccident` | 7 |
| Road wear, road maintenance requests | `Game.Net.NetCondition` ❌; requests are mixed into `/services?kind=park` 🟡 | 802 maintenance requests waiting, 843 failed pathfinding (park + road + accident vehicles), 1 road depot |
| Parking occupancy | `Game.Net.ParkingLane`, `ParkingFacility` ❌ | — |
| Lines, stops, waiting passengers, ticket price, vehicle interval | 🚧 `/lines`, `/stops`, `/transitprefabs` (merged, teammate); passenger statistics `StatisticType.PassengerCount*` ❌ | 1 bus line (18 waypoints, ticket 8), 28 stops; Transportation upkeep 182k |
| Cargo counts per mode | `StatisticType.CargoCountTruck/Train/Ship/Airplane` ❌ | — |
| Outside connections | `/areainfo` ✅ | 2 highways (4 one-way ends), 2 train, 2 ship, 2 air, 1 powerline; **no water connection** |

### Levers
| Lever | Effect | API |
|---|---|---|
| Build any road/highway/one-way/asymmetric/bridge, paths, quays, pipes, power lines | network | ✅ `/road` (tracks locked) |
| Replace road type (Small→Medium, highway 2→3 lanes) | capacity | ✅ `/upgrade` (≤4 segments per batch) |
| Flip a one-way / asymmetric direction | flow | 🟡 `/upgrade` replaces along the curve; no explicit invert option |
| Street trees, grass, wide sidewalk, sound barrier, lighting, bike lanes, bus lanes, bicycle restriction | noise ×0.5, looks, cycling, transit priority | 🚧 `/roadupgrade` + `/roadupgrades` (merged, teammate; edge upgrades only) |
| Traffic lights / remove lights / all-way stop at a node | intersection control | ❌ (`/roadupgrades` lists them as "node (not supported here)") |
| No-left/right/straight turn, crosswalk add/remove | lane rules | ❌ ("node-end/directional (not supported here)") |
| Roundabouts and cul-de-sacs (net objects, unlocked) | junction capacity | ❌ not listed by `/prefabs`; untested via `/building` |
| Road Zones (remove/re-add zoning grid) | shape blocks | 🚧 `/roadupgrade` with `RoadZones` (edge upgrade) |
| Road maintenance depot | wear, accidents | ✅ `/building` RoadMaintenanceDepot01 |
| Bus depot, stops, lines | transit | ✅ depot via `/building`; 🚧 `/busstop`, `/busline` (merged, teammate) |
| Ticket price, vehicle count per line | ridership vs cost | 🟡 read via `/lines`; set ❌ (route policies, `RouteModifierType`) |
| Taxi depot / taxi stands / base fare | short trips | 🟡 depot ✅; stands 🔒 (`NA/EU_TaxiStop01` locked) and ❌ placement; base fare ❌ (policy) |
| Train, tram, subway, ship, air lines; cargo routes | mass transit, cheaper freight, tourism | ❌ (all locked per `/transitprefabs`; the line tool exists only for buses) |
| Parking lots/halls, parking fees | parking search traffic, income | ❌ (locked: ParkingAreasNode, 2 pts) |
| Unlock transport nodes | — | ✅ `POST /devtree` |
| New outside connection (extend highway/rail to map edge) | more move-in/import capacity | 🟡 `/road` + `/buytile` (needs edge tiles) |
| Transport policies (Combustion Engine Ban, Heavy Traffic Ban, Speed Bumps, High-Speed Highways, Urban Cycling, Bicycle Restriction) | see §13 | ❌ |

### Prefabs in this build (🔒 = locked)
- Roads unlocked: all small/medium/large/XL variants, alleys, gravel, pedestrian streets, quays, bridges (Truss Arch, Tied Arch, Wooden Covered, Suspension/Cable-stayed/Extradosed), highways 1–5 lanes one-way, 2–4 lanes two-way, asymmetric highways, Double/Oneway Public Transport Lane. 🔒 Grand Bridge (Intersections→GrandBridgeNode 8 pts), ExtradosedBridge01 (rail), Hydroelectric dam.
- Road services (unlocked): Traffic Lights, All-way Stop, Forbid Left/Right/Straight, Crosswalk, Lighting, Sound Barrier, Trees, Grass, Wide Sidewalk, Bikelane (one/both sides), Bicycle Traffic Restriction, RoadZones; RoadMaintenanceDepot01 (75k); 38 roundabout/cul-de-sac objects (RoundaboutsNode owned).
- Transit unlocked: BusDepot01 (150k, Extra Garage 75k), BusStation01–03 (200k/100k/150k; they unlocked once a bus depot was built — `/unlockinfo` shows the "Bus Depot Built Req"), TaxiDepot01 (100k, Taxi Garage 32k), TaxiDepot02 (35k). 🔒 taxi stands (`NA/EU_TaxiStop01`), TrainStation01–03, TrainStationTerminus01, CargoTrainTerminal01/02, RailYard01/02 (TrainNode, **1 pt**), TramDepot01, tram stations (TramNode 2), SubwayStation*, SubwayYard* (SubwayNode 4), Harbor01, CargoHarbor01 (HarborNode 2), Airport01 (AirportNode 4), InternationalAirport01 (8), SpaceCenter01 (8); all tracks and seaways.
- Parking 🔒: ParkingLot01–17, ParkingHall01–04, AutomatedParkingBuilding01, bicycle parking/storage (ParkingAreasNode 2 → UndergroundParkingNode 4 → AutomatedParkingBuildingNode 8).
- Bike/pedestrian: bike paths and bridges, pavement paths, piers (all unlocked).

### Rules of thumb
- Build a hierarchy: highways → large/medium arterials → small local streets. A 95k city on 2,006 small-road segments and zero large roads is arterial-starved; upgrade the main through-routes to Medium/Large before adding transit. [general knowledge + live data]
- Don't let local traffic use the highway connectors as its only link between districts; bottlenecks on the 2-lane map highways are the outside-connection feeders, so widen them to 3 lanes or add a parallel arterial. [GK]
- Traffic lights only where a minor road waits too long at a busy junction (glossary); roundabouts for medium/large crossings [GK]; ban left turns onto busy arterials rather than adding lights [GK].
- Put grass or trees on both sides of busy roads through housing: noise ×0.5 (IL). Sound barriers only along highways next to homes, both sides.
- Transit: depot first, then a loop line; stations are optional; keep the depot near the route (glossary). Train (1 pt) uses the two existing map rail connections for passengers and cargo, and cargo rail cuts freight cost for industry (glossary).
- Keep police, fire and ambulances able to reach highways — they clear accidents (glossary).

### Live diagnosis
- Network imbalance: Medium 97 vs Small 2,006 segments; Large 0. Bottlenecks 31–51 during the day.
- Public transport is one bus line (built during the day by the transit teammate). Train/Harbor/Airport are cheap (1/2/4 pts) and the map already has 2 rail, 2 ship and 2 air connections plus 64 pre-built rail segments — currently unused.
- `MaintenanceRequest` is shared by parks, road wear (`NetDeteriorationSystem`) and accident vehicles (`DamagedVehicleSystem`); `/services?kind=park` therefore shows 802 waiting / 843 failed — mostly road work for **one** RoadMaintenanceDepot01. Add road depots (75k each) and separate the metric.


---

## 10. Environment — Alderman of Environment

**Workers:** pollution officer (air, ground, noise), water-quality officer (groundwater, surface water).
**Headline metrics:** AirPollution / NoisePollution / GroundPollution / WaterPollution factors, polluted groundwater cells, *Noise Pollution* icons.

### Mechanics that matter
- **Building emissions**: `PollutionData {m_GroundPollution, m_AirPollution, m_NoisePollution, m_ScaleWithRenters}` per prefab, × `PollutionModifierData` (upgrades such as filters) and efficiency/renters (`BuildingPollutionAddSystem.GetBuildingPollution`), × city modifiers `IndustrialAirPollution`/`IndustrialGroundPollution` (*Advanced Pollution Management*: −25 % each, garbage +15 %). Abandoned buildings and homeless people make noise.
- **Spread** (`PollutionParameterData`: radii, multipliers, `m_DistanceExponent`): **air moves with the wind** (`AirPollutionSystem` advection with `m_WindAdvectionSpeed` over the `WindSystem` cell map) and fades (`m_AirFade`); ground pollution fades slowly (`m_GroundFade`); noise is rebuilt every update and does not accumulate.
- **Traffic pollution** (`NetPollutionSystem`): per road `Game.Net.Pollution` from traffic × `NetPollutionData.m_Factors`; tunnels and elevation change the spread. Road noise multipliers from upgrades (IL `CheckUpgrades`): sound barrier both sides ×(0, 0.5, 0), one side ×(0, 0.5, 1.5); street trees/grass both sides ×0.5, one side (0.5, 0.75, 1); median beautification ×0.875.
- **Trees and plants**: `ObjectPolluteSystem` makes plants absorb pollution into `Plant.m_Pollution` (they wither visibly); **the IL shows no removal from the pollution maps**, so trees are not a pollution fix (they do help attractiveness near attractions, §8, and halve road noise as a road upgrade).
- **Water**: an untreated sewage outlet pollutes surface water; pumps then deliver dirty water (*Dirty Water Pump*), which spreads through pipes and makes people sick (WaterPollution factor, health only; DirtyWater efficiency factor).
- **Groundwater** is polluted by ground pollution above it; the whole deposit becomes contaminated (§4.2).
- **Effects**: air and ground pollution hit **health only** (capped, × `PollutionHealthAffect`), noise hits **wellbeing only** (§11); ground pollution lowers land value (`m_GroundPollutionLandValueDivisor`); air/ground/noise icons above limits.

### Key metrics & where to read them
| Metric | Read via | Now |
|---|---|---|
| Air / ground / noise grids + worst cells | `/pollution?type=air|ground|noise` ✅ | ground and noise saturated over the north industry cluster (x 0…600, z 2800–3200) and at (−1600, −1200); air peaks at (200, 3800–4000) (landfill/incinerator cluster) (11:12) |
| Groundwater amount and pollution | `/groundwater` ✅ | 34 polluted cells under northern industry/landfills and near (−2700, −1600) |
| Pollution happiness factors | `/happiness` ✅ | Air −0.41, Noise −0.10, Ground 0, Water 0 |
| Noise icons | `/problems`, `/hotspots?type=Noise` ✅ | 18 (low-density homes at ≈ (−560, 2740)) |
| City averages | `PollutionInfoviewUISystem` ❌ | — |
| Surface-water pollution, wind field | `WaterSystem`, `WindSystem` ❌ | — |

### Levers
| Lever | API |
|---|---|
| Site or move polluters, rezone buffers | ✅ `/building`, `/bulldoze`, `/zone` |
| Road sound barriers, trees, grass | 🚧 `/roadupgrade` |
| Plant filter upgrades | ❌ (upgrades) |
| Advanced Pollution Management (city), Heavy Traffic Ban / Combustion Engine Ban / Speed Bumps (district) | ❌ (policies) |
| Wastewater treatment instead of outlets | ✅ `/building` |
| Industrial Waste Processing Site | ✅ unlock (8 pts) |

### Rules of thumb
- Industry, landfills and incinerators downwind and away from housing and groundwater deposits; leave a commercial/office buffer between industry and homes [GK].
- Noise is local and immediate: sound barriers along highways next to homes (both sides), street trees/grass on busy streets, Speed Bumps/Heavy Traffic Ban in residential districts.
- Air/ground pollution cost health (and so sickness, §5); noise costs wellbeing.
- Never place a pump on a deposit that touches industry; one polluted cell poisons the deposit.

### Live diagnosis
- The north industry cluster carries saturated ground pollution and noise, polluted groundwater beneath it, ~18 noise icons on bordering low-density homes and 5 burned-down buildings: a classic case for a buffer zone and Advanced Pollution Management.

---

## 11. Citizens & wellbeing — Alderman of Social Affairs

**Workers:** none of their own; reads every factor and briefs the other aldermen.
**Headline metrics:** average happiness and health, the most negative factors, move-away reasons.

### How the numbers are built (IL `CitizenHappinessSystem`)
- Runs every 16 frames on 1/16 of citizens. Each factor returns `int2(health, wellbeing)`. `GetHappinessFactor(f)` returns `float3(avg (h+w)/2, avg health, avg wellbeing) − HappinessFactorParameterData[f].m_BaseLevel`, averaged over **all** sampled citizens (integer averages). **So in `/happiness`: x = happiness contribution, y = health, z = wellbeing**, net of a hidden base level (Entertainment base ≈ 4, Healthcare ≈ 9, others ≈ 0, inferred). A small average can hide a big penalty on a minority. A factor is 0 while its service is locked.
- **Aggregation**: `wellbeingTarget = max(0, 50 + Σ wellbeing terms)`, `healthTarget = 50 + Σ health terms (+1 for bicycle users)`; local modifiers `LocalModifierType.Wellbeing/Health` (e.g. Welfare Office) and district modifier `Wellbeing` (Gated Community, Recreational Use) apply on top. Each value then steps ±1 per tick toward its target (biased random walk). Health is capped by age: <2 y 100, <3 y 90, <6 y 80, then 80 − 10 per year after 5. `Happiness = (wellbeing + health)/2`; city average = `/city.happiness`.
- Health-only factors (air/ground pollution, water pollution, sickness) and wellbeing-only factors count half toward happiness; Garbage, Water, Sewage, Healthcare, DeathPenalty and Homelessness hit both.

### The 26 factors (`CitizenHappinessSystem.HappinessFactor`)
`P.x` = `CitizenHappinessParameterData` field (prefab value unless marked live). H = health, W = wellbeing. cov(S) = service coverage on the home's road edge.

| Factor | Formula / driver (IL) | Where to read | Levers |
|---|---|---|---|
| Telecom | quality q = `TelecomCoverage.SampleNetworkQuality`; q ≥ base: W = round(((q−base)/(1−base))²·P.m_TelecomBonusMultiplier) else W = −round((1−q/base)²·P.m_TelecomPenaltyMultiplier) | `/happiness`; map ❌ | towers, masts, server farm in dense areas |
| Crime | W = −min(P.m_MaxCrimePenalty, round(max(0, crime(home) − P.m_NegligibleCrime)·P.m_CrimeMultiplier)) − CrimeVictim.m_Effect | `/happiness`; crime map ❌ | police coverage, wellbeing, jobs (§6) |
| AirPollution | H = −min(P.m_MaxAirAndGroundPollutionBonus, air(pos)/P.m_PollutionBonusDivisor) × `PollutionHealthAffect` | `/pollution?type=air` ✅ | siting, Advanced Pollution Management |
| Apartment | space per resident s = `m_SpaceMultiplier`·lot area/(household size·properties); W = 0.8·(4·(level−1) + 24.555 − 70.21/(1+(s/0.0369)^25.24)^0.01495). Each building level +3.2 W; cramped units go negative fast (L1: s 0.2 → −10, s 1.0 → +4, s 5 → +11; L5: s 0.2 → +3, s 5 → +24) | `/happiness` | level-ups, lower density, LowRent where needed |
| Electricity | W = −round(P.m_ElectricityWellbeingPenalty·saturate(cooldown/P.m_ElectricityPenaltyDelay)) | `/city`, `/problems` | supply + grid |
| Healthcare | H = round(P.m_HealthCareHealthMultiplier·cov), W = round(P.m_HealthCareWellbeingMultiplier·cov) (live 2 and 0.8); sickness: H −= health/2 while sick | `/coverage?service=Healthcare` ✅ | clinics/hospitals, low pollution |
| GroundPollution | as Air with ground map; health only | `/pollution?type=ground` ✅ | siting |
| NoisePollution | W = −min(P.m_MaxNoisePollutionBonus, noise/divisor); wellbeing only | `/pollution?type=noise` ✅ | siting, barriers, road greenery, district bans |
| Water | H/W = −round(penalty·saturate(cooldown/delay)) | `/city` ✅ | pumps + pipes |
| WaterPollution | H = round(`PollutionHealthAffect`·P.m_WaterPollutionBonusMultiplier·min(1, 10·pollution)) | ❌ per building | clean intakes, treatment |
| Sewage | H/W = −round(effect·saturate(cooldown/delay)) — ramps up over time | `/city` ✅ | outlets, treatment |
| Garbage | pen = max(0, (garbage − 500)/1000); H = W = −min(10, pen) (live 500/1000) | `/services?kind=garbage` ✅ | pickup reach, facilities, Recycling policy |
| Entertainment | W = round(P.m_EntertainmentWellbeingMultiplier·min(1, sqrt(cov(Park)·`Entertainment` modifier / 1.5))) (live multiplier 20); capped at cov ≥ 1.5 | `/coverage?service=Park` ✅ | parks near homes (now capped) |
| Education | W = round(sqrt(k)·P.m_EducationWellbeingMultiplier·(cov(Education) − P.m_NeutralEducation)), k = household size **only for children** (live 3 and 5) | `/coverage?service=Education` ✅ | school coverage in family areas |
| Mail | m = max(0, max(sending, receiving) − P.m_NegligibleMail); m < 25: W = ((25−m)²+62)/125 but 0 unless mail was delivered; m ≥ 25: W = −(min(50, m−25)²+125)/250; × P.m_MailMultiplier (≈ 3 → +15 max, −30 min) | `/services?kind=post` ✅ | post offices, vans, mailboxes |
| Welfare | W = round(P.m_WelfareMultiplier·cov(Welfare)·max(0, (50 − happiness)/50)) (live multiplier 2) — only for citizens below 50 | `/coverage?service=Welfare` ✅ | welfare offices where people are unhappy |
| Leisure | resident W = (m_LeisureCounter − 128)/16 (−8…+7); tourist +7; counter decays, rises when leisure is spent (`LeisureSystem`) | `/happiness` | reachable leisure of each `LeisureType`, free time |
| Tax | W = round((10 − residentialRate(education))·`TaxHappiness`·−P.m_Tax{Uneducated…HighlyEducated}Multiplier); neutral at 10 % | `/taxes` ✅ | residential tax (≈ +1.2 happiness per point below 10) |
| Buildings | students get `School.m_StudentHealth/Wellbeing`; jailed criminals get `Prison.m_PrisonerHealth/Wellbeing` | `/services` | school/prison upgrades |
| Consumption | W = clamp(round(curve(max(0, income − rent)/household size)), −40, 40) — disposable income per member | ❌ | wages (education × job level), rent (land value), jobs |
| TrafficPenalty | W = P.m_PenaltyEffect while `Citizen.m_PenaltyCounter` > 0; counter set to 255 when a citizen is stuck in a building **without road access** (teleported) or a **trip finds no path** | `/happiness`; causes ❌ | fix disconnected areas, one-way traps, buildings without road |
| DeathPenalty | H = −P.m_DeathHealthPenalty, W = −P.m_DeathWellbeingPenalty for every member of a household with a dead member, until collected | `/services?kind=deathcare` ✅ | hearses, cemetery/crematorium capacity |
| Homelessness | int2(P.m_HomelessHealthEffect, P.m_HomelessWellbeingEffect) + Apartment at s = 0.01 (≈ −36 W) | ❌ count | housing supply, LowRent; parks and abandoned buildings act as shelters |
| ElectricityFee | W = round(curve(fee/default)) | ❌ fees | electricity fee |
| WaterFee | H, W = round(curves(fee/default)) | ❌ fees | water fee |
| Unemployment | W = −min(P.m_MaxAccumulatedUnemployedWellbeingPenalty, unemployedDays·P.m_UnemployedWellbeingPenaltyAccumulatePerDay) | ❌ counts | jobs matching education (§2, §7) |

### What happiness drives (IL)
- **Move-out**: per household tick, leaves if `rand(0..999) < −53.35h + 5.408·sqrt(95.96h² + 1013h + 6576) − 298.5` (h = household average happiness): h 0 → 14 %, 10 → 4.5 %, 20 → 1.6 %, 25 → 0.7 %, ≥ 30 → ~0. **Below ~30 households start leaving.** Statistics carry a `MoveAwayReason` (NoSuitableProperty, NotHappy, NoAdults, NoMoney, TouristNoTarget, TouristNoHotel, TouristNoMoney, …).
- **Productivity**: worker output × (0.75 + happiness/200).
- **Crime**: probability peaks at wellbeing 25.
- **Education**: enrolment and dropout use wellbeing; graduation uses wellbeing/75 (§7).
- **Health**: low health → exponentially more sickness and death (§5).
- **Residential demand**: Happiness factor (above neutral raises demand); home choice (`HouseholdFindPropertySystem`) scores homes with the same ingredients.
- **XP**: happiness XP every update (§12); some unlocks have happiness requirements (`CitizenRequirementData.MinimumHappiness`).

### Key metrics & where to read them
| Metric | Read via | Now (19:20) |
|---|---|---|
| Average happiness / health | `/city` ✅ | 82 / 68 (78 / 62 at 05:33) |
| Factor averages | `/happiness` ✅ | worst: Unemployment −1.42, TrafficPenalty −0.90, Garbage −0.74, AirPollution −0.41; best: Mail +7.49, Consumption +6.40, Entertainment +6.00 (capped), Healthcare +4.63 |
| Base levels, parameters, per-building breakdown, maps | ❌ | — |
| Distributions, homeless, move-away reasons | `CountHouseholdDataSystem`, `CityStatisticsSystem` (`StatisticType` WellbeingLevel, HealthLevel, HomelessCount, MovedAwayReason, CitizensMovedIn/Away, BirthRate, DeathRate, Unemployed …) ❌ | — |

### Rules of thumb
- Read x, y and z together: y < 0 means a health problem (pollution, sickness), z < 0 a wellbeing problem.
- Fix the factors that hit **both** health and wellbeing first (garbage, sewage, water, death, homelessness).
- Entertainment and Mail are capped: more parks or post offices add nothing city-wide (they still fix local gaps and help tourism/land value).
- TrafficPenalty is a connectivity problem, not congestion: look for buildings without road access and failed pathfinding (`/services` failedPathfind counts, `/buildings` withoutRoad).
- One residential tax point ≈ +1.2 happiness city-wide.

### Live diagnosis
- **Unemployment** (−1.42) is now the worst factor: industrial demand cannot be served (no free industrial cells) and offices are oversupplied (§2).
- **TrafficPenalty** grew from −0.61 to −0.90 during the day; failed pathfinding is everywhere: post 103, park/road maintenance 843, prisoner transport 8, garbage 25, fire 6. Same root cause: unreachable spots or broken connections.
- Garbage improved from −2.36 to −0.74 after the morning's fixes.

---

## 12. Progression — Alderman of Development (milestones, development tree, unlocks)

**Workers:** XP planner, unlock strategist.
**Headline metrics:** XP to next milestone, unspent development points, expansion permits.

### Mechanics that matter
- `Game.Prefabs.MilestoneData` {m_Index, m_Reward (money), m_DevTreePoints, m_MapTiles (expansion permits), m_LoanLimit, m_XpRequried, m_Major, m_IsVictory}. 20 milestones; rewards 25k…500k cash; permits 3…56 per milestone.
- `MilestoneSystem.currentXP` is XP **above the last threshold**; progress = currentXP / (next − last).
- XP sources (`Game.Simulation.XPReason`): ServiceBuilding, ServiceUpgrade, ElectricityNetwork, Population, Happiness, Income, Road, TrainTrack, TramTrack, SubwayTrack, Waterway, Pipe, PowerLine.
  - One-off: `PlaceableObjectData.m_XPReward` per building (`XPBuiltSystem`), `PlaceableNetData.m_XPReward` per network length (`NetXPSystem`), signature buildings ("Building a Signature Building grants you XP").
  - Continuous (IL `XPAccumulationSystem/XPAccumulateJob`): when population ≥ 10, `XP_pop = floor(XPPerPopulation · max(0, population − XP.m_MaximumPopulation) / kUpdatesPerDay)` — **only new record population counts**; `XP_happiness = floor(XPPerHappiness · averageHappiness / kUpdatesPerDay)` every update. Income only updates `XP.m_MaximumIncome`; no XP for it in this build.
- **Development tree**: `DevTreeSystem.points`, `DevTreeNodeData` {m_Service, m_Cost}, `DevTreeNodeRequirement`. Basic nodes unlock automatically with milestones; the rest costs points (glossary *DevelopmentTree*).
- **Map tiles**: need money **and** expansion permits; purchased tiles add upkeep that grows with tile price and number owned; keep tiles connected (glossary *MapTiles*). Owned tiles reaching the map edge allow new outside connections.

### Key metrics & where to read them
| Metric | Read via | Now |
|---|---|---|
| Milestone, XP, progress | `/milestones`, `/city.milestone` ✅ | M14 achieved; 49,627 / 57,000 XP (87 %) to M15 (305,700) |
| Dev points, nodes, buyable | `/devtree` ✅ | **24 points unspent**, 79 spent |
| Dev points / loan limit per milestone | `MilestoneData.m_DevTreePoints`, `m_LoanLimit` 🟡 (not in `/milestones`) | — |
| Permits and tiles | `/tiles` ✅, `/areainfo` ✅ | 54 / 529 tiles owned, 125 permits available |
| XP per prefab | `PlaceableObjectData.m_XPReward` ❌ (not in `/prefabs`) | — |
| What unlocks a prefab (milestone, node, object/zone built, citizens) and progress | `GET /unlockinfo?name=` ✅ (one prefab per call) | e.g. `EU_CommercialHighSignature01`: needs EU Commercial High built, progress 0 |

### Levers
| Lever | API |
|---|---|
| Buy dev-tree node | ✅ `POST /devtree {node}` |
| Buy map tile | ✅ `POST /buytile` (dryRun for cost) |
| Earn XP: build services, upgrades, networks, signature buildings; raise happiness; grow population | ✅ build endpoints; ❌ service upgrades |
| Close milestone/signature popups | ✅ `/ui resume` |

### Dev tree status (live)
Buyable now (cost): TrainNode 1, EmergencyShelterNode 1, FirewatchTowerNode 1, HarborNode 2, ParkingAreasNode 2, GasPowerPlantNode 2, HydroelectricPowerPlantNode 2, AirportNode 4, IntersectionsNode 4, ServerFarmNode 4, GeologicalResearchStationNode 4, RadioTelescopeNode 4, CentralBankNode 8, DesalinationPlantNode 8, DiseaseControlCenterNode 8, IndustrialWasteProcessingNode 8. Locked behind those: LargeCoal 4 → Nuclear 8, Tram 2 → Subway 4, InternationalAirport 8, SpaceCenter 8, UndergroundParking 4 → AutomatedParking 8, LargeEmergencyShelter 4 → EarlyDisasterWarning 8, Heliport 2, SatelliteUplink 8, LargeHadronCollider 8, GrandBridge 8.

### Rules of thumb
- Spend points; unspent points do nothing. Cheapest high-value set for 24 points here: Train 1 + Harbor 2 + Airport 4 (tourism, cargo, imports/exports, uses existing map connections) + EmergencyShelter 1 + FirewatchTower 1 + ParkingAreas 2 + Intersections 4 + ServerFarm 4 (office/telecom) = 19; the remaining 5 cover Tram 2 + Heliport 2 (+1 spare). Desalination, Disease Control, Central Bank and Hazardous Waste (8 each) only when a domain shows a concrete need. (A starting proposal; the aldermen argue their case to the mayor.)
- XP to M15 (~7.4k at 19:20): happiness XP flows every update; one-off XP from every new service building, upgrade, road/pipe/power/track length, and from beating the population record.
- Buy tiles before you need them for industry/natural resources or outside connections; tile upkeep is recurring.

---

## 13. City policies — Policy clerk (reports to the mayor)

**Headline metric:** which policies are active and what they cost. **Today the AI cannot read or set any policy** — none has ever been used by our scripts.

### Mechanics (IL `Game.Policies`, `Game.Prefabs`)
- A policy is a `PolicyPrefab` with `PolicyData {m_Visibility}`, optional `PolicySliderData {m_Range, m_Default, m_Step, m_Unit}`, and option/modifier components. **The option component decides the scope**: `CityOptionData` (city), `DistrictOptionData` (district), `BuildingOptionData` (building), `RouteOptionData` (transport line). Effects come from `CityModifierData` / `DistrictModifierData` / building and route modifier data (`ModifierValueMode` Relative / Absolute / InverseRelative). Category enum `PolicyCategory`: CityPlanning, Budget, Traffic, Culture, Services.
- Active state: buffer `Game.Policies.Policy {m_Policy, m_Flags (Active), m_Adjustment}` on the City entity, district areas, buildings and routes. Changes are `Game.Policies.Modify` events (`ModifiedSystem`); modifiers are rebuilt by `City/District/Building/RouteModifierInitializeSystem`.
- **Public calls to toggle**: `Game.UI.InGame.PoliciesUISystem.SetCityPolicy(policy, active, adjustment)` and `SetPolicy(target, policy, active, adjustment)`. Unlock state = `Locked` on the policy prefab. "More policies are unlocked as you reach certain milestones"; "Some city policies are unlocked through city service buildings" (locale; e.g. City Hall unlocks City Promotion). Some policies have monthly upkeep (budget tooltip "Policy Upkeeps": "Total cost of active policies").
- Option enums: `CityOption` {UnlimitedHighwaySpeed, PaidTaxiStart, ImportOutsideServices}; `DistrictOption` {PaidParking, ForbidCombustionEngines, ForbidTransitTraffic, ForbidHeavyTraffic, ForbidBicycles}; `BuildingOption` {PaidParking, Inactive, Empty}; `RouteOption` {Day, Night, Inactive, PaidTicket}.
- Modifier enums: `CityModifierType` (41 values: Attractiveness, CrimeAccumulation, DisasterWarningTime, DisasterDamageRate, DiseaseProbability, ParkEntertainment, CriminalMonitorProbability, IndustrialAirPollution, IndustrialGroundPollution, IndustrialGarbage, RecoveryFailChange, Ore/OilResourceAmount, UniversityInterest, OfficeSoftwareDemand, IndustrialElectronicsDemand, Office/IndustrialElectronicsEfficiency, TelecomCapacity, Entertainment, HighwayTrafficSafety, PrisonTime, CrimeProbability, College/UniversityGraduation, ImportCost, LoanInterest, BuildingLevelingCost, ExportCost, TaxiStartingFee, IndustrialEfficiency, OfficeEfficiency, PollutionHealthAffect, HospitalEfficiency, fish modifiers, CityServiceImportCost, CityServiceBuildingBaseUpkeepCost, CrimeResponseTime, TaxHappiness) — fed by policies **and** by buildings (City Hall, Central Bank, CIB, universities, research). `DistrictModifierType`: GarbageProduction, ProductConsumption, ParkingFee, BuildingFireHazard, BuildingFireResponseTime, BuildingUpkeep, LowCommercialTax, Wellbeing, CrimeAccumulation, StreetSpeedLimit, StreetTrafficSafety, EnergyConsumptionAwareness, CarReserveProbability, BikeProbability. `RouteModifierType`: TicketPrice, VehicleInterval.

### The 22 policies in this build
Titles and effects are the game's own text (`Policy.TITLE/DESCRIPTION`). Scope is inferred from which option/modifier enum carries the effect — verify against the prefab once a `/policies` endpoint exists.

| Policy (UI title) | Scope | Effect (game text) | Option / modifier (inferred) | Who wants it |
|---|---|---|---|---|
| Advanced Pollution Management | city | industrial air & ground pollution −25 %, garbage +15 % | CityMod IndustrialAirPollution, IndustrialGroundPollution, IndustrialGarbage | Environment, Health |
| City Promotion | city (unlocked by City Hall) | city attractiveness +100 %, crime near attractions +20 % | CityMod Attractiveness | Tourism |
| High-Speed Highways | city | no highway speed limit; accidents +10 % | CityOption UnlimitedHighwaySpeed, CityMod HighwayTrafficSafety | Transport |
| Import City Services ("Paid Outside Services") | city | neighbours supply police, healthcare, deathcare, garbage, fire; cost based on population, per 1000 citizens | CityOption ImportOutsideServices; expenses Import*Service | Health, Safety, Public Works (stop-gap) |
| Pre-Release Programs | city | prisoners educated before release; less reoffending | CityMod RecoveryFailChange / CrimeProbability | Safety (needs a prison) |
| Taxi Base Fare ("Taxi Starting Fee", slider) | city | base fare per taxi ride | CityOption PaidTaxiStart, CityMod TaxiStartingFee | Transport, Finance |
| Urban Cycling Initiative | district (modifier type; text says "city promotes cycling") | more cycling instead of driving; minor health bonus | DistrictMod BikeProbability | Transport, Health |
| Energy Consumption Awareness | district | electricity consumption −5 % | DistrictMod EnergyConsumptionAwareness | Public Works |
| Recycling | district | less resource use (and garbage), less free time | DistrictMod GarbageProduction, ProductConsumption | Public Works |
| Small Business Enthusiast | district | slightly lower tax for low-density businesses | DistrictMod LowCommercialTax | Economy |
| Gated Community | district | only residents/workers enter; wellbeing up, crime down (city services unaffected) | DistrictOption ForbidTransitTraffic, DistrictMod Wellbeing, CrimeAccumulation | Social Affairs, Safety |
| Combustion Engine Ban | district | non-resident fuel vehicles banned; less noise and air pollution | DistrictOption ForbidCombustionEngines | Environment |
| Heavy Traffic Ban | district | trucks banned (highways exempt); less noise/air, less congestion | DistrictOption ForbidHeavyTraffic | Transport, Environment |
| Speed Bumps | district | lower speed, fewer accidents, much less noise | DistrictMod StreetSpeedLimit, StreetTrafficSafety | Environment, Transport |
| Fire Safety Sprinkler System | district | lower fire hazard, higher level-up cost | DistrictMod BuildingFireHazard | Safety |
| Recreational Use | district | adult-tourist attractiveness up, residents' wellbeing up | DistrictMod Wellbeing (+ attractiveness) | Tourism, Social Affairs |
| Roadside Parking Fee (slider) | district | fee for street parking | DistrictOption PaidParking, DistrictMod ParkingFee | Finance, Transport |
| Bicycle Traffic Restriction | district (also a road upgrade) | bikes off carriageways, onto lanes/paths | DistrictOption ForbidBicycles | Transport |
| Parking Fee ("Lot Parking Fee", slider) | building | fee on a parking lot | BuildingOption PaidParking, BuildingMod ParkingFee | Finance |
| Out of Service | building | building non-operational; upkeep, power and water drop to very low | BuildingOption Inactive | Finance |
| *(Emptying — building action)* | building | send stored garbage/bodies elsewhere | BuildingOption Empty | Public Works, Health |
| Ticket price (slider) | route | line ticket price → FeePublicTransport income | RouteOption PaidTicket, RouteMod TicketPrice | Transport, Finance |
| Vehicles (slider) | route | number of vehicles on the line (new ones need depot time) | RouteMod VehicleInterval | Transport |

### Levers and API status
| Lever | API |
|---|---|
| List policies with lock state, scope, slider range, active state | ❌ |
| Toggle a city policy / set its slider | ❌ (`PoliciesUISystem.SetCityPolicy`) |
| District policies | ❌ (needs districts first, §1) |
| Building actions (Out of Service, Emptying) | ❌ |
| Route sliders (ticket price, vehicles) | 🟡 read via `/lines`; set ❌ |
| Active city/district modifiers (what is in effect, including building effects) | ❌ (`Game.City.CityModifier` buffer) |

### Rules of thumb
- City-wide policies first (no districts needed): **Advanced Pollution Management** for the north industry cluster (§10), **City Promotion** once hotels exist (§8), **Import City Services** as a temporary patch for coverage holes (it costs per 1000 citizens), **Pre-Release Programs** after a prison is built.
- District policies need districts: Speed Bumps / Heavy Traffic Ban for quiet residential districts, Gated Community for wellbeing pockets, Small Business Enthusiast where commercial low struggles, Energy Awareness/Recycling only when power/garbage are tight.
- Out of Service idles over-built service buildings to save upkeep without demolishing them.

---

## Appendix A — endpoint inventory

As of 2026-11-20 (`Dispatch` in `mod/ClaudeCityApi/Api.cs`; 🚧 = merged teammate work still in progress).

| Group | Endpoints |
|---|---|
| Session | `GET /status`, `GET /maps`, `POST /newgame`, `POST /continue`, `GET /saves`, `POST /load`, `POST /save`, `POST /speed`, `POST /wait`, `POST /ui`, `POST /camera`, `POST /share`, `POST /reload` |
| Read the city | `GET /city`, `GET /map`, `GET /areainfo`, `POST /terrain`, `GET /nets`, `GET /prefabs`, `GET /buildings`, `GET /problems`, `GET /icontypes`, `GET /zonestats`, `GET /milestones`, `GET /happiness`, `GET /tiles`, `GET /devtree`, `GET /services`, `GET /coverage`, `GET /hotspots`, `GET /groundwater`, `GET /pollution`, `GET /unlockinfo` |
| Change the city | `POST /road`, `POST /upgrade` (replace road type), `POST /zone`, `POST /building`, `POST /bulldoze`, `POST /buytile`, `POST /devtree`, `POST /taxes`, `POST /budget` (`/taxes` and `/budget` read with GET) |
| 🚧 Teammates | `GET /findspot`, `GET /freeland` (where a building fits); `GET /roadupgrades`, `POST /roadupgrade` (edge upgrades); `GET /transitprefabs`, `GET /stops`, `POST /busstop`, `POST /busline`, `GET /lines` |

**Not reachable at all today**: policies, districts, service fees, loans, per-education/per-resource taxes, service building upgrades, building actions (Out of Service, Emptying, Historical), node intersection controls, non-bus transit lines and cargo routes, landscaping/trees/surfaces/terraforming, specialized industry areas, the Lot Tool, city statistics and info-view numbers, demand factors, land value, traffic flow, telecom/crime/fire-hazard maps.

## Appendix B — quick wins found while writing this

1. **24 development points unspent** — Train 1, Harbor 2, Airport 4, EmergencyShelter 1, FirewatchTower 1, ParkingAreas 2 are buyable now (`POST /devtree`). The map already has 2 rail, 2 ship and 2 air connections and 64 rail segments.
2. **No prison** although PrisonNode is owned: 8 prisoner transports fail and 9 of 10 police stations are out of patrol cars (Prison01, 650k).
3. **Unemployment is the worst factor** while industrial demand is 58 with 0.2 % free industrial cells and 14 % of office cells sit empty: zone industry (NE, downwind, per the masterplan), stop zoning office.
4. **Schools**: lower levels full with only small schools for ~95k people; build ElementarySchool01/HighSchool01 (the 8k/40k classroom extensions need the missing upgrade endpoint).
5. **Road maintenance**: one RoadMaintenanceDepot01 for ~2,300 road segments; the 800+ waiting "park" maintenance requests are mostly road work.
6. **Never-zoned zone types** (Commercial High, Office High, Mixed Old Town, Low Rent, Waterfront) keep 51 signature buildings locked (`/unlockinfo` shows e.g. `EU_CommercialHighSignature01` needs EU Commercial High built, progress 0).
7. **Unused but unlocked**: WaterPark01 (attraction), PostSortingFacility01 (node owned, 103 post requests failing), all nine specialized-industry hubs, IceflakeArena01 and the NA residential/mixed signature buildings (free, XP; verify they are not placed yet), FireStation01 (only station with the Disaster Response Unit).
8. **Policies never used** — all 22. Advanced Pollution Management and City Promotion are city-wide and need no districts.
9. **Failed pathfinding everywhere** (post 103, maintenance 843, prisoner 8, garbage 25, fire 6) and a rising TrafficPenalty point to unreachable or disconnected spots — worth a connectivity audit before adding buildings.
10. **Electricity**: 41 wind turbines plus 2 incinerators, no batteries; both incinerators show *Powerline Not Connected* (their output may not reach the grid).
