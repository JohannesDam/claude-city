using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using NetNode = Game.Net.Node;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Roundabouts are "net objects" (Game.Prefabs.NetObjectData with CompositionFlags.General.Roundabout) that the
    /// object tool attaches to a net node: ObjectToolSystem.SnapJob puts the control point on the node
    /// (m_OriginalEntity = node, or an existing net object there, which is then replaced) and
    /// ObjectToolBaseSystem.CreateDefinitionsJob.UpdateObject adds CreationFlags.Attach and an update definition for
    /// the node (UpdateAttachedParent), so the node and its edges are regenerated with the roundabout composition.
    /// Placement goes through AgentToolSystem.CreateObjectDefinitions (the vanilla CreateDefinitions) with such a
    /// control point. Removal mirrors the bulldozer (AddEntity delete + AddAttachedParent node update).
    /// </summary>
    public class NetObjectPlaceJob : AgentToolSystem.BuildJob
    {
        public Api Api;
        public Entity Prefab, Node, Existing;
        NativeList<ControlPoint> points;
        NativeReference<ObjectToolBaseSystem.AttachmentData> attachment;
        HashSet<Entity> before;

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            var em = tool.EntityManager;
            if (!em.Exists(Node) || em.HasComponent<Deleted>(Node) || !em.HasComponent<NetNode>(Node)) { Result["error"] = "node vanished"; return deps; }
            var node = em.GetComponentData<NetNode>(Node);
            float elev = 0;
            if (em.HasComponent<Game.Net.Elevation>(Node)) { var el = em.GetComponentData<Game.Net.Elevation>(Node).m_Elevation; elev = (el.x + el.y) * 0.5f; }
            bool replace = Existing != Entity.Null && em.Exists(Existing) && !em.HasComponent<Deleted>(Existing);
            points = new NativeList<ControlPoint>(1, Allocator.Persistent);
            attachment = new NativeReference<ObjectToolBaseSystem.AttachmentData>(Allocator.Persistent);
            var fwd = math.mul(node.m_Rotation, new float3(0, 0, 1));
            points.Add(new ControlPoint
            {
                m_Position = node.m_Position,
                m_HitPosition = node.m_Position,
                m_Rotation = node.m_Rotation,
                m_Direction = math.normalizesafe(fwd.xz, new float2(0, 1)),
                m_HitDirection = new float3(0, 1, 0),
                m_OriginalEntity = replace ? Existing : Node,
                m_ElementIndex = new int2(-1, -1),
                m_Elevation = elev,
            });
            var h = tool.CreateObjectDefinitions(Prefab, points, attachment, deps);
            h.Complete();
            if (!DryRun) before = Api.SnapshotEdges();
            Result["replacing"] = replace ? Util.Id(Existing) : null;
            return h;
        }

        public override void AfterApply(AgentToolSystem tool)
        {
            try { Api.ReportNodeAfter(Node, Prefab, before, Result); }
            catch (Exception e) { Result["trackError"] = e.Message; }
        }

        public override void Dispose()
        {
            if (points.IsCreated) points.Dispose();
            if (attachment.IsCreated) attachment.Dispose();
        }
    }

    /// <summary>Bulldozer-style removal of an object attached to a node: delete definition + node update definition.</summary>
    public class NetObjectRemoveJob : AgentToolSystem.BuildJob
    {
        public Api Api;
        public Entity Object, Node;
        HashSet<Entity> before;

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            var em = tool.EntityManager;
            if (!em.Exists(Object) || em.HasComponent<Deleted>(Object) || !em.HasComponent<ObjTransform>(Object)) { Result["error"] = "object vanished"; return deps; }
            var tr = em.GetComponentData<ObjTransform>(Object);
            var od = new ObjectDefinition
            {
                m_Position = tr.m_Position, m_Rotation = tr.m_Rotation, m_LocalPosition = tr.m_Position, m_LocalRotation = tr.m_Rotation,
                m_ParentMesh = -1, m_Probability = 100, m_PrefabSubIndex = -1,
            };
            if (em.HasComponent<Game.Objects.Elevation>(Object))
            {
                var el = em.GetComponentData<Game.Objects.Elevation>(Object);
                od.m_Elevation = el.m_Elevation;
                od.m_ParentMesh = Game.Objects.ObjectUtils.GetSubParentMesh(el.m_Flags);
            }
            var d = ecb.CreateEntity();
            ecb.AddComponent(d, new CreationDefinition { m_Original = Object, m_Prefab = em.GetComponentData<PrefabRef>(Object).m_Prefab, m_Flags = CreationFlags.Delete });
            ecb.AddComponent(d, default(Updated));
            ecb.AddComponent(d, od);
            if (em.Exists(Node) && em.HasComponent<NetNode>(Node))
            {
                var node = em.GetComponentData<NetNode>(Node);
                var p = node.m_Position;
                var n = ecb.CreateEntity();
                ecb.AddComponent(n, new CreationDefinition { m_Original = Node });
                ecb.AddComponent(n, default(Updated));
                ecb.AddComponent(n, new NetCourse
                {
                    m_Curve = new Bezier4x3(p, p, p, p), m_Length = 0f, m_FixedIndex = -1,
                    m_StartPosition = new CoursePos { m_Entity = Node, m_Position = p, m_Rotation = node.m_Rotation, m_CourseDelta = 0f },
                    m_EndPosition = new CoursePos { m_Entity = Node, m_Position = p, m_Rotation = node.m_Rotation, m_CourseDelta = 1f },
                });
            }
            if (!DryRun) before = Api.SnapshotEdges();
            return deps;
        }

        public override void AfterApply(AgentToolSystem tool)
        {
            try { Api.ReportNodeAfter(Node, Entity.Null, before, Result); }
            catch (Exception e) { Result["trackError"] = e.Message; }
        }
    }

    public partial class Api
    {
        bool IsRoundaboutPrefab(Entity e) => em.HasComponent<NetObjectData>(e) && em.HasComponent<PrefabData>(e)
            && (em.GetComponentData<NetObjectData>(e).m_CompositionFlags.m_General & CompositionFlags.General.Roundabout) != 0;

        /// <summary>Net objects attached to a node (roundabouts and other node net objects).</summary>
        List<Entity> NetObjectsOn(Entity node)
        {
            var res = new List<Entity>();
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Objects.Attached>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var o in arr)
                    if (em.GetComponentData<Game.Objects.Attached>(o).m_Parent == node && em.HasComponent<NetObjectData>(em.GetComponentData<PrefabRef>(o).m_Prefab)) res.Add(o);
            return res;
        }

        /// <summary>GET /roundabouts?x&amp;z&amp;radius — roundabout prefabs (lock state, cost, diameter) and placed roundabouts.</summary>
        object Roundabouts(Pending p)
        {
            var list = new List<object>();
            var pq = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<NetObjectData>());
            var prefabs = new HashSet<Entity>();
            using (var arr = pq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (!IsRoundaboutPrefab(e)) continue;
                    prefabs.Add(e);
                    var d = new Dictionary<string, object> { { "name", PrefabName(e) }, { "locked", IsLocked(e) } };
                    if (em.HasComponent<PlaceableObjectData>(e)) d["cost"] = em.GetComponentData<PlaceableObjectData>(e).m_ConstructionCost;
                    if (em.HasComponent<ObjectGeometryData>(e)) { var g = em.GetComponentData<ObjectGeometryData>(e); d["diameter"] = Math.Round(math.max(g.m_Size.x, g.m_Size.z), 1); }
                    var nod = em.GetComponentData<NetObjectData>(e);
                    d["requireRoad"] = nod.m_RequireRoad.ToString();
                    d["roadPassThrough"] = nod.m_RoadPassThrough.ToString();
                    if (em.HasComponent<UIObjectData>(e) && em.GetComponentData<UIObjectData>(e).m_Group != Entity.Null) d["group"] = PrefabName(em.GetComponentData<UIObjectData>(e).m_Group);
                    list.Add(d);
                }
            list.Sort((x, y) => string.Compare((string)((Dictionary<string, object>)x)["name"], (string)((Dictionary<string, object>)y)["name"], StringComparison.Ordinal));
            var placed = new List<object>();
            int placedTotal = 0;
            bool area = p.Q("x") != null && p.Q("z") != null;
            var c = new float2(p.QF("x", 0), p.QF("z", 0));
            float radius = p.QF("radius", 1000f);
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Objects.Attached>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var o in arr)
                {
                    var pf = em.GetComponentData<PrefabRef>(o).m_Prefab;
                    if (!prefabs.Contains(pf)) continue;
                    placedTotal++;
                    var pos = em.GetComponentData<ObjTransform>(o).m_Position;
                    if (area && math.distance(pos.xz, c) > radius) continue;
                    if (placed.Count >= 200) continue;
                    placed.Add(new Dictionary<string, object> { { "id", Id(o) }, { "prefab", PrefabName(pf) }, { "pos", F2(pos) }, { "node", Id(em.GetComponentData<Game.Objects.Attached>(o).m_Parent) } });
                }
            return new Dictionary<string, object> { { "count", list.Count }, { "prefabs", list }, { "placedTotal", placedTotal }, { "placed", placed } };
        }

        /// <summary>
        /// POST /roundabout {node:id | at:[x,z] (+radius 30, minDegree 3), prefab (default: smallest unlocked), remove:false, dryRun}
        /// Places (or replaces/removes) a roundabout net object on an existing node, through the object tool's own definitions.
        /// </summary>
        object Roundabout(Args a)
        {
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy", 409);
            var nq = NodeQuery();
            Entity node = Entity.Null;
            if (a.Has("node"))
            {
                node = Resolve(a.Raw("node"), nq);
                if (node == Entity.Null || !IsLive(node) || !em.HasComponent<NetNode>(node)) return new ApiError("node not found: " + a.Raw("node") + " (stale id? use at:[x,z])");
            }
            else if (a.Has("at"))
            {
                float3 at;
                try { at = P(a.Raw("at")); } catch { return new ApiError("at must be [x, z]"); }
                float radius = math.clamp(a.F("radius", 30f), 1f, 300f);
                int minDegree = math.max(1, a.I("minDegree", 3));
                float bd = radius;
                using (var arr = nq.ToEntityArray(Allocator.Temp))
                    foreach (var n in arr)
                    {
                        if (Degree(n) < minDegree) continue;
                        float d = math.distance(em.GetComponentData<NetNode>(n).m_Position.xz, at.xz);
                        if (d <= bd) { bd = d; node = n; }
                    }
                if (node == Entity.Null) return new ApiError("no node with degree >= " + minDegree + " within " + radius + " m of at (GET /nodes)");
            }
            else return new ApiError("give node:id or at:[x,z]");

            var existing = NetObjectsOn(node);
            var info = new Dictionary<string, object> { { "node", NodeJson(node, em.GetComponentData<NetNode>(node).m_Position.xz) } };
            if (existing.Count > 0) info["existing"] = existing.Select(o => (object)new Dictionary<string, object> { { "id", Id(o) }, { "prefab", PrefabName(em.GetComponentData<PrefabRef>(o).m_Prefab) } }).ToList();

            if (a.B("remove"))
            {
                var target = existing.FirstOrDefault(o => IsRoundaboutPrefab(em.GetComponentData<PrefabRef>(o).m_Prefab));
                if (target == Entity.Null) { info["applied"] = false; info["note"] = "no roundabout on this node"; return info; }
                var rj = new NetObjectRemoveJob { Api = this, Object = target, Node = node, DryRun = a.B("dryRun") };
                info["removing"] = Id(target);
                tool.Enqueue(rj);
                return new BuildOperation(rj, info);
            }

            Entity prefab;
            if (a.Has("prefab"))
            {
                prefab = FindPrefab(a.Str("prefab"), IsRoundaboutPrefab);
                if (prefab == Entity.Null) return new ApiError("roundabout prefab not found: " + a.Str("prefab") + " (GET /roundabouts)");
            }
            else
            {
                prefab = Entity.Null; float best = float.MaxValue;
                var pq = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<NetObjectData>());
                using (var arr = pq.ToEntityArray(Allocator.Temp))
                    foreach (var e in arr)
                    {
                        // only real roundabouts (pillars carry the composition flag too); size class: small|medium|large|xl (default medium)
                        var pname = PrefabName(e) ?? "";
                        if (!IsRoundaboutPrefab(e) || IsLocked(e) || pname.IndexOf("Roundabout", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var want = (a.Str("size") ?? "medium").ToLowerInvariant();
                        if (!pname.StartsWith(want == "xl" ? "XL" : char.ToUpperInvariant(want[0]) + want.Substring(1), StringComparison.OrdinalIgnoreCase)) continue;
                        float size = em.HasComponent<ObjectGeometryData>(e) ? em.GetComponentData<ObjectGeometryData>(e).m_Size.x : 0;
                        if (size < best || (size == best && string.CompareOrdinal(pname, PrefabName(prefab) ?? "") < 0)) { best = size; prefab = e; }
                    }
                if (prefab == Entity.Null) return new ApiError("no unlocked roundabout prefab (GET /roundabouts)");
            }
            if (IsLocked(prefab)) return new ApiError(PrefabName(prefab) + " is still locked");
            info["prefab"] = PrefabName(prefab);
            var ex = existing.FirstOrDefault();
            var job = new NetObjectPlaceJob { Api = this, Prefab = prefab, Node = node, Existing = ex, DryRun = a.B("dryRun") };
            tool.Enqueue(job);
            return new BuildOperation(job, info);
        }

        /// <summary>After a node change: node state, net objects on it, and edge ids that changed around it.</summary>
        internal void ReportNodeAfter(Entity node, Entity prefab, HashSet<Entity> before, Dictionary<string, object> result)
        {
            if (IsLive(node) && em.HasComponent<NetNode>(node))
            {
                result["nodeAfter"] = NodeJson(node, em.GetComponentData<NetNode>(node).m_Position.xz);
                var objs = NetObjectsOn(node);
                result["netObjects"] = objs.Select(o => (object)new Dictionary<string, object> { { "id", Id(o) }, { "prefab", PrefabName(em.GetComponentData<PrefabRef>(o).m_Prefab) } }).ToList();
                if (prefab != Entity.Null) result["placedId"] = objs.Where(o => em.GetComponentData<PrefabRef>(o).m_Prefab == prefab).Select(o => Id(o)).FirstOrDefault();
            }
            else result["nodeAfter"] = null;
            if (before == null) return;
            var live = SnapshotEdges();
            result["staleEdgeIds"] = before.Where(e => !live.Contains(e)).Take(100).Select(e => (object)Id(e)).ToList();
            result["newEdgeIds"] = live.Where(e => !before.Contains(e)).Take(100).Select(e => (object)Id(e)).ToList();
        }
    }
}
