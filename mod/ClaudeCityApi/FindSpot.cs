using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using NetEdge = Game.Net.Edge;
using NetNode = Game.Net.Node;
using ObjTransform = Game.Objects.Transform;
using AreaNode = Game.Areas.Node;
using AreaTriangle = Game.Areas.Triangle;

namespace ClaudeCityBridge
{
    /// <summary>
    /// GET /findspot: where does a (large) building fit along a road, without dry-running /building.
    /// GET /freeland: largest free rectangles of buildable land, and whether they touch a road.
    ///
    /// Both build a 2D obstacle model of the search area that mirrors the game's object validation
    /// (Game.Objects.ValidationHelpers.ValidateObject): other objects' base quads (buildings use
    /// their lot minus 0.2 m per side), net edge + node geometry strips (EdgeGeometry /
    /// Start/EndNodeGeometry segments), physical area triangles and unowned map tiles, filtered by
    /// the same collision masks (ObjectUtils/NetUtils/AreaUtils.GetCollisionMask). Overridable
    /// objects (trees, small props) are ignored because placement removes them; empty zoned cells
    /// are not obstacles. Water uses the game's InWater rule (depth &gt; 0.2 m anywhere under the
    /// lot); steepness is a soft filter of ours (the game flattens terrain for buildings).
    /// </summary>
    public partial class Api
    {
        // ------------------------------------------------------------------ obstacle model

        const int K_BUILDING = 0, K_OBJECT = 1, K_ROAD = 2, K_NET = 3, K_AREA = 4, K_LIMITS = 5, K_WATER = 6, K_STEEP = 7;
        static readonly string[] KindNames = { "building", "object", "road", "net", "area", "cityLimits", "water", "steep", "zonedBuilding" }; // [8] = K_ZONED (Demolish.cs)
        const float OverlapEps = 0.02f;   // penetration needed to count as overlap (game: 0.01 tolerance)
        const float WaterDepthLimit = 0.2f; // CheckSurface: InWater when sampled depth > 0.2 m

        sealed class Obst
        {
            public float2[] Poly;          // convex polygon, or null for a circle
            public float2 C; public float R; // circle
            public float2 Min, Max;        // xz bounds
            public int Kind;
            public bool Frontable;         // a road a building can face/connect to
            public Entity Src;
        }

        /// <summary>Oriented rectangle: centre, right axis U, forward axis V (towards the road), half extents.</summary>
        struct LotRect { public float2 C, U, V; public float Ex, Ez; public float2 Min, Max; }

        static LotRect MakeRect(float2 pos, float2 fwd, Bounds2 local, float side = 0, float back = 0, float front = 0)
        {
            var u = new float2(fwd.y, -fwd.x); // object local +x for LookRotation(fwd, up)
            float x0 = local.min.x - side, x1 = local.max.x + side, z0 = local.min.y - back, z1 = local.max.y + front;
            var r = new LotRect { U = u, V = fwd, Ex = (x1 - x0) * 0.5f, Ez = (z1 - z0) * 0.5f };
            r.C = pos + u * ((x0 + x1) * 0.5f) + fwd * ((z0 + z1) * 0.5f);
            var ext = math.abs(u) * r.Ex + math.abs(fwd) * r.Ez;
            r.Min = r.C - ext; r.Max = r.C + ext;
            return r;
        }

        static LotRect CellRect(float2 c, float half) =>
            new LotRect { C = c, U = new float2(1, 0), V = new float2(0, 1), Ex = half, Ez = half, Min = c - half, Max = c + half };

        static float2[] Corners(ref LotRect r) => new[]
        {
            r.C - r.U * r.Ex - r.V * r.Ez, r.C + r.U * r.Ex - r.V * r.Ez,
            r.C + r.U * r.Ex + r.V * r.Ez, r.C - r.U * r.Ex + r.V * r.Ez,
        };

        /// <summary>Separating-axis test of an oriented rectangle against a convex polygon or circle.</summary>
        static bool Overlaps(ref LotRect r, Obst o, float eps)
        {
            if (o.Max.x < r.Min.x || o.Min.x > r.Max.x || o.Max.y < r.Min.y || o.Min.y > r.Max.y) return false;
            if (o.Poly == null)
            {
                if (o.R <= eps) return false;
                var d = o.C - r.C;
                float qx = math.max(math.abs(math.dot(d, r.U)) - r.Ex, 0f), qz = math.max(math.abs(math.dot(d, r.V)) - r.Ez, 0f);
                float rr = o.R - eps;
                return qx * qx + qz * qz < rr * rr;
            }
            var p = o.Poly;
            if (!AxisOverlap(ref r, p, r.U, eps) || !AxisOverlap(ref r, p, r.V, eps)) return false;
            int n = p.Length;
            for (int i = 0; i < n; i++)
            {
                var e = p[i + 1 == n ? 0 : i + 1] - p[i];
                float len = math.length(e);
                if (len < 1e-3f) continue;
                if (!AxisOverlap(ref r, p, new float2(-e.y, e.x) / len, eps)) return false;
            }
            return true;
        }

        static bool AxisOverlap(ref LotRect r, float2[] p, float2 ax, float eps)
        {
            float h = r.Ex * math.abs(math.dot(r.U, ax)) + r.Ez * math.abs(math.dot(r.V, ax));
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < p.Length; i++)
            {
                float d = math.dot(p[i] - r.C, ax); // relative to the rect centre for precision
                if (d < mn) mn = d;
                if (d > mx) mx = d;
            }
            return math.min(h, mx) - math.max(-h, mn) > eps;
        }

        /// <summary>Obstacles of one search area, bucketed in a uniform grid for rectangle queries.</summary>
        sealed partial class ObstSet
        {
            public readonly List<Obst> All = new List<Obst>();
            public readonly float2 AreaMin, AreaMax;
            readonly float cell;
            readonly int cols, rows;
            readonly List<int>[] grid;
            int[] stamp = new int[0];
            int cur;

            public ObstSet(float2 min, float2 max, float cell)
            {
                AreaMin = min; AreaMax = max; this.cell = cell;
                cols = math.max(1, (int)math.ceil((max.x - min.x) / cell));
                rows = math.max(1, (int)math.ceil((max.y - min.y) / cell));
                grid = new List<int>[cols * rows];
            }

            public bool InArea(float2 mn, float2 mx) => !(mx.x < AreaMin.x || mn.x > AreaMax.x || mx.y < AreaMin.y || mn.y > AreaMax.y);

            public void AddPoly(float2[] pts, int kind, Entity src, bool frontable)
            {
                var mn = pts[0]; var mx = pts[0];
                for (int i = 1; i < pts.Length; i++) { mn = math.min(mn, pts[i]); mx = math.max(mx, pts[i]); }
                if (!InArea(mn, mx)) return;
                Insert(new Obst { Poly = pts, Min = mn, Max = mx, Kind = kind, Src = src, Frontable = frontable });
            }

            public void AddCircle(float2 c, float r, int kind, Entity src)
            {
                if (!InArea(c - r, c + r)) return;
                Insert(new Obst { C = c, R = r, Min = c - r, Max = c + r, Kind = kind, Src = src });
            }

            void Insert(Obst o)
            {
                int idx = All.Count;
                All.Add(o);
                Range(o.Min, o.Max, out int c0, out int c1, out int r0, out int r1);
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                    {
                        int k = r * cols + c;
                        (grid[k] ?? (grid[k] = new List<int>())).Add(idx);
                    }
            }

