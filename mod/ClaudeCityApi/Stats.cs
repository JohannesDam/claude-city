using System;
using System.Collections.Generic;
using System.Linq;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using GCity = Game.City;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        // ------------------------------------------------------------------ /stats

        /// <summary>Last n samples of one statistic entity (m_TotalValue rounded like the Statistics panel), oldest first.</summary>
        List<long> StatSeries(Entity e, int n)
        {
            var l = new List<long>();
            if (e == Entity.Null || !em.Exists(e) || !em.HasBuffer<GCity.CityStatistic>(e)) return l;
            var buf = em.GetBuffer<GCity.CityStatistic>(e, true);
            for (int i = math.max(0, buf.Length - n); i < buf.Length; i++)
            {
                double v = Math.Round(buf[i].m_TotalValue, MidpointRounding.AwayFromZero);
                l.Add(v > long.MaxValue ? long.MaxValue : v < long.MinValue ? long.MinValue : (long)v);
            }
            return l;
        }

        static object StatLeaf(List<long> s, int n)
        {
            long cur = s.Count > 0 ? s[s.Count - 1] : 0;
            if (n <= 1) return cur;
            return new Dictionary<string, object> { { "current", cur }, { "history", s } };
        }

        /// <summary>
        /// GET /stats[?names=Population,Unemployed,MovedAwayReason&amp;n=12&amp;zeros=1]: the game's city statistics
        /// (CityStatisticsSystem, the data behind the Statistics panel). names = Game.City.StatisticType names
        /// (default all); n = samples per series (default 1 = current value; 32 samples per in-game day);
        /// parametric statistics (per resource, education level, move-away reason, income source...) are split
        /// by parameter with the game's own parameter names; zeros=1 keeps all-zero parameters.
        /// </summary>
        object Stats(Pending p)
        {
            var css = Sys<CityStatisticsSystem>();
            css.CompleteWriters();
            var lookup = css.GetLookup();
            int n = (int)math.clamp(p.QF("n", 1f), 1f, 2048f);
            bool zeros = p.Q("zeros", "0") == "1" || p.Q("zeros", "") == "true";
            var namesQ = p.Q("names") ?? p.Q("types");
            HashSet<string> want = null;
            if (!string.IsNullOrEmpty(namesQ) && namesQ != "all")
                want = new HashSet<string>(namesQ.Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0));

            // prefab metadata per statistic type (collection/unit type, parameter names)
            var ps = Sys<PrefabSystem>();
            var meta = new Dictionary<GCity.StatisticType, StatisticsPrefab>();
            var mq = em.CreateEntityQuery(ComponentType.ReadOnly<StatisticsData>(), ComponentType.ReadOnly<PrefabData>());
            using (var arr = mq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var sd = em.GetComponentData<StatisticsData>(e);
                    if (meta.ContainsKey(sd.m_StatisticType)) continue;
                    if (ps.TryGetPrefab<StatisticsPrefab>(e, out var sp) && sp != null) meta[sd.m_StatisticType] = sp;
                }

            // live statistic entities by (type, parameter)
            var byType = new SortedDictionary<int, List<KeyValuePair<int, Entity>>>();
            using (var keys = lookup.GetKeyArray(Allocator.Temp))
                foreach (var k in keys)
                {
                    if (!lookup.TryGetValue(k, out var ent)) continue;
                    int t = (int)k.type;
                    if (!byType.TryGetValue(t, out var l)) byType[t] = l = new List<KeyValuePair<int, Entity>>();
                    l.Add(new KeyValuePair<int, Entity>(k.parameter, ent));
                }

            var res = new Dictionary<string, object>();
            var seen = new HashSet<string>();
            foreach (var kv in byType)
            {
                var t = (GCity.StatisticType)kv.Key;
                var tn = Enum.IsDefined(typeof(GCity.StatisticType), t) ? t.ToString() : "type" + kv.Key;
                if (want != null && !want.Contains(tn.ToLowerInvariant())) continue;
                seen.Add(tn.ToLowerInvariant());
                meta.TryGetValue(t, out var sp);
                var entry = new Dictionary<string, object>();
                if (sp != null)
                {
                    entry["collection"] = sp.m_CollectionType.ToString();
                    entry["unit"] = sp.m_UnitType.ToString();
                }
                var par = sp as ParametricStatistic;
                var list = kv.Value;
                list.Sort((a, b) => a.Key.CompareTo(b.Key));
                if (par == null && list.Count == 1 && list[0].Key == 0)
                {
                    entry["value"] = StatLeaf(StatSeries(list[0].Value, n), n);
                }
                else
                {
                    var pd = new Dictionary<string, object>();
                    long total = 0;
                    foreach (var pe in list)
                    {
                        var s = StatSeries(pe.Value, n);
                        long cur = s.Count > 0 ? s[s.Count - 1] : 0;
                        total += cur;
                        if (!zeros && s.All(x => x == 0)) continue;
                        string pn = null;
                        if (par != null) try { pn = par.GetParameterName(pe.Key); } catch { }
                        if (string.IsNullOrEmpty(pn)) pn = pe.Key.ToString();
                        if (pd.ContainsKey(pn)) pn += "#" + pe.Key;
                        pd[pn] = StatLeaf(s, n);
                    }
                    entry["total"] = total;
                    entry["byParameter"] = pd;
                }
                res[tn] = entry;
            }

            var r = new Dictionary<string, object>
            {
                { "note", "values = CityStatistic.m_TotalValue as the Statistics panel plots them; collection Daily = per-day amount, Point = snapshot, Cumulative = running total. history is oldest -> newest, one sample per CityStatisticsSystem update (8192 frames = 45 in-game minutes, 32 per day). total = sum of current parameter values." },
                { "date", DateString() },
                { "sampleCount", css.sampleCount },
                { "samplesPerDay", 32 },
                { "stats", res },
            };
            if (want != null)
            {
                var missing = want.Where(w => !seen.Contains(w)).ToList();
                if (missing.Count > 0)
                {
                    r["unknown"] = missing;
                    r["available"] = byType.Keys.Select(k => (object)((GCity.StatisticType)k).ToString()).ToList();
                }
            }
            return r;
        }
    }
}
