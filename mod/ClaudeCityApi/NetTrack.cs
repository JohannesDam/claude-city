using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using NetEdge = Game.Net.Edge;
using NetNode = Game.Net.Node;

namespace ClaudeCityBridge
{
    /// <summary>
    /// NetJob that also reports what it built: after a successful apply it lists the edges and nodes
    /// along the course (in course order, new and reused), new edges elsewhere (roads the course
    /// crossed and split) and the ids that went stale (edges replaced by the split/merge).
    /// </summary>
    public class TrackedNetJob : NetJob
    {
        public Api Api;
        HashSet<Entity> before;
        List<float3> course;

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            var h = base.Create(tool, ecb, deps); // re-snaps and stores the final positions in Points
            try
            {
                course = Points.Select(p => p.m_Position).ToList();
                if (!DryRun) before = Api.SnapshotEdges();
            }
            catch (Exception e) { Result["trackError"] = e.Message; }
            return h;
        }

        public override void AfterApply(AgentToolSystem tool)
        {
            base.AfterApply(tool);
            if (before == null || course == null) return;
            try { Api.ReportBuiltCourse(Prefab, course, before, Result); }
            catch (Exception e) { Result["trackError"] = e.Message; }
        }
    }

    /// <summary>
    /// Waits for a zoning job like BuildOperation and, while the preview exists, diffs the game's
    /// temporary zone blocks against their originals: cells that change zone, per new/old zone.
    /// Works for dry runs too (it is the preview the game applies).
    /// </summary>
    public class ZoneCountOperation : IOperation
    {
        readonly Api api;
        readonly AgentToolSystem.BuildJob job;
        readonly Dictionary<string, object> extra;
        readonly int started = Environment.TickCount;
        Dictionary<string, object> last;
        string error;

        public ZoneCountOperation(Api api, AgentToolSystem.BuildJob job, Dictionary<string, object> extra) { this.api = api; this.job = job; this.extra = extra; }

        public bool Step(out object result)
        {
            result = null;
            if (!job.Done)
            {
                // state 1 = preview being generated, 5 = preview stable and about to be evaluated
                if (job.State == 1 || job.State == 5)
                {
                    try { var c = api.CountZonePreview(); if (c != null) last = c; }
                    catch (Exception e) { error = e.Message; }
                }
                if (Environment.TickCount - started > 30000) { result = new ApiError("build timed out (is the game running and in-game?)", 504); return true; }
                return false;
            }
            if (extra != null) foreach (var kv in extra) job.Result[kv.Key] = kv.Value;
            if (last != null) foreach (var kv in last) job.Result[kv.Key] = kv.Value;
            else { job.Result["cellsChanged"] = 0; job.Result["cellsNote"] = "no zone preview blocks were seen"; }
            if (error != null) job.Result["cellsError"] = error;
            result = job.Result;
            return true;
        }
    }

    public partial class Api
    {
        internal HashSet<Entity> SnapshotEdges()
        {
            var set = new HashSet<Entity>();
            using (var arr = EdgeQuery().ToEntityArray(Allocator.Temp))
                foreach (var e in arr) set.Add(e);
            return set;
        }

        Dictionary<string, object> EdgeBrief(Entity e)
        {
            var cv = em.GetComponentData<Curve>(e);
            var edge = em.GetComponentData<NetEdge>(e);
            return new Dictionary<string, object>
            {
                { "id", Id(e) }, { "prefab", PrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab) },
                { "start", F2(cv.m_Bezier.a) }, { "end", F2(cv.m_Bezier.d) }, { "length", Math.Round(cv.m_Length, 1) },
                { "startNode", Id(edge.m_Start) }, { "endNode", Id(edge.m_End) },
            };
        }

        /// <summary>After a road build: edges/nodes along the course in order, other new edges, stale ids.</summary>
        internal void ReportBuiltCourse(Entity prefab, List<float3> course, HashSet<Entity> before, Dictionary<string, object> result)
        {
            var poly = new List<float2>();
            foreach (var p in course) if (poly.Count == 0 || math.distance(poly[poly.Count - 1], p.xz) > 0.01f) poly.Add(p.xz);
            if (poly.Count < 2) return;
            const float tol = 3f;
            var along = new List<Tuple<float, Entity, bool, bool>>();
            var otherNew = new List<object>();
            int otherNewCount = 0;
            var live = new HashSet<Entity>();
            using (var arr = EdgeQuery().ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    live.Add(e);
                    bool isNew = !before.Contains(e);
                    var cv = em.GetComponentData<Curve>(e).m_Bezier;
                    bool on = true;
                    foreach (var t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                        if (PolyDist(MathUtils.Position(cv, t).xz, poly, out _, out _) > tol) { on = false; break; }
                    if (on && (isNew || em.GetComponentData<PrefabRef>(e).m_Prefab == prefab))
                    {
                        PolyDist(MathUtils.Position(cv, 0.5f).xz, poly, out float s, out float2 dir);
                        bool fwd = math.dot(MathUtils.Tangent(cv, 0.5f).xz, dir) >= 0;
                        along.Add(Tuple.Create(s, e, fwd, isNew));
                        continue;
                    }
                    if (isNew) { otherNewCount++; if (otherNew.Count < 50) otherNew.Add(EdgeBrief(e)); }
                }
            along.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            var edges = new List<object>();
            var newIds = new List<object>();
            var nodes = new List<object>();
            var nodeSeen = new HashSet<Entity>();
            void AddNode(Entity n)
            {
                if (n == Entity.Null || !nodeSeen.Add(n) || !em.HasComponent<NetNode>(n)) return;
                nodes.Add(new Dictionary<string, object> { { "id", Id(n) }, { "pos", F2(em.GetComponentData<NetNode>(n).m_Position) }, { "degree", Degree(n) } });
            }
            foreach (var it in along)
            {
                var d = EdgeBrief(it.Item2);
                d["forward"] = it.Item3; // true: start->end follows the course direction
                d["new"] = it.Item4;
                edges.Add(d);
                if (it.Item4) newIds.Add(Id(it.Item2));
                var ed = em.GetComponentData<NetEdge>(it.Item2);
                AddNode(it.Item3 ? ed.m_Start : ed.m_End);
                AddNode(it.Item3 ? ed.m_End : ed.m_Start);
            }
            var stale = before.Where(e => !live.Contains(e)).ToList();
            result["builtEdges"] = edges;
            result["newEdgeIds"] = newIds;
            result["builtNodes"] = nodes;
            result["otherNewEdges"] = otherNew;
            if (otherNewCount > otherNew.Count) result["otherNewEdgesTotal"] = otherNewCount;
            result["staleEdgeCount"] = stale.Count;
            result["staleEdgeIds"] = stale.Take(100).Select(e => (object)Id(e)).ToList();
        }

        static Dictionary<string, object> ByName(Dictionary<ushort, int> counts, Func<ushort, string> name)
        {
            var r = new Dictionary<string, object>();
            foreach (var kv in counts.OrderByDescending(kv => kv.Value))
            {
                var n = name(kv.Key);
                r[n] = (r.TryGetValue(n, out var c) ? (int)c : 0) + kv.Value;
            }
            return r;
        }

        /// <summary>Zone cells that the current preview changes (temporary blocks vs. their originals); null if no preview.</summary>
        internal Dictionary<string, object> CountZonePreview()
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Block>(), ComponentType.ReadOnly<Cell>(), ComponentType.ReadOnly<Temp>(), ComponentType.Exclude<Deleted>());
            int blocks = 0, changed = 0, hidden = 0, occupied = 0;
            var to = new Dictionary<ushort, int>();
            var from = new Dictionary<ushort, int>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
            {
                if (arr.Length == 0) return null;
                foreach (var tb in arr)
                {
                    blocks++;
                    var orig = em.GetComponentData<Temp>(tb).m_Original;
                    var tc = em.GetBuffer<Cell>(tb, true);
                    bool ho = orig != Entity.Null && em.Exists(orig) && em.HasBuffer<Cell>(orig);
                    for (int i = 0; i < tc.Length; i++)
                    {
                        var nz = tc[i].m_Zone.m_Index;
                        ushort oz = 0;
                        CellFlags ost = tc[i].m_State;
                        if (ho)
                        {
                            var oc = em.GetBuffer<Cell>(orig, true);
                            if (i < oc.Length) { oz = oc[i].m_Zone.m_Index; ost = oc[i].m_State; }
                        }
                        if (nz == oz) continue;
                        if ((tc[i].m_State & CellFlags.Visible) == 0) { hidden++; continue; }
                        changed++;
                        to[nz] = to.TryGetValue(nz, out var a) ? a + 1 : 1;
                        from[oz] = from.TryGetValue(oz, out var b) ? b + 1 : 1;
                        if ((ost & CellFlags.Occupied) != 0) occupied++;
                    }
                }
            }
            var zs = Sys<Game.Prefabs.ZoneSystem>();
            string Name(ushort k) => k == 0 ? "none" : (PrefabName(zs.GetPrefab(new ZoneType { m_Index = k })) ?? ("zone#" + k));
            var r = new Dictionary<string, object>
            {
                { "cellsChanged", changed }, { "areaM2", changed * 64 },
                { "cellsByZone", ByName(to, Name) },
                { "cellsFromZone", ByName(from, Name) },
                { "occupiedCellsRezoned", occupied }, { "previewBlocks", blocks },
            };
            if (hidden > 0) r["hiddenCellsChanged"] = hidden;
            return r;
        }
    }
}
