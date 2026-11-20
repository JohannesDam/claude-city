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
using Unity.Mathematics;
using NetNode = Game.Net.Node;
using NetEdge = Game.Net.Edge;

namespace ClaudeCityBridge
{
    /// <summary>
    /// At-grade net course like CourseNetJob, but each span may use its own prefab (a smaller road class for the
    /// piece that ties into an existing road). Pieces share their joints, so the game joins them at one node.
    /// Subclasses NetJob so the demolition footprint (Api.FootprintOf) sees Prefab + Points.
    /// </summary>
    public class FitCourseJob : NetJob
    {
        public Api Api;
        public float MaxSegment = 96f;
        public List<CoursePos> Joints = new List<CoursePos>();
        public List<float2> Requested = new List<float2>();
        public List<CourseNetJob.Span> Spans = new List<CourseNetJob.Span>();
        public List<Entity> SpanPrefabs = new List<Entity>(); // per span; Entity.Null = Prefab
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
                    || !(em.HasComponent<NetNode>(p.m_Entity) || em.HasComponent<NetEdge>(p.m_Entity))))
                {
                    Result["staleRefsDropped"] = (Result.TryGetValue("staleRefsDropped", out var prev) ? (int)prev : 0) + 1;
                    p.m_Entity = Entity.Null;
                    p.m_SplitPosition = 0;
                    p.m_Position.y = Api.TerrainHeight(p.m_Position);
                }
                Joints[i] = p;
            }
            var pieces = new List<Tuple<Bezier4x3, CoursePos, CoursePos, Entity>>();
            for (int i = 0; i < Spans.Count; i++)
            {
                var A = Joints[i]; var B = Joints[i + 1];
                var pf = i < SpanPrefabs.Count && SpanPrefabs[i] != Entity.Null ? SpanPrefabs[i] : Prefab;
                var cv = CourseNetJob.BuildCurve(Spans[i], A.m_Position, B.m_Position, Requested[i], Requested[i + 1]);
                float len = MathUtils.Length(cv);
                int k = math.max(1, (int)math.ceil(len / MaxSegment));
                var prevJ = A;
                for (int s = 0; s < k; s++)
                {
                    var sub = k == 1 ? cv : MathUtils.Cut(cv, new float2(s / (float)k, (s + 1) / (float)k));
                    CoursePos endJ;
                    if (s == k - 1) endJ = B;
                    else
                    {
                        var p = sub.d;
                        p.y = Api.TerrainHeight(p);
                        endJ = new CoursePos { m_Position = p, m_ParentMesh = -1 };
                    }
                    sub.a = prevJ.m_Position;
                    sub.d = endJ.m_Position;
                    sub.b.y = math.lerp(sub.a.y, sub.d.y, 1f / 3f);
                    sub.c.y = math.lerp(sub.a.y, sub.d.y, 2f / 3f);
                    pieces.Add(Tuple.Create(sub, prevJ, endJ, pf));
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
                ecb.AddComponent(e, new CreationDefinition { m_Prefab = pieces[i].Item4, m_RandomSeed = rnd.NextInt(), m_Flags = CreationFlags.SubElevation });
                ecb.AddComponent(e, default(Updated));
                ecb.AddComponent(e, new NetCourse { m_Curve = curve, m_StartPosition = a, m_EndPosition = b, m_Length = MathUtils.Length(curve), m_FixedIndex = -1 });
                for (int s = 0; s <= 8; s++) trace.Add(MathUtils.Position(curve, s / 8f));
            }
            Result["courses"] = n;
            if (!DryRun) before = Api.SnapshotEdges();
            return deps;
        }

        public override void AfterApply(AgentToolSystem tool)
        {
            if (before == null || trace == null) return;
            try { Api.ReportBuiltCourse(Prefab, trace, before, Result); }
            catch (Exception e) { Result["trackError"] = e.Message; }
        }
    }

    /// <summary>
    /// POST /road with fit:{...} (or POST /roadfit): from a rough polyline or start/end anchors, a small set of course
    /// variants is planned and screened without frames (curve radius, length, distance to the requested points,
    /// zoned lots under the road), then the best few are previewed through the game one after another
    /// (ProbeOperation): the first clean one (0 errors, nothing demolished unless allowed) is built unless dryRun.
    /// Variant families (no path planning, just small sets):
    ///  * smooth: through the given points (as curve:"smooth"), with end tangents from the anchors;
    ///  * tangents: start/end only with end directions (an existing edge is met at a right angle, a dead end is
    ///    continued, or the circular arc from one end's direction) - these give much larger radii than a curve
    ///    forced through a midpoint;
    ///  * nudge/relax: interior points moved within the tolerance (sideways, or towards the tangent curve);
    ///  * moveT: a T-junction slid along the target edge (+-8 m steps within the tolerance);
    ///  * tieIn: the best variants with the last ~30 m at a road class smaller than the course.
    /// </summary>
    public partial class Api
    {
        sealed class FitVariant
        {
            public string Name;
            public List<float2> Req = new List<float2>();
            public List<CourseNetJob.Span> Spans = new List<CourseNetJob.Span>();
            public List<Entity> SpanPrefabs = new List<Entity>();
            public float Penalty;
            public Dictionary<string, object> Detail = new Dictionary<string, object>();
            public float2 PrefA, PrefB; // preferred leaving direction at the start / arrival direction at the end (0 = free)
            public bool TeeA, TeeB;     // that end is a T-junction (onto an edge or a through-node)
            // screening
            public List<Bezier4x3> Curves; public float MinR, MaxTurn, Len, Dev, SkewA, SkewB; public int GeomHits; public double Score; public bool Discard;
        }

        /// <summary>Degrees between a curve's direction and a preferred direction (0 when there is no preference).</summary>
        static float SkewDeg(float3 tangent, float2 pref)
        {
            if (math.all(pref == 0)) return 0;
            var t = math.normalizesafe(tangent.xz);
            return math.degrees(math.acos(math.clamp(math.dot(t, math.normalizesafe(pref)), -1f, 1f)));
        }

        static float2 Perp(float2 v) => new float2(-v.y, v.x);
        static float2 MirrorTangent(float2 t, float2 chord) => math.normalizesafe(2f * math.dot(t, chord) * chord - t, chord);

        /// <summary>The direction a course should leave an anchor with (into the course): square onto an edge or through-node, straight out of a dead end; 0 = free.</summary>
        float2 AnchorDirection(CoursePos cp, float2 inward, out string kind)
        {
            kind = "free";
            var e = cp.m_Entity;
            if (e == Entity.Null || !em.Exists(e)) return 0;
            if (em.HasComponent<NetEdge>(e) && em.HasComponent<Curve>(e))
            {
                var cv = em.GetComponentData<Curve>(e).m_Bezier;
                var tan = math.normalizesafe(MathUtils.Tangent(cv, cp.m_SplitPosition).xz);
                var nrm = Perp(tan);
                kind = "edge";
                return math.dot(nrm, inward) >= 0 ? nrm : -nrm;
            }
            if (em.HasComponent<NetNode>(e) && em.HasBuffer<ConnectedEdge>(e))
            {
                var ce = em.GetBuffer<ConnectedEdge>(e, true);
                var dirs = new List<float2>(); // from the node along each connected top-level edge
                for (int i = 0; i < ce.Length; i++)
                {
                    var ed = ce[i].m_Edge;
                    if (em.HasComponent<Owner>(ed) || !em.HasComponent<Curve>(ed) || !em.HasComponent<NetEdge>(ed)) continue;
                    var cv = em.GetComponentData<Curve>(ed).m_Bezier;
                    var edge = em.GetComponentData<NetEdge>(ed);
                    dirs.Add(edge.m_Start == e ? math.normalizesafe(MathUtils.StartTangent(cv).xz) : -math.normalizesafe(MathUtils.EndTangent(cv).xz));
                }
                if (dirs.Count == 1 && math.dot(-dirs[0], inward) > 0.2f) { kind = "deadEnd"; return -dirs[0]; }
                if (dirs.Count == 2)
                {
                    var nrm = Perp(math.normalizesafe(dirs[0] - dirs[1]));
                    kind = "node";
                    return math.dot(nrm, inward) >= 0 ? nrm : -nrm;
                }
                kind = "junction";
            }
            return 0;
        }

        /// <summary>Spans through the joints: Catmull-Rom (centripetal) interior tangents; end tangents t0/t1 or, when 0, mirrored (RoadAdvanced's rule).</summary>
        static List<CourseNetJob.Span> SmoothSpans(List<float2> xz, float2 t0, float2 t1)
        {
            int n = xz.Count;
            var spans = new List<CourseNetJob.Span>();
            if (n == 2 && math.all(t0 == 0) && math.all(t1 == 0)) { spans.Add(new CourseNetJob.Span { Kind = CourseNetJob.SpanKind.Straight }); return spans; }
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
            tan[0] = math.any(t0 != 0) ? t0 : n > 2 ? MirrorTangent(tan[1], c0) : (math.any(t1 != 0) ? MirrorTangent(t1, c0) : c0);
            tan[n - 1] = math.any(t1 != 0) ? t1 : n > 2 ? MirrorTangent(tan[n - 2], c1) : (math.any(t0 != 0) ? MirrorTangent(t0, c1) : c1);
            for (int i = 0; i + 1 < n; i++) spans.Add(new CourseNetJob.Span { Kind = CourseNetJob.SpanKind.Tangents, T0 = tan[i], T1 = tan[i + 1] });
            return spans;
        }

        /// <summary>The next smaller road class for a tie-in: fit.tieInPrefab, else Large -&gt; Medium -&gt; Small Road by name.</summary>
        Entity SmallerRoad(Entity prefab, object want)
        {
            Func<Entity, bool> isRoad = e => em.HasComponent<RoadData>(e) && em.HasComponent<PlaceableNetData>(e);
            if (want is string s && s.Length > 0) { var p = FindPrefab(s, isRoad); return p != Entity.Null && !IsLocked(p) ? p : Entity.Null; }
            if (want is bool b && !b) return Entity.Null;
            var name = PrefabName(prefab) ?? "";
            string next = name.IndexOf("Large", StringComparison.OrdinalIgnoreCase) >= 0 ? "Medium Road"
                : name.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) >= 0 ? "Small Road" : null;
            if (next == null) return Entity.Null;
            var q = FindPrefab(next, isRoad);
            return q != Entity.Null && !IsLocked(q) && q != prefab ? q : Entity.Null;
        }

        static float PolylineDistance(float2 p, List<float2> poly)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < poly.Count; i++)
            {
                var a = poly[i]; var e = poly[i + 1] - a;
                float l2 = math.lengthsq(e);
                float t = l2 > 1e-6f ? math.clamp(math.dot(p - a, e) / l2, 0f, 1f) : 0f;
                best = math.min(best, math.distance(p, a + e * t));
            }
            return best;
        }

        /// <summary>POST /road goes to RoadFit with fit:{...} or fit:true (fit:false = a plain /road).</summary>
        static bool WantsFit(Args a) => a.Raw("fit") is Dictionary<string, object> || a.B("fit");

        /// <summary>POST /road {fit:{...}} and POST /roadfit.</summary>
        object RoadFit(Pending pend)
        {
            var a = pend.Body;
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy with another build", 409);
            var prefab = FindPrefab(a.Str("prefab", "Small Road"), e => em.HasComponent<NetData>(e) && em.HasComponent<PlaceableNetData>(e));
            if (prefab == Entity.Null) return new ApiError("net prefab not found: " + a.Str("prefab"));
            if (IsLocked(prefab)) return new ApiError(PrefabName(prefab) + " is still locked");
            var f = new Args(a.Raw("fit") as Dictionary<string, object>);
            float tol = math.clamp(f.Has("tolerance") ? f.F("tolerance") : a.F("tolerance", 30f), 0f, 200f);
            float minRadius = math.max(0f, f.Has("minRadius") ? f.F("minRadius") : a.F("minRadius", 60f));
            int maxTries = math.clamp(f.Has("maxTries") ? f.I("maxTries") : a.I("maxTries", 6), 1, 16);
            bool all = f.B("all") || a.B("all") || a.Str("dryRun") == "all";
            bool dry = a.B("dryRun") || a.Str("dryRun") == "all";
            float snap = a.F("snap", 8f);
            bool wantRoad = em.HasComponent<RoadData>(prefab);
            float width = em.HasComponent<NetGeometryData>(prefab) ? em.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth : 8f;
            bool allowDemolish = a.B("allowDemolish", false);
            var limits = allowDemolish
                ? new ProbeGuard.Limits { MaxHouseholds = a.Has("maxHouseholds") ? math.max(0, a.I("maxHouseholds")) : int.MaxValue, MaxCompanies = a.Has("maxCompanies") ? math.max(0, a.I("maxCompanies")) : int.MaxValue, MaxBuildings = a.Has("maxBuildings") ? math.max(0, a.I("maxBuildings")) : -1 }
                : new ProbeGuard.Limits { MaxHouseholds = 0, MaxCompanies = 0, MaxBuildings = 0 };
            bool demolishFree = limits.MaxBuildings == 0;

            // the rough course: points, or start (+ via) + end
            var pts = new List<float2>();
            try
            {
                List<object> raw = a.List("points");
                if (raw == null && a.Has("start") && a.Has("end"))
                {
                    raw = new List<object> { a.Raw("start") };
                    if (a.List("via") != null) raw.AddRange(a.List("via"));
                    raw.Add(a.Raw("end"));
                }
                if (raw == null || raw.Count < 2) return new ApiError("need points:[[x,z],...] (2+) or start/end (+via)");
                foreach (var o in raw)
                {
                    if (HasThird(o)) return new ApiError("fit builds at grade only: give [x, z] points (no elevation)");
                    pts.Add(XZOf(o));
                }
            }
            catch (ArgumentException ex) { return new ApiError(ex.Message); }
            for (int i = pts.Count - 1; i > 0; i--) if (math.distance(pts[i], pts[i - 1]) < 1f) pts.RemoveAt(i);
            if (pts.Count < 2) return new ApiError("the course is too short");

            float2? startDir = null, endDir = null;
            try
            {
                if (a.Has("startDir")) startDir = math.normalizesafe(XZOf(a.Raw("startDir")));
                if (a.Has("endDir")) endDir = math.normalizesafe(XZOf(a.Raw("endDir")));
            }
            catch (ArgumentException) { return new ApiError("startDir/endDir must be [dx, dz]"); }

            // snapping, cached per requested end position (variants move the ends)
            var snapCache = new Dictionary<long, CoursePos>();
            CoursePos Snap(float2 p)
            {
                long key = ((long)math.round(p.x * 10f) << 32) ^ (uint)(int)math.round(p.y * 10f);
                if (!snapCache.TryGetValue(key, out var cp)) snapCache[key] = cp = SnapPoint(new float3(p.x, 0, p.y), snap, wantRoad, new List<object>(), prefab);
                return cp;
            }
            var interior = pts.Skip(1).Take(pts.Count - 2).ToList();
            var A = Snap(pts[0]); var B = Snap(pts[pts.Count - 1]);
            var SA = A.m_Position.xz; var SB = B.m_Position.xz;
            var chord = math.normalizesafe(SB - SA, new float2(1, 0));
            var inA = math.normalizesafe((interior.Count > 0 ? interior[0] : SB) - SA, chord);
            var inB = math.normalizesafe((interior.Count > 0 ? interior[interior.Count - 1] : SA) - SB, -chord);
            var anchA = AnchorDirection(A, inA, out var kindA);
            var anchB = AnchorDirection(B, inB, out var kindB);
            var dA = startDir ?? anchA;
            var dB = endDir ?? -anchB; // arrival direction at B
            if (startDir != null) kindA = "startDir";
            if (endDir != null) kindB = "endDir";
            var z0 = new float2(0);
            bool teeA = kindA == "edge" || kindA == "node", teeB = kindB == "edge" || kindB == "node";

            // ------------------------------------------------------------ variants
            var variants = new List<FitVariant>();
            void Add(string name, List<float2> req, List<CourseNetJob.Span> spans, float penalty, Dictionary<string, object> detail = null)
            {
                var v = new FitVariant { Name = name, Req = req, Spans = spans, Penalty = penalty, PrefA = dA, PrefB = dB, TeeA = teeA, TeeB = teeB };
                for (int i = 0; i < spans.Count; i++) v.SpanPrefabs.Add(Entity.Null);
                if (detail != null) v.Detail = detail;
                variants.Add(v);
            }
            var full = new List<float2>(pts);
            var ends = new List<float2> { pts[0], pts[pts.Count - 1] };
            // as asked (curve:"smooth" through the points, or straight for 2 points); tangents are unit xz directions, 0 = free
            Add(interior.Count > 0 ? "smooth" : "straight", full, SmoothSpans(interior.Count > 0 ? full : ends, startDir ?? z0, endDir ?? z0), 0f);
            bool anchored = math.any(dA != 0) || math.any(dB != 0);
            if (interior.Count > 0 && anchored) Add("smoothAnchored", full, SmoothSpans(full, dA, dB), 0f);
            // start/end directions only
            var tanPairs = new List<KeyValuePair<string, float2[]>>();
            if (math.any(dA != 0) && math.any(dB != 0)) tanPairs.Add(new KeyValuePair<string, float2[]>("tangents", new[] { dA, dB }));
            if (math.any(dA != 0)) tanPairs.Add(new KeyValuePair<string, float2[]>("arcFromStart", new[] { dA, MirrorTangent(dA, chord) }));
            if (math.any(dB != 0)) tanPairs.Add(new KeyValuePair<string, float2[]>("arcIntoEnd", new[] { MirrorTangent(dB, chord), dB }));
            foreach (var tp in tanPairs) Add(tp.Key, ends, new List<CourseNetJob.Span> { new CourseNetJob.Span { Kind = CourseNetJob.SpanKind.Tangents, T0 = tp.Value[0], T1 = tp.Value[1] } }, 0f);
            if (interior.Count > 0) Add("straight", ends, new List<CourseNetJob.Span> { new CourseNetJob.Span { Kind = CourseNetJob.SpanKind.Straight } }, 0f);
            // interior points nudged sideways within the tolerance
            if (interior.Count > 0 && tol > 0)
            {
                var nC = Perp(chord);
                foreach (var o in new[] { -tol, -tol * 0.5f, tol * 0.5f, tol })
                {
                    var req = new List<float2> { pts[0] };
                    req.AddRange(interior.Select(p => p + nC * o));
                    req.Add(pts[pts.Count - 1]);
                    Add("nudge" + (o > 0 ? "+" : "") + Math.Round(o), req, SmoothSpans(req, dA, dB), math.abs(o) * 0.2f, new Dictionary<string, object> { { "offset", Math.Round(o, 1) } });
                }
            }
            // a T-junction slid along the target edge
            bool moveT = f.B("moveT", true);
            if (moveT && tol >= 4f)
                foreach (int endIdx in new[] { 0, 1 })
                {
                    var cp = endIdx == 0 ? A : B;
                    if (cp.m_Entity == Entity.Null || !em.HasComponent<NetEdge>(cp.m_Entity) || !em.HasComponent<Curve>(cp.m_Entity)) continue;
                    var ecv = em.GetComponentData<Curve>(cp.m_Entity);
                    float elen = math.max(1f, ecv.m_Length);
                    float keep = (snap + 4f) / elen;
                    for (float s = 8f; s <= math.min(tol, 48f) + 0.01f; s += 8f)
                        foreach (int sg in new[] { -1, 1 })
                        {
                            float t2 = cp.m_SplitPosition + sg * s / elen;
                            if (t2 < keep || t2 > 1f - keep) continue;
                            var mp = MathUtils.Position(ecv.m_Bezier, t2).xz;
                            var req = new List<float2>(full);
                            req[endIdx == 0 ? 0 : req.Count - 1] = mp;
                            var mcp = Snap(mp);
                            var mOther = endIdx == 0 ? dB : dA;
                            var inner = endIdx == 0 ? (interior.Count > 0 ? interior[0] : SB) : (interior.Count > 0 ? interior[interior.Count - 1] : SA);
                            var md = AnchorDirection(mcp, math.normalizesafe(inner - mcp.m_Position.xz, chord), out _);
                            if (endIdx == 1) md = -md;
                            var t0 = endIdx == 0 ? md : mOther;
                            var t1 = endIdx == 0 ? mOther : md;
                            var detail = new Dictionary<string, object> { { "end", endIdx == 0 ? "start" : "end" }, { "shift", sg * s }, { "edge", Id(cp.m_Entity) } };
                            if (interior.Count == 0 && math.any(t0 != 0) && math.any(t1 != 0))
                                Add("moveT", new List<float2> { req[0], req[req.Count - 1] }, new List<CourseNetJob.Span> { new CourseNetJob.Span { Kind = CourseNetJob.SpanKind.Tangents, T0 = t0, T1 = t1 } }, s * 0.5f, detail);
                            else Add("moveT", req, SmoothSpans(req, t0, t1), s * 0.5f, detail);
                            var mv = variants[variants.Count - 1];
                            mv.PrefA = t0; mv.PrefB = t1;
                            if (endIdx == 0) mv.TeeA = true; else mv.TeeB = true;
                        }
                }

            // ------------------------------------------------------------ screening (no frames)
            Entity tieIn = SmallerRoad(prefab, f.Raw("tieIn") ?? f.Raw("tieInPrefab"));
            void Screen(FitVariant v)
            {
                var joints = v.Req.Select((p, i) => i == 0 || i == v.Req.Count - 1 ? Snap(p).m_Position.xz : p).ToList();
                v.Curves = new List<Bezier4x3>();
                v.MinR = float.PositiveInfinity; v.MaxTurn = 0; v.Len = 0;
                var poly = new List<float2>();
                for (int i = 0; i < v.Spans.Count; i++)
                {
                    var p0 = new float3(joints[i].x, 0, joints[i].y); var p1 = new float3(joints[i + 1].x, 0, joints[i + 1].y);
                    var cv = CourseNetJob.BuildCurve(v.Spans[i], p0, p1, v.Req[i], v.Req[i + 1]);
                    v.Curves.Add(cv);
                    CurveStats(cv, out float r, out float turn);
                    if (!float.IsInfinity(r)) v.MinR = math.min(v.MinR, r);
                    v.MaxTurn = math.max(v.MaxTurn, turn);
                    v.Len += MathUtils.Length(cv);
                    for (int s = i == 0 ? 0 : 1; s <= 16; s++) poly.Add(MathUtils.Position(cv, s / 16f).xz);
                }
                v.Dev = 0;
                foreach (var ip in interior) v.Dev = math.max(v.Dev, PolylineDistance(ip, poly));
                if (v.Dev > tol + 0.5f && v.Name != "smooth") v.Discard = true;
                // how far the ends turn away from a square T / a continued dead end (acute junctions validate badly)
                v.SkewA = SkewDeg(MathUtils.StartTangent(v.Curves[0]), v.PrefA);
                v.SkewB = SkewDeg(MathUtils.EndTangent(v.Curves[v.Curves.Count - 1]), v.PrefB);
                try { v.GeomHits = GrowablesUnder(FootprintOf(MakeFitJob(v, prefab, true, snap, wantRoad, Snap))).Count; } catch { v.GeomHits = 0; }
                double score = v.Len + 0.5 * v.Dev + v.Penalty + 0.3 * (v.SkewA + v.SkewB);
                if ((v.TeeA && v.SkewA > 50f) || (v.TeeB && v.SkewB > 50f)) score += 200; // T-junction angle below ~40 degrees
                if (!float.IsInfinity(v.MinR) && v.MinR < minRadius) score += 1000 + (minRadius - v.MinR) * 10;
                if (v.MaxTurn > 100f) score += 400;
                if (v.GeomHits > 0) score += demolishFree ? 2000 + 300 * v.GeomHits : 50 * v.GeomHits;
                v.Score = score;
            }
            foreach (var v in variants) Screen(v);
            // a smaller road class at the tie-in(s) of the two best plain variants
            var tieEnds = new List<int>();
            if (A.m_Entity != Entity.Null) tieEnds.Add(0);
            if (B.m_Entity != Entity.Null) tieEnds.Add(1);
            if (tieIn != Entity.Null && tieEnds.Count > 0)
                foreach (var bv in variants.Where(v => !v.Discard).OrderBy(v => v.Score).Take(2).ToList())
                {
                    var tv = SplitForTieIn(bv, tieEnds, tieIn);
                    if (tv == null) continue;
                    Screen(tv);
                    tv.Score += 60; // a fallback: same course, narrower junction
                    variants.Add(tv);
                }
            // distinct shapes, best first
            var seen = new HashSet<string>();
            var ranked = new List<FitVariant>();
            foreach (var v in variants.Where(v => !v.Discard).OrderBy(v => v.Score))
            {
                var key = string.Join("|", v.Curves.Select(cv => string.Join(",", new[] { 0.25f, 0.5f, 0.75f }.Select(t => { var q = MathUtils.Position(cv, t).xz; return Math.Round(q.x) + ":" + Math.Round(q.y); })))) + "|" + string.Join(",", v.SpanPrefabs.Select(e => e.Index));
                if (seen.Add(key)) ranked.Add(v);
            }
            if (ranked.Count == 0) return new ApiError("no course variant stays within the tolerance of the given points");
            var chosenSet = ranked.Take(maxTries).ToList();

            var probes = new List<ProbeOperation.Probe>();
            foreach (var v in chosenSet)
            {
                var cv = v;
                var info = VariantInfo(cv, prefab, width, minRadius);
                probes.Add(new ProbeOperation.Probe
                {
                    Info = info,
                    Make = dryJob => MakeFitJob(cv, prefab, dryJob, snap, wantRoad, null),
                    Guard = job => new ProbeGuard(this, job, limits, Entity.Null, default(float3), false),
                    // among clean courses: least demolition, then the cheapest
                    RankOf = r => DemolitionRank(r) * 1e6 + NumOf(r, "cost"),
                });
            }
            var extra = new Dictionary<string, object>
            {
                { "prefab", PrefabName(prefab) },
                { "fit", new Dictionary<string, object> { { "tolerance", tol }, { "minRadius", minRadius }, { "maxTries", maxTries }, { "tieInPrefab", tieIn == Entity.Null ? null : PrefabName(tieIn) }, { "allowDemolish", allowDemolish } } },
                { "anchors", new Dictionary<string, object>
                    {
                        { "start", AnchorInfo(A, kindA, dA) }, { "end", AnchorInfo(B, kindB, dB) },
                    } },
                { "variants", new Dictionary<string, object>
                    {
                        { "generated", variants.Count }, { "distinct", ranked.Count }, { "previewing", chosenSet.Count },
                        { "screened", ranked.Take(12).Select(v => (object)new Dictionary<string, object>
                            {
                                { "name", v.Name }, { "score", Math.Round(v.Score, 1) }, { "length", Math.Round(v.Len, 1) },
                                { "minRadius", float.IsInfinity(v.MinR) ? null : (object)Math.Round(v.MinR, 1) }, { "deviation", Math.Round(v.Dev, 1) },
                                { "lotOverlapsZoned", v.GeomHits }, { "skew", new List<object> { Math.Round(v.SkewA), Math.Round(v.SkewB) } },
                            }).ToList() },
                    } },
                { "usage", "candidates[] = previewed course variants (passing first): geometry (minRadius, length, warnings), errors/problems, demolition. chosen.request is the equivalent POST /road body (segments with explicit control points). Not dryRun: the first clean variant was built (result.builtEdges). fit.all previews every variant and builds the cheapest clean one." },
            };
            return new ProbeOperation(tool, probes, dry, all, HttpTimeoutMs(pend), extra, (res, chosen) =>
            {
                if (chosen == null) { res["hint"] = "no previewed variant was clean: see candidates[].reason and variants.screened; widen fit.tolerance, lower fit.minRadius, raise fit.maxTries, or allowDemolish"; return; }
                res["geometry"] = chosen.Info["geometry"];
                res["request"] = chosen.Info["request"];
                var r = chosen.Result;
                if (r != null) foreach (var k in new[] { "builtEdges", "newEdgeIds", "builtNodes", "staleEdgeIds" }) if (r.TryGetValue(k, out var v)) res[k] = v;
            });
        }

        Dictionary<string, object> AnchorInfo(CoursePos cp, string kind, float2 dir)
        {
            var d = new Dictionary<string, object> { { "pos", F2(cp.m_Position) }, { "kind", kind } };
            if (cp.m_Entity != Entity.Null) d["id"] = Id(cp.m_Entity);
            if (math.any(dir != 0)) d["direction"] = XZ(dir, 3);
            return d;
        }

        /// <summary>A copy of a variant whose end span(s) at the given tie-in ends are cut ~30 m from the end; the end piece uses the smaller prefab.</summary>
        FitVariant SplitForTieIn(FitVariant src, List<int> ends, Entity small)
        {
            var v = new FitVariant { Name = src.Name + "+tieIn", Penalty = src.Penalty, Detail = new Dictionary<string, object>(src.Detail), PrefA = src.PrefA, PrefB = src.PrefB, TeeA = src.TeeA, TeeB = src.TeeB };
            v.Detail["tieInPrefab"] = PrefabName(small);
            int last = src.Spans.Count - 1;
            // requested ends = the snapped ends of the planned curves (re-snapping a point on the edge finds the same
            // edge), so Cubic pieces need no snap shift: BuildCurve moves control points by (snapped - requested)
            v.Req.Add(src.Curves[0].a.xz);
            for (int i = 0; i <= last; i++)
            {
                var cv = src.Curves[i];
                float len = MathUtils.Length(cv);
                var endReq = i == last ? src.Curves[last].d.xz : src.Req[i + 1];
                bool cutStart = i == 0 && ends.Contains(0), cutEnd = i == last && ends.Contains(1);
                if (!cutStart && !cutEnd) { v.Spans.Add(src.Spans[i]); v.SpanPrefabs.Add(Entity.Null); v.Req.Add(endReq); continue; }
                if (len < 24f || (cutStart && cutEnd && len < 64f)) { v.Spans.Add(src.Spans[i]); v.SpanPrefabs.Add(small); v.Req.Add(endReq); continue; }
                // cut ~30 m from the tie-in end(s) (arc length approximated by the parameter); explicit cubic pieces
                float cut = math.min(32f, 0.4f * len) / len;
                var ts = new List<float> { 0f };
                if (cutStart) ts.Add(cut);
                if (cutEnd) ts.Add(1f - cut);
                ts.Add(1f);
                for (int k = 0; k + 1 < ts.Count; k++)
                {
                    var piece = MathUtils.Cut(cv, new float2(ts[k], ts[k + 1]));
                    v.Spans.Add(new CourseNetJob.Span { Kind = CourseNetJob.SpanKind.Cubic, C1 = piece.b.xz, C2 = piece.c.xz });
                    bool isTie = (cutStart && k == 0) || (cutEnd && k == ts.Count - 2);
                    v.SpanPrefabs.Add(isTie ? small : Entity.Null);
                    v.Req.Add(k == ts.Count - 2 ? endReq : piece.d.xz); // inner cut joints are free points on the curve
                }
            }
            return v.SpanPrefabs.All(e => e == Entity.Null) ? null : v;
        }

        /// <param name="planSnap">cached snapping while planning (null: snap now; the job re-snaps when it runs anyway)</param>
        FitCourseJob MakeFitJob(FitVariant v, Entity prefab, bool dry, float snap, bool wantRoad, Func<float2, CoursePos> planSnap)
        {
            var job = new FitCourseJob { Api = this, Prefab = prefab, DryRun = dry };
            int n = v.Req.Count;
            for (int i = 0; i < n; i++)
            {
                var p = new float3(v.Req[i].x, 0, v.Req[i].y);
                if (i == 0 || i == n - 1) job.Joints.Add(planSnap != null ? planSnap(v.Req[i]) : SnapPoint(p, snap, wantRoad, new List<object>(), prefab));
                else { p.y = TerrainHeight(p); job.Joints.Add(new CoursePos { m_Position = p, m_ParentMesh = -1 }); }
                job.Requested.Add(v.Req[i]);
            }
            job.Spans.AddRange(v.Spans);
            job.SpanPrefabs.AddRange(v.SpanPrefabs);
            var reqPos = v.Req.Select(q => new float3(q.x, 0, q.y)).ToArray();
            job.Resnap = (i, cp) => i == 0 || i == n - 1 ? SnapPoint(reqPos[i], snap, wantRoad, new List<object>(), prefab) : cp;
            // footprint polyline (demolition safety): the planned curves every 8 m
            for (int i = 0; i < v.Spans.Count; i++)
            {
                var cv = CourseNetJob.BuildCurve(v.Spans[i], job.Joints[i].m_Position, job.Joints[i + 1].m_Position, v.Req[i], v.Req[i + 1]);
                int k = math.max(1, (int)math.ceil(MathUtils.Length(cv) / 8f));
                for (int s = i == 0 ? 0 : 1; s <= k; s++) job.Points.Add(new CoursePos { m_Position = MathUtils.Position(cv, s / (float)k), m_ParentMesh = -1 });
            }
            return job;
        }

        Dictionary<string, object> VariantInfo(FitVariant v, Entity prefab, float width, float minRadius)
        {
            var spans = new List<object>();
            var segs = new List<object>();
            for (int i = 0; i < v.Curves.Count; i++)
            {
                var cv = v.Curves[i];
                CurveStats(cv, out float r, out float turn);
                var sd = new Dictionary<string, object>
                {
                    { "kind", v.Spans[i].Kind.ToString().ToLowerInvariant() }, { "from", F2(cv.a) }, { "to", F2(cv.d) },
                    { "length", Math.Round(MathUtils.Length(cv), 1) }, { "turnDeg", Math.Round(turn, 1) },
                    { "minRadius", float.IsInfinity(r) ? null : (object)Math.Round(r, 1) },
                };
                if (v.SpanPrefabs[i] != Entity.Null) sd["prefab"] = PrefabName(v.SpanPrefabs[i]);
                spans.Add(sd);
                var seg = new Dictionary<string, object> { { "end", XZ(cv.d.xz, 2) } };
                if (i == 0) seg["start"] = XZ(cv.a.xz, 2);
                if (!IsStraight(cv)) { seg["control1"] = XZ(cv.b.xz, 2); seg["control2"] = XZ(cv.c.xz, 2); }
                segs.Add(seg);
            }
            var warnings = new List<object>();
            if (!float.IsInfinity(v.MinR) && v.MinR < minRadius) warnings.Add("minimum radius " + Math.Round(v.MinR, 1) + " m is below fit.minRadius " + minRadius + " m (no variant met it)");
            if (!float.IsInfinity(v.MinR) && v.MinR < width) warnings.Add("a curve radius (" + Math.Round(v.MinR, 1) + " m) is below the road width (" + width + " m)");
            if (v.MaxTurn > 100f) warnings.Add("a span turns more than 100 degrees");
            var geom = new Dictionary<string, object>
            {
                { "length", Math.Round(v.Len, 1) }, { "minRadius", float.IsInfinity(v.MinR) ? null : (object)Math.Round(v.MinR, 1) },
                { "deviation", Math.Round(v.Dev, 1) }, { "endSkewDeg", new List<object> { Math.Round(v.SkewA, 1), Math.Round(v.SkewB, 1) } }, { "spans", spans },
            };
            if (warnings.Count > 0) geom["warnings"] = warnings;
            var request = new Dictionary<string, object> { { "prefab", PrefabName(prefab) }, { "segments", segs } };
            if (v.SpanPrefabs.Any(e => e != Entity.Null))
                request["note"] = "spans with their own 'prefab' (tie-in) need separate /road calls; segments here use one prefab";
            var info = new Dictionary<string, object>
            {
                { "variant", v.Name }, { "score", Math.Round(v.Score, 1) }, { "geometry", geom }, { "request", request },
                { "lotOverlapsZoned", v.GeomHits },
            };
            foreach (var kv in v.Detail) info[kv.Key] = kv.Value;
            return info;
        }
    }
}
