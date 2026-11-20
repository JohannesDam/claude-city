using System;
using System.Collections.Generic;
using System.Linq;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using NetNode = Game.Net.Node;
using NetEdge = Game.Net.Edge;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Connecting a net (high-voltage line, pipe, ...) to a building's connection point.
    ///
    /// Power plants, emergency batteries, incinerators etc. carry a connection point: a node in the
    /// building's SubNet buffer (Owner = the building) whose prefab requires a layer
    /// (NetData.m_RequiredLayers, e.g. PowerlineHigh). ConnectionWarningSystem.CheckOwnersJob shows
    /// "Powerline Not Connected" until a non-owned edge of that layer starts or ends at that node
    /// (FindEdgeConnections over the node's ConnectedEdge buffer). The vanilla net tool connects by
    /// snapping its control point to the node (NetToolSystem.SnapJob.NetIterator.HandleGeometry, which
    /// does not skip owned nodes, only checks NetUtils.CanConnect), so ControlPoint.m_OriginalEntity =
    /// the node, and CreateDefinitionsJob.GetCoursePos turns that into CoursePos.m_Entity = the node.
    /// The generators then reuse that node (Temp.m_Original = node) instead of creating a new one.
    /// /road does the same: "connectTo" names the building (or the node) for an endpoint, and plain
    /// endpoints of non-road nets also snap to a compatible connection node within the snap distance.
    /// </summary>
    public partial class Api
    {
        static bool NetLayersMatch(NetData point, NetData net) =>
            (point.m_RequiredLayers & net.m_RequiredLayers) != 0 && NetUtils.CanConnect(point, net);

        /// <summary>Top building of an owned entity (walks the Owner chain), or Null.</summary>
        Entity OwnerBuilding(Entity e)
        {
            for (int i = 0; i < 6 && e != Entity.Null && em.Exists(e); i++)
            {
                if (!em.HasComponent<Owner>(e)) return em.HasComponent<Building>(e) ? e : Entity.Null;
                e = em.GetComponentData<Owner>(e).m_Owner;
                if (em.HasComponent<Building>(e) && !em.HasComponent<Owner>(e)) return e;
            }
            return Entity.Null;
        }

        /// <summary>Is this node a connection point the given net can attach to (owned by a building, matching layer)?</summary>
        bool IsConnectionNodeFor(Entity node, NetData net)
        {
            if (!em.HasComponent<NetNode>(node) || !em.HasComponent<Owner>(node) || !em.HasComponent<PrefabRef>(node)) return false;
            if (em.HasComponent<Deleted>(node) || em.HasComponent<Temp>(node)) return false;
            var pf = em.GetComponentData<PrefabRef>(node).m_Prefab;
            if (!em.HasComponent<NetData>(pf)) return false;
            return NetLayersMatch(em.GetComponentData<NetData>(pf), net);
        }

        /// <summary>The building's (and its installed upgrades') connection nodes for a net prefab.</summary>
        internal List<Entity> ConnectionNodes(Entity building, Entity netPrefab)
        {
            var res = new List<Entity>();
            if (!em.HasComponent<NetData>(netPrefab)) return res;
            var net = em.GetComponentData<NetData>(netPrefab);
            var owners = new List<Entity> { building };
            if (em.HasBuffer<InstalledUpgrade>(building))
            {
                var up = em.GetBuffer<InstalledUpgrade>(building, true);
                for (int i = 0; i < up.Length; i++) owners.Add(up[i].m_Upgrade);
            }
            foreach (var o in owners)
            {
                if (!em.Exists(o) || !em.HasBuffer<Game.Net.SubNet>(o)) continue;
                var sn = em.GetBuffer<Game.Net.SubNet>(o, true);
                for (int i = 0; i < sn.Length; i++)
                {
                    var n = sn[i].m_SubNet;
                    if (em.Exists(n) && IsConnectionNodeFor(n, net) && !res.Contains(n)) res.Add(n);
                }
            }
            return res;
        }

        /// <summary>All connection points of a building (any layer), for reports: [{node, prefab, layers, pos, externalEdges}].</summary>
        internal List<object> ConnectionPointsJson(Entity building)
        {
            var list = new List<object>();
            var owners = new List<Entity> { building };
            if (em.HasBuffer<InstalledUpgrade>(building))
            {
                var up = em.GetBuffer<InstalledUpgrade>(building, true);
                for (int i = 0; i < up.Length; i++) owners.Add(up[i].m_Upgrade);
            }
            foreach (var o in owners)
            {
                if (!em.Exists(o) || !em.HasBuffer<Game.Net.SubNet>(o)) continue;
                var sn = em.GetBuffer<Game.Net.SubNet>(o, true);
                for (int i = 0; i < sn.Length; i++)
                {
                    var n = sn[i].m_SubNet;
                    if (!em.Exists(n) || !em.HasComponent<NetNode>(n) || !em.HasComponent<PrefabRef>(n)) continue;
                    var pf = em.GetComponentData<PrefabRef>(n).m_Prefab;
                    if (!em.HasComponent<NetData>(pf)) continue;
                    var req = em.GetComponentData<NetData>(pf).m_RequiredLayers;
                    if ((req & (Layer.PowerlineHigh | Layer.PowerlineLow | Layer.WaterPipe | Layer.SewagePipe)) == 0) continue;
                    list.Add(ConnectionPointJson(n, req));
                }
            }
            return list;
        }

        Dictionary<string, object> ConnectionPointJson(Entity node, Layer layers)
        {
            return new Dictionary<string, object>
            {
                { "node", Id(node) }, { "prefab", PrefabName(em.GetComponentData<PrefabRef>(node).m_Prefab) },
                { "layers", layers.ToString() }, { "pos", F2(em.GetComponentData<NetNode>(node).m_Position) },
                { "externalEdges", ExternalEdgesAt(node, layers) },
            };
        }

        /// <summary>Edges of the given layers that start or end at the node and are not owned by the node's building.</summary>
        int ExternalEdgesAt(Entity node, Layer layers)
        {
            if (!em.HasBuffer<ConnectedEdge>(node)) return 0;
            var owner = OwnerBuilding(node);
            int n = 0;
            var ce = em.GetBuffer<ConnectedEdge>(node, true);
            for (int i = 0; i < ce.Length; i++)
            {
                var ed = ce[i].m_Edge;
                if (!em.Exists(ed) || !em.HasComponent<NetEdge>(ed) || em.HasComponent<Deleted>(ed)) continue;
                var edge = em.GetComponentData<NetEdge>(ed);
                if (edge.m_Start != node && edge.m_End != node) continue;
                if (owner != Entity.Null && OwnerBuilding(ed) == owner) continue;
                var pf = em.GetComponentData<PrefabRef>(ed).m_Prefab;
                if (em.HasComponent<NetData>(pf) && (em.GetComponentData<NetData>(pf).m_RequiredLayers & layers) != 0) n++;
            }
            return n;
        }

        /// <summary>CoursePos that references an existing (connection) node, like GetCoursePos for a snapped control point.</summary>
        CoursePos NodeCoursePos(Entity node)
        {
            var np = em.GetComponentData<NetNode>(node).m_Position;
            float2 elev = em.HasComponent<Game.Net.Elevation>(node) ? em.GetComponentData<Game.Net.Elevation>(node).m_Elevation : default(float2);
            return new CoursePos { m_Entity = node, m_Position = np, m_Elevation = elev, m_ParentMesh = -1 };
        }

        /// <summary>Nearest building connection node within snap distance that the net prefab can attach to (Null if none).</summary>
        Entity NearestConnectionNode(float3 p, float snap, Entity netPrefab, out float dist)
        {
            dist = float.MaxValue;
            if (netPrefab == Entity.Null || !em.HasComponent<NetData>(netPrefab)) return Entity.Null;
            var net = em.GetComponentData<NetData>(netPrefab);
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<NetNode>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            Entity best = Entity.Null;
            using (var ents = q.ToEntityArray(Allocator.TempJob))
            using (var nodes = q.ToComponentDataArray<NetNode>(Allocator.TempJob))
                for (int i = 0; i < ents.Length; i++)
                {
                    float d = math.distance(nodes[i].m_Position.xz, p.xz);
                    if (d >= snap || d >= dist) continue;
                    if (!IsConnectionNodeFor(ents[i], net) || OwnerBuilding(ents[i]) == Entity.Null) continue;
                    best = ents[i]; dist = d;
                }
            return best;
        }

        /// <summary>One endpoint attached to a connection node by "connectTo".</summary>
        internal sealed class NetConnection
        {
            public int Index;            // 0 = first point, -1 = last point (resolved by the caller)
            public string End;           // "start" | "end"
            public Entity Building, Node;
            public Layer Layers;
            public float2 Requested;
            public Dictionary<string, object> Info;
        }

        /// <summary>
        /// Parses "connectTo": a building id (or connection-node id) for the nearer endpoint, {start, end}, or [start, end]
        /// (null entries allowed). Returns an error or null; fills conns with the resolved endpoint attachments.
        /// </summary>
        internal ApiError ParseConnectTo(Args a, Entity netPrefab, float2 first, float2 last, List<NetConnection> conns)
        {
            if (!a.Has("connectTo")) return null;
            if (!em.HasComponent<NetData>(netPrefab)) return new ApiError("connectTo: " + PrefabName(netPrefab) + " has no net data");
            var raw = a.Raw("connectTo");
            object startId = null, endId = null, eitherId = null;
            if (raw is Dictionary<string, object> dd)
            {
                dd.TryGetValue("start", out startId);
                dd.TryGetValue("end", out endId);
                if (startId == null && endId == null) return new ApiError("connectTo {start, end}: give at least one building id");
            }
            else if (raw is List<object> l)
            {
                if (l.Count != 2) return new ApiError("connectTo as a list must be [startBuilding|null, endBuilding|null]");
                startId = l[0]; endId = l[1];
                if (startId == null && endId == null) return new ApiError("connectTo [start, end]: give at least one building id");
            }
            else eitherId = raw;

            var anyQ = em.CreateEntityQuery(new EntityQueryDesc
            {
                Any = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<NetNode>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });

            ApiError Resolve1(object id, string which, float2 near, out NetConnection c)
            {
                c = null;
                var target = Resolve(id, anyQ);
                if (target == Entity.Null || !IsLive(target)) return new ApiError("connectTo: building or node not found: " + id, 404);
                var net = em.GetComponentData<NetData>(netPrefab);
                Entity building, node = Entity.Null;
                if (em.HasComponent<NetNode>(target) && !em.HasComponent<Building>(target))
                {
                    if (!IsConnectionNodeFor(target, net)) return new ApiError("connectTo: node " + Id(target) + " is not a building connection point for " + PrefabName(netPrefab));
                    node = target;
                    building = OwnerBuilding(target);
                }
                else
                {
                    building = em.HasComponent<Owner>(target) && OwnerBuilding(target) != Entity.Null ? OwnerBuilding(target) : target;
                    if (!em.HasComponent<Building>(building) || !em.HasComponent<PrefabRef>(building)) return new ApiError("connectTo: " + Id(target) + " is not a building or a connection node", 404);
                    var cands = ConnectionNodes(building, netPrefab);
                    if (cands.Count == 0)
                    {
                        var err = new ApiError("connectTo: " + PrefabName(em.GetComponentData<PrefabRef>(building).m_Prefab) + " " + Id(building)
                            + " has no connection point for " + PrefabName(netPrefab) + " (required layers " + net.m_RequiredLayers + ")");
                        var pts = ConnectionPointsJson(building);
                        if (pts.Count > 0) err["connectionPoints"] = pts;
                        return err;
                    }
                    float bd = float.MaxValue;
                    foreach (var n in cands)
                    {
                        float d = math.distance(em.GetComponentData<NetNode>(n).m_Position.xz, near);
                        if (d < bd) { bd = d; node = n; }
                    }
                }
                var nodePf = em.GetComponentData<PrefabRef>(node).m_Prefab;
                var layers = em.GetComponentData<NetData>(nodePf).m_RequiredLayers & net.m_RequiredLayers;
                var np = em.GetComponentData<NetNode>(node).m_Position;
                c = new NetConnection { End = which, Building = building, Node = node, Layers = layers, Requested = near };
                c.Info = new Dictionary<string, object>
                {
                    { "endpoint", which }, { "building", building == Entity.Null ? null : Id(building) },
                    { "buildingPrefab", building == Entity.Null ? null : PrefabName(em.GetComponentData<PrefabRef>(building).m_Prefab) },
                    { "node", Id(node) }, { "nodePrefab", PrefabName(nodePf) }, { "layers", layers.ToString() },
                    { "pos", F2(np) }, { "requested", new List<object> { Math.Round(near.x, 1), Math.Round(near.y, 1) } },
                    { "moved", Math.Round(math.distance(np.xz, near), 1) },
                    { "externalEdgesBefore", ExternalEdgesAt(node, layers) },
                };
                return null;
            }

            if (eitherId != null)
            {
                // the endpoint nearer to the building's connection point gets attached
                var target = Resolve(eitherId, anyQ);
                string which = "start";
                if (target != Entity.Null && IsLive(target))
                {
                    var b = em.HasComponent<NetNode>(target) && !em.HasComponent<Building>(target) ? target : (OwnerBuilding(target) != Entity.Null ? OwnerBuilding(target) : target);
                    var nodes = em.HasComponent<NetNode>(b) ? new List<Entity> { b } : ConnectionNodes(b, netPrefab);
                    if (nodes.Count > 0)
                    {
                        float ds = nodes.Min(n => math.distance(em.GetComponentData<NetNode>(n).m_Position.xz, first));
                        float de = nodes.Min(n => math.distance(em.GetComponentData<NetNode>(n).m_Position.xz, last));
                        which = de < ds ? "end" : "start";
                    }
                }
                var err = Resolve1(eitherId, which, which == "start" ? first : last, out var c);
                if (err != null) return err;
                c.Index = which == "start" ? 0 : -1;
                conns.Add(c);
                return null;
            }
            if (startId != null)
            {
                var err = Resolve1(startId, "start", first, out var c);
                if (err != null) return err;
                c.Index = 0;
                conns.Add(c);
            }
            if (endId != null)
            {
                var err = Resolve1(endId, "end", last, out var c);
                if (err != null) return err;
                c.Index = -1;
                conns.Add(c);
            }
            if (conns.Count == 2 && conns[0].Node == conns[1].Node) return new ApiError("connectTo: start and end resolve to the same connection node " + Id(conns[0].Node));
            return null;
        }

        /// <summary>The explicit connectTo attachments plus endpoints that snapped to a connection point on their own.</summary>
        internal List<NetConnection> AttachedConnections(List<object> report, List<NetConnection> explicitConns, Entity netPrefab)
        {
            var res = new List<NetConnection>(explicitConns);
            if (!em.HasComponent<NetData>(netPrefab)) return res;
            var net = em.GetComponentData<NetData>(netPrefab);
            foreach (var o in report)
            {
                if (!(o is Dictionary<string, object> d) || !(d.TryGetValue("snappedTo", out var s) && (s as string) == "connectionPoint")) continue;
                var node = Resolve(d["id"], NodeQuery());
                if (node == Entity.Null || res.Any(c => c.Node == node) || !em.HasComponent<NetNode>(node)) continue;
                var nodePf = em.GetComponentData<PrefabRef>(node).m_Prefab;
                var layers = em.GetComponentData<NetData>(nodePf).m_RequiredLayers & net.m_RequiredLayers;
                var b = OwnerBuilding(node);
                var c = new NetConnection { End = "auto", Building = b, Node = node, Layers = layers };
                c.Info = new Dictionary<string, object>
                {
                    { "endpoint", "snapped" }, { "building", b == Entity.Null ? null : Id(b) },
                    { "buildingPrefab", b == Entity.Null ? null : PrefabName(em.GetComponentData<PrefabRef>(b).m_Prefab) },
                    { "node", Id(node) }, { "nodePrefab", PrefabName(nodePf) }, { "layers", layers.ToString() },
                    { "pos", F2(em.GetComponentData<NetNode>(node).m_Position) }, { "externalEdgesBefore", ExternalEdgesAt(node, layers) },
                };
                res.Add(c);
            }
            return res;
        }

        /// <summary>
        /// Wraps a /road build: while the preview exists it checks that the temporary node at each connection point
        /// is the building's node (Temp.m_Original) with the new edge attached (no extra floating node next to it);
        /// after a real apply it reads the node's external edges and the building's notification icons.
        /// </summary>
        internal sealed class NetConnectOperation : BuildOperation, IOperation
        {
            readonly Api api;
            readonly AgentToolSystem.BuildJob job;
            readonly List<NetConnection> conns;
            readonly Dictionary<Entity, Dictionary<string, object>> seen = new Dictionary<Entity, Dictionary<string, object>>();
            EntityQuery tempNodes;
            bool qReady;
            int afterFrames;

            public NetConnectOperation(Api api, AgentToolSystem.BuildJob job, List<NetConnection> conns, Dictionary<string, object> extra)
                : base(job, extra) { this.api = api; this.job = job; this.conns = conns; }

            public new bool Step(out object result)
            {
                if (!job.Done && (job.State == 1 || job.State == 5) && job.Frames >= 4)
                {
                    try { Observe(); } catch (Exception e) { Log.Warn("connectTo preview: " + e.Message); }
                }
                if (!base.Step(out result)) return false;
                if (!(result is Dictionary<string, object> r)) return true;
                bool applied = r.TryGetValue("applied", out var ap) && ap is bool b && b;
                // the warning icons are refreshed by ConnectionWarningSystem a few frames after the apply
                if (applied && afterFrames++ < 10) { result = null; return false; }
                var list = new List<object>();
                foreach (var c in conns)
                {
                    var d = new Dictionary<string, object>(c.Info);
                    if (seen.TryGetValue(c.Node, out var pv)) d["preview"] = pv;
                    else d["preview"] = new Dictionary<string, object> { { "attached", false }, { "note", "no temporary copy of the connection node was seen in the preview" } };
                    if (applied && api.em.Exists(c.Node) && !api.em.HasComponent<Deleted>(c.Node))
                    {
                        int ext = api.ExternalEdgesAt(c.Node, c.Layers);
                        d["externalEdgesAfter"] = ext;
                        d["connected"] = ext > 0;
                        if (c.Building != Entity.Null && api.em.Exists(c.Building)) d["buildingIcons"] = api.IconNames(c.Building);
                    }
                    list.Add(d);
                }
                r["connections"] = list;
                bool allAttached = conns.All(c => seen.TryGetValue(c.Node, out var pv) && pv.TryGetValue("attached", out var at) && at is bool ab && ab);
                r["connected"] = applied ? conns.All(c => api.em.Exists(c.Node) && api.ExternalEdgesAt(c.Node, c.Layers) > 0) : allAttached;
                return true;
            }

            void Observe()
            {
                var em = api.em;
                if (!qReady)
                {
                    tempNodes = em.CreateEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<NetNode>(), ComponentType.Exclude<Deleted>());
                    qReady = true;
                }
                using (var ents = tempNodes.ToEntityArray(Allocator.Temp))
                    foreach (var c in conns)
                    {
                        if (!em.Exists(c.Node)) continue;
                        var target = em.GetComponentData<NetNode>(c.Node).m_Position;
                        Entity copy = Entity.Null;
                        int floating = 0;
                        foreach (var t in ents)
                        {
                            var orig = em.GetComponentData<Temp>(t).m_Original;
                            if (orig == c.Node) { copy = t; continue; }
                            if (orig == Entity.Null && math.distance(em.GetComponentData<NetNode>(t).m_Position.xz, target.xz) < 1f) floating++;
                        }
                        var d = new Dictionary<string, object> { { "attached", false } };
                        if (copy != Entity.Null)
                        {
                            int newEdges = 0;
                            if (em.HasBuffer<ConnectedEdge>(copy))
                            {
                                var ce = em.GetBuffer<ConnectedEdge>(copy, true);
                                for (int i = 0; i < ce.Length; i++)
                                {
                                    var ed = ce[i].m_Edge;
                                    if (!em.Exists(ed) || !em.HasComponent<Temp>(ed) || !em.HasComponent<NetEdge>(ed)) continue;
                                    var edge = em.GetComponentData<NetEdge>(ed);
                                    if (edge.m_Start != copy && edge.m_End != copy) continue;
                                    if (em.GetComponentData<Temp>(ed).m_Original != Entity.Null) continue; // an existing edge
                                    var pf = em.GetComponentData<PrefabRef>(ed).m_Prefab;
                                    if (em.HasComponent<NetData>(pf) && (em.GetComponentData<NetData>(pf).m_RequiredLayers & c.Layers) != 0) newEdges++;
                                }
                            }
                            d["attached"] = newEdges > 0;
                            d["newEdgesAtNode"] = newEdges;
                            d["previewNode"] = Id(copy);
                        }
                        if (floating > 0) d["floatingNodesAtPoint"] = floating;
                        if (copy != Entity.Null || !seen.ContainsKey(c.Node)) seen[c.Node] = d;
                    }
            }
        }
    }
}
