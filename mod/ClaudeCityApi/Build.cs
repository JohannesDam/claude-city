using System;
using System.Collections.Generic;
using Colossal.Mathematics;
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
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        AgentToolSystem Tool => world.GetOrCreateSystemManaged<AgentToolSystem>();

        EntityQuery EdgeQuery() => em.CreateEntityQuery(ComponentType.ReadOnly<NetEdge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Owner>());
        EntityQuery NodeQuery() => em.CreateEntityQuery(ComponentType.ReadOnly<NetNode>(), ComponentType.ReadOnly<ConnectedEdge>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Owner>());

        static float3 P(object o)
        {
            var l = o as List<object>;
            if (l == null || l.Count < 2) throw new ArgumentException("point must be [x, z]");
            return new float3((float)(double)l[0], 0, (float)(double)l[1]);
        }

        bool SameNetKind(Entity edge, bool wantRoad)
        {
            return em.HasComponent<Road>(edge) == wantRoad;
        }

        /// <summary>Find what a new net point should connect to: an existing node, a split point on an edge, or nothing.</summary>
        CoursePos SnapPoint(float3 p, float snap, bool wantRoad, List<object> report)
        {
            var cp = new CoursePos { m_Position = p, m_Elevation = 0, m_ParentMesh = -1 };
            Entity bestNode = Entity.Null; float bestNodeD = snap;
            using (var nodes = NodeQuery().ToEntityArray(Allocator.Temp))
            {
                foreach (var n in nodes)
                {
                    var np = em.GetComponentData<NetNode>(n).m_Position;
                    var d = math.distance(np.xz, p.xz);
                    if (d >= bestNodeD) continue;
                    var ce = em.GetBuffer<ConnectedEdge>(n, true);
                    bool ok = false;
                    for (int i = 0; i < ce.Length; i++) if (SameNetKind(ce[i].m_Edge, wantRoad) && !em.HasComponent<Owner>(ce[i].m_Edge)) { ok = true; break; }
                    if (!ok) continue;
                    bestNode = n; bestNodeD = d;
                }
            }
            if (bestNode != Entity.Null)
            {
                cp.m_Entity = bestNode;
                cp.m_Position = em.GetComponentData<NetNode>(bestNode).m_Position;
                report.Add(new Dictionary<string, object> { { "snappedTo", "node" }, { "id", Id(bestNode) }, { "pos", F2(cp.m_Position) } });
                return cp;
            }
            Entity bestEdge = Entity.Null; float bestEdgeD = float.MaxValue; float bestT = 0;
            using (var edges = EdgeQuery().ToEntityArray(Allocator.Temp))
            {
                foreach (var e in edges)
                {
                    if (!SameNetKind(e, wantRoad)) continue;
                    var cv = em.GetComponentData<Curve>(e).m_Bezier;
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    float hw = em.HasComponent<NetGeometryData>(prefab) ? em.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth * 0.5f : 0;
                    float t;
                    var d = MathUtils.Distance(cv.xz, p.xz, out t);
                    if (d > snap + hw || d >= bestEdgeD) continue;
                    bestEdge = e; bestEdgeD = d; bestT = t;
                }
            }
            if (bestEdge != Entity.Null)
            {
                var cv = em.GetComponentData<Curve>(bestEdge);
                var edge = em.GetComponentData<NetEdge>(bestEdge);
                float len = cv.m_Length;
                if (bestT * len < 2f) { cp.m_Entity = edge.m_Start; cp.m_Position = em.GetComponentData<NetNode>(edge.m_Start).m_Position; report.Add(new Dictionary<string, object> { { "snappedTo", "node" }, { "id", Id(edge.m_Start) } }); return cp; }
                if ((1 - bestT) * len < 2f) { cp.m_Entity = edge.m_End; cp.m_Position = em.GetComponentData<NetNode>(edge.m_End).m_Position; report.Add(new Dictionary<string, object> { { "snappedTo", "node" }, { "id", Id(edge.m_End) } }); return cp; }
                cp.m_Entity = bestEdge;
                cp.m_SplitPosition = bestT;
                cp.m_Position = MathUtils.Position(cv.m_Bezier, bestT);
                report.Add(new Dictionary<string, object> { { "snappedTo", "edge" }, { "id", Id(bestEdge) }, { "t", Math.Round(bestT, 3) }, { "pos", F2(cp.m_Position) } });
                return cp;
            }
            cp.m_Position.y = TerrainHeight(p);
            report.Add(new Dictionary<string, object> { { "snappedTo", "none" }, { "pos", F2(cp.m_Position) } });
            return cp;
        }

        object Road(Args a)
        {
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy with another build", 409);
            var prefab = FindPrefab(a.Str("prefab", "Small Road"), e => em.HasComponent<NetData>(e) && em.HasComponent<PlaceableNetData>(e));
            if (prefab == Entity.Null) return new ApiError("net prefab not found: " + a.Str("prefab"));
            if (IsLocked(prefab)) return new ApiError(PrefabName(prefab) + " is still locked");
            var pts = a.List("points");
            if (pts == null || pts.Count < 2) return new ApiError("need at least 2 points");
            bool wantRoad = em.HasComponent<RoadData>(prefab);
            float snap = a.F("snap", 8f);
            // optional subdivision so long segments follow the terrain
            float maxSeg = a.F("maxSegment", 96f);
            var raw = new List<float3>();
            foreach (var o in pts) raw.Add(P(o));
            var dense = new List<float3> { raw[0] };
            for (int i = 1; i < raw.Count; i++)
            {
                var d = math.distance(raw[i - 1].xz, raw[i].xz);
                int k = math.max(1, (int)math.ceil(d / maxSeg));
                for (int s = 1; s <= k; s++) dense.Add(math.lerp(raw[i - 1], raw[i], s / (float)k));
            }
            var job = new NetJob { Prefab = prefab, DryRun = a.B("dryRun") };
            var report = new List<object>();
            for (int i = 0; i < dense.Count; i++)
            {
                bool end = i == 0 || i == dense.Count - 1;
                var rawPt = raw.Exists(r => math.distance(r.xz, dense[i].xz) < 0.01f);
                if (end || rawPt) job.Points.Add(SnapPoint(dense[i], snap, wantRoad, report));
                else { var p = dense[i]; p.y = TerrainHeight(p); job.Points.Add(new CoursePos { m_Position = p, m_ParentMesh = -1 }); }
            }
            // snapping is resolved again right before the definitions are written, so the course never
            // references nodes/edges that a previous build has just replaced
            var snapIdx = new HashSet<int>();
            for (int i = 0; i < job.Points.Count; i++) if (job.Points[i].m_Entity != Entity.Null || i == 0 || i == job.Points.Count - 1) snapIdx.Add(i);
            job.Resnap = (i, cp) => snapIdx.Contains(i) ? SnapPoint(cp.m_Position, snap, wantRoad, new List<object>()) : cp;
            tool.Enqueue(job);
            return new BuildOperation(job, new Dictionary<string, object> { { "prefab", PrefabName(prefab) }, { "segments", job.Points.Count - 1 }, { "snapping", report } });
        }

        object Zone(Args a)
        {
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy with another build", 409);
            var zoneName = a.Str("zone", "");
            bool dezone = zoneName == "none" || zoneName == "";
            Entity prefab = dezone ? FindPrefab("Unzoned", e => em.HasComponent<ZoneData>(e)) : FindPrefab(zoneName, e => em.HasComponent<ZoneData>(e));
            if (prefab == Entity.Null && dezone)
            {
                // any zone prefab works as the carrier for a dezone definition
                prefab = FindPrefab("Residential", e => em.HasComponent<ZoneData>(e));
            }
            if (prefab == Entity.Null) return new ApiError("zone prefab not found: " + zoneName);
            if (!dezone && IsLocked(prefab)) return new ApiError(PrefabName(prefab) + " is still locked");
            var job = new ZoneJob { Prefab = prefab, Dezone = dezone, Overwrite = a.B("overwrite", true), DryRun = a.B("dryRun") };
            var mode = a.Str("mode", a.Has("edges") ? "road" : "rect");
            if (mode == "rect")
            {
                var pts = a.List("points");
                if (pts == null || pts.Count < 2) return new ApiError("rect mode needs points: 2 opposite corners or 4 corners");
                float3 A, B, C, D;
                if (pts.Count == 2)
                {
                    var p0 = P(pts[0]); var p1 = P(pts[1]);
                    A = new float3(p0.x, 0, p0.z); B = new float3(p1.x, 0, p0.z); C = new float3(p1.x, 0, p1.z); D = new float3(p0.x, 0, p1.z);
                }
                else { A = P(pts[0]); B = P(pts[1]); C = P(pts[2]); D = P(pts[3]); }
                A.y = TerrainHeight(A); B.y = TerrainHeight(B); C.y = TerrainHeight(C); D.y = TerrainHeight(D);
                job.Quads.Add(new Quad3(A, B, C, D));
            }
            else
            {
                var ids = a.List("edges");
                if (ids == null || ids.Count == 0) return new ApiError("road mode needs edges: [ids]");
                int depth = math.clamp(a.I("depth", 6), 1, 6);
                var side = a.Str("side", "both");
                var eq = EdgeQuery();
                foreach (var id in ids)
                {
                    var e = Resolve(id, eq);
                    if (e == Entity.Null) return new ApiError("edge not found: " + id);
                    var cv = em.GetComponentData<Curve>(e);
                    var prefabE = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    float hw = em.HasComponent<NetGeometryData>(prefabE) ? em.GetComponentData<NetGeometryData>(prefabE).m_DefaultWidth * 0.5f : 4f;
                    int pieces = math.max(1, (int)math.ceil(cv.m_Length / 16f));
                    for (int k = 0; k < pieces; k++)
                    {
                        float t0 = k / (float)pieces, t1 = (k + 1) / (float)pieces;
                        var p0 = MathUtils.Position(cv.m_Bezier, t0);
                        var p1 = MathUtils.Position(cv.m_Bezier, t1);
                        var dir = math.normalizesafe((p1 - p0).xz);
                        var left = new float2(-dir.y, dir.x);
                        foreach (int s in new[] { 1, -1 })
                        {
                            if (side == "left" && s < 0) continue;
                            if (side == "right" && s > 0) continue;
                            var n = left * s;
                            float inner = hw + 0.5f, outer = hw + depth * 8f - 0.5f;
                            var qa = new float3(p0.x + n.x * inner, p0.y, p0.z + n.y * inner);
                            var qb = new float3(p0.x + n.x * outer, p0.y, p0.z + n.y * outer);
                            var qc = new float3(p1.x + n.x * outer, p1.y, p1.z + n.y * outer);
                            var qd = new float3(p1.x + n.x * inner, p1.y, p1.z + n.y * inner);
                            job.Quads.Add(new Quad3(qa, qb, qc, qd));
                        }
                    }
                }
            }
            tool.Enqueue(job);
            return new BuildOperation(job, new Dictionary<string, object> { { "zone", dezone ? "none" : PrefabName(prefab) }, { "quads", job.Quads.Count } });
        }

        object PlaceBuilding(Args a)
        {
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy with another build", 409);
            var prefab = FindPrefab(a.Str("prefab", ""), e => em.HasComponent<PlaceableObjectData>(e) && em.HasComponent<BuildingData>(e));
            if (prefab == Entity.Null) prefab = FindPrefab(a.Str("prefab", ""), e => em.HasComponent<PlaceableObjectData>(e));
            if (prefab == Entity.Null) return new ApiError("object prefab not found: " + a.Str("prefab"));
            if (IsLocked(prefab)) return new ApiError(PrefabName(prefab) + " is still locked");
            int2 lot = em.HasComponent<BuildingData>(prefab) ? em.GetComponentData<BuildingData>(prefab).m_LotSize : new int2(1, 1);
            float3 pos;
            quaternion rot;
            var info = new Dictionary<string, object> { { "prefab", PrefabName(prefab) }, { "lot", new List<object> { lot.x, lot.y } } };
            if (a.Has("edge"))
            {
                var e = Resolve(a.Raw("edge"), EdgeQuery());
                if (e == Entity.Null) return new ApiError("edge not found: " + a.Raw("edge"));
                var cv = em.GetComponentData<Curve>(e);
                var prefabE = em.GetComponentData<PrefabRef>(e).m_Prefab;
                float hw = em.HasComponent<NetGeometryData>(prefabE) ? em.GetComponentData<NetGeometryData>(prefabE).m_DefaultWidth * 0.5f : 4f;
                float t = math.clamp(a.F("t", 0.5f), 0f, 1f);
                var p = MathUtils.Position(cv.m_Bezier, t);
                var dir = math.normalizesafe(MathUtils.Tangent(cv.m_Bezier, t).xz);
                var left = new float2(-dir.y, dir.x);
                var n = a.Str("side", "left") == "right" ? -left : left;
                float depth = lot.y * 8f;
                var c = p.xz + n * (hw + depth * 0.5f + a.F("gap", 0.5f));
                pos = new float3(c.x, 0, c.y);
                var fwd = -n; // building front (+z) faces the road
                rot = quaternion.LookRotationSafe(new float3(fwd.x, 0, fwd.y), new float3(0, 1, 0));
                info["edge"] = Id(e);
            }
            else
            {
                pos = new float3(a.F("x"), 0, a.F("z"));
                rot = quaternion.RotateY(math.radians(a.F("angle")));
            }
            pos.y = TerrainHeight(pos);
            info["pos"] = F2(pos);
            info["facing"] = F2(math.mul(rot, new float3(0, 0, 1)));
            var job = new ObjectJob { Prefab = prefab, Position = pos, Rotation = rot, DryRun = a.B("dryRun") };
            var capturedPrefab = prefab;
            var capturedPos = pos;
            job.After = t => FindPlaced(capturedPrefab, capturedPos);
            tool.Enqueue(job);
            return new BuildOperation(job, info);
        }

        Dictionary<string, object> FindPlaced(Entity prefab, float3 pos)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            Entity best = Entity.Null; float bd = 30f;
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (em.GetComponentData<PrefabRef>(e).m_Prefab != prefab) continue;
                    var d = math.distance(em.GetComponentData<ObjTransform>(e).m_Position.xz, pos.xz);
                    if (d < bd) { bd = d; best = e; }
                }
            var r = new Dictionary<string, object>();
            if (best == Entity.Null) { r["placedId"] = null; return r; }
            r["placedId"] = Id(best);
            if (em.HasComponent<Building>(best)) r["roadConnected"] = em.GetComponentData<Building>(best).m_RoadEdge != Entity.Null;
            return r;
        }
    }
}
