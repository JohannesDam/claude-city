using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using GB = Game.Buildings;
using GN = Game.Net;
using GP = Game.Prefabs;
using GV = Game.Vehicles;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Extra /services kinds: electricity, water, telecom (utility building status as the in-game
    /// ElectricitySection / BatterySection / WaterSection / SewageSection show it) and roads (road
    /// maintenance depots, road wear, maintenance requests split by what they are for).
    /// Hooked into Services.cs via CollectMoreServices / MoreServiceDemand / MoreServiceDiagnosis.
    /// </summary>
    public partial class Api
    {
        static readonly string[] MoreServiceKinds = { "electricity", "water", "telecom", "roads" };

        List<object> IconNames(Entity e)
        {
            var l = new List<object>();
            if (!em.HasBuffer<Game.Notifications.IconElement>(e)) return l;
            var buf = em.GetBuffer<Game.Notifications.IconElement>(e, true);
            for (int i = 0; i < buf.Length; i++)
            {
                var ic = buf[i].m_Icon;
                if (em.Exists(ic) && em.HasComponent<GP.PrefabRef>(ic) && !em.HasComponent<Deleted>(ic))
                    l.Add(PrefabName(em.GetComponentData<GP.PrefabRef>(ic).m_Prefab));
            }
            return l;
        }

        void AddIcons(SvcInfo s)
        {
            var ic = IconNames(s.Entity);
            if (ic.Count == 0) return;
            s.Data["icons"] = ic;
            foreach (var n in ic) s.Status.Add("icon: " + n);
        }

        void CollectMoreServices(string kind, List<SvcInfo> res)
        {
            switch (kind)
            {
                case "electricity":
                    {
                        var seen = new HashSet<Entity>();
                        foreach (var e in Facilities<GB.ElectricityProducer>()) { res.Add(PowerInfo(e)); seen.Add(e); }
                        foreach (var e in Facilities<GB.Battery>()) if (!seen.Contains(e)) { res.Add(BatteryOnlyInfo(e)); seen.Add(e); }
                        foreach (var e in Facilities<GB.EmergencyGenerator>()) if (!seen.Contains(e)) res.Add(GeneratorInfo(e));
                        foreach (var s0 in res) AddGridConnection(s0); // InsightServices.cs: HV/LV warnings + flow edge
                        break;
                    }
                case "water":
                    foreach (var e in Facilities<GB.WaterPumpingStation>()) res.Add(PumpInfo(e));
                    foreach (var e in Facilities<GB.SewageOutlet>()) res.Add(OutletInfo(e));
                    foreach (var e in Facilities<GB.WaterTower>()) res.Add(TowerInfo(e));
                    break;
                case "telecom":
                    foreach (var e in Facilities<GB.TelecomFacility>()) res.Add(TelecomInfo(e));
                    break;
                case "roads":
                    foreach (var e in Facilities<GB.MaintenanceDepot>()) { var s = RoadDepotInfo(e); if (s != null) res.Add(s); }
                    break;
            }
        }

        // ---- electricity

        string PowerType(Entity e, Entity prefab)
        {
            if (em.HasComponent<GP.WindPoweredData>(prefab)) return "wind";
            if (em.HasComponent<GP.SolarPoweredData>(prefab)) return "solar";
            if (em.HasComponent<GP.GarbagePoweredData>(prefab)) return "incinerator";
            if (em.HasComponent<GP.WaterPoweredData>(prefab)) return "hydro";
            if (em.HasComponent<GP.GroundWaterPoweredData>(prefab)) return "geothermal";
            if (em.HasComponent<GP.EmergencyGeneratorData>(prefab)) return "emergency generator";
            if (em.HasComponent<GP.BatteryData>(prefab) && !em.HasComponent<GP.PowerPlantData>(prefab)) return "battery";
            return "power plant";
        }

        Dictionary<string, object> BatteryJson(Entity e, Entity prefab)
        {
            var bat = em.GetComponentData<GB.Battery>(e);
            GP.BatteryData bd;
            GP.UpgradeUtils.TryGetCombinedComponent(em, e, prefab, out bd);
            float hours = 0;
            long ticks = bd.capacityTicks;
            if (bat.m_LastFlow > 0) hours = math.min((ticks - bat.m_StoredEnergy) / (float)bat.m_LastFlow / 2048f, 12f);
            else if (bat.m_LastFlow < 0) hours = math.min(bat.m_StoredEnergy / (float)(-bat.m_LastFlow) / 2048f, 12f);
            return new Dictionary<string, object>
            {
                { "charge", bat.storedEnergyHours }, { "capacity", bd.m_Capacity },
                { "pct", ticks > 0 ? Math.Round(100.0 * bat.m_StoredEnergy / ticks, 1) : 0.0 },
                { "flow", bat.m_LastFlow }, { "state", bat.m_LastFlow > 0 ? "charging" : bat.m_LastFlow < 0 ? "discharging" : "idle" },
                { "remainingTime", Math.Round(hours, 2) }, { "powerOutput", bd.m_PowerOutput },
            };
        }

        SvcInfo PowerInfo(Entity e)
        {
            var s = BaseInfo(e, "electricity", out var prefab, out var ve);
            var ep = em.GetComponentData<GB.ElectricityProducer>(e);
            s.Type = PowerType(e, prefab);
            s.Rate = ep.m_LastProduction;
            s.RateCap = ep.m_Capacity;
            int nominal = 0;
            if (GP.UpgradeUtils.TryGetCombinedComponent(em, e, prefab, out GP.PowerPlantData pp)) nominal += pp.m_ElectricityProduction;
            if (GP.UpgradeUtils.TryGetCombinedComponent(em, e, prefab, out GP.WindPoweredData wp)) { nominal += wp.m_Production; s.Data["maximumWind"] = Math.Round(wp.m_MaximumWind, 1); }
            if (GP.UpgradeUtils.TryGetCombinedComponent(em, e, prefab, out GP.SolarPoweredData sp)) nominal += sp.m_Production;
            if (GP.UpgradeUtils.TryGetCombinedComponent(em, e, prefab, out GP.GroundWaterPoweredData gw)) nominal += gw.m_Production;
            if (GP.UpgradeUtils.TryGetCombinedComponent(em, e, prefab, out GP.GarbagePoweredData gp)) s.Data["garbagePowered"] = new Dictionary<string, object> { { "capacity", gp.m_Capacity }, { "productionPerUnit", Math.Round(gp.m_ProductionPerUnit, 3) } };
            if (em.HasComponent<GB.WaterPowered>(e)) s.Data["waterPowered"] = true;
            if (nominal > 0)
            {
                s.Data["nominalProduction"] = nominal;
                s.Data["capacityPctOfNominal"] = Math.Round(100.0 * ep.m_Capacity / nominal, 1);
                if (ep.m_Capacity < 0.5 * nominal) s.Status.Add("capacity below half of nominal (see efficiencyFactors: WindSpeed/SunIntensity/workers/budget)");
            }
            s.Data["production"] = ep.m_LastProduction; // follows demand: low production with spare capacity is normal
            s.Data["capacity"] = ep.m_Capacity;         // already scaled by efficiency (wind, sun, workers, budget)
            if (ep.m_Capacity > 0) s.Data["productionPct"] = Math.Round(100.0 * ep.m_LastProduction / ep.m_Capacity, 1);
            if (em.HasComponent<GB.Battery>(e)) s.Data["battery"] = BatteryJson(e, prefab);
            AddIcons(s);
            return s;
        }

        SvcInfo BatteryOnlyInfo(Entity e)
        {
            var s = BaseInfo(e, "electricity", out var prefab, out var ve);
            s.Type = "battery";
            var bj = BatteryJson(e, prefab);
            s.Data["battery"] = bj;
            s.Stored = em.GetComponentData<GB.Battery>(e).storedEnergyHours;
            s.StoreCap = Convert.ToInt64(bj["capacity"]);
            AddIcons(s);
            return s;
        }

        SvcInfo GeneratorInfo(Entity e)
        {
            var s = BaseInfo(e, "electricity", out var prefab, out var ve);
            s.Type = "emergency generator";
            s.Rate = em.GetComponentData<GB.EmergencyGenerator>(e).m_Production;
            if (GP.UpgradeUtils.TryGetCombinedComponent(em, e, prefab, out GP.EmergencyGeneratorData gd))
            {
                s.RateCap = gd.m_ElectricityProduction;
                s.Data["activationThreshold"] = new List<object> { Math.Round(gd.m_ActivationThreshold.min, 2), Math.Round(gd.m_ActivationThreshold.max, 2) };
            }
            AddIcons(s);
            return s;
        }

        // ---- water

        SvcInfo PumpInfo(Entity e)
        {
            var s = BaseInfo(e, "water", out var prefab, out var ve);
            var ps = em.GetComponentData<GB.WaterPumpingStation>(e);
            GP.UpgradeUtils.TryGetCombinedComponent(em, e, prefab, out GP.WaterPumpingStationData pd);
            s.Type = (pd.m_Types & GP.AllowedWaterTypes.SurfaceWater) != 0 && (pd.m_Types & GP.AllowedWaterTypes.Groundwater) != 0 ? "pump (ground+surface)"
                : (pd.m_Types & GP.AllowedWaterTypes.SurfaceWater) != 0 ? "surface water pump" : "groundwater pump";
            s.Rate = ps.m_LastProduction;
            s.RateCap = ps.m_Capacity;
            s.Data["nominalCapacity"] = pd.m_Capacity;
            if (pd.m_Capacity > 0) s.Data["capacityPctOfNominal"] = Math.Round(100.0 * ps.m_Capacity / pd.m_Capacity, 1);
            s.Data["pollution"] = Math.Round(ps.m_Pollution, 3);
            s.Data["purification"] = Math.Round(pd.m_Purification, 3);
            if (ps.m_Pollution > 0.01f) s.Status.Add("delivers polluted water (" + Math.Round(ps.m_Pollution * 100, 1) + "%)");
            if (pd.m_Capacity > 0 && ps.m_Capacity < 0.5 * pd.m_Capacity) s.Status.Add("capacity below half of nominal (groundwater/water depth/efficiency)");
            AddIcons(s);
            return s;
        }

        SvcInfo OutletInfo(Entity e)
        {
            var s = BaseInfo(e, "water", out var prefab, out var ve);
            var so = em.GetComponentData<GB.SewageOutlet>(e);
            GP.UpgradeUtils.TryGetCombinedComponent(em, e, prefab, out GP.SewageOutletData sd);
            s.Type = em.HasComponent<GP.WastewaterTreatmentPlantData>(prefab) ? "treatment plant" : "sewage outlet";
            s.Rate = so.m_LastProcessed;
            s.RateCap = so.m_Capacity;
            s.Data["purified"] = so.m_LastPurified;
            s.Data["purification"] = Math.Round(sd.m_Purification, 3);
            if (so.m_Capacity > 0 && so.m_LastProcessed >= 0.95f * so.m_Capacity) s.Status.Add("at capacity");
            if (sd.m_Purification <= 0f) s.Status.Add("no purification (pollutes the water it drains into)");
            AddIcons(s);
            return s;
        }

        SvcInfo TowerInfo(Entity e)
        {
            var s = BaseInfo(e, "water", out var prefab, out var ve);
            var wt = em.GetComponentData<GB.WaterTower>(e);
            s.Type = "water tower";
            s.Stored = wt.m_StoredWater;
            s.Data["polluted"] = wt.m_Polluted;
            s.Data["lastStored"] = wt.m_LastStoredWater;
            if (wt.m_Polluted > 0) s.Status.Add("stores polluted water");
            AddIcons(s);
            return s;
        }

        // ---- telecom

        SvcInfo TelecomInfo(Entity e)
        {
            var s = BaseInfo(e, "telecom", out var prefab, out var ve);
            var tf = em.GetComponentData<GB.TelecomFacility>(e);
            GP.UpgradeUtils.TryGetCombinedComponent(em, e, prefab, out GP.TelecomFacilityData td);
            s.Type = td.m_PenetrateTerrain ? "satellite/antenna (penetrates terrain)" : "antenna";
            s.Data["range"] = Math.Round(td.m_Range);
            s.Data["networkCapacity"] = Math.Round(td.m_NetworkCapacity);
            s.Data["hasCoverage"] = (tf.m_Flags & GB.TelecomFacilityFlags.HasCoverage) != 0;
            if ((tf.m_Flags & GB.TelecomFacilityFlags.HasCoverage) == 0) s.Status.Add("no coverage (not working)");
            AddIcons(s);
            return s;
        }

        // ---- roads

        SvcInfo RoadDepotInfo(Entity e)
        {
            var prefab0 = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
            var md = Combined<GP.MaintenanceDepotData>(e, prefab0);
            if ((md.m_MaintenanceType & (MaintenanceType.Road | MaintenanceType.Snow | MaintenanceType.Vehicle)) == 0) return null;
            var s = BaseInfo(e, "roads", out var prefab, out var ve);
            s.Type = "road maintenance depot";
            CountVehicles(s, ComponentType.ReadOnly<GV.MaintenanceVehicle>(), md.m_VehicleCapacity, ve);
            if (md.m_VehicleCapacity > 0 && s.VehInUse >= s.VehUsable) s.Status.Add("no vehicles available");
            s.Data["maintenanceType"] = md.m_MaintenanceType.ToString();
            s.Data["vehicleEfficiency"] = Math.Round(md.m_VehicleEfficiency, 2);
            AddIcons(s);
            return s;
        }

        /// <summary>What a MaintenanceRequest is for: ParkAISystem (park), NetDeteriorationSystem (road), DamagedVehicleSystem (vehicle).</summary>
        string MaintTargetKind(Entity request)
        {
            var t = em.GetComponentData<MaintenanceRequest>(request).m_Target;
            if (t == Entity.Null || !em.Exists(t)) return "gone";
            if (em.HasComponent<GB.Park>(t)) return "park";
            if (em.HasComponent<GN.NetCondition>(t) || em.HasComponent<GN.Edge>(t)) return "road";
            if (em.HasComponent<GV.Vehicle>(t)) return "vehicle";
            return "other";
        }

        Dictionary<string, object> MaintenanceRequestsByTarget()
        {
            var d = new Dictionary<string, object>();
            foreach (var k in new[] { "road", "park", "vehicle", "other", "gone" })
            {
                var kk = k;
                var st = RequestStats<MaintenanceRequest>(r => MaintTargetKind(r) == kk);
                if (DInt(st, "waiting") + DInt(st, "dispatched") > 0) d[k] = st;
            }
            return d;
        }

        void RoadWear(Dictionary<string, object> d, int worstN)
        {
            // NetDeteriorationSystem: wear 0..10, a maintenance request when max(wear) > 1 (priority = wear*10 - 10)
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GN.NetCondition>(), ComponentType.ReadOnly<GN.Road>(), ComponentType.ReadOnly<GN.Edge>(),
                ComponentType.ReadOnly<GN.Curve>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Native>());
            var buckets = new int[5];
            var labels = new[] { "0-10%", "10-25%", "25-50%", "50-75%", "75-100%" };
            int total = 0, wantMaint = 0; double lenWorn = 0, lenAll = 0;
            var worst = new List<KeyValuePair<float, Entity>>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (em.HasComponent<Owner>(e)) continue; // building sub-nets
                    var w = math.cmax(em.GetComponentData<GN.NetCondition>(e).m_Wear) / 10f;
                    total++;
                    float len = em.GetComponentData<GN.Curve>(e).m_Length;
                    lenAll += len;
                    int b = w < 0.1f ? 0 : w < 0.25f ? 1 : w < 0.5f ? 2 : w < 0.75f ? 3 : 4;
                    buckets[b]++;
                    if (w > 0.1f) { wantMaint++; lenWorn += len; }
                    worst.Add(new KeyValuePair<float, Entity>(w, e));
                }
            var hist = new Dictionary<string, object>();
            for (int i = 0; i < 5; i++) hist[labels[i]] = buckets[i];
            d["roadSegments"] = total;
            d["wearHistogram"] = hist;
            d["segmentsWantingMaintenance"] = wantMaint;
            d["wornLengthPct"] = lenAll > 0 ? Math.Round(100.0 * lenWorn / lenAll, 1) : 0.0;
            d["worstRoads"] = worst.OrderByDescending(k => k.Key).Take(worstN).Select(k =>
            {
                var cv = em.GetComponentData<GN.Curve>(k.Value).m_Bezier;
                return (object)new Dictionary<string, object>
                {
                    { "id", Id(k.Value) }, { "prefab", PrefabName(em.GetComponentData<GP.PrefabRef>(k.Value).m_Prefab) },
                    { "wearPct", Math.Round(k.Key * 100, 1) }, { "pos", F2(MathUtils.Position(cv, 0.5f)) },
                };
            }).ToList();
        }

        void MoreServiceDemand(string kind, Dictionary<string, object> d)
        {
            switch (kind)
            {
                case "electricity":
                    {
                        var el = Sys<ElectricityStatisticsSystem>();
                        d["production"] = el.production;
                        d["consumption"] = el.consumption;
                        d["fulfilledConsumption"] = el.fulfilledConsumption;
                        d["batteryCharge"] = el.batteryCharge;
                        d["batteryCapacity"] = el.batteryCapacity;
                        d["note"] = "units as the in-game electricity panel (production/capacity of the buildings below use the same units)";
                        break;
                    }
                case "water":
                    {
                        var wa = Sys<WaterStatisticsSystem>();
                        d["freshCapacity"] = wa.freshCapacity;
                        d["freshConsumption"] = wa.freshConsumption;
                        d["fulfilledFreshConsumption"] = wa.fulfilledFreshConsumption;
                        d["sewageCapacity"] = wa.sewageCapacity;
                        d["sewageConsumption"] = wa.sewageConsumption;
                        d["fulfilledSewageConsumption"] = wa.fulfilledSewageConsumption;
                        break;
                    }
                case "roads":
                    RoadWear(d, 15);
                    d["maintenanceRequestsByTarget"] = MaintenanceRequestsByTarget();
                    d["requests"] = RequestStats<MaintenanceRequest>(r => { var k = MaintTargetKind(r); return k == "road" || k == "vehicle"; });
                    break;
            }
        }

        void MoreServiceDiagnosis(string kind, List<SvcInfo> list, Dictionary<string, object> demand, List<object> h)
        {
            if (kind == "electricity")
            {
                var below = list.Where(s => s.Status.Any(st => st.StartsWith("capacity below"))).ToList();
                if (below.Count > 0) h.Add($"{below.Count} producers below half of nominal capacity: " + string.Join(", ", below.Take(6).Select(s => s.Prefab + " " + Id(s.Entity))));
                int prod = DInt(demand, "production"), cons = DInt(demand, "consumption");
                if (cons > 0 && prod < cons) h.Add($"production {prod} below consumption {cons}");
                var iconed = list.Where(s => s.Data.ContainsKey("icons")).ToList();
                if (iconed.Count > 0) h.Add($"{iconed.Count} utility buildings show notification icons (see icons)");
            }
            if (kind == "water")
            {
                var dirty = list.Where(s => s.Status.Any(st => st.StartsWith("delivers polluted"))).ToList();
                if (dirty.Count > 0) h.Add($"{dirty.Count} pumps deliver polluted water: " + string.Join(", ", dirty.Take(6).Select(s => s.Prefab + " " + Id(s.Entity))));
                var noPur = list.Count(s => s.Status.Any(st => st.StartsWith("no purification")));
                if (noPur > 0) h.Add($"{noPur} sewage outlets without purification");
            }
            if (kind == "telecom")
            {
                var off = list.Count(s => s.Status.Any(st => st.StartsWith("no coverage")));
                if (off > 0) h.Add($"{off} telecom buildings without coverage");
            }
            if (kind == "roads")
            {
                if (demand.TryGetValue("maintenanceRequestsByTarget", out var mt) && mt is Dictionary<string, object> m && m.TryGetValue("road", out var rd) && rd is Dictionary<string, object> rds)
                {
                    int failed = DInt(rds, "failedPathfind");
                    if (failed > 0) h.Add($"{failed} road maintenance requests failed pathfinding: depot too far / unreachable roads");
                }
                if (list.Count == 0) h.Add("no road maintenance depot: road wear only grows");
            }
        }
    }
}
