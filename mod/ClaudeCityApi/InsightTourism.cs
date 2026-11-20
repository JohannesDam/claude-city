using System;
using System.Collections.Generic;
using System.Linq;
using Game.City;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using GB = Game.Buildings;
using GCo = Game.Companies;
using GP = Game.Prefabs;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// GET /tourism?limit=15 : the tourism info view and the numbers behind it.
    /// City attractiveness (TourismSystem.TourismJob): S = sum over all AttractivenessProvider of (a/100)^2,
    /// attractiveness = 200 / (1 + exp(-0.3 S)) - 100, then CityModifier Attractiveness; Tourism.m_Lodging =
    /// (rooms used, rooms total) over hotels (LodgingProvider: renters + free rooms). Per attraction the
    /// AttractivenessSection formula: base AttractionData (with upgrades) x building efficiency (not for
    /// signature buildings) x park maintenance factor x (1 + 1% x terrain bonus (forest, shore, height)).
    /// </summary>
    public partial class Api
    {
        object TourismEndpoint(Pending p)
        {
            int limit = (int)math.clamp(p.QF("limit", 15f), 1f, 200f);
            var city = Sys<CitySystem>().City;
            var r = new Dictionary<string, object>();
            Tourism tour = default;
            bool hasTour = city != Entity.Null && em.HasComponent<Tourism>(city);
            if (hasTour) tour = em.GetComponentData<Tourism>(city);

            // ---- attractions
            var aq = em.CreateEntityQuery(ComponentType.ReadOnly<GB.AttractivenessProvider>(), ComponentType.ReadOnly<GP.PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var rows = new List<Dictionary<string, object>>();
            double sumSq = 0;
            bool terrainOk = false;
            CellMapData<TerrainAttractiveness> terrain = default;
            TerrainHeightData heights = default;
            GP.AttractivenessParameterData ap = default;
            try
            {
                if (TrySingleton(out ap))
                {
                    JobHandle deps;
                    terrain = Sys<TerrainAttractivenessSystem>().GetData(true, out deps);
                    deps.Complete();
                    heights = Sys<TerrainSystem>().GetHeightData(false);
                    terrainOk = true;
                }
            }
            catch { }
            var factors = new NativeArray<int>(5, Allocator.Temp);
            try
            {
                using (var ents = aq.ToEntityArray(Allocator.Temp))
                using (var provs = aq.ToComponentDataArray<GB.AttractivenessProvider>(Allocator.Temp))
                    for (int i = 0; i < ents.Length; i++)
                    {
                        var e = ents[i];
                        int a = provs[i].m_Attractiveness;
                        sumSq += (double)a * a / 10000.0;
                        if (a <= 0) continue;
                        var prefab = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
                        var d = new Dictionary<string, object> { { "id", Id(e) }, { "prefab", PrefabName(prefab) }, { "attractiveness", a } };
                        if (em.HasComponent<ObjTransform>(e)) d["pos"] = F2(em.GetComponentData<ObjTransform>(e).m_Position);
                        if (em.HasComponent<GB.Signature>(e)) d["signature"] = true;
                        // factor breakdown as AttractivenessSection
                        for (int k = 0; k < factors.Length; k++) factors[k] = 0;
                        float baseA = 0f;
                        if (GP.UpgradeUtils.TryGetCombinedComponent(em, e, prefab, out GP.AttractionData ad)) baseA = ad.m_Attractiveness;
                        float val = baseA;
                        if (!em.HasComponent<GB.Signature>(e) && em.HasBuffer<GB.Efficiency>(e))
                        {
                            float eff = GB.BuildingUtils.GetEfficiency(em.GetBuffer<GB.Efficiency>(e, true));
                            val *= eff;
                            AttractionSystem.SetFactor(factors, AttractionSystem.AttractivenessFactor.Efficiency, (eff - 1f) * 100f);
                        }
                        if (em.HasComponent<GB.Park>(e) && GP.UpgradeUtils.TryGetCombinedComponent(em, e, prefab, out GP.ParkData pd))
                        {
                            float m = pd.m_MaintenancePool > 0 ? (float)em.GetComponentData<GB.Park>(e).m_Maintenance / pd.m_MaintenancePool : 0f;
                            float mf = math.min(1f, 0.25f + 0.25f * math.floor(m / 0.3f));
                            val *= mf;
                            AttractionSystem.SetFactor(factors, AttractionSystem.AttractivenessFactor.Maintenance, (mf - 1f) * 100f);
                            d["parkMaintenance"] = Math.Round(m, 2);
                        }
                        if (terrainOk && em.HasComponent<ObjTransform>(e))
                        {
                            float tb = TerrainAttractivenessSystem.EvaluateAttractiveness(em.GetComponentData<ObjTransform>(e).m_Position, terrain, heights, ap, factors);
                            val *= 1f + 0.01f * tb;
                        }
                        d["base"] = (int)baseA;
                        var fd = new Dictionary<string, object>();
                        for (int k = 0; k < factors.Length; k++)
                            if (factors[k] != 0) fd[((AttractionSystem.AttractivenessFactor)k).ToString()] = factors[k];
                        if (fd.Count > 0) d["factorsPct"] = fd;
                        d["computed"] = (int)math.round(val);
                        rows.Add(d);
                    }
            }
            finally { factors.Dispose(); }
            double cityRaw = 200.0 / (1.0 + Math.Exp(-0.3 * sumSq)) - 100.0;
            foreach (var d in rows)
            {
                double a = Convert.ToDouble(d["attractiveness"]);
                d["sharePct"] = sumSq > 0 ? Math.Round(100.0 * (a * a / 10000.0) / sumSq, 1) : 0.0;
            }
            // marginal effect of one more attraction of attractiveness A: dAttr = f(S + (A/100)^2) - f(S)
            Func<double, double> f = s => 200.0 / (1.0 + Math.Exp(-0.3 * s)) - 100.0;
            var marginal = new Dictionary<string, object>();
            foreach (var A in new[] { 50, 100, 200, 400 }) marginal["+" + A] = Math.Round(f(sumSq + A * A / 10000.0) - f(sumSq), 2);

            // ---- hotels
            var hq = em.CreateEntityQuery(ComponentType.ReadOnly<GCo.LodgingProvider>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            int hotels = 0, used = 0, total = 0;
            long priceSum = 0;
            var hotelRows = new List<Dictionary<string, object>>();
            using (var ents = hq.ToEntityArray(Allocator.Temp))
            using (var lps = hq.ToComponentDataArray<GCo.LodgingProvider>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    int renters = em.HasBuffer<GB.Renter>(e) ? em.GetBuffer<GB.Renter>(e, true).Length : 0;
                    int rooms = renters + lps[i].m_FreeRooms;
                    hotels++; used += renters; total += rooms; priceSum += lps[i].m_Price;
                    var d = new Dictionary<string, object> { { "company", Id(e) }, { "rooms", rooms }, { "used", renters }, { "price", lps[i].m_Price } };
                    if (em.HasComponent<GB.PropertyRenter>(e))
                    {
                        var prop = em.GetComponentData<GB.PropertyRenter>(e).m_Property;
                        if (prop != Entity.Null && em.Exists(prop))
                        {
                            d["building"] = Id(prop);
                            if (em.HasComponent<GP.PrefabRef>(prop)) d["prefab"] = PrefabName(em.GetComponentData<GP.PrefabRef>(prop).m_Prefab);
                            if (em.HasComponent<ObjTransform>(prop)) d["pos"] = F2(em.GetComponentData<ObjTransform>(prop).m_Position);
                        }
                    }
                    hotelRows.Add(d);
                }

            var css = Sys<CityStatisticsSystem>();
            int Stat(StatisticType t) { try { return css.GetStatisticValue(t, 0); } catch { return -1; } }
            var cityBlock = new Dictionary<string, object>
            {
                { "attractiveness", hasTour ? tour.m_Attractiveness : (int)Math.Round(cityRaw) },
                { "attractivenessBeforeModifiers", Math.Round(cityRaw, 1) },
                { "attractivenessScore", Math.Round(sumSq, 3) },
                { "currentTourists", hasTour ? tour.m_CurrentTourists : Stat(StatisticType.TouristCount) },
                { "averageTourists", hasTour ? tour.m_AverageTourists : 0 },
                { "targetTourists", TourismSystem.GetTargetTourists(hasTour ? tour.m_Attractiveness : (int)Math.Round(cityRaw)) },
                { "touristIncome", Stat(StatisticType.TouristIncome) },
            };
            r["city"] = cityBlock;
            r["lodging"] = new Dictionary<string, object>
            {
                { "hotels", hotels }, { "roomsUsed", hasTour ? tour.m_Lodging.x : used }, { "roomsTotal", hasTour ? tour.m_Lodging.y : total },
                { "occupancyPct", (hasTour ? tour.m_Lodging.y : total) > 0 ? Math.Round(100.0 * (hasTour ? tour.m_Lodging.x : used) / (hasTour ? tour.m_Lodging.y : total), 1) : 0.0 },
                { "avgRoomPrice", hotels > 0 ? Math.Round((double)priceSum / hotels, 1) : 0.0 },
                { "statLodgingUsed", Stat(StatisticType.LodgingUsed) }, { "statLodgingTotal", Stat(StatisticType.LodgingTotal) },
                { "fullest", hotelRows.OrderByDescending(h => (int)h["rooms"] > 0 ? (double)(int)h["used"] / (int)h["rooms"] : 0).Take(math.min(limit, 10)).Cast<object>().ToList() },
            };
            r["attractions"] = new Dictionary<string, object>
            {
                { "count", rows.Count },
                { "top", rows.OrderByDescending(d => Convert.ToInt32(d["attractiveness"])).Take(limit).Cast<object>().ToList() },
                { "marginalCityAttractivenessForOneMore", marginal },
            };
            r["legend"] = "city attractiveness = 200/(1+exp(-0.3*S))-100 with S = sum((a/100)^2) over attractions (so one strong attraction counts far more than several weak ones; it saturates at 100), then city policies/modifiers. targetTourists = TourismSystem.GetTargetTourists(attractiveness). Per attraction: base x efficiency x park maintenance x (1 + terrain% from forest/shore/height); factorsPct as the building info panel; sharePct = share of S. Tourists need hotel rooms: occupancy near 100% caps tourism.";
            return r;
        }
    }
}
