using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Notifications;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace ClaudeCityBridge
{
    /// <summary>
    /// A headless tool that builds through the game's own tool pipeline: it writes creation
    /// definitions (exactly like the vanilla net/zone/object tools), waits for the game to generate
    /// and validate the temporary preview entities, reads cost and errors, and then applies
    /// (or clears) them. Money, unlocks and validation therefore behave like normal play.
    /// </summary>
    public class AgentToolSystem : ObjectToolBaseSystem
    {
        public override string toolID => "Claude Agent Tool";
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        public abstract class BuildJob
        {
            public bool DryRun;
            public int State;
            public int Frames;
            public int LastTempCount = -1;
            public int StableFrames;
            public bool Done;
            public readonly Dictionary<string, object> Result = new Dictionary<string, object>();
            /// <summary>Create definition entities. Called on the main thread in the tool phase.</summary>
            public abstract JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps);
            /// <summary>Called a few frames after a successful apply.</summary>
            public virtual void AfterApply(AgentToolSystem tool) { }
            public virtual void Dispose() { }
        }

        readonly Queue<BuildJob> queue = new Queue<BuildJob>();
        BuildJob current;
        ToolBaseSystem previous;
        EntityQuery definitionQuery;
        EntityQuery tempQuery;
        EntityQuery iconQuery;
        ToolOutputBarrier barrier;
        public CityConfigurationSystem CityConfig;
        int idle;

        protected override void OnCreate()
        {
            base.OnCreate();
            definitionQuery = GetDefinitionQuery();
            tempQuery = GetEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.Exclude<Deleted>());
            iconQuery = GetEntityQuery(ComponentType.ReadOnly<Icon>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>());
            barrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            CityConfig = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
        }

        public bool Busy => current != null || queue.Count > 0;
        public static int SettleFrames = 60;
        public static int MinPreviewFrames = 20;

        public void Enqueue(BuildJob job)
        {
            queue.Enqueue(job);
            if (m_ToolSystem.activeTool != this)
            {
                previous = m_ToolSystem.activeTool;
                m_ToolSystem.activeTool = this;
            }
        }

        // Expose protected helpers to jobs.
        public JobHandle CreateObjectDefinitions(Entity prefab, NativeList<ControlPoint> points, NativeReference<AttachmentData> attachment, JobHandle deps)
        {
            return CreateDefinitions(prefab, Entity.Null, Entity.Null, Entity.Null, Entity.Null, Entity.Null,
                CityConfig.defaultTheme, points, attachment, false, CityConfig.leftHandTraffic, false, false,
                0f, 0f, 0f, 0f, 0.1f, RandomSeed.Next(), Snap.Upright, Game.Tools.AgeMask.Sapling, false, default(PlacementOverrides), deps);
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (current == null)
            {
                if (queue.Count > 0) { current = queue.Dequeue(); current.State = 0; }
                else
                {
                    applyMode = ApplyMode.None;
                    if (++idle > 2)
                    {
                        idle = 0;
                        var back = previous != null && previous != this ? previous : (ToolBaseSystem)m_DefaultToolSystem;
                        previous = null;
                        m_ToolSystem.activeTool = back;
                    }
                    return inputDeps;
                }
            }
            idle = 0;
            var j = current;
            j.Frames++;
            switch (j.State)
            {
                case 0: // write definitions
                    {
                        applyMode = ApplyMode.Clear;
                        inputDeps = DestroyDefinitions(definitionQuery, barrier, inputDeps);
                        var ecb = barrier.CreateCommandBuffer();
                        inputDeps = j.Create(this, ecb, inputDeps);
                        barrier.AddJobHandleForProducer(inputDeps);
                        j.State = 1;
                        return inputDeps;
                    }
                case 1: // let the game generate temps and validate; apply only once the preview is stable
                    {
                        applyMode = ApplyMode.None;
                        int count = tempQuery.CalculateEntityCount();
                        if (count == j.LastTempCount) j.StableFrames++; else { j.StableFrames = 0; j.LastTempCount = count; }
                        bool stable = j.Frames >= MinPreviewFrames && j.StableFrames >= 5;
                        if (stable || j.Frames >= 300) { j.Result["previewFrames"] = j.Frames; j.State = 5; }
                        return inputDeps;
                    }
                case 5: // evaluate preview
                    {
                        inputDeps.Complete();
                        Evaluate(j, out int temps, out int errors);
                        bool apply = !j.DryRun && errors == 0 && temps > 0;
                        j.Result["applied"] = apply;
                        if (temps == 0) j.Result["warning"] = "nothing was generated (check prefab/positions)";
                        applyMode = apply ? ApplyMode.Apply : ApplyMode.Clear;
                        inputDeps = DestroyDefinitions(definitionQuery, barrier, inputDeps);
                        j.State = apply ? 6 : 20;
                        return inputDeps;
                    }
                case 6:
                    // let the apply systems and their follow-ups (node/edge replacement, zone blocks,
                    // buildings removed by the new road) settle before anything else touches them:
                    // rapid back-to-back builds crashed the game natively a few times
                    applyMode = ApplyMode.None;
                    if (j.Frames < SettleFrames + 6) return inputDeps;
                    j.State = 18;
                    return inputDeps;
                case 18:
                    applyMode = ApplyMode.None;
                    inputDeps.Complete();
                    try { j.AfterApply(this); } catch (Exception e) { j.Result["afterApplyError"] = e.Message; }
                    Finish(j);
                    return inputDeps;
                case 20: case 21: case 22: case 23:
                    applyMode = ApplyMode.None;
                    j.State++;
                    return inputDeps;
                default: // cleared (dry run or errors)
                    applyMode = ApplyMode.None;
                    Finish(j);
                    return inputDeps;
            }
        }

        void Finish(BuildJob j)
        {
            j.Dispose();
            j.Done = true;
            current = null;
        }

        void Evaluate(BuildJob j, out int temps, out int errors)
        {
            var em = EntityManager;
            using (var ents = tempQuery.ToEntityArray(Allocator.Temp))
            {
                temps = ents.Length;
                long cost = 0;
                errors = 0;
                int warnings = 0;
                var errorEntities = new HashSet<Entity>();
                foreach (var e in ents)
                {
                    var t = em.GetComponentData<Temp>(e);
                    cost += t.m_Cost;
                    if (em.HasComponent<Error>(e)) { errors++; errorEntities.Add(e); }
                    else if (em.HasComponent<Warning>(e)) { warnings++; errorEntities.Add(e); }
                }
                j.Result["previewEntities"] = temps;
                j.Result["cost"] = cost;
                j.Result["errors"] = errors;
                j.Result["warnings"] = warnings;
                if (errorEntities.Count > 0)
                {
                    var names = new Dictionary<string, int>();
                    using (var icons = iconQuery.ToEntityArray(Allocator.Temp))
                    {
                        foreach (var ic in icons)
                        {
                            var owner = em.GetComponentData<Owner>(ic).m_Owner;
                            if (!errorEntities.Contains(owner)) continue;
                            var name = Util.PrefabName(World, em.GetComponentData<PrefabRef>(ic).m_Prefab);
                            names[name] = names.TryGetValue(name, out var c) ? c + 1 : 1;
                        }
                    }
                    var list = new List<object>();
                    foreach (var kv in names) list.Add(kv.Key + (kv.Value > 1 ? " x" + kv.Value : ""));
                    j.Result["problems"] = list;
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------
    // Build jobs
    // ------------------------------------------------------------------------------------------

    /// <summary>Straight road (or other net) segments along a polyline.</summary>
    public class NetJob : AgentToolSystem.BuildJob
    {
        public Entity Prefab;
        public List<CoursePos> Points = new List<CoursePos>();
        /// <summary>Optional: re-resolve what point i connects to, evaluated in the same frame the definitions are written.</summary>
        public Func<int, CoursePos, CoursePos> Resnap;

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            var em = tool.EntityManager;
            for (int i = 0; i < Points.Count; i++)
            {
                var p = Points[i];
                if (Resnap != null) p = Resnap(i, p);
                // never hand the generators a reference to an entity that is gone or being replaced
                if (p.m_Entity != Entity.Null && (!em.Exists(p.m_Entity) || em.HasComponent<Deleted>(p.m_Entity) || em.HasComponent<Temp>(p.m_Entity)
                    || !(em.HasComponent<Game.Net.Node>(p.m_Entity) || em.HasComponent<Game.Net.Edge>(p.m_Entity))))
                {
                    Log.Warn("NetJob: dropped stale reference " + Util.Id(p.m_Entity) + " at point " + i);
                    Result["staleRefsDropped"] = (Result.TryGetValue("staleRefsDropped", out var prevStale) ? (int)prevStale : 0) + 1;
                    p.m_Entity = Entity.Null;
                    p.m_SplitPosition = 0;
                }
                Points[i] = p;
            }
            var rnd = new Unity.Mathematics.Random((uint)(Environment.TickCount | 1));
            int n = Points.Count - 1;
            for (int i = 0; i < n; i++)
            {
                var a = Points[i];
                var b = Points[i + 1];
                var curve = NetUtils.StraightCurve(a.m_Position, b.m_Position);
                a.m_CourseDelta = 0f;
                b.m_CourseDelta = 1f;
                a.m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, 0f));
                b.m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, 1f));
                a.m_Flags = CoursePosFlags.IsLeft | CoursePosFlags.IsRight | (i == 0 ? CoursePosFlags.IsFirst : 0);
                b.m_Flags = CoursePosFlags.IsLeft | CoursePosFlags.IsRight | (i == n - 1 ? CoursePosFlags.IsLast : 0);
                a.m_ParentMesh = -1;
                b.m_ParentMesh = -1;
                // Only the first/last positions may reference existing entities; interior joints are new nodes.
                if (i > 0) { a.m_Entity = Entity.Null; a.m_SplitPosition = 0; }
                if (i < n - 1) { b.m_Entity = Entity.Null; b.m_SplitPosition = 0; }
                if (i > 0 && Points[i].m_Entity != Entity.Null) { a.m_Entity = Points[i].m_Entity; a.m_SplitPosition = Points[i].m_SplitPosition; }
                if (i < n - 1 && Points[i + 1].m_Entity != Entity.Null) { b.m_Entity = Points[i + 1].m_Entity; b.m_SplitPosition = Points[i + 1].m_SplitPosition; }

                var course = new NetCourse
                {
                    m_Curve = curve,
                    m_StartPosition = a,
                    m_EndPosition = b,
                    m_Length = MathUtils.Length(curve),
                    m_FixedIndex = -1,
                };
                var e = ecb.CreateEntity();
                ecb.AddComponent(e, new CreationDefinition
                {
                    m_Prefab = Prefab,
                    m_RandomSeed = rnd.NextInt(),
                    m_Flags = CreationFlags.SubElevation,
                });
                ecb.AddComponent(e, default(Updated));
                ecb.AddComponent(e, course);
            }
            return deps;
        }
    }

    /// <summary>Marquee zoning with arbitrary quads.</summary>
    public class ZoneJob : AgentToolSystem.BuildJob
    {
        public Entity Prefab;
        public bool Dezone;
        public bool Overwrite = true;
        public List<Quad3> Quads = new List<Quad3>();

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            foreach (var q in Quads)
            {
                var e = ecb.CreateEntity();
                ecb.AddComponent(e, new CreationDefinition { m_Prefab = Prefab });
                ecb.AddComponent(e, default(Updated));
                var flags = ZoningFlags.Marquee | (Dezone ? ZoningFlags.Dezone | ZoningFlags.Overwrite : ZoningFlags.Zone | (Overwrite ? ZoningFlags.Overwrite : 0));
                ecb.AddComponent(e, new Zoning { m_Position = q, m_Flags = flags });
            }
            return deps;
        }
    }

    /// <summary>Single object/building placement through ObjectToolBaseSystem.CreateDefinitions.</summary>
    public class ObjectJob : AgentToolSystem.BuildJob
    {
        public Entity Prefab;
        public float3 Position;
        public quaternion Rotation;
        public Func<AgentToolSystem, Dictionary<string, object>> After;
        NativeList<ControlPoint> points;
        NativeReference<ObjectToolBaseSystem.AttachmentData> attachment;

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            points = new NativeList<ControlPoint>(1, Allocator.Persistent);
            attachment = new NativeReference<ObjectToolBaseSystem.AttachmentData>(Allocator.Persistent);
            var fwd = math.mul(Rotation, new float3(0, 0, 1));
            var cp = new ControlPoint
            {
                m_Position = Position,
                m_HitPosition = Position,
                m_Rotation = Rotation,
                m_Direction = math.normalizesafe(fwd.xz),
                m_HitDirection = new float3(0, 1, 0),
                m_OriginalEntity = Entity.Null,
                m_ElementIndex = new int2(-1, -1),
            };
            points.Add(cp);
            var h = tool.CreateObjectDefinitions(Prefab, points, attachment, deps);
            h.Complete();
            return h;
        }

        public override void AfterApply(AgentToolSystem tool)
        {
            if (After != null) foreach (var kv in After(tool)) Result[kv.Key] = kv.Value;
        }

        public override void Dispose()
        {
            if (points.IsCreated) points.Dispose();
            if (attachment.IsCreated) attachment.Dispose();
        }
    }

    /// <summary>Waits for a queued build job and returns its result.</summary>
    public class BuildOperation : IOperation
    {
        readonly AgentToolSystem.BuildJob job;
        readonly Dictionary<string, object> extra;
        readonly int started = Environment.TickCount;
        public BuildOperation(AgentToolSystem.BuildJob job, Dictionary<string, object> extra = null) { this.job = job; this.extra = extra; }
        public bool Step(out object result)
        {
            result = null;
            if (!job.Done)
            {
                if (Environment.TickCount - started > 30000) { result = new ApiError("build timed out (is the game running and in-game?)", 504); return true; }
                return false;
            }
            if (extra != null) foreach (var kv in extra) job.Result[kv.Key] = kv.Value;
            result = job.Result;
            return true;
        }
    }
}
