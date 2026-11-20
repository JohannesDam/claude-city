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
using Unity.Mathematics;
using NetEdge = Game.Net.Edge;
using NetNode = Game.Net.Node;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Edge selection shared by /roadupgrade, /upgrade and /bulldoze (ids with partial apply, or
    /// geometry: box / polyline), GET /nets paging + per-segment upgrades, GET /nodes.
    /// </summary>
    public partial class Api
    {
        // ------------------------------------------------------------------ edge selection

        public sealed class EdgeSel
        {
            public readonly List<Entity> Edges = new List<Entity>();
            public ApiError Error;
            public int ById, ByGeometry, FilteredOut;
            public readonly List<object> Missing = new List<object>();
            public readonly List<object> NotTopLevel = new List<object>();
            /// <summary>Polyline-selected edges whose start->end runs against the polyline direction.</summary>
            public readonly HashSet<Entity> Reversed = new HashSet<Entity>();
            public string Geometry;

            public Dictionary<string, object> Report()
            {
                var r = new Dictionary<string, object> { { "selected", Edges.Count }, { "byId", ById } };
                if (Geometry != null) { r["byGeometry"] = ByGeometry; r["geometry"] = Geometry; }
                if (FilteredOut > 0) r["filteredOutByPrefab"] = FilteredOut;
                if (Missing.Count > 0) r["missing"] = Missing;
                if (NotTopLevel.Count > 0) r["notTopLevel"] = NotTopLevel;
                if (Missing.Count > 0) r["note"] = "missing ids are stale (merged/split/replaced) or wrong; they were skipped. Re-select with box/polyline or GET /nets.";
                return r;
            }
        }

        static bool TryBox(object o, out float2 min, out float2 max)
        {
            min = max = 0;
            var l = o as List<object>;
            if (l == null) return false;
            float[] v;
            if (l.Count == 4 && l.All(x => x is double)) v = l.Select(x => (float)(double)x).ToArray();
            else if (l.Count == 2 && l[0] is List<object> p0 && l[1] is List<object> p1 && p0.Count >= 2 && p1.Count >= 2 && p0[0] is double && p0[1] is double && p1[0] is double && p1[1] is double)
                v = new[] { (float)(double)p0[0], (float)(double)p0[1], (float)(double)p1[0], (float)(double)p1[1] };
            else return false;
            min = new float2(math.min(v[0], v[2]), math.min(v[1], v[3]));
            max = new float2(math.max(v[0], v[2]), math.max(v[1], v[3]));
            return true;
        }

        static List<float2> ReadPolyline(object o)
        {
            var l = o as List<object>;
            if (l == null) return null;
            var res = new List<float2>();
            foreach (var x in l)
            {
                var pt = x as List<object>;
                if (pt == null || pt.Count < 2 || !(pt[0] is double) || !(pt[1] is double)) return null;
                res.Add(new float2((float)(double)pt[0], (float)(double)pt[1]));
            }
            return res.Count >= 2 ? res : null;
        }

        /// <summary>Distance from p to a polyline (xz); also the arc length of the closest point and that segment's direction.</summary>
        static float PolyDist(float2 p, List<float2> poly, out float along, out float2 segDir)
        {
            float best = float.MaxValue, acc = 0; along = 0; segDir = 0;
            for (int i = 0; i + 1 < poly.Count; i++)
            {
                var a = poly[i]; var ab = poly[i + 1] - a;
                float len = math.length(ab);
                float t = len > 1e-4f ? math.clamp(math.dot(p - a, ab) / (len * len), 0f, 1f) : 0f;
                float d = math.distance(p, a + ab * t);
                if (d < best) { best = d; along = acc + t * len; segDir = len > 1e-4f ? ab / len : 0; }
                acc += len;
            }
            return best;
        }

        /// <summary>Edge runs along the polyline: 5 interior samples within tol and roughly parallel (30 deg) at the middle.</summary>
        static bool AlongPolyline(Bezier4x3 cv, List<float2> poly, float tol, out float along, out bool reversed)
        {
            along = 0; reversed = false;
            foreach (var t in new[] { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f })
                if (PolyDist(MathUtils.Position(cv, t).xz, poly, out _, out _) > tol) return false;
            PolyDist(MathUtils.Position(cv, 0.5f).xz, poly, out along, out var dir);
            var tan = math.normalizesafe(MathUtils.Tangent(cv, 0.5f).xz);
            if (math.lengthsq(tan) < 0.5f || math.lengthsq(dir) < 0.5f) return true;
            float dot = math.dot(tan, dir);
            reversed = dot < 0;
            return math.abs(dot) >= 0.866f;
        }

        static string FlipSide(string side) => side == "left" ? "right" : side == "right" ? "left" : side;

        /// <summary>"A|B|C" -> tokens; null means any ("", null or "*").</summary>
        static string[] NameTokens(string filter)
        {
            if (string.IsNullOrEmpty(filter)) return null;
            var t = filter.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            return t.Length == 0 || t.Contains("*") ? null : t;
        }

        /// <summary>Case-insensitive substring match against any token (null tokens = any).</summary>
        static bool NameMatches(string name, string[] tokens)
        {
            if (tokens == null) return true;
            name = name ?? "";
            foreach (var t in tokens) if (name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static bool InBox(float2 p, float2 min, float2 max) => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y;

        static bool BoxMatch(Bezier4x3 cv, float2 min, float2 max, string mode)
        {
            if (mode == "mid") return InBox(MathUtils.Position(cv, 0.5f).xz, min, max);
            bool any = false, all = true;
            for (int i = 0; i <= 8; i++)
            {
                bool inside = InBox(MathUtils.Position(cv, i / 8f).xz, min, max);
                any |= inside; all &= inside;
            }
            return mode == "all" ? all : any;
        }

        /// <summary>
        /// Selects top-level net edges from a request body:
        ///   edges:[ids]  (stale/unknown ids are skipped and listed in "missing"; strict:true fails instead)
        ///   box:[x0,z0,x1,z1] (boxMatch: mid|any|all, default mid = curve midpoint inside)
        ///   polyline:[[x,z],...], tolerance (m, default 12): edges running along the line
        ///   prefabFilter: name substring for the geometry selectors ("*" = any); maxEdges (default 500)
        /// <paramref name="kind"/> restricts geometry selection (e.g. roads only); ids are not kind-filtered.
        /// </summary>
        public EdgeSel SelectEdges(Args a, Func<Entity, bool> kind, bool needPrefabFilterForGeometry)
        {
            var sel = new EdgeSel();
            var seen = new HashSet<Entity>();
            bool strict = a.B("strict");
            var ids = a.List("edges");
            bool hasBox = a.Has("box"), hasPoly = a.Has("polyline");
            if (ids == null && !hasBox && !hasPoly)
            {
                sel.Error = new ApiError("select edges with edges:[ids], box:[x0,z0,x1,z1] or polyline:[[x,z],...] (+tolerance, prefabFilter)");
                return sel;
            }
            if (ids != null)
            {
                var eq = EdgeQuery();
                foreach (var id in ids)
                {
                    var e = Resolve(id, eq);
                    if (e == Entity.Null || !IsLive(e) || !em.HasComponent<NetEdge>(e))
                    {
                        if (strict) { sel.Error = new ApiError("edge not found: " + id + " (stale id? use box/polyline or GET /nets)"); return sel; }
                        sel.Missing.Add(id);
                        continue;
                    }
                    if (em.HasComponent<Owner>(e))
                    {
                        if (strict) { sel.Error = new ApiError("edge " + id + " belongs to a building/other object"); return sel; }
                        sel.NotTopLevel.Add(id);
                        continue;
                    }
                    if (seen.Add(e)) { sel.Edges.Add(e); sel.ById++; }
                }
            }
            if (!hasBox && !hasPoly) return sel;

            var pf = a.Str("prefabFilter") ?? a.Str("nets");
            if (string.IsNullOrEmpty(pf) && needPrefabFilterForGeometry)
            {
                sel.Error = new ApiError("box/polyline selection here needs prefabFilter / nets (prefab name substrings separated by |, e.g. \"Small Road|Medium Road\", or \"*\" for any net)");
                return sel;
            }
            var tokens = NameTokens(pf);
            bool anyPrefab = tokens == null;
            float2 bmin = 0, bmax = 0;
            string boxMode = a.Str("boxMatch", "mid").ToLowerInvariant();
            if (boxMode != "mid" && boxMode != "any" && boxMode != "all") boxMode = "mid";
            if (hasBox && !TryBox(a.Raw("box"), out bmin, out bmax)) { sel.Error = new ApiError("box must be [x0,z0,x1,z1] or [[x0,z0],[x1,z1]]"); return sel; }
            List<float2> poly = null;
            float tol = math.clamp(a.F("tolerance", 12f), 0.5f, 200f);
            if (hasPoly && (poly = ReadPolyline(a.Raw("polyline"))) == null) { sel.Error = new ApiError("polyline must be [[x,z],[x,z],...] with at least 2 points"); return sel; }
            int maxEdges = math.clamp(a.I("maxEdges", 500), 1, 20000);
            var desc = new List<string>();
            if (hasBox) desc.Add("box " + boxMode + " [" + bmin.x + "," + bmin.y + "," + bmax.x + "," + bmax.y + "]");
            if (hasPoly) desc.Add("polyline " + poly.Count + " pts tol " + tol + " m");
            if (!anyPrefab) desc.Add("prefab~\"" + pf + "\"");
            sel.Geometry = string.Join(", ", desc);

            var geo = new List<KeyValuePair<float, Entity>>();
            var rev = new HashSet<Entity>();
            using (var arr = EdgeQuery().ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (seen.Contains(e) || (kind != null && !kind(e))) continue;
                    var cv = em.GetComponentData<Curve>(e).m_Bezier;
                    if (hasBox && !BoxMatch(cv, bmin, bmax, boxMode)) continue;
                    float along = 0; bool reversed = false;
                    if (hasPoly && !AlongPolyline(cv, poly, tol, out along, out reversed)) continue;
                    if (!anyPrefab && !NameMatches(PrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab), tokens)) { sel.FilteredOut++; continue; }
                    geo.Add(new KeyValuePair<float, Entity>(along, e));
                    if (reversed) rev.Add(e);
                }
            if (geo.Count > maxEdges)
            {
                sel.Error = new ApiError("geometry selection matched " + geo.Count + " edges (> maxEdges " + maxEdges + "); narrow it or raise maxEdges");
                return sel;
            }
            if (hasPoly) geo.Sort((x, y) => x.Key.CompareTo(y.Key)); // in order along the polyline
            foreach (var kv in geo)
                if (seen.Add(kv.Value)) { sel.Edges.Add(kv.Value); sel.ByGeometry++; if (rev.Contains(kv.Value)) sel.Reversed.Add(kv.Value); }
            return sel;
        }

        /// <summary>Geometry kind filter: roads only (Road component).</summary>
        bool IsRoadEdge(Entity e) => em.HasComponent<Road>(e);

        /// <summary>Geometry kind filter for replacing with <paramref name="target"/>: edges sharing a required net layer.</summary>
        Func<Entity, bool> SameLayerAs(Entity target)
        {
            if (!em.HasComponent<NetData>(target)) return e => true;
            var layers = em.GetComponentData<NetData>(target).m_RequiredLayers;
            bool road = em.HasComponent<RoadData>(target);
            return e =>
            {
                if (road != em.HasComponent<Road>(e)) return false;
                var p = em.GetComponentData<PrefabRef>(e).m_Prefab;
                return em.HasComponent<NetData>(p) && (em.GetComponentData<NetData>(p).m_RequiredLayers & layers) != 0;
            };
        }

        // ------------------------------------------------------------------ upgrade names

        sealed class UpgradeSig { public string Name; public CompositionFlags.Side Side; public CompositionFlags.General Middle, Whole; }

        /// <summary>Edge upgrade prefabs as (side / middle / whole-edge) flag signatures, one per distinct signature.</summary>
        List<UpgradeSig> EdgeUpgradeSigs()
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<PlaceableNetData>());
            var cand = new List<KeyValuePair<UpgradeSig, bool>>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (!IsNetUpgradePrefab(e)) continue;
                    var set = em.GetComponentData<PlaceableNetData>(e).m_SetUpgradeFlags;
                    if ((set.m_General & GeneralNodeMask) != 0) continue;
                    if (((set.m_Left | set.m_Right) & SideDirectionalMask) != 0) continue;
                    var s = new UpgradeSig
                    {
                        Name = PrefabName(e),
                        Side = set.m_Right != 0 ? set.m_Right : set.m_Left,
                        Middle = set.m_General & GeneralOptionMask,
                        Whole = set.m_General & ~GeneralOptionMask,
                    };
                    if (s.Side == 0 && s.Middle == 0 && s.Whole == 0) continue;
                    cand.Add(new KeyValuePair<UpgradeSig, bool>(s, set.m_Left == 0 || set.m_Right == 0));
                }
            // one name per signature: prefer the single-side variant ("Bikelane" over "Bikelane - BothSides"), then the shortest name
            return cand.GroupBy(c => (uint)c.Key.Side + "|" + (uint)c.Key.Middle + "|" + (uint)c.Key.Whole)
                .Select(g => g.OrderByDescending(c => c.Value).ThenBy(c => c.Key.Name.Length).ThenBy(c => c.Key.Name, StringComparer.Ordinal).First().Key)
                .ToList();
        }

        /// <summary>{"Trees":["left","right","middle"],"Lighting":["edge"]} from an edge's Upgraded flags (left/right relative to start->end).</summary>
        static Dictionary<string, object> UpgradeNames(CompositionFlags u, List<UpgradeSig> sigs)
        {
            var r = new Dictionary<string, object>();
            foreach (var s in sigs)
            {
                var sides = new List<object>();
                if (s.Side != 0)
                {
                    if ((u.m_Left & s.Side) == s.Side) sides.Add("left");
                    if ((u.m_Right & s.Side) == s.Side) sides.Add("right");
                }
                if (s.Middle != 0 && (u.m_General & s.Middle) == s.Middle) sides.Add("middle");
                if (s.Whole != 0 && (u.m_General & s.Whole) == s.Whole) sides.Add("edge");
                if (sides.Count > 0) r[s.Name] = sides;
            }
            return r;
        }

        static Entity ParseId(object id)
        {
            var parts = (id?.ToString() ?? "").Split(':');
            return parts.Length == 2 && int.TryParse(parts[0], out var i) && int.TryParse(parts[1], out var v) ? new Entity { Index = i, Version = v } : Entity.Null;
        }

        /// <summary>Upgrades, elevation and tunnel/elevated state of one edge, added to its /nets entry.</summary>
        void AddEdgeDetails(Entity e, Dictionary<string, object> d, List<UpgradeSig> sigs)
        {
            if (e == Entity.Null || !em.Exists(e)) return;
            var u = em.HasComponent<Upgraded>(e) ? em.GetComponentData<Upgraded>(e).m_Flags : default(CompositionFlags);
            d["upgrades"] = UpgradeNames(u, sigs);
            if (!IsEmpty(u)) d["upgradeFlags"] = FlagsJson(u);
            if (em.HasComponent<Composition>(e))
            {
                var ce = em.GetComponentData<Composition>(e).m_Edge;
                if (ce != Entity.Null && em.HasComponent<NetCompositionData>(ce))
                {
                    var g = em.GetComponentData<NetCompositionData>(ce).m_Flags.m_General;
                    if ((g & CompositionFlags.General.Elevated) != 0) d["elevated"] = true;
                    if ((g & CompositionFlags.General.Tunnel) != 0) d["tunnel"] = true;
                }
            }
            if (em.HasComponent<Game.Net.Elevation>(e))
            {
                var el = em.GetComponentData<Game.Net.Elevation>(e).m_Elevation;
                d["elevation"] = Math.Round((el.x + el.y) * 0.5f, 1);
            }
        }

        // ------------------------------------------------------------------ GET /nets (paged)

        static int QI(Pending p, string k, int def) => (int)math.round(p.QF(k, def));

        /// <summary>
        /// GET /nets?x&amp;z&amp;radius=500&amp;filter=&amp;offset=0&amp;limit=200 — segments sorted by distance, with
        /// total/truncated/nextOffset paging and per-segment upgrades (left/right relative to start->end).
        /// </summary>
        object NetsPaged(Pending p)
        {
            var l = NetList(p.QF("x", 0), p.QF("z", 0), p.QF("radius", 500), p.Q("filter"));
            int offset = math.max(0, QI(p, "offset", 0));
            int limit = math.clamp(QI(p, "limit", 200), 1, 5000);
            var page = l.Skip(offset).Take(limit).ToList();
            var sigs = EdgeUpgradeSigs();
            foreach (var d in page) AddEdgeDetails(ParseId(d["id"]), d, sigs);
            bool truncated = offset + page.Count < l.Count;
            var r = new Dictionary<string, object>
            {
                { "count", l.Count }, { "total", l.Count }, { "offset", offset }, { "limit", limit },
                { "returned", page.Count }, { "truncated", truncated }, { "segments", page },
            };
            if (truncated) r["nextOffset"] = offset + page.Count;
            return r;
        }

        // ------------------------------------------------------------------ GET /nodes

        /// <summary>Node composition flags as the net tool sees them (OR over the connected edges' node ends).</summary>
        CompositionFlags NodeCompositionFlags(Entity node)
        {
            var acc = default(CompositionFlags);
            if (!em.HasBuffer<ConnectedEdge>(node)) return acc;
            var ces = em.GetBuffer<ConnectedEdge>(node, true);
            for (int i = 0; i < ces.Length; i++)
            {
                var ed = ces[i].m_Edge;
                if (!em.HasComponent<NetEdge>(ed) || !em.HasComponent<Composition>(ed)) continue;
                var edge = em.GetComponentData<NetEdge>(ed);
                var comp = em.GetComponentData<Composition>(ed);
                var nc = edge.m_Start == node ? comp.m_StartNode : edge.m_End == node ? comp.m_EndNode : Entity.Null;
                if (nc != Entity.Null && em.HasComponent<NetCompositionData>(nc)) acc |= em.GetComponentData<NetCompositionData>(nc).m_Flags;
            }
            return acc;
        }

        Dictionary<string, object> NodeJson(Entity n, float2 c)
        {
            var np = em.GetComponentData<NetNode>(n).m_Position;
            var d = new Dictionary<string, object> { { "id", Id(n) }, { "pos", F2(np) }, { "degree", Degree(n) }, { "distance", Math.Round(math.distance(np.xz, c), 1) } };
            if (em.HasComponent<PrefabRef>(n)) d["prefab"] = PrefabName(em.GetComponentData<PrefabRef>(n).m_Prefab);
            var acc = NodeCompositionFlags(n);
            var up = em.HasComponent<Upgraded>(n) ? em.GetComponentData<Upgraded>(n).m_Flags : default(CompositionFlags);
            var g = acc.m_General | up.m_General;
            d["trafficLights"] = (acc.m_General & CompositionFlags.General.TrafficLights) != 0;
            d["allWayStop"] = (g & CompositionFlags.General.AllWayStop) != 0;
            if ((acc.m_General & CompositionFlags.General.Roundabout) != 0) d["roundabout"] = true;
            if ((acc.m_General & CompositionFlags.General.LevelCrossing) != 0) d["levelCrossing"] = true;
            if (!IsEmpty(up)) d["upgradeFlags"] = FlagsJson(up);
            if (em.HasComponent<Game.Net.Elevation>(n))
            {
                var el = em.GetComponentData<Game.Net.Elevation>(n).m_Elevation;
                d["elevation"] = Math.Round((el.x + el.y) * 0.5f, 1);
            }
            var edges = new List<object>();
            if (em.HasBuffer<ConnectedEdge>(n))
            {
                var ces = em.GetBuffer<ConnectedEdge>(n, true);
                for (int i = 0; i < ces.Length; i++)
                {
                    var ed = ces[i].m_Edge;
                    if (!em.HasComponent<NetEdge>(ed) || em.HasComponent<Owner>(ed)) continue;
                    edges.Add(new Dictionary<string, object> { { "id", Id(ed) }, { "prefab", PrefabName(em.GetComponentData<PrefabRef>(ed).m_Prefab) } });
                }
            }
            d["edges"] = edges;
            return d;
        }

        /// <summary>GET /nodes?x&amp;z&amp;radius=200&amp;minDegree=3&amp;filter=&amp;offset=0&amp;limit=200 — intersections with traffic control state.</summary>
        object Nodes(Pending p)
        {
            var c = new float2(p.QF("x", 0), p.QF("z", 0));
            float radius = p.QF("radius", 200);
            int minDegree = math.max(0, QI(p, "minDegree", 3));
            var filter = p.Q("filter");
            int offset = math.max(0, QI(p, "offset", 0));
            int limit = math.clamp(QI(p, "limit", 200), 1, 5000);
            var found = new List<KeyValuePair<float, Entity>>();
            using (var arr = NodeQuery().ToEntityArray(Allocator.Temp))
                foreach (var n in arr)
                {
                    var np = em.GetComponentData<NetNode>(n).m_Position;
                    float d = math.distance(np.xz, c);
                    if (d > radius || Degree(n) < minDegree) continue;
                    if (!string.IsNullOrEmpty(filter) && (!em.HasComponent<PrefabRef>(n) || (PrefabName(em.GetComponentData<PrefabRef>(n).m_Prefab) ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    found.Add(new KeyValuePair<float, Entity>(d, n));
                }
            found.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Value.Index.CompareTo(y.Value.Index));
            var page = found.Skip(offset).Take(limit).Select(kv => (object)NodeJson(kv.Value, c)).ToList();
            bool truncated = offset + page.Count < found.Count;
            var r = new Dictionary<string, object>
            {
                { "total", found.Count }, { "offset", offset }, { "limit", limit }, { "returned", page.Count }, { "truncated", truncated }, { "nodes", page },
            };
            if (truncated) r["nextOffset"] = offset + page.Count;
            return r;
        }
    }
}
