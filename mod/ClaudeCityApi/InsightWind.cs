using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using GB = Game.Buildings;
using GP = Game.Prefabs;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// GET /wind?x&amp;z[&amp;prefab]                 : wind at a point and the expected output of each wind turbine prefab there.
    /// GET /wind?size=6000&amp;cell=256[&amp;x&amp;z&amp;prefab] : grid of the turbine wind factor (0-9) + best spots.
    /// Uses the same data and formula as the game: WindSystem wind map (64x64 cells, bilinear, what
    /// PowerPlantTickJob samples at the building position) and PowerPlantAISystem.GetWindProduction:
    /// production = m_Production x saturate((|wind|^2 / m_MaximumWind^2)^1.5) = m_Production x min(1, (|wind|/max)^3)
    /// (before workers/budget efficiency). This is also the building's WindSpeed efficiency factor.
    /// </summary>
    public partial class Api
    {
        static string Compass(float2 v)
        {
            if (math.lengthsq(v) < 1e-12f) return "calm";
            double deg = (Math.Atan2(v.x, v.y) * 180.0 / Math.PI + 360.0) % 360.0; // 0 = north (+z), 90 = east (+x)
            string[] n = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return n[(int)Math.Round(deg / 45.0) % 8];
        }

        static double Bearing(float2 v) => Math.Round((Math.Atan2(v.x, v.y) * 180.0 / Math.PI + 360.0) % 360.0);

        List<KeyValuePair<Entity, GP.WindPoweredData>> WindPrefabs(string filter)
        {
            var res = new List<KeyValuePair<Entity, GP.WindPoweredData>>();
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GP.WindPoweredData>(), ComponentType.ReadOnly<GP.PrefabData>());
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (!em.HasComponent<GP.BuildingData>(e)) continue; // skip upgrades without a building of their own
                    var name = PrefabName(e);
                    if (!string.IsNullOrEmpty(filter) && (name == null || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    var wd = em.GetComponentData<GP.WindPoweredData>(e);
                    if (wd.m_Production <= 0 || wd.m_MaximumWind <= 0) continue;
                    res.Add(new KeyValuePair<Entity, GP.WindPoweredData>(e, wd));
                }
            return res.OrderBy(kv => IsLocked(kv.Key)).ThenBy(kv => PrefabName(kv.Key)).ToList();
        }

        static float WindFactor(GP.WindPoweredData wd, Wind w)
        {
            var f = Game.Simulation.PowerPlantAISystem.GetWindProduction(wd, w, 1f);
            return f.y > 0 ? f.x / f.y : 0f;
        }

        object WindEndpoint(Pending p)
        {
            var ws = Sys<WindSystem>();
            JobHandle deps;
            var map = ws.GetMap(true, out deps);
            deps.Complete();
            var prefabs = WindPrefabs(p.Q("prefab"));
            if (prefabs.Count == 0) return new ApiError("no wind turbine prefab matches '" + p.Q("prefab") + "'");
            var main = prefabs[0];

            // map-wide statistics over the 64x64 wind cells
            int cells = map.Length;
            var speeds = new float[cells];
            for (int i = 0; i < cells; i++) speeds[i] = math.length(map[i].m_Wind);
            var sorted = speeds.OrderBy(s => s).ToArray();
            float mapAvg = cells > 0 ? speeds.Average() : 0f, mapMax = cells > 0 ? sorted[cells - 1] : 0f;
            float2 mean = 0;
            for (int i = 0; i < cells; i++) mean += map[i].m_Wind;
            if (cells > 0) mean /= cells;
            float2 prevailing = 0;
            try { prevailing = Sys<WindSimulationSystem>().constantWind; } catch { }

            var r = new Dictionary<string, object>
            {
                { "turbinePrefabs", prefabs.Select(kv => (object)new Dictionary<string, object>
                    {
                        { "prefab", PrefabName(kv.Key) }, { "locked", IsLocked(kv.Key) }, { "nominalProduction", kv.Value.m_Production },
                        { "maximumWind", Math.Round(kv.Value.m_MaximumWind, 3) },
                        { "cost", em.HasComponent<GP.PlaceableObjectData>(kv.Key) ? em.GetComponentData<GP.PlaceableObjectData>(kv.Key).m_ConstructionCost : 0 },
                    }).ToList() },
                { "map", new Dictionary<string, object>
                    {
                        { "avgSpeed", Math.Round(mapAvg, 3) }, { "maxSpeed", Math.Round(mapMax, 3) },
                        { "meanDirection", Compass(mean) }, { "meanBearing", Bearing(mean) },
                        { "prevailingDirection", Compass(prevailing) }, { "prevailingBearing", Bearing(prevailing) },
                        { "avgFactor", cells > 0 ? Math.Round(speeds.Average(s => WindFactor(main.Value, new Wind { m_Wind = new float2(s, 0) })), 3) : 0.0 },
                    } },
            };

            bool grid = p.Q("size") != null || p.Q("grid") == "1" || p.Q("x") == null;
            if (!grid)
            {
                float x = p.QF("x", 0f), z = p.QF("z", 0f);
                var w = WindSystem.GetWind(new float3(x, 0, z), map);
                float sp = math.length(w.m_Wind);
                int rank = Array.BinarySearch(sorted, sp);
                if (rank < 0) rank = ~rank;
                r["pos"] = new List<object> { Math.Round(x), Math.Round(z) };
                r["wind"] = new Dictionary<string, object>
                {
                    { "speed", Math.Round(sp, 3) }, { "towards", Compass(w.m_Wind) }, { "bearing", Bearing(w.m_Wind) },
                    { "vector", new List<object> { Math.Round(w.m_Wind.x, 3), Math.Round(w.m_Wind.y, 3) } },
                    { "percentileOfMap", cells > 0 ? Math.Round(100.0 * rank / cells, 1) : 0.0 },
                };
                r["turbines"] = prefabs.Select(kv =>
                {
                    var f = Game.Simulation.PowerPlantAISystem.GetWindProduction(kv.Value, w, 1f);
                    return (object)new Dictionary<string, object>
                    {
                        { "prefab", PrefabName(kv.Key) }, { "windFactor", Math.Round(f.y > 0 ? f.x / f.y : 0, 3) },
                        { "expectedProduction", Math.Round(f.x) }, { "nominalProduction", Math.Round(f.y) },
                        { "windForFullOutput", Math.Round(kv.Value.m_MaximumWind, 3) },
                    };
                }).ToList();
            }
            else
            {
                float cell = math.clamp(p.QF("cell", 256f), 64f, 1024f);
                // default area: the owned map tiles plus one tile around them
                float defCx = 0f, defCz = 0f, defSize = 8000f;
                try
                {
                    var owned = AllTiles().Where(t => t.owned).ToList();
                    if (owned.Count > 0)
                    {
                        float minX = owned.Min(t => t.centre.x), maxX = owned.Max(t => t.centre.x), minZ = owned.Min(t => t.centre.y), maxZ = owned.Max(t => t.centre.y);
                        defCx = (minX + maxX) / 2; defCz = (minZ + maxZ) / 2;
                        defSize = math.max(maxX - minX, maxZ - minZ) + 3 * 623f;
                    }
                }
                catch { }
                float size = math.clamp(p.QF("size", defSize), cell * 4, cell * 100);
                float cx = p.QF("x", defCx), cz = p.QF("z", defCz);
                int n = (int)(size / cell);
                float x0 = cx - n * cell / 2, z0 = cz - n * cell / 2;
                var sb = new StringBuilder();
                var spots = new List<KeyValuePair<float, float3>>();
                for (int row = 0; row < n; row++)
                {
                    float z = z0 + (n - 1 - row + 0.5f) * cell;
                    sb.Append(((int)z).ToString().PadLeft(7)).Append(' ');
                    for (int c = 0; c < n; c++)
                    {
                        var pos = new float3(x0 + (c + 0.5f) * cell, 0, z);
                        float f = WindFactor(main.Value, WindSystem.GetWind(pos, map));
                        sb.Append((char)('0' + math.clamp((int)(f * 10f), 0, 9)));
                        spots.Add(new KeyValuePair<float, float3>(f, pos));
                    }
                    sb.Append('\n');
                }
                r["grid"] = new Dictionary<string, object>
                {
                    { "x0", x0 }, { "z0", z0 }, { "cell", cell }, { "prefab", PrefabName(main.Key) },
                    { "legend", "digit = wind factor of " + PrefabName(main.Key) + " x10 (9 = 90-100% of nominal output, 0 = <10%); rows north->south, columns west->east; the wind map itself is 64x64 cells (~900 m), finer cells are interpolated" },
                    { "rows", sb.ToString() },
                    { "best", spots.OrderByDescending(s => s.Key).Take(12).Select(s => (object)new Dictionary<string, object>
                        { { "pos", new List<object> { Math.Round(s.Value.x), Math.Round(s.Value.z) } }, { "windFactor", Math.Round(s.Key, 3) }, { "expectedProduction", Math.Round(s.Key * main.Value.m_Production) } }).ToList() },
                };
            }

            // existing turbines: computed factor vs. the game's WindSpeed efficiency factor (validation)
            var tq = em.CreateEntityQuery(ComponentType.ReadOnly<GB.ElectricityProducer>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.ReadOnly<ObjTransform>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var existing = new List<Dictionary<string, object>>();
            using (var arr = tq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var pf = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
                    if (!GP.UpgradeUtils.TryGetCombinedComponent(em, e, pf, out GP.WindPoweredData wd) || wd.m_Production <= 0) continue;
                    var pos = em.GetComponentData<ObjTransform>(e).m_Position;
                    var w = WindSystem.GetWind(pos, map);
                    var d = new Dictionary<string, object>
                    {
                        { "id", Id(e) }, { "prefab", PrefabName(pf) }, { "pos", F2(pos) },
                        { "windSpeed", Math.Round(math.length(w.m_Wind), 3) }, { "windFactor", Math.Round(WindFactor(wd, w), 3) },
                    };
                    if (em.HasBuffer<GB.Efficiency>(e))
                    {
                        var eb = em.GetBuffer<GB.Efficiency>(e, true);
                        for (int i = 0; i < eb.Length; i++) if (eb[i].m_Factor == GB.EfficiencyFactor.WindSpeed) d["gameWindSpeedFactor"] = Math.Round(eb[i].m_Efficiency, 3);
                    }
                    var ep = em.GetComponentData<GB.ElectricityProducer>(e);
                    d["capacity"] = ep.m_Capacity; d["production"] = ep.m_LastProduction;
                    existing.Add(d);
                }
            if (existing.Count > 0)
            {
                r["existingTurbines"] = new Dictionary<string, object>
                {
                    { "count", existing.Count },
                    { "avgWindFactor", Math.Round(existing.Average(d => Convert.ToDouble(d["windFactor"])), 3) },
                    { "worst", existing.OrderBy(d => Convert.ToDouble(d["windFactor"])).Take(5).Cast<object>().ToList() },
                    { "best", existing.OrderByDescending(d => Convert.ToDouble(d["windFactor"])).Take(5).Cast<object>().ToList() },
                };
            }
            r["note"] = "instantaneous wind (WindSimulationSystem follows the weather, so absolute values drift; the ranking of sites is more stable). windFactor = the building's WindSpeed efficiency factor; real output = nominal x windFactor x other efficiency (workers, budget). gameWindSpeedFactor on existing turbines may lag the current wind by up to one power-plant update.";
            return r;
        }
    }
}
