using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using NetEdge = Game.Net.Edge;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Replace existing road segments with another net prefab (e.g. Small Road -> Medium Road),
    /// like drawing over a road in the road tool's replace mode: one definition per segment with
    /// m_Original = the segment and a course along its exact curve and nodes.
    /// </summary>
    public class ReplaceJob : AgentToolSystem.BuildJob
    {
        public Entity Prefab;
        public List<Entity> Edges = new List<Entity>();

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            var em = tool.EntityManager;
            var rnd = new Unity.Mathematics.Random((uint)(Environment.TickCount | 1));
            int n = 0;
            foreach (var e in Edges)
            {
                if (!em.Exists(e) || em.HasComponent<Deleted>(e) || !em.HasComponent<NetEdge>(e)) continue;
                var edge = em.GetComponentData<NetEdge>(e);
                var cv = em.GetComponentData<Curve>(e).m_Bezier;
                var start = new CoursePos { m_Entity = edge.m_Start, m_Position = cv.a, m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(cv)), m_CourseDelta = 0f, m_ParentMesh = -1 };
                var end = new CoursePos { m_Entity = edge.m_End, m_Position = cv.d, m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(cv)), m_CourseDelta = 1f, m_ParentMesh = -1 };
                var d = ecb.CreateEntity();
                ecb.AddComponent(d, new CreationDefinition { m_Original = e, m_Prefab = Prefab, m_Flags = CreationFlags.SubElevation | CreationFlags.Align, m_RandomSeed = rnd.NextInt() });
                ecb.AddComponent(d, default(Updated));
                ecb.AddComponent(d, new NetCourse { m_Curve = cv, m_StartPosition = start, m_EndPosition = end, m_Length = MathUtils.Length(cv), m_FixedIndex = -1 });
                n++;
            }
            Result["segmentsReplaced"] = n;
            return deps;
        }
    }

    public partial class Api
    {
        /// <summary>POST /upgrade {prefab, edges:[ids] | x,z,radius,along:"x"|"z", filter}</summary>
        object Upgrade(Args a)
        {
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy", 409);
            var prefab = FindPrefab(a.Str("prefab", "Medium Road"), e => em.HasComponent<NetData>(e) && em.HasComponent<PlaceableNetData>(e));
            if (prefab == Entity.Null) return new ApiError("net prefab not found");
            if (IsLocked(prefab)) return new ApiError(PrefabName(prefab) + " is still locked");
            var job = new ReplaceJob { Prefab = prefab, DryRun = a.B("dryRun") };
            // edges:[ids] (stale ids skipped + reported) and/or box/polyline with prefabFilter (required); same-prefab edges are skipped
            var sel = SelectEdges(a, SameLayerAs(prefab), true);
            if (sel.Error != null) return sel.Error;
            int samePrefab = 0;
            foreach (var e in sel.Edges) { if (em.GetComponentData<PrefabRef>(e).m_Prefab == prefab) samePrefab++; else job.Edges.Add(e); }
            if (job.Edges.Count == 0) return new ApiError("no edges to replace" + (samePrefab > 0 ? " (" + samePrefab + " already " + PrefabName(prefab) + ")" : "")) { { "selection", sel.Report() } };
            tool.Enqueue(job);
            return new BuildOperation(job, new Dictionary<string, object> { { "prefab", PrefabName(prefab) }, { "edges", job.Edges.Count }, { "edgeIds", job.Edges.Select(e => (object)Id(e)).ToList() }, { "skippedSamePrefab", samePrefab }, { "selection", sel.Report() } });
        }
    }
}