            void Range(float2 mn, float2 mx, out int c0, out int c1, out int r0, out int r1)
            {
                c0 = math.clamp((int)math.floor((mn.x - AreaMin.x) / cell), 0, cols - 1);
                c1 = math.clamp((int)math.floor((mx.x - AreaMin.x) / cell), 0, cols - 1);
                r0 = math.clamp((int)math.floor((mn.y - AreaMin.y) / cell), 0, rows - 1);
                r1 = math.clamp((int)math.floor((mx.y - AreaMin.y) / cell), 0, rows - 1);
            }

            /// <summary>First obstacle the rectangle overlaps (null = free).</summary>
            public Obst FirstHit(ref LotRect r, float eps)
            {
                if (stamp.Length < All.Count) stamp = new int[All.Count];
                cur++;
                Range(r.Min, r.Max, out int c0, out int c1, out int r0, out int r1);
                for (int y = r0; y <= r1; y++)
                    for (int x = c0; x <= c1; x++)
                    {
                        var l = grid[y * cols + x];
                        if (l == null) continue;
                        for (int i = 0; i < l.Count; i++)
                        {
                            int idx = l[i];
                            if (stamp[idx] == cur) continue;
                            stamp[idx] = cur;
                            var o = All[idx];
                            if (Overlaps(ref r, o, eps)) return o;
                        }
                    }
                return null;
            }
        }

        static float ApproxLen(Bezier4x3 b) => math.distance(b.a.xz, b.b.xz) + math.distance(b.b.xz, b.c.xz) + math.distance(b.c.xz, b.d.xz);

        static bool IsStraight(Bezier4x3 b)
        {
            var a = b.a.xz; var ad = b.d.xz - a;
            float l = math.length(ad);
            if (l < 0.01f) return math.distance(b.b.xz, a) < 0.05f && math.distance(b.c.xz, a) < 0.05f;
            var nrm = new float2(-ad.y, ad.x) / l;
            return math.abs(math.dot(b.b.xz - a, nrm)) < 0.05f && math.abs(math.dot(b.c.xz - a, nrm)) < 0.05f;
        }

