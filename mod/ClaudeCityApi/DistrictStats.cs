using System;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using GA = Game.Areas;
using GB = Game.Buildings;
using GCit = Game.Citizens;
using GCo = Game.Companies;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// GET /districts: the city per district (what the district info panel shows, plus land use), so the advisory
    /// board can spread city-wide needs over districts and builders can see how their district is doing.
    /// Per district: buildings by kind (zone / service), households, residents, avg happiness/wellbeing/health,
    /// companies, workplaces and workers, abandoned/condemned buildings and service buildings by service.
    /// Buildings outside every district are reported under "(no district)". ?detail=0 drops the per-kind maps.
    /// </summary>
    public partial class Api
    {
        class DistrictAgg
        {
            public string Name; public Entity Area;
            public int Buildings, Households, Residents, Companies, Workplaces, Workers, Abandoned, Condemned, ServiceBuildings;
            public long Happy, Well, Health; public int Citizens;
            public Dictionary<string, int> ByZone = new Dictionary<string, int>();
            public Dictionary<string, int> ByService = new Dictionary<string, int>();
            public double MinX = double.MaxValue, MaxX = double.MinValue, MinZ = double.MaxValue, MaxZ = double.MinValue;
        }

        object DistrictStatsEndpoint(Pending p)
        {
            bool detail = p.Q("detail", "1") != "0";
            var aggs = new Dictionary<Entity, DistrictAgg>();
            var none = new DistrictAgg { Name = "(no district)", Area = Entity.Null };

            var dq = em.CreateEntityQuery(ComponentType.ReadOnly<GA.District>(), ComponentType.ReadOnly<GA.Node>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = dq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var a = new DistrictAgg { Name = EntityLabel(e), Area = e };
                    var nb = em.GetBuffer<GA.Node>(e, true);
                    for (int i = 0; i < nb.Length; i++)
                    {
                        var v = nb[i].m_Position;
                        a.MinX = Math.Min(a.MinX, v.x); a.MaxX = Math.Max(a.MaxX, v.x);
                        a.MinZ = Math.Min(a.MinZ, v.z); a.MaxZ = Math.Max(a.MaxZ, v.z);
                    }
                    aggs[e] = a;
                }

            var bq = em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = bq.ToEntityArray(Allocator.Temp))
                foreach (var b in arr)
                {
                    var district = em.HasComponent<GA.CurrentDistrict>(b) ? em.GetComponentData<GA.CurrentDistrict>(b).m_District : Entity.Null;
                    var a = district != Entity.Null && aggs.TryGetValue(district, out var found) ? found : none;
                    var pf = em.GetComponentData<PrefabRef>(b).m_Prefab;
                    a.Buildings++;
                    if (em.HasComponent<GB.Abandoned>(b)) a.Abandoned++;
                    if (em.HasComponent<GB.Condemned>(b)) a.Condemned++;
                    if (em.HasComponent<ServiceObjectData>(pf))
                    {
                        a.ServiceBuildings++;
                        var s = PrefabName(em.GetComponentData<ServiceObjectData>(pf).m_Service) ?? "?";
                        a.ByService[s] = (a.ByService.TryGetValue(s, out var c) ? c : 0) + 1;
                    }
                    else if (em.HasComponent<SpawnableBuildingData>(pf))
                    {
                        var z = PrefabName(em.GetComponentData<SpawnableBuildingData>(pf).m_ZonePrefab) ?? "?";
                        a.ByZone[z] = (a.ByZone.TryGetValue(z, out var c) ? c : 0) + 1;
                    }
                    if (em.HasBuffer<GB.Renter>(b))
                    {
                        var rb = em.GetBuffer<GB.Renter>(b, true);
                        for (int i = 0; i < rb.Length; i++)
                        {
                            var r = rb[i].m_Renter;
                            if (em.HasComponent<GCit.Household>(r))
                            {
                                a.Households++;
                                if (!em.HasBuffer<GCit.HouseholdCitizen>(r)) continue;
                                var hc = em.GetBuffer<GCit.HouseholdCitizen>(r, true);
                                for (int j = 0; j < hc.Length; j++)
                                {
                                    var ce = hc[j].m_Citizen;
                                    if (!em.HasComponent<GCit.Citizen>(ce)) continue;
                                    var cit = em.GetComponentData<GCit.Citizen>(ce);
                                    a.Residents++; a.Citizens++;
                                    a.Happy += cit.Happiness; a.Well += cit.m_WellBeing; a.Health += cit.m_Health;
                                }
                            }
                            else if (em.HasComponent<GCo.WorkProvider>(r))
                            {
                                a.Companies++;
                                a.Workplaces += em.GetComponentData<GCo.WorkProvider>(r).m_MaxWorkers;
                                if (em.HasBuffer<GCo.Employee>(r)) a.Workers += em.GetBuffer<GCo.Employee>(r, true).Length;
                            }
                        }
                    }
                    if (em.HasComponent<GCo.WorkProvider>(b))
                    {
                        a.Workplaces += em.GetComponentData<GCo.WorkProvider>(b).m_MaxWorkers;
                        if (em.HasBuffer<GCo.Employee>(b)) a.Workers += em.GetBuffer<GCo.Employee>(b, true).Length;
                    }
                }

            object Out(DistrictAgg a)
            {
                var d = new Dictionary<string, object>
                {
                    ["name"] = a.Name,
                    ["buildings"] = a.Buildings, ["households"] = a.Households, ["residents"] = a.Residents,
                    ["companies"] = a.Companies, ["workplaces"] = a.Workplaces, ["workers"] = a.Workers,
                    ["serviceBuildings"] = a.ServiceBuildings, ["abandoned"] = a.Abandoned, ["condemned"] = a.Condemned,
                };
                if (a.Area != Entity.Null)
                {
                    d["id"] = Id(a.Area);
                    d["bbox"] = new List<object> { Math.Round(a.MinX), Math.Round(a.MinZ), Math.Round(a.MaxX), Math.Round(a.MaxZ) };
                }
                if (a.Citizens > 0)
                {
                    // citizen averages on the same 0-100 scale as the city happiness in GET /city
                    d["happiness"] = Math.Round((double)a.Happy / a.Citizens, 1);
                    d["wellbeing"] = Math.Round((double)a.Well / a.Citizens, 1);
                    d["health"] = Math.Round((double)a.Health / a.Citizens, 1);
                }
                if (detail)
                {
                    d["byZone"] = a.ByZone.OrderByDescending(k => k.Value).ToDictionary(k => k.Key, k => (object)k.Value);
                    d["byService"] = a.ByService.OrderByDescending(k => k.Value).ToDictionary(k => k.Key, k => (object)k.Value);
                }
                return d;
            }

            var list = aggs.Values.OrderByDescending(a => a.Residents).Select(Out).ToList();
            list.Add(Out(none));
            return new Dictionary<string, object>
            {
                ["count"] = aggs.Count,
                ["note"] = "happiness/wellbeing/health are citizen averages (0-100, same scale as GET /city happiness); residents counted via households in each building; workplaces = max workers of companies and service buildings",
                ["districts"] = list,
            };
        }
    }
}
