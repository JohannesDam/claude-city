using System;
using System.Collections.Generic;
using System.Linq;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using GB = Game.Buildings;
using GP = Game.Prefabs;

namespace ClaudeCityBridge
{
    /// <summary>
    /// GET /deathcare?seconds=30 : measures real deathcare throughput per facility by watching every
    /// facility's body queue (Patient buffer) and LongTermStoredCount once per frame while the game runs.
    /// A body that leaves a crematorium queue was cremated; a body that leaves a cemetery queue was stored
    /// (LongTermStoredCount +1). Result in bodies per in-game day; also cached so GET /services?kind=deathcare
    /// shows it as processing.measured / intake.measured. Read-only; does not change game speed.
    /// </summary>
    public partial class Api
    {
        Dictionary<Entity, object> deathcareMeasured;
        Dictionary<string, object> deathcareMeasuredSummary;

        object DeathcareMeasure(Pending p)
        {
            int timeoutMs = p.Query.TryGetValue("timeout", out var t) && int.TryParse(t, out var tt) ? tt : 60000;
            float maxSecs = math.max(2f, timeoutMs / 1000f - 5f);
            float secs = math.clamp(p.QF("seconds", 30f), 2f, math.min(120f, maxSecs));
            return new DeathcareSampler(this, (int)(secs * 1000));
        }

        HashSet<Entity> BodyQueue(Entity f)
        {
            var set = new HashSet<Entity>();
            if (!em.HasBuffer<GB.Patient>(f)) return set;
            var buf = em.GetBuffer<GB.Patient>(f, true);
            for (int i = 0; i < buf.Length; i++) set.Add(buf[i].m_Patient);
            return set;
        }

        class DeathcareSampler : IOperation
        {
            readonly Api api;
            readonly int maxMs;
            bool started;
            uint startFrame;
            int startTick;
            readonly List<Entity> facs = new List<Entity>();
            readonly Dictionary<Entity, HashSet<Entity>> prev = new Dictionary<Entity, HashSet<Entity>>();
            readonly Dictionary<Entity, int> lt = new Dictionary<Entity, int>();
            // [0] bodies that left the queue, [1] bodies that arrived, [2] long-term +, [3] long-term -
            readonly Dictionary<Entity, int[]> counts = new Dictionary<Entity, int[]>();

            public DeathcareSampler(Api api, int maxMs) { this.api = api; this.maxMs = maxMs; }

            int LongTerm(Entity f) => api.em.HasComponent<GB.DeathcareFacility>(f) ? api.em.GetComponentData<GB.DeathcareFacility>(f).m_LongTermStoredCount : 0;

            void Sample()
            {
                foreach (var f in facs)
                {
                    if (!api.em.Exists(f) || !api.em.HasComponent<GB.DeathcareFacility>(f)) continue;
                    var now = api.BodyQueue(f);
                    var c = counts[f];
                    var before = prev[f];
                    foreach (var b in before) if (!now.Contains(b)) c[0]++;
                    foreach (var b in now) if (!before.Contains(b)) c[1]++;
                    prev[f] = now;
                    int l = LongTerm(f);
                    if (l > lt[f]) c[2] += l - lt[f]; else c[3] += lt[f] - l;
                    lt[f] = l;
                }
            }

            public bool Step(out object result)
            {
                result = null;
                var sim = api.world.GetOrCreateSystemManaged<SimulationSystem>();
                if (!started)
                {
                    started = true;
                    startFrame = sim.frameIndex;
                    startTick = Environment.TickCount;
                    facs.AddRange(api.Facilities<GB.DeathcareFacility>());
                    foreach (var f in facs) { prev[f] = api.BodyQueue(f); lt[f] = LongTerm(f); counts[f] = new int[4]; }
                    return false;
                }
                try { Sample(); }
                catch (Exception ex) { result = new ApiError("sampling: " + ex.Message, 500); return true; }
                int ms = Environment.TickCount - startTick;
                if (ms < maxMs) return false;
                try { result = api.DeathcareMeasureResult(facs, counts, sim.frameIndex - startFrame, ms); }
                catch (Exception ex) { result = new ApiError("result: " + ex.Message, 500); }
                return true;
            }
        }