        /// <summary>
        /// A net geometry segment (area between its left and right curve) as a strip of quads. Quads that
        /// lie entirely under the terrain (pipes, tunnels) or entirely higher than the new object
        /// (bridge decks) are left out, like the game's 3D object-vs-net test.
        /// </summary>
        static void AddSegment(ObstSet set, Game.Net.Segment s, int kind, Entity src, bool frontable, TerrainProbe tp, float objHeight)
        {
            if (math.all(s.m_Length <= 0.1f)) return; // empty piece; the game skips these too
            var L = s.m_Left; var R = s.m_Right;
            float len = math.max(ApproxLen(L), ApproxLen(R));
            float piece = IsStraight(L) && IsStraight(R) ? 16f : 4f;
            int n = math.clamp((int)math.ceil(len / piece), 1, 64);
            float3 pl = L.a, pr = R.a;
            int vl = Vertical(tp, pl, objHeight), vr = Vertical(tp, pr, objHeight);
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                var nl = MathUtils.Position(L, t); var nr = MathUtils.Position(R, t);
                int wl = Vertical(tp, nl, objHeight), wr = Vertical(tp, nr, objHeight);
                bool skip = (vl == -1 && vr == -1 && wl == -1 && wr == -1) || (vl == 1 && vr == 1 && wl == 1 && wr == 1);
                if (!skip) set.AddPoly(new[] { pl.xz, nl.xz, nr.xz, pr.xz }, kind, src, frontable);
                pl = nl; pr = nr; vl = wl; vr = wr;
            }
        }

        /// <summary>-1 below the terrain, 1 above the object's height, 0 in between.</summary>
        static int Vertical(TerrainProbe tp, float3 p, float objHeight)
        {
            float h = tp.Height(p.xz);
            return p.y < h - 0.5f ? -1 : (p.y > h + objHeight ? 1 : 0);
        }

        /// <summary>Fallback for nets without EdgeGeometry: the curve widened to +-hw.</summary>
        static void AddCurveStrip(ObstSet set, Bezier4x3 cv, float hw, int kind, Entity src, bool frontable)
        {
            float len = ApproxLen(cv);
            int n = IsStraight(cv) ? 1 : math.clamp((int)math.ceil(len / 4f), 1, 64);
            float2 prevL = 0, prevR = 0;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                var pt = MathUtils.Position(cv, t).xz;
                var dir = math.normalizesafe(MathUtils.Tangent(cv, t).xz);
                var left = new float2(-dir.y, dir.x) * hw;
                if (i > 0) set.AddPoly(new[] { prevL, pt + left, pt - left, prevR }, kind, src, frontable);
                prevL = pt + left; prevR = pt - left;
            }
            // round-ish caps so node areas are covered
            set.AddCircle(cv.a.xz, hw, kind, src);
            set.AddCircle(cv.d.xz, hw, kind, src);
        }

        CollisionMask NetMask(Entity composition, CollisionMask fallback)
        {
            if (composition != Entity.Null && em.HasComponent<NetCompositionData>(composition))
                return NetUtils.GetCollisionMask(em.GetComponentData<NetCompositionData>(composition), true);
            return fallback;
        }

        /// <summary>A road a service building can face: top-level, not highway, not elevated or in a tunnel.</summary>
        bool FrontableRoad(Entity e)
        {
            if (!em.HasComponent<Road>(e) || em.HasComponent<Owner>(e)) return false;
            var pf = em.GetComponentData<PrefabRef>(e).m_Prefab;
            if (em.HasComponent<RoadData>(pf) && (em.GetComponentData<RoadData>(pf).m_Flags & Game.Prefabs.RoadFlags.UseHighwayRules) != 0) return false;
            if (em.HasComponent<Composition>(e))
            {
                var ce = em.GetComponentData<Composition>(e).m_Edge;
                if (ce != Entity.Null && em.HasComponent<NetCompositionData>(ce))
                {
                    var g = em.GetComponentData<NetCompositionData>(ce).m_Flags.m_General;
                    if ((g & (CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel)) != 0) return false;
                }
            }
            return true;
        }

        void AddObjects(ObstSet set, EntityQuery q, CollisionMask bmask, Dictionary<Entity, ObjectGeometryData?> geoCache)
        {
            const float objReach = 400f; // no object base reaches further than this from its pivot
            var min = set.AreaMin; var max = set.AreaMax;
            using (var ents = q.ToEntityArray(Allocator.TempJob))
            using (var trs = q.ToComponentDataArray<ObjTransform>(Allocator.TempJob))
            using (var prs = q.ToComponentDataArray<PrefabRef>(Allocator.TempJob))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    var pos = trs[i].m_Position.xz;
                    if (pos.x < min.x - objReach || pos.x > max.x + objReach || pos.y < min.y - objReach || pos.y > max.y + objReach) continue;
                    var pf = prs[i].m_Prefab;
                    if (!geoCache.TryGetValue(pf, out var g))
                    {
                        g = em.HasComponent<ObjectGeometryData>(pf) ? em.GetComponentData<ObjectGeometryData>(pf) : (ObjectGeometryData?)null;
                        geoCache[pf] = g;
                    }
                    if (g == null) continue;
                    var geo = g.Value;
                    // placement overrides (removes) these instead of failing (trees, small props)
                    if ((geo.m_Flags & Game.Objects.GeometryFlags.Overridable) != 0) continue;
                    var e = ents[i];
                    if ((geo.m_Flags & Game.Objects.GeometryFlags.IgnoreSecondaryCollision) != 0 && em.HasComponent<Game.Objects.Secondary>(e)) continue;
                    var m = em.HasComponent<Game.Objects.Elevation>(e)
                        ? Game.Objects.ObjectUtils.GetCollisionMask(geo, em.GetComponentData<Game.Objects.Elevation>(e), true)
                        : Game.Objects.ObjectUtils.GetCollisionMask(geo, true);
                    if ((m & bmask) == 0) continue;
                    int kind = em.HasComponent<Building>(e) ? K_BUILDING : K_OBJECT;
                    var rot = trs[i].m_Rotation;
                    var b = geo.m_Bounds;
                    if ((geo.m_Flags & Game.Objects.GeometryFlags.Circular) != 0)
                    {
                        var cc = pos + math.mul(rot, new float3((b.min.x + b.max.x) * 0.5f, 0, (b.min.z + b.max.z) * 0.5f)).xz;
                        set.AddCircle(cc, math.max(geo.m_Size.x, geo.m_Size.z) * 0.5f, kind, e);
                    }
                    else
                    {
                        // same as ObjectUtils.CalculateBaseCorners(position, rotation, bounds)
                        set.AddPoly(new[]
                        {
                            pos + math.mul(rot, new float3(b.min.x, 0, b.min.z)).xz,
                            pos + math.mul(rot, new float3(b.min.x, 0, b.max.z)).xz,
                            pos + math.mul(rot, new float3(b.max.x, 0, b.max.z)).xz,
                            pos + math.mul(rot, new float3(b.max.x, 0, b.min.z)).xz,
                        }, kind, e, false);
                    }
                }
            }
        }

        /// <summary>Everything in [min,max] that a new object with collision mask <paramref name="bmask"/> would overlap.</summary>
        ObstSet CollectObstacles(float2 min, float2 max, CollisionMask bmask, TerrainProbe tp, float objHeight, bool growablesFree = false)
        {
            var set = new ObstSet(min, max, 32f);

            // --- objects: top-level objects (buildings, props with collision, outside connections) plus
            // sub-buildings (extensions/upgrades have an Owner but can stick out of the main lot). Other
            // sub-objects (lot props, street furniture) sit inside their owner's footprint.
            var geoCache = new Dictionary<Entity, ObjectGeometryData?>();
            var topQ = em.CreateEntityQuery(ComponentType.ReadOnly<ObjTransform>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Owner>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Hidden>(),
                ComponentType.Exclude<Game.Objects.Moving>(), ComponentType.Exclude<Game.Objects.Tree>(),
                ComponentType.Exclude<Game.Vehicles.Vehicle>(), ComponentType.Exclude<Game.Creatures.Creature>());
            var subQ = em.CreateEntityQuery(ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Hidden>());
            AddObjects(set, topQ, bmask, geoCache);
            AddObjects(set, subQ, bmask, geoCache);
            AddGrowableObstacles(set, bmask, geoCache, growablesFree); // growables (Overridable, skipped above): obstacles unless growables=free

            // --- nets: edge halves and the node pieces belonging to each edge
            var nq = em.CreateEntityQuery(ComponentType.ReadOnly<NetEdge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Hidden>());
            using (var ents = nq.ToEntityArray(Allocator.TempJob))
                foreach (var e in ents)
                {
                    var cv = em.GetComponentData<Curve>(e).m_Bezier;
                    var cmin = math.min(math.min(cv.a.xz, cv.b.xz), math.min(cv.c.xz, cv.d.xz));
                    var cmax = math.max(math.max(cv.a.xz, cv.b.xz), math.max(cv.c.xz, cv.d.xz));
                    if (!set.InArea(cmin - 64f, cmax + 64f)) continue;
                    bool isRoad = em.HasComponent<Road>(e);
                    int kind = isRoad ? K_ROAD : K_NET;
                    bool front = FrontableRoad(e);
                    var comp = em.HasComponent<Composition>(e) ? em.GetComponentData<Composition>(e) : default(Composition);
                    var fallback = comp.m_Edge == Entity.Null ? (CollisionMask)11 : (CollisionMask)0;
                    // the game tests buildings against nets in 3D: pipes/tunnels below the ground and high
                    // bridges above the building do not collide even when their masks would
                    float tmin = math.min(math.min(tp.Height(cv.a.xz), tp.Height(cv.d.xz)), tp.Height(MathUtils.Position(cv, 0.5f).xz));
                    float tmax = math.max(math.max(tp.Height(cv.a.xz), tp.Height(cv.d.xz)), tp.Height(MathUtils.Position(cv, 0.5f).xz));
                    Func<Bounds1, bool> inReach = y => y.max >= tmin - 0.5f && y.min <= tmax + objHeight;
                    if (em.HasComponent<EdgeGeometry>(e))
                    {
                        var g = em.GetComponentData<EdgeGeometry>(e);
                        if ((NetMask(comp.m_Edge, fallback) & bmask) != 0 && inReach(g.m_Bounds.y))
                        {
                            AddSegment(set, g.m_Start, kind, e, front, tp, objHeight);
                            AddSegment(set, g.m_End, kind, e, front, tp, objHeight);
                        }
                        if (em.HasComponent<StartNodeGeometry>(e) && (NetMask(comp.m_StartNode, fallback) & bmask) != 0)
                        {
                            var ng = em.GetComponentData<StartNodeGeometry>(e).m_Geometry;
                            if (inReach(ng.m_Bounds.y)) { AddSegment(set, ng.m_Left, kind, e, front, tp, objHeight); AddSegment(set, ng.m_Right, kind, e, front, tp, objHeight); }
                        }
                        if (em.HasComponent<EndNodeGeometry>(e) && (NetMask(comp.m_EndNode, fallback) & bmask) != 0)
                        {
                            var ng = em.GetComponentData<EndNodeGeometry>(e).m_Geometry;
                            if (inReach(ng.m_Bounds.y)) { AddSegment(set, ng.m_Left, kind, e, front, tp, objHeight); AddSegment(set, ng.m_Right, kind, e, front, tp, objHeight); }
                        }
                    }
                    else if (comp.m_Edge != Entity.Null && em.HasComponent<NetCompositionData>(comp.m_Edge) && (NetMask(comp.m_Edge, 0) & bmask) != 0)
                    {
                        var cd = em.GetComponentData<NetCompositionData>(comp.m_Edge);
                        float ymin = math.min(math.min(cv.a.y, cv.b.y), math.min(cv.c.y, cv.d.y)), ymax = math.max(math.max(cv.a.y, cv.b.y), math.max(cv.c.y, cv.d.y));
                        if (!inReach(new Bounds1(ymin + cd.m_HeightRange.min, ymax + cd.m_HeightRange.max))) continue;
                        AddCurveStrip(set, cv, math.max(0.5f, cd.m_Width * 0.5f), kind, e, front);
                    }
                }

            // --- unowned map tiles (city limits): the tile polygon itself (tiles are convex squares)
            var tq = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Areas.MapTile>(), ComponentType.ReadOnly<AreaNode>(), ComponentType.ReadOnly<Native>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var ents = tq.ToEntityArray(Allocator.TempJob))
                foreach (var t in ents)
                {
                    var nodes = em.GetBuffer<AreaNode>(t, true);
                    if (nodes.Length < 3) continue;
                    var poly = new float2[nodes.Length];
                    for (int i = 0; i < nodes.Length; i++) poly[i] = nodes[i].m_Position.xz;
                    set.AddPoly(poly, K_LIMITS, t, false);
                }

            // --- other areas: native protected areas and physical areas (e.g. building sub-lots)
            var aq = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Areas.Area>(), ComponentType.ReadOnly<AreaNode>(), ComponentType.ReadOnly<AreaTriangle>(),
                ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Game.Areas.MapTile>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Hidden>());
            using (var ents = aq.ToEntityArray(Allocator.TempJob))
                foreach (var a in ents)
                {
                    if ((em.GetComponentData<Game.Areas.Area>(a).m_Flags & Game.Areas.AreaFlags.Slave) != 0) continue;
                    var pf = em.GetComponentData<PrefabRef>(a).m_Prefab;
                    if (!em.HasComponent<AreaGeometryData>(pf)) continue;
                    var ag = em.GetComponentData<AreaGeometryData>(pf);
                    if (ag.m_Type == Game.Areas.AreaType.MapTile) continue;
                    bool native = em.HasComponent<Native>(a);
                    int kind;
                    if ((ag.m_Flags & Game.Areas.GeometryFlags.ProtectedArea) != 0)
                    {
                        if (!native) continue;
                        kind = K_LIMITS;
                    }
                    else if ((ag.m_Flags & Game.Areas.GeometryFlags.PhysicalGeometry) != 0)
                    {
                        if ((Game.Areas.AreaUtils.GetCollisionMask(ag) & bmask) == 0) continue;
                        kind = K_AREA;
                    }
                    else continue;
                    var nodes = em.GetBuffer<AreaNode>(a, true);
                    var tris = em.GetBuffer<AreaTriangle>(a, true);
                    for (int i = 0; i < tris.Length; i++)
                    {
                        var ix = tris[i].m_Indices;
                        if (ix.x >= nodes.Length || ix.y >= nodes.Length || ix.z >= nodes.Length) continue;
                        set.AddPoly(new[] { nodes[ix.x].m_Position.xz, nodes[ix.y].m_Position.xz, nodes[ix.z].m_Position.xz }, kind, a, false);
                    }
                }
            return set;
        }

        // ------------------------------------------------------------------ terrain / water

        sealed class TerrainProbe
        {
            public TerrainHeightData H;
            public WaterSurfaceData<SurfaceWater> W;
            public float Height(float2 p) => TerrainUtils.SampleHeight(ref H, new float3(p.x, 0, p.y));
            public float Depth(float2 p) => WaterUtils.SampleDepth(ref W, new float3(p.x, 0, p.y));
        }

        TerrainProbe Probe()
        {
            var tp = new TerrainProbe { H = Sys<TerrainSystem>().GetHeightData(false) };
            JobHandle deps;
            tp.W = Sys<WaterSystem>().GetSurfaceData(out deps);
            deps.Complete();
            return tp;
        }

        struct LotTerrain { public bool Water; public float HMin, HMax, SteepPct; }

        /// <summary>Samples the lot on a lattice (spacing ~8 m, 0.5 m inside the edges): water, height range, share of steep steps.</summary>
        static LotTerrain ProbeLot(TerrainProbe tp, ref LotRect r, float spacing, float steep, bool checkWater)
        {
            float ax = math.max(0f, r.Ex - 0.5f), az = math.max(0f, r.Ez - 0.5f);
            int nx = math.max(1, (int)math.ceil(2 * ax / spacing)), nz = math.max(1, (int)math.ceil(2 * az / spacing));
            float dx = 2 * ax / nx, dz = 2 * az / nz;
            var h = new float[nx + 1, nz + 1];
            var res = new LotTerrain { HMin = float.MaxValue, HMax = float.MinValue };
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    var pt = r.C + r.U * (-ax + i * dx) + r.V * (-az + j * dz);
                    if (checkWater && tp.Depth(pt) > WaterDepthLimit) { res.Water = true; return res; }
                    float y = tp.Height(pt);
                    h[i, j] = y;
                    if (y < res.HMin) res.HMin = y;
                    if (y > res.HMax) res.HMax = y;
                }
            int pairs = 0, steepPairs = 0;
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    if (i < nx && dx > 0.5f) { pairs++; if (math.abs(h[i + 1, j] - h[i, j]) / dx > steep) steepPairs++; }
                    if (j < nz && dz > 0.5f) { pairs++; if (math.abs(h[i, j + 1] - h[i, j]) / dz > steep) steepPairs++; }
                }
            res.SteepPct = pairs > 0 ? 100f * steepPairs / pairs : 0f;
            return res;
        }

        // ------------------------------------------------------------------ lot spec (prefab or w,d)

        sealed class LotSpec
        {
            public Entity Prefab; public string Name; public int2 Lot; public Bounds2 Local; public CollisionMask Mask = (CollisionMask)11;
            public bool SkipWater;
            public float Height = 20f; // building height for the 3D net check (prefab bounds when known)
            public float Depth => Lot.y * 8f; // what PlaceBuilding uses to offset the centre from the road
        }

        object ReadLotSpec(Pending p, out LotSpec spec)
        {
            spec = new LotSpec();
            var name = p.Q("prefab");
            if (!string.IsNullOrEmpty(name))
            {
                // same lookup as PlaceBuilding
                var prefab = FindPrefab(name, e => em.HasComponent<PlaceableObjectData>(e) && em.HasComponent<BuildingData>(e));
                if (prefab == Entity.Null) prefab = FindPrefab(name, e => em.HasComponent<PlaceableObjectData>(e));
                if (prefab == Entity.Null) return new ApiError("object prefab not found: " + name);
                spec.Prefab = prefab;
                spec.Name = PrefabName(prefab);
                spec.Lot = em.HasComponent<BuildingData>(prefab) ? em.GetComponentData<BuildingData>(prefab).m_LotSize : new int2(1, 1);
                if (em.HasComponent<ObjectGeometryData>(prefab))
                {
                    var geo = em.GetComponentData<ObjectGeometryData>(prefab);
                    spec.Local = new Bounds2(geo.m_Bounds.min.xz, geo.m_Bounds.max.xz);
                    spec.Height = math.max(5f, geo.m_Bounds.max.y);
                    spec.Mask = Game.Objects.ObjectUtils.GetCollisionMask(geo, true);
                    if (spec.Mask == 0) spec.Mask = (CollisionMask)3;
                }
                else spec.Local = LotBounds(spec.Lot);
                if (em.HasComponent<PlaceableObjectData>(prefab))
                {
                    var pl = em.GetComponentData<PlaceableObjectData>(prefab).m_Flags;
                    spec.SkipWater = (pl & (Game.Objects.PlacementFlags.Shoreline | Game.Objects.PlacementFlags.Floating | Game.Objects.PlacementFlags.Underwater)) != 0;
                }
                return null;
            }
            if (p.Q("w") != null && p.Q("d") != null)
            {
                spec.Lot = new int2(math.clamp((int)p.QF("w", 1), 1, 100), math.clamp((int)p.QF("d", 1), 1, 100));
                spec.Local = LotBounds(spec.Lot);
                return null;
            }
            return new ApiError("give prefab=<name> or w=<cells>&d=<cells> (lot width along the road, depth away from it; 1 cell = 8 m)");
        }

        /// <summary>A building's collision base: the lot shrunk by 0.2 m per side (BuildingInitializeSystem.InitializeLotSize).</summary>
        static Bounds2 LotBounds(int2 lot)
        {
            var half = ((float2)lot * 8f - 0.4f) * 0.5f;
            return new Bounds2(-half, half);
        }

        static bool QFlag(Pending p, string k) { var v = p.Q(k); return v == "1" || v == "true"; }

        static List<object> XZ(float2 v, int digits = 1) => new List<object> { Math.Round(v.x, digits), Math.Round(v.y, digits) };

        // ------------------------------------------------------------------ GET /findspot

        sealed class Spot
        {
            public string Mode; public Entity Edge; public float T; public bool Right; public float Gap; public string Road;
            public float2 Pos, Fwd, Front; public LotRect Rect;
            public float HRange, SteepPct, Slack, Dist, Score; public bool Roomy, DeadEndRoad;
            public float Setback; // the requested setback that fitted (Gap adds the composition's extra width)
        }

        /// <summary>
        /// Setbacks (m between the road's default edge and the lot front) /findspot tries at every position,
        /// smallest first: setbacks=0.5,2,3.5 or gap=... (one value or a list); default 0.5, 2 and 3.5 m
        /// (a lot that hits a curb, node or roadside object at 0.5 m often fits a little further back).
        /// </summary>
        static List<float> ReadSetbacks(Pending p)
        {
            var s = p.Q("setbacks") ?? p.Q("gap") ?? "0.5,2,3.5";
            var res = new List<float>();
            foreach (var part in s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (float.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                {
                    v = math.clamp(v, 0f, 8f);
                    if (!res.Any(x => math.abs(x - v) < 0.01f)) res.Add(v);
                }
            if (res.Count == 0) res.Add(0.5f);
            res.Sort();
            return res.Count > 6 ? res.GetRange(0, 6) : res;
        }

        /// <summary>
        /// GET /findspot?prefab=|w=&amp;d=&amp;x&amp;z&amp;radius=300&amp;n=5&amp;step=4&amp;setbacks=0.5,2,3.5 (or gap=)&amp;side=both&amp;filter=&amp;steep=0.25&amp;maxSteepPct=10&amp;deadEnds=0
        /// Ranked placements where the lot fits with its front on a road (same meaning as POST /building edge/t/side/gap).
        /// </summary>
        object FindSpot(Pending p)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (p.Q("x") == null || p.Q("z") == null) return new ApiError("x and z are required");
            var err = ReadLotSpec(p, out var spec);
            if (err != null) return err;
            var c = new float2(p.QF("x", 0f), p.QF("z", 0f));
            float radius = math.clamp(p.QF("radius", 300f), 16f, 1000f);
            int want = math.clamp((int)p.QF("n", 5f), 1, 50);
            float step = math.clamp(p.QF("step", 4f), 2f, 32f);
            var setbacks = ReadSetbacks(p);
            float steep = math.max(0.01f, p.QF("steep", 0.25f));
            float maxSteepPct = p.QF("maxSteepPct", 10f);
            string sideQ = (p.Q("side", "both") ?? "both").ToLowerInvariant();
            string filter = p.Q("filter");
            bool deadEnds = QFlag(p, "deadEnds");

            float depth = spec.Depth;
            float lotSpan = math.length(spec.Local.max - spec.Local.min);
            float reach = radius + depth + lotSpan + 48f;
            var tp = Probe();
            var obs = CollectObstacles(c - reach, c + reach, spec.Mask, tp, spec.Height, GrowablesFree(p));
            long msCollect = sw.ElapsedMilliseconds;

            // road edges to slide along
            var eq = em.CreateEntityQuery(ComponentType.ReadOnly<NetEdge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Road>(),
                ComponentType.Exclude<Owner>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Hidden>());
            var fronts = new List<Entity>();
            float totalLen = 0;
            using (var arr = eq.ToEntityArray(Allocator.TempJob))
                foreach (var e in arr)
                {
                    if (!FrontableRoad(e)) continue;
                    var cv = em.GetComponentData<Curve>(e);
                    if (MathUtils.Distance(cv.m_Bezier.xz, c, out float _t) > radius) continue;
                    if (!string.IsNullOrEmpty(filter) && (PrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab) ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    fronts.Add(e);
                    totalLen += cv.m_Length;
                }
            int nSides = sideQ == "both" ? 2 : 1;
            int maxPositions = setbacks.Count > 1 ? 40000 : 60000; // failing positions are evaluated once per setback
            if (totalLen * nSides / step > maxPositions) step = totalLen * nSides / maxPositions;

            var rejected = new int[KindNames.Length];
            int positions = 0;
            var valid = new List<Spot>();
            foreach (var e in fronts)
            {
                var cv = em.GetComponentData<Curve>(e);
                var pf = em.GetComponentData<PrefabRef>(e).m_Prefab;
                string roadName = PrefabName(pf);
                // PlaceBuilding offsets by NetGeometryData default width; widen the gap if the actual composition is wider
                float hwPlace = em.HasComponent<NetGeometryData>(pf) ? em.GetComponentData<NetGeometryData>(pf).m_DefaultWidth * 0.5f : 4f;
                float extraWidth = 0f; // gap passed to /building = setback + this
                if (em.HasComponent<Composition>(e))
                {
                    var ce = em.GetComponentData<Composition>(e).m_Edge;
                    if (ce != Entity.Null && em.HasComponent<NetCompositionData>(ce))
                        extraWidth = math.max(0f, em.GetComponentData<NetCompositionData>(ce).m_Width * 0.5f - hwPlace);
                }
                var edge = em.GetComponentData<NetEdge>(e);
                bool deadEndRoad = Degree(edge.m_Start) == 1 || Degree(edge.m_End) == 1;
                int K = math.max(1, (int)math.ceil(cv.m_Length / step));
                foreach (bool right in new[] { false, true })
                {
                    if (sideQ == "left" && right) continue;
                    if (sideQ == "right" && !right) continue;
                    var seq = new Spot[K + 1];
                    var ok = new bool[K + 1];
                    for (int i = 0; i <= K; i++)
                    {
                        float t = i / (float)K;
                        var pt = MathUtils.Position(cv.m_Bezier, t);
                        if (math.distance(pt.xz, c) > radius) continue;
                        positions++;
                        // identical to PlaceBuilding (edge mode); the smallest setback that fits wins
                        var dir = math.normalizesafe(MathUtils.Tangent(cv.m_Bezier, t).xz);
                        var left = new float2(-dir.y, dir.x);
                        var n = right ? -left : left;
                        var fwd = -n;
                        int firstWhy = -1;
                        foreach (var sb in setbacks)
                        {
                            float edgeGap = sb + extraWidth;
                            var pos = pt.xz + n * (hwPlace + depth * 0.5f + edgeGap);
                            var rect = MakeRect(pos, fwd, spec.Local);
                            int why = EvalLot(obs, tp, ref rect, spec, steep, maxSteepPct, out var lt);
                            if (why >= 0) { if (firstWhy < 0) firstWhy = why; continue; }
                            ok[i] = true;
                            seq[i] = new Spot
                            {
                                Mode = "edge", Edge = e, T = t, Right = right, Gap = edgeGap, Setback = sb, Road = roadName, DeadEndRoad = deadEndRoad,
                                Pos = pos, Fwd = fwd, Front = pt.xz, Rect = rect, HRange = lt.HMax - lt.HMin, SteepPct = lt.SteepPct,
                            };
                            break;
                        }
                        if (!ok[i] && firstWhy >= 0) rejected[firstWhy]++; // reason at the smallest setback
                    }
                    // slack: how far the lot could slide along this road side before it stops fitting
                    float stepLen = cv.m_Length / K;
                    for (int i = 0; i <= K; i++)
                    {
                        if (!ok[i]) continue;
                        int a = i, b = i;
                        while (a - 1 >= 0 && ok[a - 1]) a--;
                        while (b + 1 <= K && ok[b + 1]) b++;
                        seq[i].Slack = math.min(i - a, b - i) * stepLen;
                        valid.Add(seq[i]);
                    }
                }
            }

            // optional: lot beyond the end of a dead-end road, facing along it (free x,z,angle placement)
            int deadEndTried = 0;
            if (deadEnds)
                foreach (var e in fronts)
                {
                    var cv = em.GetComponentData<Curve>(e);
                    var edge = em.GetComponentData<NetEdge>(e);
                    var pf = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    float hw = em.HasComponent<NetGeometryData>(pf) ? em.GetComponentData<NetGeometryData>(pf).m_DefaultWidth * 0.5f : 4f;
                    foreach (bool atStart in new[] { true, false })
                    {
                        var node = atStart ? edge.m_Start : edge.m_End;
                        if (Degree(node) != 1 || !em.HasComponent<NetNode>(node)) continue;
                        var np = em.GetComponentData<NetNode>(node).m_Position.xz;
                        if (math.distance(np, c) > radius) continue;
                        var dirOut = atStart ? -math.normalizesafe(MathUtils.StartTangent(cv.m_Bezier).xz) : math.normalizesafe(MathUtils.EndTangent(cv.m_Bezier).xz);
                        float ext = hw;
                        bool hasNg = atStart ? em.HasComponent<StartNodeGeometry>(e) : em.HasComponent<EndNodeGeometry>(e);
                        if (hasNg)
                        {
                            var ng = atStart ? em.GetComponentData<StartNodeGeometry>(e).m_Geometry : em.GetComponentData<EndNodeGeometry>(e).m_Geometry;
                            foreach (var bz in new[] { ng.m_Left.m_Left, ng.m_Left.m_Right, ng.m_Right.m_Left, ng.m_Right.m_Right })
                                for (int k = 0; k <= 4; k++) ext = math.max(ext, math.dot(MathUtils.Position(bz, k / 4f).xz - np, dirOut));
                        }
                        deadEndTried++;
                        var fwd = -dirOut;
                        int firstWhy = -1;
                        bool fits = false;
                        foreach (var sb in setbacks)
                        {
                            var pos = np + dirOut * (ext + sb + depth * 0.5f);
                            var rect = MakeRect(pos, fwd, spec.Local);
                            int why = EvalLot(obs, tp, ref rect, spec, steep, maxSteepPct, out var lt);
                            if (why >= 0) { if (firstWhy < 0) firstWhy = why; continue; }
                            valid.Add(new Spot
                            {
                                Mode = "deadEnd", Edge = e, T = atStart ? 0f : 1f, Gap = sb, Setback = sb, Road = PrefabName(pf), DeadEndRoad = true,
                                Pos = pos, Fwd = fwd, Front = np, Rect = rect, HRange = lt.HMax - lt.HMin, SteepPct = lt.SteepPct,
                            });
                            fits = true;
                            break;
                        }
                        if (!fits && firstWhy >= 0) rejected[firstWhy]++;
                    }
                }

            // score: near the requested point, flat, room to slide, room behind/beside
            float distNorm = radius + depth;
            foreach (var s in valid)
            {
                var roomy = MakeRect(s.Pos, s.Fwd, spec.Local, 2f, 2f, 0f);
                s.Roomy = obs.FirstHit(ref roomy, OverlapEps) == null;
                s.Dist = math.distance(s.Rect.C, c);
                s.Score = 100f - 40f * math.min(1f, s.Dist / distNorm) - math.min(20f, s.HRange)
                    - 10f * (1f - math.min(s.Slack, 16f) / 16f) - (s.Roomy ? 0f : 8f) - (s.Mode == "deadEnd" ? 10f : 0f)
                    - 1.5f * (s.Setback - setbacks[0]); // a deeper setback means a longer driveway
            }
            valid.Sort((a, b) => b.Score.CompareTo(a.Score));

            // distinct spots: suppress candidates whose centre is close to a better one
            var lotSize = spec.Local.max - spec.Local.min;
            float sep = math.max(16f, 0.5f * math.min(lotSize.x, lotSize.y));
            var picked = new List<Spot>();
            foreach (var s in valid)
            {
                if (picked.Count >= want) break;
                bool near = false;
                foreach (var q in picked) if (math.distance(q.Rect.C, s.Rect.C) < sep) { near = true; break; }
                if (!near) picked.Add(s);
            }

            var cands = new List<object>();
            int rank = 0;
            foreach (var s in picked)
            {
                rank++;
                float angle = math.degrees(math.atan2(s.Fwd.x, s.Fwd.y));
                var xz = new Dictionary<string, object> { { "x", Math.Round(s.Pos.x, 2) }, { "z", Math.Round(s.Pos.y, 2) }, { "angle", Math.Round(angle, 2) } };
                if (spec.Name != null) xz["prefab"] = spec.Name;
                var d = new Dictionary<string, object>
                {
                    { "rank", rank }, { "score", Math.Round(s.Score, 1) }, { "mode", s.Mode },
                    { "distance", Math.Round(s.Dist, 1) },
                    { "edge", Id(s.Edge) }, { "road", s.Road },
                    { "x", Math.Round(s.Pos.x, 2) }, { "z", Math.Round(s.Pos.y, 2) }, { "angle", Math.Round(angle, 2) },
                    { "front", XZ(s.Front) },
                    { "corners", Corners(ref s.Rect).Select(v => (object)XZ(v)).ToList() },
                    { "slack", Math.Round(s.Slack, 1) }, { "roomy", s.Roomy },
                    { "heightRange", Math.Round(s.HRange, 1) }, { "steepPct", Math.Round(s.SteepPct, 1) },
                    { "deadEndRoad", s.DeadEndRoad },
                };
                d["setback"] = Math.Round(s.Setback, 2);
                if (s.Mode == "edge")
                {
                    d["t"] = Math.Round(s.T, 5);
                    d["side"] = s.Right ? "right" : "left";
                    d["gap"] = Math.Round(s.Gap, 2);
                    var b = new Dictionary<string, object> { { "edge", Id(s.Edge) }, { "t", Math.Round(s.T, 5) }, { "side", s.Right ? "right" : "left" }, { "gap", Math.Round(s.Gap, 2) } };
                    if (spec.Name != null) b["prefab"] = spec.Name;
                    d["building"] = b;
                    d["buildingXZ"] = xz;
                }
                else d["building"] = xz; // dead end: free placement only
                AddGrowableHits(d, obs, ref s.Rect);
                cands.Add(d);
            }

            var rej = new Dictionary<string, object>();
            for (int i = 0; i < rejected.Length; i++) if (rejected[i] > 0) rej[KindNames[i]] = rejected[i];
            var r = new Dictionary<string, object>
            {
                { "prefab", spec.Name }, { "lot", new List<object> { spec.Lot.x, spec.Lot.y } },
                { "lotMetres", new List<object> { spec.Lot.x * 8, spec.Lot.y * 8 } },
                { "centre", XZ(c) }, { "radius", radius }, { "step", Math.Round(step, 2) },
                { "setbacks", setbacks.Select(v => (object)Math.Round(v, 2)).ToList() },
                { "searched", new Dictionary<string, object> { { "roadEdges", fronts.Count }, { "positions", positions }, { "deadEnds", deadEndTried }, { "valid", valid.Count }, { "obstacles", obs.All.Count } } },
                { "rejected", rej },
                { "candidates", cands },
                { "ms", new Dictionary<string, object> { { "collect", msCollect }, { "total", sw.ElapsedMilliseconds } } },
                { "usage", "POST /building with candidates[i].building (edge/t/side/gap reproduce exactly this position); buildingXZ is the same spot as x/z/angle. Higher score = better (near, flat, room to slide 'slack' m, 'roomy' = 2 m spare beside/behind). Each position tries the setbacks (m from the road edge to the lot front; setbacks=0.5,2,3.5 or gap=) smallest first; 'setback' is the one that fitted." },
            };
            if (spec.SkipWater) r["note"] = "prefab is a shoreline/floating/underwater object: the no-water rule was not applied";
            if (cands.Count == 0)
                r["hint"] = "nothing fits along roads within radius: see 'rejected' for what blocks it; try a larger radius, side=, deadEnds=1, maxSteepPct=100, or GET /freeland to find empty land and build an access road into it";
            NoteGrowables(r, obs);
            return r;
        }

        /// <summary>-1 when the lot fits; otherwise the K_* reason of the first problem found.</summary>
        static int EvalLot(ObstSet obs, TerrainProbe tp, ref LotRect r, LotSpec spec, float steep, float maxSteepPct, out LotTerrain lt)
        {
            lt = default;
            var hit = obs.FirstHit(ref r, OverlapEps);
            if (hit != null) return hit.Kind;
            lt = ProbeLot(tp, ref r, 8f, steep, !spec.SkipWater);
            if (lt.Water) return K_WATER;
            if (maxSteepPct < 100f && lt.SteepPct > maxSteepPct) return K_STEEP;
            return -1;
        }

        // ------------------------------------------------------------------ GET /freeland

        /// <summary>
        /// GET /freeland?x&amp;z&amp;radius=400&amp;cell=8&amp;n=5&amp;min=32&amp;steep=0.25&amp;prefab=|w=&amp;d=&amp;grid=0
        /// Largest axis-aligned rectangles of free land (owned, dry, not steep, nothing on it), greedily
        /// (largest first, then the next largest in what is left), with the roads they touch.
        /// </summary>
        object FreeLand(Pending p)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (p.Q("x") == null || p.Q("z") == null) return new ApiError("x and z are required");
            var c = new float2(p.QF("x", 0f), p.QF("z", 0f));
            float radius = math.clamp(p.QF("radius", 400f), 32f, 1000f);
            float cell = math.clamp(p.QF("cell", 8f), 4f, 32f);
            int want = math.clamp((int)p.QF("n", 5f), 1, 20);
            float steep = math.max(0.01f, p.QF("steep", 0.25f));
            int minCells = math.max(1, (int)math.ceil(math.max(p.QF("min", 32f), cell) / cell));
            float2 lotM = 0; bool haveLot = false; float objHeight = 20f;
            string lotName = null;
            if (p.Q("prefab") != null || p.Q("w") != null)
            {
                var err = ReadLotSpec(p, out var spec);
                if (err != null) return err;
                lotM = (float2)spec.Lot * 8f; haveLot = true; lotName = spec.Name; objHeight = spec.Height;
            }
            int N = (int)math.ceil(2 * radius / cell);
            var g0 = c - N * cell * 0.5f;
            var tp = Probe();
            var obs = CollectObstacles(g0 - cell, g0 + N * cell + cell, (CollisionMask)11, tp, objHeight, GrowablesFree(p));

            // lattice of cell corners: owned / wet / height
            var owned = new List<float2[]>();
            var tq = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Areas.MapTile>(), ComponentType.ReadOnly<AreaNode>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var tiles = tq.ToEntityArray(Allocator.TempJob))
                foreach (var t in tiles)
                {
                    if (em.HasComponent<Native>(t)) continue;
                    var b = em.GetBuffer<AreaNode>(t, true);
                    var poly = new float2[b.Length];
                    float2 mn = float.MaxValue, mx = float.MinValue;
                    for (int i = 0; i < b.Length; i++) { poly[i] = b[i].m_Position.xz; mn = math.min(mn, poly[i]); mx = math.max(mx, poly[i]); }
                    if (mx.x < g0.x || mn.x > g0.x + N * cell || mx.y < g0.y || mn.y > g0.y + N * cell) continue;
                    owned.Add(poly);
                }
            int L = N + 1;
            var hgt = new float[L * L];
            var own = new bool[L * L];
            var wet = new bool[L * L];
            for (int r = 0; r < L; r++)
                for (int col = 0; col < L; col++)
                {
                    var pt = g0 + new float2(col, r) * cell;
                    int k = r * L + col;
                    hgt[k] = tp.Height(pt);
                    wet[k] = tp.Depth(pt) > WaterDepthLimit;
                    foreach (var poly in owned) if (InsidePoly(poly, pt)) { own[k] = true; break; }
                }

            // cell states
            const byte S_FREE = 0, S_LIMIT = 1, S_WATER = 2, S_STEEP = 3;
            var state = new byte[N * N];
            var occ = new sbyte[N * N];     // -1 none, else K_* of the first obstacle
            var road = new bool[N * N];
            var roadEdge = new Entity[N * N];
            for (int i = 0; i < occ.Length; i++) occ[i] = -1;
            for (int r = 0; r < N; r++)
                for (int col = 0; col < N; col++)
                {
                    int a = r * L + col, b = a + 1, d = a + L, e = d + 1;
                    byte s = S_FREE;
                    if (!(own[a] && own[b] && own[d] && own[e])) s = S_LIMIT;
                    else if (wet[a] || wet[b] || wet[d] || wet[e] || tp.Depth(g0 + new float2(col + 0.5f, r + 0.5f) * cell) > WaterDepthLimit) s = S_WATER;
                    else
                    {
                        float m = math.max(math.max(math.abs(hgt[a] - hgt[b]), math.abs(hgt[d] - hgt[e])), math.max(math.abs(hgt[a] - hgt[d]), math.abs(hgt[b] - hgt[e])));
                        if (m / cell > steep) s = S_STEEP;
                    }
                    state[r * N + col] = s;
                }
            foreach (var o in obs.All)
            {
                if (o.Kind == K_LIMITS) continue; // ownership comes from the lattice
                int c0 = math.max(0, (int)math.floor((o.Min.x - g0.x) / cell)), c1 = math.min(N - 1, (int)math.floor((o.Max.x - g0.x) / cell));
                int r0 = math.max(0, (int)math.floor((o.Min.y - g0.y) / cell)), r1 = math.min(N - 1, (int)math.floor((o.Max.y - g0.y) / cell));
                for (int r = r0; r <= r1; r++)
                    for (int col = c0; col <= c1; col++)
                    {
                        int k = r * N + col;
                        if (occ[k] >= 0 && !(o.Frontable && !road[k])) continue;
                        var cr = CellRect(g0 + new float2(col + 0.5f, r + 0.5f) * cell, cell * 0.5f);
                        if (!Overlaps(ref cr, o, 0.05f)) continue;
                        if (occ[k] < 0) occ[k] = (sbyte)o.Kind;
                        if (o.Frontable && !road[k]) { road[k] = true; roadEdge[k] = o.Src; }
                    }
            }
            var free = new bool[N * N];
            int nFree = 0, nLimit = 0, nWater = 0, nSteep = 0, nOcc = 0, nRoad = 0;
            for (int k = 0; k < free.Length; k++)
            {
                free[k] = state[k] == S_FREE && occ[k] < 0;
                if (free[k]) nFree++;
                if (road[k]) nRoad++;
                else if (occ[k] >= 0) nOcc++;
                else if (state[k] == S_LIMIT) nLimit++;
                else if (state[k] == S_WATER) nWater++;
                else if (state[k] == S_STEEP) nSteep++;
            }

            // greedy largest rectangles
            Func<int, int, bool> fits = (h, w) =>
            {
                if (haveLot)
                {
                    float wm = w * cell, dm = h * cell;
                    return (lotM.x <= wm + 0.01f && lotM.y <= dm + 0.01f) || (lotM.y <= wm + 0.01f && lotM.x <= dm + 0.01f);
                }
                return h >= minCells && w >= minCells;
            };
            var label = new int[N * N];
            var rects = new List<object>();
            var frontRoads = obs.All.Where(o => o.Frontable).ToList();
            for (int k = 0; k < want; k++)
            {
                if (!BestRect(free, N, N, fits, out int br, out int bc, out int bh, out int bw)) break;
                for (int r = br; r < br + bh; r++)
                    for (int col = bc; col < bc + bw; col++) { free[r * N + col] = false; label[r * N + col] = k + 1; }
                var mn = g0 + new float2(bc, br) * cell;
                var mx = g0 + new float2(bc + bw, br + bh) * cell;
                var ctr = (mn + mx) * 0.5f;
                // roads in the ring of cells just outside the rectangle
                var sides = new Dictionary<string, object>();
                var edges = new List<string>();
                Action<string, int, int> ring = (side, r, col) =>
                {
                    if (r < 0 || r >= N || col < 0 || col >= N) return;
                    int i = r * N + col;
                    if (!road[i]) return;
                    sides[side] = sides.TryGetValue(side, out var v) ? (int)v + 1 : 1;
                    var id = Id(roadEdge[i]);
                    if (!edges.Contains(id) && edges.Count < 6) edges.Add(id);
                };
                for (int col = bc; col < bc + bw; col++) { ring("south", br - 1, col); ring("north", br + bh, col); }
                for (int r = br; r < br + bh; r++) { ring("west", r, bc - 1); ring("east", r, bc + bw); }
                var d = new Dictionary<string, object>
                {
                    { "rank", k + 1 }, { "min", XZ(mn) }, { "max", XZ(mx) }, { "centre", XZ(ctr) },
                    { "size", new List<object> { Math.Round(mx.x - mn.x), Math.Round(mx.y - mn.y) } },
                    { "lotCells", new List<object> { (int)math.floor((mx.x - mn.x) / 8f + 0.001f), (int)math.floor((mx.y - mn.y) / 8f + 0.001f) } },
                    { "area", Math.Round((mx.x - mn.x) * (mx.y - mn.y)) },
                    { "distance", Math.Round(math.distance(ctr, c), 1) },
                    { "touchesRoad", sides.Count > 0 },
                };
                if (sides.Count > 0) { d["roadSides"] = sides; d["roadEdges"] = edges; }
                else
                {
                    // nearest frontable road: where an access road could start
                    float best = float.MaxValue; Obst bo = null; float2 bp = 0;
                    foreach (var o in frontRoads)
                    {
                        if (o.Poly == null) continue;
                        foreach (var v in o.Poly)
                        {
                            var q = math.clamp(v, mn, mx);
                            float dd = math.distance(q, v);
                            if (dd < best) { best = dd; bo = o; bp = v; }
                        }
                    }
                    if (bo != null) d["nearestRoad"] = new Dictionary<string, object> { { "edge", Id(bo.Src) }, { "distance", Math.Round(best, 1) }, { "at", XZ(bp) } };
                }
                if (haveLot)
                {
                    float wm = mx.x - mn.x, dm = mx.y - mn.y;
                    d["lotFacing"] = new List<object>();
                    // which way the lot's front (width side) can face inside this rectangle
                    if (lotM.x <= wm + 0.01f && lotM.y <= dm + 0.01f) ((List<object>)d["lotFacing"]).AddRange(new object[] { "north", "south" });
                    if (lotM.y <= wm + 0.01f && lotM.x <= dm + 0.01f) ((List<object>)d["lotFacing"]).AddRange(new object[] { "east", "west" });
                }
                AddGrowableHits(d, obs, mn, mx);
                rects.Add(d);
            }

            var res = new Dictionary<string, object>
            {
                { "x0", Math.Round(g0.x, 1) }, { "z0", Math.Round(g0.y, 1) }, { "cell", cell }, { "cols", N }, { "rows", N },
                { "cells", new Dictionary<string, object> { { "free", nFree }, { "road", nRoad }, { "occupied", nOcc }, { "notOwned", nLimit }, { "water", nWater }, { "steep", nSteep } } },
                { "rects", rects },
                { "ms", sw.ElapsedMilliseconds },
                { "usage", "rects are axis-aligned, largest first (each excludes the earlier ones). size = [x extent, z extent] m; lotCells = how many 8 m lot cells fit. touchesRoad: a frontable road runs along that side (roadSides = cells of contact); otherwise nearestRoad says where an access road could start." },
            };
            if (haveLot) res["lot"] = new Dictionary<string, object> { { "prefab", lotName }, { "metres", XZ(lotM, 0) } };
            NoteGrowables(res, obs);
            if (QFlag(p, "grid"))
            {
                const string labels = "123456789ABCDEFGHIJK";
                var sb = new StringBuilder();
                for (int r = N - 1; r >= 0; r--)
                {
                    sb.Append(((int)(g0.y + (r + 0.5f) * cell)).ToString().PadLeft(7)).Append(' ');
                    for (int col = 0; col < N; col++)
                    {
                        int k = r * N + col;
                        char ch;
                        if (label[k] > 0) ch = labels[label[k] - 1];
                        else if (road[k]) ch = '#';
                        else if (occ[k] == K_ROAD || occ[k] == K_NET) ch = '=';
                        else if (occ[k] == K_ZONED) ch = 'Z';
                        else if (occ[k] == K_BUILDING) ch = 'B';
                        else if (occ[k] == K_OBJECT) ch = 'o';
                        else if (occ[k] == K_AREA) ch = 'a';
                        else if (state[k] == S_LIMIT) ch = ',';
                        else if (state[k] == S_WATER) ch = '~';
                        else if (state[k] == S_STEEP) ch = '^';
                        else ch = '.';
                        sb.Append(ch);
                    }
                    sb.Append('\n');
                }
                res["legend"] = "1-9,A.. = rect rank | . free | # road (frontable) | = other net/highway | Z zoned (growable) building | B building | o object | a area | , not owned | ~ water | ^ steep; row 0 = north, row label = z of cell centre, column 0 = x0";
                res["grid"] = sb.ToString();
            }
            return res;
        }

        /// <summary>Largest-area rectangle of true cells (rows = z, cols = x) that satisfies ok(height, width).</summary>
        static bool BestRect(bool[] free, int rows, int cols, Func<int, int, bool> ok, out int br, out int bc, out int bh, out int bw)
        {
            var hist = new int[cols];
            var stack = new int[cols + 1];
            long best = 0;
            br = bc = bh = bw = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int col = 0; col < cols; col++) hist[col] = free[r * cols + col] ? hist[col] + 1 : 0;
                int sp = 0;
                for (int col = 0; col <= cols; col++)
                {
                    int hc = col < cols ? hist[col] : 0;
                    while (sp > 0 && hist[stack[sp - 1]] >= hc)
                    {
                        int top = stack[--sp];
                        int h = hist[top];
                        int left = sp > 0 ? stack[sp - 1] + 1 : 0;
                        int w = col - left;
                        if (h > 0 && (long)h * w > best && ok(h, w)) { best = (long)h * w; br = r - h + 1; bc = left; bh = h; bw = w; }
                    }
                    if (col < cols) stack[sp++] = col;
                }
            }
            return best > 0;
        }

        static bool InsidePoly(float2[] poly, float2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                    (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                    inside = !inside;
            }
            return inside;
        }
    }
}
