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

namespace ClaudeCityBridge
{
    /// <summary>
    /// Net courses with curves and elevation, written like NetToolSystem.CreateDefinitionsJob:
    ///  - "control" spans use NetUtils.FitCurve(Segment(start, control), Segment(end, control)) exactly like the
    ///    vanilla simple-curve mode (CreateSimpleCurve);
    ///  - "smooth" spans use NetUtils.FitCurve(start, startTangent, endTangent, end) with tangents from a
    ///    centripetal Catmull-Rom through the points (continuous-curve style);
    ///  - y of a free joint = terrain + elevation, CoursePos.m_Elevation = elevation (FixElevation/GetCoursePos),
    ///    elevation clamped to PlaceableNetData.m_ElevationRange; control-point heights are linearised
    ///    (LinearizeElevation).
    /// The curve is built in Create() from the re-snapped endpoints, so a snap that moves an endpoint keeps the shape.
    /// At-grade spans longer than MaxSegment are cut (MathUtils.Cut) so joints follow the terrain; elevated spans are
    /// left whole so CourseSplitSystem places pillars/splits by the prefab's m_ElevatedLength itself. Course splitting
    /// at crossings, validation and cost are the game's own.
    /// </summary>
    /// <remarks>Subclasses NetJob only so other code that inspects NetJob (Prefab, Points as the at-grade footprint
    /// polyline, e.g. the demolition-safety footprint) sees it; Create() is fully replaced and does not use Points.</remarks>
    public class CourseNetJob : NetJob
    {
        public enum SpanKind { Straight, Control, Tangents, Cubic }
        public class Span { public SpanKind Kind; public float2 Q, C1, C2, T0, T1; }

        public Api Api;
        public float MaxSegment = 96f;
        public float ElevationLimit = 2f;
        public List<CoursePos> Joints = new List<CoursePos>();
        public List<float> Elev = new List<float>();
        public List<float2> Requested = new List<float2>(); // requested xz of each joint (for Cubic control shifts)
        public List<Span> Spans = new List<Span>();
        HashSet<Entity> before;
        List<float3> trace;

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            var em = tool.EntityManager;
            for (int i = 0; i < Joints.Count; i++)
            {
                var p = Joints[i];
                if (Resnap != null) p = Resnap(i, p);
                if (p.m_Entity != Entity.Null && (!em.Exists(p.m_Entity) || em.HasComponent<Deleted>(p.m_Entity) || em.HasComponent<Temp>(p.m_Entity)
                    || !(em.HasComponent<NetNode>(p.m_Entity) || em.HasComponent<Game.Net.Edge>(p.m_Entity))))
                {
                    Result["staleRefsDropped"] = (Result.TryGetValue("staleRefsDropped", out var prev) ? (int)prev : 0) + 1;
                    p.m_Entity = Entity.Null;
                    p.m_SplitPosition = 0;
                    p.m_Position.y = Api.TerrainHeight(p.m_Position) + Elev[i];
                    p.m_Elevation = new float2(Elev[i]);
                }
                Joints[i] = p;
            }

            // final pieces: (curve, start joint, end joint)
            var pieces = new List<Tuple<Bezier4x3, CoursePos, CoursePos>>();
            for (int i = 0; i < Spans.Count; i++)
            {
                var A = Joints[i]; var B = Joints[i + 1];
                var cv = BuildCurve(Spans[i], A.m_Position, B.m_Position, Requested[i], Requested[i + 1]);
                float eA = A.m_Elevation.x, eB = B.m_Elevation.x;
                bool elevated = math.max(math.abs(eA), math.abs(eB)) >= ElevationLimit;
                float len = MathUtils.Length(cv);
                int k = elevated ? 1 : math.max(1, (int)math.ceil(len / MaxSegment));
                var prevJ = A;
                for (int s = 0; s < k; s++)
                {
                    float t0 = s / (float)k, t1 = (s + 1) / (float)k;
                    var sub = k == 1 ? cv : MathUtils.Cut(cv, new float2(t0, t1));
                    CoursePos endJ;
                    if (s == k - 1) endJ = B;
                    else
                    {
                        float e = math.lerp(eA, eB, t1);
                        var p = sub.d;
                        p.y = Api.TerrainHeight(p) + e;
                        endJ = new CoursePos { m_Position = p, m_Elevation = new float2(e), m_ParentMesh = -1 };
                    }
                    sub.a = prevJ.m_Position;
                    sub.d = endJ.m_Position;
                    sub.b.y = math.lerp(sub.a.y, sub.d.y, 1f / 3f);
                    sub.c.y = math.lerp(sub.a.y, sub.d.y, 2f / 3f);
                    pieces.Add(Tuple.Create(sub, prevJ, endJ));
                    prevJ = endJ;
                }
            }

