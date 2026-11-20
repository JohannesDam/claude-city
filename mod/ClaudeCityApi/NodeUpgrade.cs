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
using NetEdge = Game.Net.Edge;
using NetNode = Game.Net.Node;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Intersection (node) upgrades — Traffic Lights, All-way Stop — written exactly like
    /// NetToolSystem does for a click on a node with an upgrade prefab (AddControlPoints node branch +
    /// CreateDefinitionsJob.CreateUpgrade(point, isStart, isEnd)):
    ///   CreationDefinition { m_Original = node, m_Prefab = the node's own prefab, m_Flags = SubElevation|Align (+Upgrade|Parent when it changes) }
    ///   Upgraded { m_Flags = (node.Upgraded &amp; ~remove) | (add &amp; ~old) }
    ///   NetCourse: a zero-length curve at the node, start/end CoursePos on the node (IsFirst / IsLast).
    /// old = node.Upgraded plus TrafficLights (if any connected node end has them) or RemoveTrafficLights.
    /// Remove mode (right click): add = unset &amp; RemoveTrafficLights, remove = set.
    /// </summary>
    public class NodeUpgradeJob : AgentToolSystem.BuildJob
    {
        public class Item { public Entity Node; public CompositionFlags NewFlags; }
        public List<Item> Items = new List<Item>();

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            var em = tool.EntityManager;
            int n = 0;
            foreach (var it in Items)
            {
                var node = it.Node;
                if (!em.Exists(node) || em.HasComponent<Deleted>(node) || !em.HasComponent<NetNode>(node) || !em.HasComponent<PrefabRef>(node)) continue;
                var p = em.GetComponentData<NetNode>(node).m_Position;
                var curve = new Bezier4x3(p, p, p, p);
                float elev = 0;
                if (em.HasComponent<Game.Net.Elevation>(node)) { var el = em.GetComponentData<Game.Net.Elevation>(node).m_Elevation; elev = (el.x + el.y) * 0.5f; }
                var start = new CoursePos
                {
                    m_Entity = node, m_Position = p, m_Elevation = new float2(elev), m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, 0f)),
                    m_CourseDelta = 0f, m_ParentMesh = -1, m_Flags = CoursePosFlags.IsFirst,
                };
                var end = start;
                end.m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, 1f));
                end.m_CourseDelta = 1f;
                end.m_Flags = CoursePosFlags.IsLast;
                var d = ecb.CreateEntity();
                ecb.AddComponent(d, new CreationDefinition
                {
                    m_Original = node,
                    m_Prefab = em.GetComponentData<PrefabRef>(node).m_Prefab,
                    m_Flags = CreationFlags.SubElevation | CreationFlags.Align | CreationFlags.Upgrade | CreationFlags.Parent,
                });
                ecb.AddComponent(d, new Upgraded { m_Flags = it.NewFlags });
                ecb.AddComponent(d, default(Updated));
                ecb.AddComponent(d, new NetCourse { m_Curve = curve, m_StartPosition = start, m_EndPosition = end, m_Length = 0f, m_FixedIndex = -1 });
                n++;
            }
            Result["definitions"] = n;
            return deps;
        }
    }

    public partial class Api
    {
        bool IsNodeUpgradePrefab(Entity e) => IsNetUpgradePrefab(e) && (em.GetComponentData<PlaceableNetData>(e).m_SetUpgradeFlags.m_General & GeneralNodeMask) != 0;

        /// <summary>
        /// POST /nodeupgrade {upgrade:"Traffic Lights"|"All-way Stop", remove:false,
        ///   nodes:[ids] | near:[x,z] (+radius 40: nearest intersection) | box:[x0,z0,x1,z1] (all intersections), dryRun}
        /// Traffic Lights: add lights (clears all-way stop). remove:true with Traffic Lights: no lights (also where the road
        /// type has them by default). All-way Stop: stop signs on every approach (clears lights); remove:true: back to default.
        /// </summary>
        object NodeUpgrade(Args a)
        {
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy", 409);
            var name = a.Str("upgrade", a.Str("prefab", ""));
            var up = FindPrefab(name, IsNodeUpgradePrefab);
            if (up == Entity.Null) return new ApiError("intersection upgrade not found: '" + name + "' (use \"Traffic Lights\" or \"All-way Stop\"; GET /roadupgrades kind=node)");
            bool remove = a.B("remove");
            if (!remove && IsLocked(up)) return new ApiError(PrefabName(up) + " is still locked");
            var pnd = em.GetComponentData<PlaceableNetData>(up);
            var set = pnd.m_SetUpgradeFlags; var unset = pnd.m_UnsetUpgradeFlags;
            CompositionFlags add, rem;
            if (remove)
            {
                var u = unset;
                u.m_General &= CompositionFlags.General.RemoveTrafficLights;
                u.m_Left = 0; u.m_Right = 0;
                add = u; rem = set;
            }
            else { add = set; rem = unset; }
            var genBits = set.m_General | unset.m_General;

            // selection
            var nodes = new List<Entity>();
            var missing = new List<object>();
            var nq = NodeQuery();
            var ids = a.List("nodes");
            if (ids != null)
                foreach (var id in ids)
                {
                    var n = Resolve(id, nq);
                    if (n == Entity.Null || !IsLive(n) || !em.HasComponent<NetNode>(n)) { missing.Add(id); continue; }
                    if (!nodes.Contains(n)) nodes.Add(n);
                }
            if (a.Has("near"))
            {
                float3 c;
                try { c = P(a.Raw("near")); } catch { return new ApiError("near must be [x, z]"); }
                float radius = math.clamp(a.F("radius", 40f), 1f, 500f);
                Entity best = Entity.Null; float bd = radius;
                using (var arr = nq.ToEntityArray(Allocator.Temp))
                    foreach (var n in arr)
                    {
                        if (Degree(n) < 3) continue;
                        float d = math.distance(em.GetComponentData<NetNode>(n).m_Position.xz, c.xz);
                        if (d <= bd) { bd = d; best = n; }
                    }
                if (best == Entity.Null) return new ApiError("no intersection (degree >= 3) within " + radius + " m of near");
                if (!nodes.Contains(best)) nodes.Add(best);
            }
            if (a.Has("box"))
            {
                if (!TryBox(a.Raw("box"), out var bmin, out var bmax)) return new ApiError("box must be [x0,z0,x1,z1]");
                int maxNodes = math.clamp(a.I("maxNodes", 100), 1, 2000);
                var inBox = new List<Entity>();
                using (var arr = nq.ToEntityArray(Allocator.Temp))
                    foreach (var n in arr)
                        if (Degree(n) >= 3 && InBox(em.GetComponentData<NetNode>(n).m_Position.xz, bmin, bmax) && !nodes.Contains(n)) inBox.Add(n);
                if (inBox.Count > maxNodes) return new ApiError("box matched " + inBox.Count + " intersections (> maxNodes " + maxNodes + ")");
                nodes.AddRange(inBox);
            }
            if (ids == null && !a.Has("near") && !a.Has("box")) return new ApiError("select intersections with nodes:[ids], near:[x,z] or box:[x0,z0,x1,z1] (GET /nodes lists them)");

            var job = new NodeUpgradeJob { DryRun = a.B("dryRun") };
            var report = new List<object>();
            foreach (var n in nodes)
            {
                var r = new Dictionary<string, object> { { "id", Id(n) }, { "pos", F2(em.GetComponentData<NetNode>(n).m_Position) }, { "degree", Degree(n) } };
                report.Add(r);
                var prefab = em.GetComponentData<PrefabRef>(n).m_Prefab;
                r["prefab"] = PrefabName(prefab);
                if (Degree(n) < 3) { r["skipped"] = "not an intersection (degree < 3)"; continue; }
                if (!em.HasComponent<NetData>(prefab) || (em.GetComponentData<NetData>(prefab).m_GeneralFlagMask & genBits) == 0) { r["skipped"] = PrefabName(up) + " is not supported on this road type"; continue; }
                var upgraded = em.HasComponent<Upgraded>(n) ? em.GetComponentData<Upgraded>(n).m_Flags : default(CompositionFlags);
                var old = upgraded;
                if (em.HasBuffer<ConnectedEdge>(n))
                {
                    bool lights = (NodeCompositionFlags(n).m_General & CompositionFlags.General.TrafficLights) != 0;
                    old.m_General |= lights ? CompositionFlags.General.TrafficLights : CompositionFlags.General.RemoveTrafficLights;
                    r["trafficLightsBefore"] = lights;
                }
                r["allWayStopBefore"] = (old.m_General & CompositionFlags.General.AllWayStop) != 0;
                var newFlags = (upgraded & ~rem) | (add & ~old);
                bool changed = ((old & ~rem) | add) != old;
                r["before"] = FlagsJson(upgraded);
                r["after"] = FlagsJson(newFlags);
                if (!changed) { r["skipped"] = remove ? "nothing to remove" : "already set"; continue; }
                r["changes"] = true;
                job.Items.Add(new NodeUpgradeJob.Item { Node = n, NewFlags = newFlags });
            }
            var info = new Dictionary<string, object> { { "upgrade", PrefabName(up) }, { "remove", remove }, { "nodes", report } };
            if (missing.Count > 0) info["missing"] = missing;
            if (job.Items.Count == 0) { info["applied"] = false; info["note"] = "nothing to change"; return info; }
            tool.Enqueue(job);
            return new BuildOperation(job, info);
        }
    }
}
