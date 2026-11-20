using System;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using GB = Game.Buildings;
using GCit = Game.Citizens;
using GP = Game.Prefabs;
using GV = Game.Vehicles;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Additions to GET /services (hooked from Services.cs with one call each):
    /// per-building upkeep, destroyed/condemned facility alerts with the game's rebuild path,
    /// deathcare processing detail, electricity trade + per-building grid connection, police capacity
    /// summary (also used by GET /crime).
    /// </summary>
    public partial class Api
    {
        // ------------------------------------------------------------------ hooks called from Services.cs

        /// <summary>BaseInfo hook: upkeep, condemned/destroyed detail.</summary>
        void InsightBase(SvcInfo s, Entity e, Entity prefab)
        {
            try { var u = BuildingUpkeep(e, prefab); if (u != null) s.Data["upkeep"] = u; }
            catch (Exception ex) { s.Data["upkeepError"] = ex.GetType().Name + ": " + ex.Message; }
            if (em.HasComponent<GB.Condemned>(e)) s.Status.Add("condemned");
            if (em.HasComponent<Destroyed>(e))
            {
                try { s.Data["destroyed"] = DestroyedInfo(e, prefab); } catch { }
            }
        }

        /// <summary>ServiceTotals hook: summed upkeep per kind.</summary>
        void InsightTotals(List<SvcInfo> list, Dictionary<string, object> t)
        {
            long total = 0, wages = 0, bas = 0, mat = 0;
            int n = 0;
            foreach (var s in list)
            {
                if (!s.Data.TryGetValue("upkeep", out var uo) || !(uo is Dictionary<string, object> u)) continue;
                n++;
                total += Convert.ToInt64(u["perMonth"]);
                wages += Convert.ToInt64(u["wages"]);
                bas += Convert.ToInt64(u["base"]);
                if (u.TryGetValue("materials", out var m)) mat += Convert.ToInt64(m);
            }
            if (n > 0)
                t["upkeep"] = new Dictionary<string, object> { { "perMonth", total }, { "base", bas }, { "wages", wages }, { "materials", mat }, { "buildings", n } };
        }

        /// <summary>ServiceDemand hook.</summary>
        void InsightDemand(string kind, Dictionary<string, object> d)
        {
            switch (kind)
            {
                case "deathcare": DeathcareCityThroughput(d); break;
                case "police": d["summary"] = PoliceSummary(); break;
                case "electricity": ElectricityTrade(d); break;
            }
        }

        /// <summary>Diagnose hook (runs before the "no obvious problem" fallback).</summary>
        void InsightDiagnosis(string kind, List<SvcInfo> list, Dictionary<string, object> demand, List<object> h)
        {
            var destroyed = list.Where(s => s.Data.ContainsKey("destroyed")).ToList();
            foreach (var s in destroyed.Take(5))
            {
                var di = s.Data["destroyed"] as Dictionary<string, object>;
                h.Add($"{s.Prefab} {Id(s.Entity)} is DESTROYED: " + (di != null && di.TryGetValue("hint", out var hint) ? hint : "see destroyed"));
            }
            var condemned = list.Count(s => s.Status.Contains("condemned"));
            if (condemned > 0) h.Add($"{condemned} buildings condemned");
            if (kind == "deathcare" && demand.TryGetValue("throughput", out var to) && to is Dictionary<string, object> tp)
            {
                if (tp.TryGetValue("hint", out var th) && th != null) h.Add(th);
            }
            if (kind == "police")
            {
                int waitVan = list.Count(s => s.Status.Contains("waiting for prison van"));
                if (waitVan > 0)
                {
                    var pr = demand.TryGetValue("prisonerTransportRequests", out var o) ? o as Dictionary<string, object> : null;
                    int failed = pr != null ? DInt(pr, "failedPathfind") : 0;
                    h.Add($"{waitVan} police stations waiting for a prison van" + (failed > 0
                        ? $"; {failed} prisoner transport requests failed pathfinding (retried with exponential cooldown up to ~6 game hours): inspect GET /requests?kind=prisonerTransport, mayor can POST /requests/reset {{\"kind\":\"prisonerTransport\",\"dryRun\":true}}"
                        : ""));
                }
            }
            if (kind == "electricity")
            {
                bool Warn(SvcInfo s, string key) => s.Data.TryGetValue("grid", out var g) && g is Dictionary<string, object> gd && gd.TryGetValue(key, out var w) && w is bool b && b;
                var hv = list.Where(s => Warn(s, "highVoltageWarning")).ToList();
                if (hv.Count > 0) h.Add($"{hv.Count} buildings show the HIGH-voltage 'not connected' warning: connect a high-voltage powerline to their HV connection point: " + string.Join(", ", hv.Take(6).Select(s => s.Prefab + " " + Id(s.Entity))));
                var lv = list.Where(s => Warn(s, "lowVoltageWarning")).ToList();
                if (lv.Count > 0) h.Add($"{lv.Count} buildings show the LOW-voltage 'not connected' warning (their low-voltage connection point has no low-voltage line/road with power): " + string.Join(", ", lv.Take(6).Select(s => s.Prefab + " " + Id(s.Entity))));
                var disc = list.Where(s => s.Data.TryGetValue("grid", out var g) && g is Dictionary<string, object> gd && gd.TryGetValue("producerEdge", out var pe) && pe is Dictionary<string, object> ped && ped.TryGetValue("disconnected", out var dc) && dc is bool db && db).ToList();
                if (disc.Count > 0) h.Add($"{disc.Count} producers/batteries are disconnected from the consumers (flow edge Disconnected): " + string.Join(", ", disc.Take(6).Select(s => s.Prefab + " " + Id(s.Entity))));
            }
        }

        // ------------------------------------------------------------------ destroyed / condemned facilities

        bool? hasDisasterResponseCache;
        int hasDisasterResponseFrame = -1;

        /// <summary>DestroyedBuildingSection: rubble is cleared by a fire station whose prefab has m_DisasterResponseCapacity &gt; 0.</summary>
        bool HasDisasterResponse()
        {
            int f = UnityEngine.Time.frameCount;
            if (hasDisasterResponseCache.HasValue && hasDisasterResponseFrame == f) return hasDisasterResponseCache.Value;
            bool any = false;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GB.FireStation>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var fs in arr)
                {
                    var pf = em.GetComponentData<GP.PrefabRef>(fs).m_Prefab;
                    if (GP.UpgradeUtils.TryGetCombinedComponent(em, fs, pf, out GP.FireStationData fd) && fd.m_DisasterResponseCapacity > 0) { any = true; break; }
                }
            hasDisasterResponseCache = any; hasDisasterResponseFrame = f;
            return any;
        }

        Dictionary<string, object> DestroyedInfo(Entity e, Entity prefab)
        {
            var de = em.GetComponentData<Destroyed>(e);
            float cleared = math.max(0f, de.m_Cleared);
            int cost = em.HasComponent<GP.PlaceableObjectData>(prefab) ? (int)em.GetComponentData<GP.PlaceableObjectData>(prefab).m_ConstructionCost : 0;
            int rebuild = cost > 0 ? Game.Objects.ObjectUtils.GetRebuildCost(cost) : 0;
            string cause = null;
            if (de.m_Event != Entity.Null && em.Exists(de.m_Event) && em.HasComponent<GP.PrefabRef>(de.m_Event)) cause = PrefabName(em.GetComponentData<GP.PrefabRef>(de.m_Event).m_Prefab);
            string status, hint;
            if (cleared >= 1f)
            {
                status = "cleared - rebuild available";
                hint = $"rubble cleared; the game offers Rebuild (info panel button -> UpgradeToolSystem, CreationFlags.Repair) for about {rebuild} (ObjectUtils.GetRebuildCost: ~half the construction cost). Not exposed by this API yet: use the game UI, or bulldoze and place a new one.";
            }
            else if (!HasDisasterResponse())
            {
                status = "no service: rubble cannot be cleared";
                hint = "no fire station with disaster response capacity exists, so the rubble is never cleared and Rebuild never becomes available: build/upgrade a fire station with disaster response (e.g. a fire station with a disaster response unit), or bulldoze the ruin and place a new building.";
            }
            else
            {
                status = $"waiting for clearing ({Math.Round(cleared * 100)}%)";
                hint = $"rubble being cleared by a disaster-response fire unit ({Math.Round(cleared * 100)}%); after 100% the game offers Rebuild for about {rebuild}. Alternative: bulldoze and place new.";
            }
            var d = new Dictionary<string, object>
            {
                { "clearedPct", Math.Round(cleared * 100, 1) }, { "status", status }, { "rebuildAvailable", cleared >= 1f },
                { "rebuildCostEstimate", rebuild }, { "constructionCost", cost }, { "hint", hint },
            };
            if (cause != null) d["cause"] = cause;
            return d;
        }

        /// <summary>All destroyed or condemned city-service buildings (any kind), for the top level of /services.</summary>
        List<object> FacilityAlerts()
        {
            var res = new List<object>();
            var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.ReadOnly<ObjTransform>() },
                Any = new[] { ComponentType.ReadOnly<Destroyed>(), ComponentType.ReadOnly<GB.Condemned>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<GB.ServiceUpgrade>() },
            });
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var prefab = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
                    if (!em.HasComponent<GP.ServiceObjectData>(prefab)) continue;
                    var d = new Dictionary<string, object>
                    {
                        { "id", Id(e) }, { "prefab", PrefabName(prefab) }, { "service", PrefabName(em.GetComponentData<GP.ServiceObjectData>(prefab).m_Service) },
                        { "pos", F2(em.GetComponentData<ObjTransform>(e).m_Position) },
                    };
                    if (em.HasComponent<Destroyed>(e)) { d["state"] = "destroyed"; foreach (var kv in DestroyedInfo(e, prefab)) d[kv.Key] = kv.Value; }
                    else { d["state"] = "condemned"; d["hint"] = "condemned buildings are demolished by the game (CondemnedBuildingSystem); place a replacement elsewhere"; }
                    res.Add(d);
                    if (res.Count >= 50) break;
                }
            return res;
        }

        /// <summary>Services() hook: top-level "facilityAlerts" when any service building is destroyed/condemned.</summary>
        void AddFacilityAlerts(Dictionary<string, object> r)
        {
            try { var a = FacilityAlerts(); if (a.Count > 0) r["facilityAlerts"] = a; }
            catch (Exception ex) { r["facilityAlertsError"] = ex.GetType().Name + ": " + ex.Message; }
        }

        // ------------------------------------------------------------------ deathcare

        /// <summary>
        /// DeathcareFacilityAISystem (every 256 frames = 1/1024 day): m_ProcessingState += efficiency x
        /// m_ProcessingRate / 1024; one queued body (Patient buffer) is cremated per whole unit; the state
        /// resets to 0 whenever nothing is queued. So m_ProcessingRate is bodies/day at full speed, and the
        /// in-game panel's "processing speed" is 0 whenever no body happens to be queued at that instant.
        /// </summary>
        void DeathcareProcessing(SvcInfo s, Entity e, GP.DeathcareFacilityData dd)
        {
            int queued = Buf<GB.Patient>(e);
            int inbound = 0, collecting = 0;
            if (em.HasBuffer<GV.OwnedVehicle>(e))
            {
                var buf = em.GetBuffer<GV.OwnedVehicle>(e, true);
                for (int i = 0; i < buf.Length; i++)
                {
                    var v = buf[i].m_Vehicle;
                    if (!em.Exists(v) || !em.HasComponent<GV.Hearse>(v)) continue;
                    var st = em.GetComponentData<GV.Hearse>(v).m_State;
                    if ((st & GV.HearseFlags.Transporting) != 0) inbound++;
                    else if ((st & GV.HearseFlags.Dispatched) != 0) collecting++;
                }
            }
            float eff = dd.m_ProcessingRate * s.Efficiency;
            s.RateCap = dd.m_ProcessingRate;
            s.Rate = queued > 0 ? eff : 0f;
            if (dd.m_ProcessingRate <= 0f)
            {
                // cemetery: no processing, bodies are stored (LongTermStoredCount) until full
                var it = new Dictionary<string, object> { { "queued", queued }, { "hearsesBringingBodies", inbound }, { "hearsesCollecting", collecting } };
                if (deathcareMeasured != null && deathcareMeasured.TryGetValue(s.Entity, out var mc)) it["measured"] = mc;
                s.Data["intake"] = it;
                return;
            }
            var p = new Dictionary<string, object>
            {
                { "unit", "bodies/day" },
                { "rate", Math.Round(s.Rate, 1) },
                { "capacity", Math.Round(dd.m_ProcessingRate, 1) },
                { "effectiveCapacity", Math.Round(eff, 1) },
                { "queued", queued },
                { "hearsesBringingBodies", inbound },
                { "hearsesCollecting", collecting },
                { "state", queued > 0 ? "processing" : inbound > 0 ? "idle between deliveries (body on its way)" : "idle (nothing queued)" },
            };
            if (eff > 0f) p["minutesPerBody"] = Math.Round(1440.0 / eff, 1);
            if (deathcareMeasured != null && deathcareMeasured.TryGetValue(s.Entity, out var m)) p["measured"] = m;
            else p["measured"] = "run GET /deathcare?seconds=30 to measure bodies/day per facility";
            s.Data["processing"] = p;
        }

        void DeathcareCityThroughput(Dictionary<string, object> d)
        {
            int deaths = 0;
            try { deaths = Sys<CityStatisticsSystem>().GetStatisticValue(Game.City.StatisticType.DeathRate, 0); } catch { }
            float cremCap = 0, cremEff = 0; long cemFree = 0; int hearsesTransporting = 0, queued = 0;
            foreach (var e in Facilities<GB.DeathcareFacility>())
            {
                var prefab = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
                var dd = Combined<GP.DeathcareFacilityData>(e, prefab);
                float eff = em.HasBuffer<GB.Efficiency>(e) ? GB.BuildingUtils.GetEfficiency(em.GetBuffer<GB.Efficiency>(e, true)) : 1f;
                if (em.HasComponent<Destroyed>(e)) eff = 0f;
                cremCap += dd.m_ProcessingRate; cremEff += dd.m_ProcessingRate * eff;
                var df = em.GetComponentData<GB.DeathcareFacility>(e);
                int bodies = df.m_LongTermStoredCount + Buf<GB.Patient>(e);
                queued += Buf<GB.Patient>(e);
                if (dd.m_LongTermStorage) cemFree += math.max(0, dd.m_StorageCapacity - bodies);
                if (em.HasBuffer<GV.OwnedVehicle>(e))
                {
                    var buf = em.GetBuffer<GV.OwnedVehicle>(e, true);
                    for (int i = 0; i < buf.Length; i++)
                    {
                        var v = buf[i].m_Vehicle;
                        if (em.Exists(v) && em.HasComponent<GV.Hearse>(v) && (em.GetComponentData<GV.Hearse>(v).m_State & GV.HearseFlags.Transporting) != 0) hearsesTransporting++;
                    }
                }
            }
            var t = new Dictionary<string, object>
            {
                { "deathsPerDay", deaths },
                { "processingCapacityPerDay", Math.Round(cremCap) },
                { "effectiveProcessingCapacityPerDay", Math.Round(cremEff) },
                { "cemeteryFreeSpace", cemFree },
                { "bodiesQueuedAtFacilities", queued },
                { "hearsesBringingBodies", hearsesTransporting },
                { "note", "deathsPerDay = StatisticType.DeathRate (daily). Bodies go to the nearest facility with room: crematoria destroy them (capacity = bodies/day), cemeteries store them until full. Measured per-facility bodies/day: GET /deathcare?seconds=30." },
            };
            if (deaths > 0 && cremEff > 0)
            {
                double share = 100.0 * deaths / cremEff;
                t["deathsVsProcessingPct"] = Math.Round(share, 1);
                if (share > 100) t["hint"] = $"deaths ({deaths}/day) exceed crematorium capacity ({Math.Round(cremEff)}/day): the excess fills cemeteries ({cemFree} places left)";
            }
            if (deathcareMeasuredSummary != null) t["lastMeasurement"] = deathcareMeasuredSummary;
            d["throughput"] = t;
        }

        // ------------------------------------------------------------------ electricity

        void ElectricityTrade(Dictionary<string, object> d)
        {
            var ts = Sys<ElectricityTradeSystem>();
            int ex = ts.export, im = ts.import;
            int conns = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Objects.ElectricityOutsideConnection>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()).CalculateEntityCount();
            d["trade"] = new Dictionary<string, object>
            {
                { "export", ex }, { "import", im }, { "net", ex - im }, { "outsideConnections", conns },
                { "note", "ElectricityTradeSystem.export/import (what the electricity info view shows), same units as production/consumption. Export earns and import costs money via the service fee system." },
            };
        }

        /// <summary>Per producer/battery: HV/LV "not connected" warnings (ConnectionWarningSystem icons) and the flow edge into the grid.</summary>
        void AddGridConnection(SvcInfo s)
        {
            var e = s.Entity;
            var g = new Dictionary<string, object>();
            // connection points: building sub-net nodes whose prefab requires a powerline layer
            // (what ConnectionWarningSystem.CheckOwnersJob checks: NetData.m_RequiredLayers PowerlineHigh/PowerlineLow)
            bool hvPoint = false, lvPoint = false;
            if (em.HasBuffer<Game.Net.SubNet>(e))
            {
                var sn = em.GetBuffer<Game.Net.SubNet>(e, true);
                for (int i = 0; i < sn.Length; i++)
                {
                    var n = sn[i].m_SubNet;
                    if (!em.Exists(n) || !em.HasBuffer<Game.Net.ConnectedEdge>(n) || !em.HasComponent<GP.PrefabRef>(n)) continue;
                    var np = em.GetComponentData<GP.PrefabRef>(n).m_Prefab;
                    if (!em.HasComponent<GP.NetData>(np)) continue;
                    var req = em.GetComponentData<GP.NetData>(np).m_RequiredLayers;
                    if ((req & Game.Net.Layer.PowerlineHigh) != 0) hvPoint = true;
                    if ((req & Game.Net.Layer.PowerlineLow) != 0) lvPoint = true;
                }
            }
            if (TrySingleton<GP.ElectricityParameterData>(out var ep))
            {
                bool hv = false, lv = false;
                if (em.HasBuffer<Game.Notifications.IconElement>(e))
                {
                    var buf = em.GetBuffer<Game.Notifications.IconElement>(e, true);
                    for (int i = 0; i < buf.Length; i++)
                    {
                        var ic = buf[i].m_Icon;
                        if (!em.Exists(ic) || !em.HasComponent<GP.PrefabRef>(ic) || em.HasComponent<Deleted>(ic)) continue;
                        var ip = em.GetComponentData<GP.PrefabRef>(ic).m_Prefab;
                        if (ip == ep.m_HighVoltageNotConnectedPrefab) hv = true;
                        if (ip == ep.m_LowVoltageNotConnectedPrefab) lv = true;
                    }
                }
                g["highVoltageWarning"] = hv;
                g["lowVoltageWarning"] = lv;
                g["highVoltagePoint"] = hvPoint || hv;
                g["highVoltageConnected"] = hv ? (object)false : hvPoint ? (object)true : null; // null = no HV connection point found
                if (lvPoint || lv) g["lowVoltageConnected"] = !lv;
            }
            if (em.HasComponent<ElectricityBuildingConnection>(e))
            {
                var bc = em.GetComponentData<ElectricityBuildingConnection>(e);
                Dictionary<string, object> Edge(Entity edge)
                {
                    if (edge == Entity.Null || !em.Exists(edge) || !em.HasComponent<ElectricityFlowEdge>(edge)) return null;
                    var fe = em.GetComponentData<ElectricityFlowEdge>(edge);
                    return new Dictionary<string, object>
                    {
                        { "capacity", fe.m_Capacity }, { "flow", fe.m_Flow },
                        { "bottleneck", fe.isBottleneck }, { "beyondBottleneck", fe.isBeyondBottleneck }, { "disconnected", fe.isDisconnected },
                    };
                }
                var pe = Edge(bc.m_ProducerEdge); if (pe != null) g["producerEdge"] = pe;
                var ce = Edge(bc.m_ChargeEdge); if (ce != null) g["chargeEdge"] = ce;
                var de = Edge(bc.m_DischargeEdge); if (de != null) g["dischargeEdge"] = de;
                g["transformer"] = bc.m_TransformerNode != Entity.Null;
            }
            else g["flowConnection"] = "none (building not in the electricity flow graph)";
            if (g.Count > 0) s.Data["grid"] = g;
        }

        // ------------------------------------------------------------------ police

        /// <summary>The police info view numbers: criminals by state, jail and prison occupancy, transport/patrol backlog.</summary>
        Dictionary<string, object> PoliceSummary()
        {
            int criminals = 0, planning = 0, robbers = 0, arrested = 0, sentenced = 0, prisoners = 0, monitored = 0;
            var cq = em.CreateEntityQuery(ComponentType.ReadOnly<GCit.Criminal>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = cq.ToComponentDataArray<GCit.Criminal>(Allocator.Temp))
                foreach (var c in arr)
                {
                    criminals++;
                    var f = c.m_Flags;
                    if ((f & GCit.CriminalFlags.Prisoner) != 0) { prisoners++; continue; }
                    if ((f & GCit.CriminalFlags.Sentenced) != 0) sentenced++;
                    if ((f & GCit.CriminalFlags.Arrested) != 0) arrested++;
                    if ((f & GCit.CriminalFlags.Robber) != 0) robbers++;
                    if ((f & (GCit.CriminalFlags.Planning | GCit.CriminalFlags.Preparing)) != 0) planning++;
                    if ((f & GCit.CriminalFlags.Monitored) != 0) monitored++;
                }
            int jailCap = 0, jailed = 0, stationsWaitingVan = 0, jailedWaitingVan = 0, prisonCap = 0, inPrison = 0, vansFree = 0, vansCap = 0;
            foreach (var e in Facilities<GB.PoliceStation>())
            {
                var prefab = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
                var pd = Combined<GP.PoliceStationData>(e, prefab);
                int occ = Buf<GB.Occupant>(e);
                jailCap += pd.m_JailCapacity; jailed += occ;
                if ((em.GetComponentData<GB.PoliceStation>(e).m_Flags & GB.PoliceStationFlags.NeedPrisonerTransport) != 0) { stationsWaitingVan++; jailedWaitingVan += occ; }
            }
            foreach (var e in Facilities<GB.Prison>())
            {
                var prefab = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
                var pd = Combined<GP.PrisonData>(e, prefab);
                prisonCap += pd.m_PrisonerCapacity; inPrison += Buf<GB.Occupant>(e);
                vansCap += pd.m_PrisonVanCapacity;
                if (em.HasBuffer<GV.OwnedVehicle>(e))
                {
                    var buf = em.GetBuffer<GV.OwnedVehicle>(e, true);
                    int moving = 0;
                    for (int i = 0; i < buf.Length; i++) { var v = buf[i].m_Vehicle; if (em.Exists(v) && em.HasComponent<GV.PrisonerTransport>(v) && !em.HasComponent<GV.ParkedCar>(v)) moving++; }
                    vansFree += math.max(0, pd.m_PrisonVanCapacity - moving);
                }
            }
            var css = Sys<CityStatisticsSystem>();
            int Stat(Game.City.StatisticType t) { try { return css.GetStatisticValue(t, 0); } catch { return -1; } }
            return new Dictionary<string, object>
            {
                { "criminals", new Dictionary<string, object> { { "total", criminals }, { "planningOrPreparing", planning }, { "robbers", robbers }, { "monitored", monitored }, { "arrested", arrested }, { "sentencedAwaitingPrison", sentenced }, { "prisoners", prisoners } } },
                { "jail", new Dictionary<string, object> { { "occupied", jailed }, { "capacity", jailCap }, { "pct", jailCap > 0 ? Math.Round(100.0 * jailed / jailCap, 1) : 0.0 }, { "stationsWaitingForPrisonVan", stationsWaitingVan }, { "jailedInThoseStations", jailedWaitingVan } } },
                { "prison", new Dictionary<string, object> { { "occupied", inPrison }, { "capacity", prisonCap }, { "pct", prisonCap > 0 ? Math.Round(100.0 * inPrison / prisonCap, 1) : 0.0 }, { "vansIdle", vansFree }, { "vanCapacity", vansCap } } },
                { "requests", new Dictionary<string, object> {
                    { "patrol", RequestStats<PolicePatrolRequest>() },
                    { "emergency", RequestStats<PoliceEmergencyRequest>() },
                    { "prisonerTransport", RequestStats<PrisonerTransportRequest>() } } },
                { "stats", new Dictionary<string, object> { { "crimesPerDay", Stat(Game.City.StatisticType.CrimeCount) }, { "escapedArrestPerDay", Stat(Game.City.StatisticType.EscapedArrestCount) }, { "crimeRate", Stat(Game.City.StatisticType.CrimeRate) } } },
                { "legend", "jail = police station cells (Occupant buffer); sentenced criminals wait in jail for a prison van (PrisonerTransportRequest from the station); prison = Prison occupants. requests: waiting = no vehicle assigned yet, failedPathfind = at least one failed dispatch (details: GET /requests?kind=policePatrol|prisonerTransport)." },
            };
        }
    }
}