            var rnd = new Unity.Mathematics.Random((uint)(Environment.TickCount | 1));
            trace = new List<float3>();
            int n = pieces.Count;
            for (int i = 0; i < n; i++)
            {
                var curve = pieces[i].Item1;
                var a = pieces[i].Item2;
                var b = pieces[i].Item3;
                a.m_CourseDelta = 0f; b.m_CourseDelta = 1f;
                a.m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, 0f));
                b.m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, 1f));
                a.m_Flags = CoursePosFlags.IsLeft | CoursePosFlags.IsRight | (i == 0 ? CoursePosFlags.IsFirst : 0);
                b.m_Flags = CoursePosFlags.IsLeft | CoursePosFlags.IsRight | (i == n - 1 ? CoursePosFlags.IsLast : 0);
                a.m_ParentMesh = -1; b.m_ParentMesh = -1;
                var e = ecb.CreateEntity();
                ecb.AddComponent(e, new CreationDefinition { m_Prefab = Prefab, m_RandomSeed = rnd.NextInt(), m_Flags = CreationFlags.SubElevation });
                ecb.AddComponent(e, default(Updated));
                ecb.AddComponent(e, new NetCourse { m_Curve = curve, m_StartPosition = a, m_EndPosition = b, m_Length = MathUtils.Length(curve), m_FixedIndex = -1 });
                for (int s = 0; s <= 8; s++) trace.Add(MathUtils.Position(curve, s / 8f));
            }
            Result["courses"] = n;
            if (!DryRun) before = Api.SnapshotEdges();
            return deps;
        }

        public static Bezier4x3 BuildCurve(Span sp, float3 A, float3 B, float2 reqA, float2 reqB)
        {
            switch (sp.Kind)
            {
                case SpanKind.Control:
                    {
                        var q = new float3(sp.Q.x, (A.y + B.y) * 0.5f, sp.Q.y);
                        return NetUtils.FitCurve(new Line3.Segment(A, q), new Line3.Segment(B, q));
                    }
                case SpanKind.Tangents:
                    return NetUtils.FitCurve(A, new float3(sp.T0.x, 0, sp.T0.y), new float3(sp.T1.x, 0, sp.T1.y), B);
                case SpanKind.Cubic:
                    {
                        // explicit control points move with their endpoint when snapping shifts it
                        var c1 = sp.C1 + (A.xz - reqA); var c2 = sp.C2 + (B.xz - reqB);
                        return new Bezier4x3(A, new float3(c1.x, A.y, c1.y), new float3(c2.x, B.y, c2.y), B);
                    }
                default:
                    return NetUtils.StraightCurve(A, B);
            }
        }

        public override void AfterApply(AgentToolSystem tool)
        {
            if (before == null || trace == null) return;
            try { Api.ReportBuiltCourse(Prefab, trace, before, Result); }
            catch (Exception e) { Result["trackError"] = e.Message; }
        }
    }

    public partial class Api
    {
        static bool HasThird(object o) => o is List<object> l && l.Count >= 3 && l[2] is double;

        static bool WantsAdvancedRoad(Args a)
        {
            if (a.Has("elevation") || a.Has("segments") || a.Has("curve")) return true;
            var pts = a.List("points");
            return pts != null && pts.Any(HasThird);
        }

        /// <summary>At-grade points snap like /road; elevated points only to an existing node at about the same elevation.</summary>
        CoursePos SnapElevated(float3 p, float e, float snap, bool wantRoad, List<object> report, Entity prefab = default(Entity))
        {
            if (math.abs(e) < 0.5f) return SnapPoint(p, snap, wantRoad, report, prefab);
            Entity best = Entity.Null; float bd = snap; float2 bestElev = 0;
            using (var nodes = NodeQuery().ToEntityArray(Allocator.Temp))
                foreach (var n in nodes)
                {
                    if (!em.HasComponent<Game.Net.Elevation>(n)) continue;
                    var el = em.GetComponentData<Game.Net.Elevation>(n).m_Elevation;
                    if (math.abs((el.x + el.y) * 0.5f - e) > 1.5f) continue;
                    var d = math.distance(em.GetComponentData<NetNode>(n).m_Position.xz, p.xz);
                    if (d >= bd) continue;
                    var ce = em.GetBuffer<ConnectedEdge>(n, true);
                    bool ok = false;
                    for (int i = 0; i < ce.Length; i++) if (SameNetKind(ce[i].m_Edge, wantRoad) && !em.HasComponent<Owner>(ce[i].m_Edge)) { ok = true; break; }
                    if (!ok) continue;
                    best = n; bd = d; bestElev = el;
                }
            if (best != Entity.Null)
            {
                var np = em.GetComponentData<NetNode>(best).m_Position;
                report.Add(new Dictionary<string, object> { { "snappedTo", "elevatedNode" }, { "id", Id(best) }, { "pos", F2(np) }, { "elevation", Math.Round((bestElev.x + bestElev.y) * 0.5f, 1) } });
                return new CoursePos { m_Entity = best, m_Position = np, m_Elevation = bestElev, m_ParentMesh = -1 };
            }
            var q = new float3(p.x, TerrainHeight(p) + e, p.z);
            report.Add(new Dictionary<string, object> { { "snappedTo", "none" }, { "pos", F2(q) }, { "elevation", Math.Round(e, 1) } });
            return new CoursePos { m_Position = q, m_Elevation = new float2(e), m_ParentMesh = -1 };
        }

        static float2 XZOf(object o)
        {
            var l = o as List<object>;
            if (l == null || l.Count < 2 || !(l[0] is double) || !(l[1] is double)) throw new ArgumentException("point must be [x, z] or [x, z, elevation]");
            return new float2((float)(double)l[0], (float)(double)l[1]);
        }

        /// <summary>xz-curvature samples of a cubic: returns min radius (m) and total turn (deg).</summary>
        static void CurveStats(Bezier4x3 cv, out float minRadius, out float turnDeg)
        {
            float2 a = cv.a.xz, b = cv.b.xz, c = cv.c.xz, d = cv.d.xz;
            minRadius = float.PositiveInfinity;
            for (int i = 0; i <= 32; i++)
            {
                float t = i / 32f, u = 1 - t;
                var d1 = 3 * (u * u * (b - a) + 2 * u * t * (c - b) + t * t * (d - c));
                var d2 = 6 * (u * (c - 2 * b + a) + t * (d - 2 * c + b));
                float sp = math.length(d1);
                if (sp < 1e-3f) continue;
                float k = math.abs(d1.x * d2.y - d1.y * d2.x) / (sp * sp * sp);
                if (k > 1e-6f) minRadius = math.min(minRadius, 1f / k);
            }
            var t0 = math.normalizesafe(MathUtils.Tangent(cv, 0f).xz);
            var t1 = math.normalizesafe(MathUtils.Tangent(cv, 1f).xz);
            turnDeg = math.degrees(math.acos(math.clamp(math.dot(t0, t1), -1f, 1f)));
        }

        /// <summary>
        /// POST /road with curves and/or elevation:
        ///   {prefab, points:[[x,z] | [x,z,elev], ...], curve:"straight"|"smooth", startDir:[dx,dz], endDir:[dx,dz],
        ///    elevation (default for points without a 3rd value), snap:8, maxSegment:96, dryRun}
        ///   {prefab, segments:[{start:[x,z(,e)], control:[x,z], end:[x,z(,e)]} | {start, control1, control2, end} | {start, end}], ...}
        ///   (segment i+1 may omit start: it continues from segment i's end)
        /// </summary>
        object RoadAdvanced(Args a)
        {
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy with another build", 409);
            var prefab = FindPrefab(a.Str("prefab", "Small Road"), e => em.HasComponent<NetData>(e) && em.HasComponent<PlaceableNetData>(e));
            if (prefab == Entity.Null) return new ApiError("net prefab not found: " + a.Str("prefab"));
            if (IsLocked(prefab)) return new ApiError(PrefabName(prefab) + " is still locked");
            bool wantRoad = em.HasComponent<RoadData>(prefab);
            float snap = a.F("snap", 8f);
            var pnd = em.GetComponentData<PlaceableNetData>(prefab);
            var range = pnd.m_ElevationRange;
            bool hasGeo = em.HasComponent<NetGeometryData>(prefab);
            var geo = hasGeo ? em.GetComponentData<NetGeometryData>(prefab) : default(NetGeometryData);
            float width = hasGeo ? geo.m_DefaultWidth : 8f;
            float defElev = a.F("elevation", 0f);
            var clamped = new List<object>();
            float Clamp(float e, int idx)
            {
                if (range.max > range.min && (e < range.min || e > range.max))
                {
                    float c = math.clamp(e, range.min, range.max);
                    clamped.Add(new Dictionary<string, object> { { "joint", idx }, { "requested", e }, { "used", c } });
                    return c;
                }
                return e;
            }

            var xz = new List<float2>();
            var elev = new List<float>();
            var spans = new List<CourseNetJob.Span>();
            string mode;
            try
            {
                if (a.Has("segments"))
                {
                    mode = "segments";
                    var segs = a.List("segments");
                    if (segs == null || segs.Count == 0) return new ApiError("segments must be a non-empty list");
                    for (int j = 0; j < segs.Count; j++)
                    {
                        var sd = segs[j] as Dictionary<string, object>;
                        if (sd == null) return new ApiError("segments[" + j + "] must be an object {start, control?, end}");
                        var sa = new Args(sd);
                        if (j == 0 || sa.Has("start"))
                        {
                            if (!sa.Has("start")) return new ApiError("segments[0] needs start");
                            var s = XZOf(sa.Raw("start"));
                            float se = HasThird(sa.Raw("start")) ? (float)(double)((List<object>)sa.Raw("start"))[2] : defElev;
                            if (j == 0) { xz.Add(s); elev.Add(Clamp(se, 0)); }
                            else if (math.distance(s, xz[xz.Count - 1]) > 1f) return new ApiError("segments[" + j + "].start is not segments[" + (j - 1) + "].end (separate roads need separate calls)");
                        }
                        if (!sa.Has("end")) return new ApiError("segments[" + j + "] needs end");
                        var en = XZOf(sa.Raw("end"));
                        float ee = HasThird(sa.Raw("end")) ? (float)(double)((List<object>)sa.Raw("end"))[2] : defElev;
                        var sp = new CourseNetJob.Span { Kind = CourseNetJob.SpanKind.Straight };
                        if (sa.Has("control")) { sp.Kind = CourseNetJob.SpanKind.Control; sp.Q = XZOf(sa.Raw("control")); }
                        else if (sa.Has("control1") && sa.Has("control2")) { sp.Kind = CourseNetJob.SpanKind.Cubic; sp.C1 = XZOf(sa.Raw("control1")); sp.C2 = XZOf(sa.Raw("control2")); }
                        xz.Add(en); elev.Add(Clamp(ee, xz.Count - 1));
                        spans.Add(sp);
                    }
                }
                else
                {
                    var pts = a.List("points");
                    if (pts == null || pts.Count < 2) return new ApiError("need at least 2 points (or segments)");
                    foreach (var o in pts)
                    {
                        xz.Add(XZOf(o));
                        elev.Add(Clamp(HasThird(o) ? (float)(double)((List<object>)o)[2] : defElev, xz.Count - 1));
                    }
                    mode = (a.Str("curve", "straight") ?? "straight").ToLowerInvariant();
                    if (mode != "straight" && mode != "smooth") return new ApiError("curve must be straight|smooth (or use segments with control points)");
                    int n = xz.Count;
                    if (mode == "straight") for (int i = 0; i + 1 < n; i++) spans.Add(new CourseNetJob.Span { Kind = CourseNetJob.SpanKind.Straight });
                    else
                    {
                        // interior tangents: centripetal Catmull-Rom (alpha 0.5). End tangents: startDir/endDir when given,
                        // else the neighbouring tangent mirrored about the end chord (continues the arc: circle points
                        // give a circle), or the chord itself for a 2-point course.
                        var tan = new float2[n];
                        for (int i = 1; i < n - 1; i++)
                        {
                            var p0 = xz[i - 1]; var p1 = xz[i]; var p2 = xz[i + 1];
                            float d1 = math.max(1e-3f, math.sqrt(math.distance(p0, p1))), d2 = math.max(1e-3f, math.sqrt(math.distance(p1, p2)));
                            var m = (p1 - p0) / d1 - (p2 - p0) / (d1 + d2) + (p2 - p1) / d2;
                            tan[i] = math.normalizesafe(m, math.normalizesafe(p2 - p1));
                        }
                        var c0 = math.normalizesafe(xz[1] - xz[0]);
                        var c1 = math.normalizesafe(xz[n - 1] - xz[n - 2]);
                        tan[0] = a.Has("startDir") ? math.normalizesafe(XZOf(a.Raw("startDir")), c0) : n > 2 ? math.normalizesafe(2 * math.dot(tan[1], c0) * c0 - tan[1], c0) : c0;
                        tan[n - 1] = a.Has("endDir") ? math.normalizesafe(XZOf(a.Raw("endDir")), c1) : n > 2 ? math.normalizesafe(2 * math.dot(tan[n - 2], c1) * c1 - tan[n - 2], c1) : c1;
                        for (int i = 0; i + 1 < n; i++) spans.Add(new CourseNetJob.Span { Kind = CourseNetJob.SpanKind.Tangents, T0 = tan[i], T1 = tan[i + 1] });
                    }
                }
            }
            catch (ArgumentException ex) { return new ApiError(ex.Message); }
            catch (InvalidCastException) { return new ApiError("coordinates must be numbers"); }

            var job = new CourseNetJob
            {
                Api = this, Prefab = prefab, DryRun = a.B("dryRun"),
                MaxSegment = math.max(16f, a.F("maxSegment", 96f)),
                ElevationLimit = hasGeo && geo.m_ElevationLimit > 0 ? geo.m_ElevationLimit : 2f,
            };
            // connectTo: attach an endpoint to a building's connection point (NetConnect.cs)
            var conns = new List<NetConnection>();
            var connErr = ParseConnectTo(a, prefab, xz[0], xz[xz.Count - 1], conns);
            if (connErr != null) return connErr;
            var fixedAt = new Dictionary<int, Entity>();
            foreach (var c in conns)
            {
                int k = c.Index == 0 ? 0 : xz.Count - 1;
                fixedAt[k] = c.Node;
                xz[k] = em.GetComponentData<NetNode>(c.Node).m_Position.xz;
                elev[k] = 0f;
            }
            var report = new List<object>();
            for (int i = 0; i < xz.Count; i++)
            {
                var p = new float3(xz[i].x, 0, xz[i].y);
                if (fixedAt.TryGetValue(i, out var cn))
                {
                    var ncp = NodeCoursePos(cn);
                    var b = OwnerBuilding(cn);
                    report.Add(new Dictionary<string, object> { { "snappedTo", "connectionPoint" }, { "id", Id(cn) }, { "building", b == Entity.Null ? null : Id(b) }, { "pos", F2(ncp.m_Position) } });
                    job.Joints.Add(ncp);
                }
                else job.Joints.Add(SnapElevated(p, elev[i], snap, wantRoad, report, prefab));
                job.Elev.Add(elev[i]);
                job.Requested.Add(xz[i]);
            }
            job.Spans.AddRange(spans);
            var snapIdx = new HashSet<int>();
            for (int i = 0; i < job.Joints.Count; i++) if (job.Joints[i].m_Entity != Entity.Null || i == 0 || i == job.Joints.Count - 1) snapIdx.Add(i);
            var reqPos = xz.Select(v => new float3(v.x, 0, v.y)).ToArray();
            var reqElev = elev.ToArray();
            job.Resnap = (i, cp) =>
            {
                if (fixedAt.TryGetValue(i, out var node)) return IsLive(node) ? NodeCoursePos(node) : new CoursePos { m_Position = cp.m_Position, m_ParentMesh = -1 };
                return snapIdx.Contains(i) ? SnapElevated(reqPos[i], reqElev[i], snap, wantRoad, new List<object>(), prefab) : cp;
            };

            // preview of the geometry (from the snapped positions now; recomputed identically at build time)
            var spanInfo = new List<object>();
            float minR = float.PositiveInfinity, maxSlope = 0, total = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                var A = job.Joints[i].m_Position; var B = job.Joints[i + 1].m_Position;
                var cv = CourseNetJob.BuildCurve(spans[i], A, B, xz[i], xz[i + 1]);
                CurveStats(cv, out float r, out float turn);
                float len = MathUtils.Length(cv);
                float h = math.distance(A.xz, B.xz);
                float slope = h > 0.5f ? math.abs(B.y - A.y) / len : 0;
                total += len; maxSlope = math.max(maxSlope, slope);
                if (!float.IsInfinity(r)) minR = math.min(minR, r);
                var si = new Dictionary<string, object>
                {
                    { "kind", spans[i].Kind.ToString().ToLowerInvariant() }, { "from", F2(A) }, { "to", F2(B) }, { "length", Math.Round(len, 1) },
                    { "turnDeg", Math.Round(turn, 1) }, { "minRadius", float.IsInfinity(r) ? (object)null : Math.Round(r, 1) },
                    { "mid", F2(MathUtils.Position(cv, 0.5f)) },
                };
                if (elev[i] != 0 || elev[i + 1] != 0) si["elevation"] = new List<object> { Math.Round(job.Joints[i].m_Elevation.x, 1), Math.Round(job.Joints[i + 1].m_Elevation.x, 1) };
                spanInfo.Add(si);
            }
            // footprint polyline for NetJob-based checks (at-grade courses only; a bridge has no ground strip)
            if (elev.All(e => math.abs(e) < 0.5f))
                for (int i = 0; i < spans.Count; i++)
                {
                    var cv = CourseNetJob.BuildCurve(spans[i], job.Joints[i].m_Position, job.Joints[i + 1].m_Position, xz[i], xz[i + 1]);
                    int k = math.max(1, (int)math.ceil(MathUtils.Length(cv) / 8f));
                    for (int s = i == 0 ? 0 : 1; s <= k; s++) job.Points.Add(new CoursePos { m_Position = MathUtils.Position(cv, s / (float)k), m_ParentMesh = -1 });
                }
            var geomInfo = new Dictionary<string, object>
            {
                { "mode", mode }, { "spans", spanInfo }, { "length", Math.Round(total, 1) },
                { "minRadius", float.IsInfinity(minR) ? (object)null : Math.Round(minR, 1) }, { "width", width },
            };
            var warnings = new List<object>();
            if (spanInfo.Any(o => (double)((Dictionary<string, object>)o)["turnDeg"] > 100.0)) warnings.Add("a span turns more than 100 degrees; add intermediate points (or split the segment) for a rounder curve");
            if (!float.IsInfinity(minR) && minR < width) warnings.Add("a curve radius (" + Math.Round(minR, 1) + " m) is below the road width (" + width + " m); the game may reject it as a tight curve");
            if (elev.Any(e => e != 0) || clamped.Count > 0)
            {
                geomInfo["elevationRange"] = new List<object> { range.min, range.max };
                geomInfo["elevationLimit"] = job.ElevationLimit;
                if (hasGeo) { geomInfo["elevatedLength"] = geo.m_ElevatedLength; geomInfo["maxSlopeAllowed"] = geo.m_MaxSlopeSteepness; }
                geomInfo["maxSlope"] = Math.Round(maxSlope, 3);
                if (hasGeo && geo.m_MaxSlopeSteepness > 0 && maxSlope > geo.m_MaxSlopeSteepness) warnings.Add("a span is steeper (" + Math.Round(maxSlope, 3) + ") than the prefab allows (" + geo.m_MaxSlopeSteepness + "); lengthen the ramp");
                if (clamped.Count > 0) geomInfo["clamped"] = clamped;
            }
            if (warnings.Count > 0) geomInfo["warnings"] = warnings;
            tool.Enqueue(job);
            var extra = new Dictionary<string, object> { { "prefab", PrefabName(prefab) }, { "segments", spans.Count }, { "snapping", report }, { "geometry", geomInfo } };
            var attached = AttachedConnections(report, conns, prefab);
            if (attached.Count > 0) return new NetConnectOperation(this, job, attached, extra);
            return new BuildOperation(job, extra);
        }
    }
}
