using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Colossal.Mathematics;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using GB = Game.Buildings;
using GN = Game.Net;
using GP = Game.Prefabs;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        /// <summary>Grid rows north->south like /pollution; values scaled 0-9 against <paramref name="scaleMax"/>.</summary>
        static string RenderGrid(float[,] vals, int n, float z0, float cell, float scaleMax, Func<float, bool> empty)
        {
            var sb = new StringBuilder();
            for (int r = 0; r < n; r++)
            {
                sb.Append(((int)(z0 + (n - 1 - r + 0.5f) * cell)).ToString().PadLeft(7)).Append(' ');
                for (int c = 0; c < n; c++)
                {
                    float v = vals[r, c];
                    sb.Append(empty(v) ? '.' : (char)('0' + math.clamp((int)(v * 10f / (scaleMax + 1e-6f)), 0, 9)));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ GET /landvalue

        /// <summary>
        /// GET /landvalue?x&amp;z&amp;size&amp;cell : LandValueSystem cell map (what the Land Value info view shows)
        /// as a 0-9 grid relative to the highest value in the area, plus the road-edge land values
        /// (Game.Net.LandValue, which zoned buildings read for rent) in the same area.
        /// </summary>
        object LandValue(Pending p)
        {
            float cell = math.clamp(p.QF("cell", 80f), 16f, 256f);
            float size = math.clamp(p.QF("size", 4000f), cell * 4, cell * 120);
            float cx = p.QF("x", 0f), cz = p.QF("z", 0f);
            int n = (int)(size / cell);
            float x0 = cx - n * cell / 2, z0 = cz - n * cell / 2;
            JobHandle deps;
            var map = Sys<LandValueSystem>().GetMap(true, out deps);
            deps.Complete();
            var vals = new float[n, n];
            float max = 0f, sum = 0f; int cnt = 0;
            var cells = new List<KeyValuePair<float, float3>>();
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    var pos = new float3(x0 + (c + 0.5f) * cell, 0, z0 + (n - 1 - r + 0.5f) * cell);
                    int idx = LandValueSystem.GetCellIndex(pos);
                    float v = idx >= 0 && idx < map.Length ? map[idx].m_LandValue : 0f;
                    vals[r, c] = v;
                    if (v > 0) { max = math.max(max, v); sum += v; cnt++; cells.Add(new KeyValuePair<float, float3>(v, pos)); }
                }
            // road edges in the area
            var bounds = new Bounds2(new float2(x0, z0), new float2(x0 + n * cell, z0 + n * cell));
            var eq = em.CreateEntityQuery(ComponentType.ReadOnly<GN.LandValue>(), ComponentType.ReadOnly<GN.Curve>(), ComponentType.ReadOnly<GN.Edge>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var edges = new List<KeyValuePair<float, Entity>>();
            using (var arr = eq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (em.HasComponent<Owner>(e)) continue;
                    var mid = MathUtils.Position(em.GetComponentData<GN.Curve>(e).m_Bezier, 0.5f);
                    if (!MathUtils.Intersect(bounds, mid.xz)) continue;
                    edges.Add(new KeyValuePair<float, Entity>(em.GetComponentData<GN.LandValue>(e).m_LandValue, e));
                }
            edges.Sort((a, b) => a.Key.CompareTo(b.Key));
            Func<KeyValuePair<float, Entity>, object> ej = k => new Dictionary<string, object>
            {
                { "id", Id(k.Value) }, { "landValue", Math.Round(k.Key, 1) },
                { "pos", F2(MathUtils.Position(em.GetComponentData<GN.Curve>(k.Value).m_Bezier, 0.5f)) },
            };
            var r2 = new Dictionary<string, object>
            {
                { "x0", x0 }, { "z0", z0 }, { "cell", cell }, { "max", Math.Round(max, 1) },
                { "mean", cnt > 0 ? Math.Round(sum / cnt, 1) : 0.0 },
                { "legend", ". no value | 0-9 relative to max; rows north->south, columns west->east" },
                { "highest", cells.OrderByDescending(k => k.Key).Take(10).Select(k => (object)new List<object> { Math.Round(k.Value.x), Math.Round(k.Value.z), Math.Round(k.Key, 1) }).ToList() },
                { "grid", RenderGrid(vals, n, z0, cell, max, v => v <= 0f) },
            };
            if (edges.Count > 0)
                r2["roadEdges"] = new Dictionary<string, object>
                {
                    { "count", edges.Count }, { "min", Math.Round(edges[0].Key, 1) }, { "median", Math.Round(edges[edges.Count / 2].Key, 1) },
                    { "max", Math.Round(edges[edges.Count - 1].Key, 1) },
                    { "highest", edges.Skip(Math.Max(0, edges.Count - 8)).Reverse().Select(ej).ToList() },
                    { "lowest", edges.Take(8).Select(ej).ToList() },
                };
            return r2;
        }

        // ------------------------------------------------------------------ GET /crime

        /// <summary>
        /// GET /crime?x&amp;z&amp;size&amp;cell&amp;limit : CrimeProducer.m_Crime of buildings per cell (max in cell) as a
        /// 0-9 grid relative to PoliceConfigurationData.m_MaxCrimeAccumulation, the worst buildings with
        /// their nearest police station, and city totals (accumulation rate x factor x 5/(5+police coverage)).
        /// </summary>
        object Crime(Pending p)
        {
            float cell = math.clamp(p.QF("cell", 128f), 16f, 512f);
            float size = math.clamp(p.QF("size", 6000f), cell * 4, cell * 120);
            float cx = p.QF("x", 0f), cz = p.QF("z", 0f);
            int limit = (int)math.clamp(p.QF("limit", 15f), 1f, 100f);
            int n = (int)(size / cell);
            float x0 = cx - n * cell / 2, z0 = cz - n * cell / 2;
            float maxAcc = 0f, tol = 0f;
            if (TrySingleton<GP.PoliceConfigurationData>(out var pc)) { maxAcc = pc.m_MaxCrimeAccumulation; tol = pc.m_CrimeAccumulationTolerance; }

            var stations = new List<KeyValuePair<Entity, float3>>();
            var sq = em.CreateEntityQuery(ComponentType.ReadOnly<GB.PoliceStation>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = sq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr) if (!em.HasComponent<GB.ServiceUpgrade>(e)) stations.Add(new KeyValuePair<Entity, float3>(e, em.GetComponentData<ObjTransform>(e).m_Position));

            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GB.CrimeProducer>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.ReadOnly<GP.PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var vals = new float[n, n];
            float cityMax = 0f; double sum = 0; int count = 0, overHalf = 0, overTol = 0;
            var worst = new List<KeyValuePair<float, Entity>>();
            var byZone = new Dictionary<string, double[]>();
            using (var ents = q.ToEntityArray(Allocator.Temp))
            using (var crimes = q.ToComponentDataArray<GB.CrimeProducer>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    float c = crimes[i].m_Crime;
                    var e = ents[i];
                    count++; sum += c; cityMax = math.max(cityMax, c);
                    if (maxAcc > 0 && c >= 0.5f * maxAcc) overHalf++;
                    if (tol > 0 && c >= tol) overTol++;
                    var prefab = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
                    string zone = em.HasComponent<GP.SpawnableBuildingData>(prefab) ? (PrefabName(em.GetComponentData<GP.SpawnableBuildingData>(prefab).m_ZonePrefab) ?? "?") : "other";
                    if (!byZone.TryGetValue(zone, out var z)) byZone[zone] = z = new double[3];
                    z[0]++; z[1] += c; z[2] = Math.Max(z[2], c);
                    if (c > 0) worst.Add(new KeyValuePair<float, Entity>(c, e));
                    var pos = em.GetComponentData<ObjTransform>(e).m_Position;
                    int col = (int)math.floor((pos.x - x0) / cell), row = n - 1 - (int)math.floor((pos.z - z0) / cell);
                    if (col >= 0 && col < n && row >= 0 && row < n) vals[row, col] = math.max(vals[row, col], c);
                }
            float scale = maxAcc > 0 ? maxAcc : math.max(cityMax, 1f);
            var worstList = worst.OrderByDescending(k => k.Key).Take(limit).Select(k =>
            {
                var e = k.Value;
                var pos = em.GetComponentData<ObjTransform>(e).m_Position;
                var d = new Dictionary<string, object>
                {
                    { "id", Id(e) }, { "prefab", PrefabName(em.GetComponentData<GP.PrefabRef>(e).m_Prefab) }, { "pos", F2(pos) },
                    { "crime", Math.Round(k.Key, 1) }, { "pctOfMax", maxAcc > 0 ? Math.Round(100.0 * k.Key / maxAcc, 1) : (double?)null },
                };
                if (stations.Count > 0)
                {
                    var best = stations.OrderBy(s => math.distance(s.Value.xz, pos.xz)).First();
                    d["nearestPolice"] = new Dictionary<string, object>
                    {
                        { "id", Id(best.Key) }, { "prefab", PrefabName(em.GetComponentData<GP.PrefabRef>(best.Key).m_Prefab) },
                        { "distance", Math.Round(math.distance(best.Value.xz, pos.xz)) },
                        { "patrolCarsAvailable", (em.GetComponentData<GB.PoliceStation>(best.Key).m_Flags & GB.PoliceStationFlags.HasAvailablePatrolCars) != 0 },
                    };
                }
                return (object)d;
            }).ToList();
            int victims = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Citizens.CrimeVictim>(), ComponentType.Exclude<Deleted>()).CalculateEntityCount();
            return new Dictionary<string, object>
            {
                { "x0", x0 }, { "z0", z0 }, { "cell", cell },
                { "maxCrimeAccumulation", Math.Round(maxAcc, 1) }, { "accumulationTolerance", Math.Round(tol, 1) },
                { "buildings", count }, { "mean", count > 0 ? Math.Round(sum / count, 1) : 0.0 }, { "max", Math.Round(cityMax, 1) },
                { "overHalfOfMax", overHalf }, { "overTolerance", overTol }, { "crimeVictims", victims },
                { "byZone", byZone.OrderByDescending(kv => kv.Value[1] / Math.Max(1, kv.Value[0])).ToDictionary(kv => kv.Key, kv => (object)new Dictionary<string, object>
                    { { "buildings", (int)kv.Value[0] }, { "mean", Math.Round(kv.Value[1] / Math.Max(1, kv.Value[0]), 1) }, { "max", Math.Round(kv.Value[2], 1) } }) },
                { "legend", ". no crime | 0-9 = max building crime in cell relative to maxCrimeAccumulation; rows north->south" },
                { "grid", RenderGrid(vals, n, z0, cell, scale, v => v <= 0f) },
                { "worst", worstList },
                { "police", PoliceSummary() }, // InsightServices.cs: jail/prison occupancy, criminals by state, patrol/transport backlog
            };
        }

        // ------------------------------------------------------------------ GET /levels

        /// <summary>
        /// GET /levels?zone=filter&amp;limit=10 : per zone prefab the level histogram and the BuildingUpkeepSystem
        /// state of each zoned building: condition vs BuildingUtils.GetLevelingCost (level up when reached,
        /// then a ResourceNeeding buffer = waiting for the level-up material delivery = "Leveling Building")
        /// and -GetAbandonCost (level down / abandon), BuildingFlags.HighRentWarning and Historical.
        /// </summary>
        object Levels(Pending p)
        {
            string filter = p.Q("zone");
            int limit = (int)math.clamp(p.QF("limit", 10f), 1f, 100f);
            var city = Sys<CitySystem>().City;
            if (!em.HasBuffer<Game.City.CityModifier>(city)) return new ApiError("city modifiers not available", 409);
            var mods = em.GetBuffer<Game.City.CityModifier>(city, true);
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<GB.BuildingCondition>(), ComponentType.ReadOnly<GP.PrefabRef>(),
                ComponentType.ReadOnly<ObjTransform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var zones = new Dictionary<string, Dictionary<string, object>>();
            var near = new Dictionary<string, List<KeyValuePair<float, Entity>>>();
            var abandonRisk = new Dictionary<string, List<KeyValuePair<float, Entity>>>();
            var waitingSamples = new Dictionary<string, List<object>>();
            using (var ents = q.ToEntityArray(Allocator.Temp))
            using (var conds = q.ToComponentDataArray<GB.BuildingCondition>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    var prefab = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
                    if (!em.HasComponent<GP.SpawnableBuildingData>(prefab) || !em.HasComponent<GP.BuildingPropertyData>(prefab)) continue;
                    var sb = em.GetComponentData<GP.SpawnableBuildingData>(prefab);
                    if (sb.m_ZonePrefab == Entity.Null || !em.HasComponent<GP.ZoneData>(sb.m_ZonePrefab)) continue;
                    var zname = PrefabName(sb.m_ZonePrefab) ?? "?";
                    if (!string.IsNullOrEmpty(filter) && zname.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!zones.TryGetValue(zname, out var z))
                    {
                        zones[zname] = z = new Dictionary<string, object>
                        {
                            { "buildings", 0 }, { "levels", new int[5] }, { "abandoned", 0 }, { "historical", 0 }, { "highRent", 0 },
                            { "waitingForMaterial", 0 }, { "materialRequested", new Dictionary<string, int>() }, { "nearLevelUp", 0 }, { "nearAbandon", 0 },
                            { "signature", 0 },
                        };
                        near[zname] = new List<KeyValuePair<float, Entity>>();
                        abandonRisk[zname] = new List<KeyValuePair<float, Entity>>();
                        waitingSamples[zname] = new List<object>();
                    }
                    void Inc(string k) => z[k] = (int)z[k] + 1;
                    Inc("buildings");
                    int lvl = math.clamp(sb.m_Level, 1, 5);
                    ((int[])z["levels"])[lvl - 1]++;
                    var b = em.GetComponentData<GB.Building>(e);
                    if (em.HasComponent<GB.Abandoned>(e)) { Inc("abandoned"); continue; }
                    if (em.HasComponent<GP.SignatureBuildingData>(prefab)) Inc("signature");
                    if ((b.m_Flags & GB.BuildingFlags.Historical) != 0) Inc("historical");
                    if ((b.m_Flags & GB.BuildingFlags.HighRentWarning) != 0) Inc("highRent");
                    var area = em.GetComponentData<GP.ZoneData>(sb.m_ZonePrefab).m_AreaType;
                    var bpd = em.GetComponentData<GP.BuildingPropertyData>(prefab);
                    int levelCost = GB.BuildingUtils.GetLevelingCost(area, bpd, sb.m_Level, mods);
                    int abandonCost = GB.BuildingUtils.GetAbandonCost(area, bpd, sb.m_Level, levelCost, mods);
                    int cond = conds[i].m_Condition;
                    if (em.HasBuffer<GB.ResourceNeeding>(e) && em.GetBuffer<GB.ResourceNeeding>(e, true).Length > 0)
                    {
                        Inc("waitingForMaterial");
                        var mr = (Dictionary<string, int>)z["materialRequested"];
                        var rn = em.GetBuffer<GB.ResourceNeeding>(e, true);
                        for (int k = 0; k < rn.Length; k++)
                        {
                            var key = rn[k].m_Resource + ((rn[k].m_Flags & GB.ResourceNeedingFlags.Delivered) != 0 ? " (delivered)" : (rn[k].m_Flags & GB.ResourceNeedingFlags.Requested) != 0 ? " (requested)" : " (not requested)");
                            mr[key] = mr.TryGetValue(key, out var c) ? c + 1 : 1;
                        }
                        if (waitingSamples[zname].Count < 5) waitingSamples[zname].Add(Id(e));
                    }
                    else if (sb.m_Level < 5 && levelCost > 0 && cond >= 0.8f * levelCost)
                    {
                        Inc("nearLevelUp");
                        near[zname].Add(new KeyValuePair<float, Entity>(cond / (float)levelCost, e));
                    }
                    if (abandonCost > 0 && cond <= -0.5f * abandonCost)
                    {
                        Inc("nearAbandon");
                        abandonRisk[zname].Add(new KeyValuePair<float, Entity>(-cond / (float)abandonCost, e));
                    }
                }
            var res = new List<object>();
            foreach (var kv in zones.OrderByDescending(k => (int)k.Value["buildings"]))
            {
                var z = kv.Value;
                var lv = (int[])z["levels"];
                z["levels"] = new Dictionary<string, object> { { "1", lv[0] }, { "2", lv[1] }, { "3", lv[2] }, { "4", lv[3] }, { "5", lv[4] } };
                z["materialRequested"] = ((Dictionary<string, int>)z["materialRequested"]).OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => (object)x.Value);
                z["zone"] = kv.Key;
                if (waitingSamples[kv.Key].Count > 0) z["waitingSamples"] = waitingSamples[kv.Key];
                z["closestToLevelUp"] = near[kv.Key].OrderByDescending(x => x.Key).Take(limit).Select(x => (object)new Dictionary<string, object>
                    { { "id", Id(x.Value) }, { "pct", Math.Round(100.0 * x.Key) }, { "pos", F2(em.GetComponentData<ObjTransform>(x.Value).m_Position) } }).ToList();
                z["closestToAbandon"] = abandonRisk[kv.Key].OrderByDescending(x => x.Key).Take(limit).Select(x => (object)new Dictionary<string, object>
                    { { "id", Id(x.Value) }, { "pct", Math.Round(100.0 * x.Key) }, { "pos", F2(em.GetComponentData<ObjTransform>(x.Value).m_Position) } }).ToList();
                res.Add(z);
            }
            return new Dictionary<string, object>
            {
                { "zones", res },
                { "legend", "nearLevelUp: condition >= 80% of the leveling cost (level < 5); waitingForMaterial: reached it and waits for the level-up resource delivery (ResourceNeeding); nearAbandon: condition <= -50% of the abandon cost (at -100% the building levels down / is abandoned); pct = progress towards that threshold. Historical buildings never level or abandon." },
            };
        }

        // ------------------------------------------------------------------ signature buildings

        /// <summary>Live (placed) buildings per signature prefab.</summary>
        Dictionary<Entity, List<Entity>> PlacedSignatures()
        {
            var d = new Dictionary<Entity, List<Entity>>();
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var pr = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
                    if (!em.HasComponent<GP.SignatureBuildingData>(pr)) continue;
                    if (!d.TryGetValue(pr, out var l)) d[pr] = l = new List<Entity>();
                    l.Add(e);
                }
            return d;
        }

        Dictionary<Entity, List<Entity>> placedSignatureCache;

        /// <summary>Adds placed/placedIds to a /prefabs row of a signature prefab (call with reset=true once per request).</summary>
        void AddSignaturePlaced(Dictionary<string, object> row, Entity prefab, bool reset)
        {
            if (reset || placedSignatureCache == null) placedSignatureCache = PlacedSignatures();
            bool placedFlag = em.HasComponent<GP.PlacedSignatureBuildingData>(prefab);
            placedSignatureCache.TryGetValue(prefab, out var l);
            row["placed"] = placedFlag || (l != null && l.Count > 0);
            if (l != null && l.Count > 0) row["placedIds"] = l.Select(x => (object)Id(x)).ToList();
        }

        /// <summary>GET /signatures : every signature building with lock state, placed (one of each may exist), id and position.</summary>
        object Signatures()
        {
            var placed = PlacedSignatures();
            placedSignatureCache = placed;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GP.SignatureBuildingData>(), ComponentType.ReadOnly<GP.PrefabData>());
            var list = new List<object>();
            int unlockedFree = 0;
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var d = new Dictionary<string, object> { { "name", PrefabName(e) }, { "locked", IsLocked(e) } };
                    AddSignaturePlaced(d, e, false);
                    if (placed.TryGetValue(e, out var l) && l.Count > 0) d["pos"] = F2(em.GetComponentData<ObjTransform>(l[0]).m_Position);
                    if (em.HasComponent<GP.SpawnableBuildingData>(e)) d["zone"] = PrefabName(em.GetComponentData<GP.SpawnableBuildingData>(e).m_ZonePrefab);
                    if (em.HasComponent<GP.BuildingData>(e)) { var lot = em.GetComponentData<GP.BuildingData>(e).m_LotSize; d["lot"] = new List<object> { lot.x, lot.y }; }
                    if (em.HasComponent<GP.PlaceableObjectData>(e)) d["cost"] = em.GetComponentData<GP.PlaceableObjectData>(e).m_ConstructionCost;
                    if (!(bool)d["locked"] && !(bool)d["placed"]) unlockedFree++;
                    list.Add(d);
                }
            list = list.OrderBy(o => (bool)((Dictionary<string, object>)o)["locked"]).ThenBy(o => (bool)((Dictionary<string, object>)o)["placed"]).ToList();
            return new Dictionary<string, object> { { "count", list.Count }, { "unlockedNotPlaced", unlockedFree }, { "signatures", list } };
        }
    }
}