        object DeathcareMeasureResult(List<Entity> facs, Dictionary<Entity, int[]> counts, uint frames, int ms)
        {
            double days = frames / (double)TimeSystem.kTicksPerDay;
            var window = new Dictionary<string, object>
            {
                { "seconds", Math.Round(ms / 1000.0, 1) }, { "simFrames", frames }, { "gameMinutes", Math.Round(days * 1440, 1) }, { "date", DateString() },
            };
            if (frames < 1024)
                return new Dictionary<string, object>
                {
                    { "window", window },
                    { "error", "the simulation barely ran (game paused or too short): run the game (speed >= 1) and retry with more seconds" },
                };
            var list = new List<object>();
            int cremated = 0, stored = 0, removedLt = 0;
            var measured = new Dictionary<Entity, object>();
            foreach (var f in facs)
            {
                if (!em.Exists(f) || !em.HasComponent<GP.PrefabRef>(f)) continue;
                var prefab = em.GetComponentData<GP.PrefabRef>(f).m_Prefab;
                var dd = Combined<GP.DeathcareFacilityData>(f, prefab);
                float eff = em.HasBuffer<GB.Efficiency>(f) ? GB.BuildingUtils.GetEfficiency(em.GetBuffer<GB.Efficiency>(f, true)) : 1f;
                var c = counts[f];
                bool crem = dd.m_ProcessingRate > 0f && !dd.m_LongTermStorage;
                var d = new Dictionary<string, object>
                {
                    { "id", Id(f) }, { "prefab", PrefabName(prefab) }, { "type", dd.m_LongTermStorage ? "cemetery" : "crematorium" },
                    { "bodiesArrived", c[1] },
                };
                var m = new Dictionary<string, object> { { "windowGameMinutes", Math.Round(days * 1440, 1) }, { "date", DateString() } };
                if (crem)
                {
                    double perDay = c[0] / days, cap = dd.m_ProcessingRate * eff;
                    cremated += c[0];
                    d["bodiesCremated"] = c[0];
                    d["perDay"] = Math.Round(perDay, 1);
                    d["effectiveCapacityPerDay"] = Math.Round(cap, 1);
                    d["utilizationPct"] = cap > 0 ? Math.Round(100.0 * perDay / cap, 1) : 0.0;
                    m["bodiesPerDay"] = Math.Round(perDay, 1); m["bodies"] = c[0]; m["utilizationPct"] = d["utilizationPct"];
                }
                else
                {
                    stored += c[2]; removedLt += c[3];
                    d["bodiesStored"] = c[2];
                    d["storedPerDay"] = Math.Round(c[2] / days, 1);
                    if (c[3] > 0) d["removedFromStorage"] = c[3]; // processing (decomposition) or emptying
                    var df = em.GetComponentData<GB.DeathcareFacility>(f);
                    int free = dd.m_StorageCapacity - df.m_LongTermStoredCount - Buf<GB.Patient>(f);
                    d["freeSpace"] = free;
                    double net = (c[2] - c[3]) / days;
                    if (net > 0.01) d["daysUntilFull"] = Math.Round(math.max(0, free) / net, 1);
                    m["storedPerDay"] = d["storedPerDay"]; m["bodies"] = c[2];
                }
                measured[f] = m;
                list.Add(d);
            }
            int deaths = 0;
            try { deaths = Sys<CityStatisticsSystem>().GetStatisticValue(Game.City.StatisticType.DeathRate, 0); } catch { }
            var totals = new Dictionary<string, object>
            {
                { "crematedPerDay", Math.Round(cremated / days, 1) },
                { "storedPerDay", Math.Round(stored / days, 1) },
                { "handledPerDay", Math.Round((cremated + stored) / days, 1) },
                { "deathsPerDay", deaths },
            };
            if (removedLt > 0) totals["removedFromStoragePerDay"] = Math.Round(removedLt / days, 1);
            deathcareMeasured = measured;
            deathcareMeasuredSummary = new Dictionary<string, object>(totals) { { "windowGameMinutes", Math.Round(days * 1440, 1) }, { "date", DateString() } };
            return new Dictionary<string, object>
            {
                { "window", window },
                { "totals", totals },
                { "facilities", list.OrderByDescending(o => ((Dictionary<string, object>)o)["type"].ToString()).ToList() },
                { "note", "counted by watching each facility's body queue every frame: bodies leaving a crematorium queue were cremated; cemetery intake = LongTermStoredCount increases. per-day figures scale the window to a full day (short windows are coarse: +-sqrt(n)). Results are kept and shown by GET /services?kind=deathcare until the next measurement." },
            };
        }
    }
}
