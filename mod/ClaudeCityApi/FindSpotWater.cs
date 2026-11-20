using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using AreaNode = Game.Areas.Node;
using PlacementFlags = Game.Objects.PlacementFlags;
using GeoFlags = Game.Objects.GeometryFlags;

namespace ClaudeCityBridge
{
    /// <summary>
    /// /findspot (and so /place) pre-filters that mirror two more parts of the game's object validation:
    ///
    /// Water (Game.Objects.ValidationHelpers.CheckSurface, read with tools/ildump): the object's geometry bounds
    /// are sampled at the water-surface interval (WaterUtils.GetSampleInterval), depth &gt;= 0.2 m = wet.
    ///  * Shoreline objects (PlacementFlags.Shoreline, e.g. harbours, pumping stations, outlets): every sample on
    ///    the FRONT edge (local +z, the road side) must be dry ("In Water", unless Floating) and every sample on
    ///    the BACK edge must be wet ("No Water" / "Not On Shoreline").
    ///  * Floating / Underwater objects without OnGround: every sample wet.
    ///  * Everything else (unless GeometryFlags.CanSubmerge): every sample dry ("In Water").
    /// Sub-objects are validated the same way with their own placement flags, so a building whose quay sub-object
    /// is Shoreline/Floating needs water there too: those parts are checked as well ("waterParts").
    ///
    /// City limits (Game.Objects.ValidationHelpers.AreaIterator): every object of the preview (the building and
    /// each sub-object, plus sub-nets and sub-areas in their own validation) that intersects an unowned map tile
    /// gives "Exceeds City Limits". The true footprint (geometry bounds + sub-objects + sub-nets + sub-areas)
    /// grown by limitMargin (default 4 m) must therefore lie inside owned tiles.
    /// </summary>
    public partial class Api
    {
        internal enum WaterRule { None, Dry, Shoreline, Wet }

        /// <summary>A sub-object that needs water (object-local transform, its geometry bounds, its rule).</summary>
        internal sealed class WaterPart { public float2 Pos; public float2 Fwd; public Bounds2 Bounds; public WaterRule Rule; public bool Floating; public string Name; }

        const float WetDepth = 0.3f;  // what we require where the game needs >= 0.2 m (waves, rounding)
        const float DryDepth = 0.15f; // what we require where the game needs < 0.2 m

        static WaterRule RuleOf(PlacementFlags pl, GeoFlags gf, bool hasPlaceable)
        {
            if ((pl & PlacementFlags.Shoreline) != 0) return WaterRule.Shoreline;
            if ((pl & (PlacementFlags.Floating | PlacementFlags.Underwater)) != 0)
                return (pl & PlacementFlags.OnGround) != 0 ? WaterRule.None : WaterRule.Wet;
            if ((gf & GeoFlags.CanSubmerge) != 0) return WaterRule.None;
            return WaterRule.Dry; // also without placement flags (the game then skips the check; dry is the safe side)
        }

        /// <summary>Fills spec.Water, spec.Surface, spec.WaterParts and the true footprint spec.Extent of a prefab.</summary>
        void ReadWaterAndExtent(Entity prefab, LotSpec spec)
        {
            var geoB = spec.Standing ? spec.Upper : spec.Local;
            GeoFlags gf = 0;
            if (em.HasComponent<ObjectGeometryData>(prefab))
            {
                var g = em.GetComponentData<ObjectGeometryData>(prefab);
                gf = g.m_Flags;
                geoB = new Bounds2(g.m_Bounds.min.xz, g.m_Bounds.max.xz);
                if (spec.Standing) geoB = geoB | spec.Local; // CheckSurface: bounds | legs
            }
            spec.Surface = geoB;
            bool hasPl = em.HasComponent<PlaceableObjectData>(prefab);
            var pl = hasPl ? em.GetComponentData<PlaceableObjectData>(prefab).m_Flags : 0;
            spec.Water = RuleOf(pl, gf, hasPl);
            spec.Floating = (pl & PlacementFlags.Floating) != 0;

            var ext = geoB;
            const float reach = 400f; // ignore sub-elements absurdly far away (data errors)
            Action<float2> grow = v => { if (math.all(math.abs(v) < reach)) ext = ext | v; };
            try
            {
                if (em.HasBuffer<Game.Prefabs.SubObject>(prefab))
                {
                    var subs = em.GetBuffer<Game.Prefabs.SubObject>(prefab, true);
                    for (int i = 0; i < subs.Length; i++)
                    {
                        var so = subs[i];
                        if (so.m_Prefab == Entity.Null || !em.HasComponent<ObjectGeometryData>(so.m_Prefab)) continue;
                        var sg = em.GetComponentData<ObjectGeometryData>(so.m_Prefab);
                        if ((sg.m_Flags & GeoFlags.Marker) != 0) continue; // editor-only markers (spawn points etc.) are not created in game
                        foreach (var v in BaseCorners(so.m_Position.xz, so.m_Rotation, sg.m_Bounds)) grow(v);
                        if (!em.HasComponent<PlaceableObjectData>(so.m_Prefab) || so.m_Probability < 100 || spec.WaterParts.Count >= 16) continue;
                        var spl = em.GetComponentData<PlaceableObjectData>(so.m_Prefab).m_Flags;
                        var rule = RuleOf(spl, sg.m_Flags, true);
                        if (rule != WaterRule.Shoreline && rule != WaterRule.Wet) continue; // only parts that NEED water
                        var f = math.mul(so.m_Rotation, new float3(0, 0, 1)).xz;
                        spec.WaterParts.Add(new WaterPart
                        {
                            Pos = so.m_Position.xz, Fwd = math.normalizesafe(f, new float2(0, 1)), Rule = rule,
                            Bounds = new Bounds2(sg.m_Bounds.min.xz, sg.m_Bounds.max.xz),
                            Floating = (spl & PlacementFlags.Floating) != 0, Name = PrefabName(so.m_Prefab),
                        });
                    }
                }
                if (em.HasBuffer<Game.Prefabs.SubNet>(prefab))
                {
                    var nets = em.GetBuffer<Game.Prefabs.SubNet>(prefab, true);
                    for (int i = 0; i < nets.Length; i++)
                    {
                        var sn = nets[i];
                        float hw = sn.m_Prefab != Entity.Null && em.HasComponent<NetGeometryData>(sn.m_Prefab) ? em.GetComponentData<NetGeometryData>(sn.m_Prefab).m_DefaultWidth * 0.5f : 0f;
                        var cv = sn.m_Curve;
                        foreach (var v in new[] { cv.a.xz, cv.b.xz, cv.c.xz, cv.d.xz }) { grow(v - hw); grow(v + hw); }
                    }
                }
                if (em.HasBuffer<Game.Prefabs.SubArea>(prefab) && em.HasBuffer<Game.Prefabs.SubAreaNode>(prefab))
                {
                    var areas = em.GetBuffer<Game.Prefabs.SubArea>(prefab, true);
                    var nodes = em.GetBuffer<Game.Prefabs.SubAreaNode>(prefab, true);
                    for (int i = 0; i < areas.Length; i++)
                    {
                        var sa = areas[i];
                        if (sa.m_Prefab == Entity.Null || !em.HasComponent<AreaGeometryData>(sa.m_Prefab)) continue;
                        var at = em.GetComponentData<AreaGeometryData>(sa.m_Prefab).m_Type;
                        if (at == Game.Areas.AreaType.District || at == Game.Areas.AreaType.MapTile) continue;
                        for (int k = math.max(0, sa.m_NodeRange.x); k < math.min(nodes.Length, sa.m_NodeRange.y); k++) grow(nodes[k].m_Position.xz);
                    }
                }
            }
            catch (Exception e) { Log.Warn("findspot footprint " + spec.Name + ": " + e.Message); }
            spec.Extent = ext;
            spec.HasExtent = math.any(ext.min < geoB.min - 0.5f) || math.any(ext.max > geoB.max + 0.5f);
        }

        sealed partial class ObstSet
        {
            /// <summary>Map tiles near the area (CollectTiles): unowned ones as obstacles, owned ones for corner tests.</summary>
            public List<Obst> NativeTiles, OwnedTiles;
        }

        void CollectTiles(ObstSet set)
        {
            set.NativeTiles = new List<Obst>();
            set.OwnedTiles = new List<Obst>();
            var lo = set.AreaMin - 700f; var hi = set.AreaMax + 700f;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Areas.MapTile>(), ComponentType.ReadOnly<AreaNode>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var ents = q.ToEntityArray(Allocator.TempJob))
                foreach (var t in ents)
                {
                    var nodes = em.GetBuffer<AreaNode>(t, true);
                    if (nodes.Length < 3) continue;
                    var poly = new float2[nodes.Length];
                    float2 mn = float.MaxValue, mx = float.MinValue;
                    for (int i = 0; i < nodes.Length; i++) { poly[i] = nodes[i].m_Position.xz; mn = math.min(mn, poly[i]); mx = math.max(mx, poly[i]); }
                    if (mx.x < lo.x || mn.x > hi.x || mx.y < lo.y || mn.y > hi.y) continue;
                    var o = new Obst { Poly = poly, Min = mn, Max = mx, Kind = K_LIMITS, Src = t };
                    (em.HasComponent<Native>(t) ? set.NativeTiles : set.OwnedTiles).Add(o);
                }
        }

        /// <summary>The true footprint grown by spec.LimitMargin lies inside owned tiles (no unowned tile, not past the map edge).</summary>
        static bool InsideCityLimits(ObstSet obs, LotSpec spec, float2 origin, float2 fwd)
        {
            if (spec.LimitMargin < 0 || obs.OwnedTiles == null) return true;
            var b = spec.HasExtent ? spec.Extent : SurfaceOf(spec);
            float m = spec.LimitMargin;
            var r = MakeRect(origin, fwd, b, m, m, m);
            foreach (var t in obs.NativeTiles) if (Overlaps(ref r, t, 0f)) return false;
            foreach (var v in Corners(ref r))
            {
                bool inside = false;
                foreach (var t in obs.OwnedTiles)
                    if (v.x >= t.Min.x - 0.01f && v.x <= t.Max.x + 0.01f && v.y >= t.Min.y - 0.01f && v.y <= t.Max.y + 0.01f && InsidePoly(t.Poly, v)) { inside = true; break; }
                if (!inside) return false;
            }
            return true;
        }

        static Bounds2 SurfaceOf(LotSpec spec) => math.all(spec.Surface.min == 0) && math.all(spec.Surface.max == 0) ? spec.Local : spec.Surface;

        /// <summary>-1 when the water rules hold; else K_WATER (wet where it must be dry) or K_NOWATER.</summary>
        static int CheckWaterRules(TerrainProbe tp, LotSpec spec, float2 origin, float2 fwd)
        {
            float interval = math.max(1f, WaterUtils.GetSampleInterval(ref tp.W));
            var surf = SurfaceOf(spec);
            int why = -1;
            if (spec.Water == WaterRule.Shoreline || spec.Water == WaterRule.Wet)
                why = SurfaceRule(tp, spec.Water, spec.Floating, surf, origin, fwd, interval);
            else if (spec.Water == WaterRule.Dry)
            {
                // ProbeLot sampled the lot rect; a larger geometry (standing objects: the rotor) is sampled here
                // (CheckSurface: plain depth only with an ExclusiveGround collision mask; otherwise the water surface
                // must rise above the object's base, so a rotor over a lake next to a turbine on a bank is fine)
                var lb = spec.Local;
                float baseY = (spec.Mask & CollisionMask.ExclusiveGround) != 0 ? float.NaN : tp.Height(origin);
                if (math.any(surf.min < lb.min - 0.5f) || math.any(surf.max > lb.max + 0.5f))
                    why = SurfaceRule(tp, WaterRule.Dry, false, surf, origin, fwd, interval, baseY);
            }
            if (why >= 0) return why;
            var u = new float2(fwd.y, -fwd.x); // object local +x
            foreach (var part in spec.WaterParts)
            {
                var po = origin + u * part.Pos.x + fwd * part.Pos.y;
                var pf = math.normalizesafe(u * part.Fwd.x + fwd * part.Fwd.y, fwd);
                why = SurfaceRule(tp, part.Rule, part.Floating, part.Bounds, po, pf, interval);
                if (why >= 0) return why;
            }
            return -1;
        }

        /// <summary>
        /// CheckSurface sampling of one object: nx = ceil(width / interval) points at (i + 0.5) / nx along the front
        /// edge (local max z) and the back edge (local min z); for grid rules ny = ceil(depth / interval) points
        /// between them.
        /// </summary>
        static int SurfaceRule(TerrainProbe tp, WaterRule rule, bool floating, Bounds2 b, float2 origin, float2 fwd, float interval, float baseY = float.NaN)
        {
            if (rule == WaterRule.None) return -1;
            var u = new float2(fwd.y, -fwd.x);
            var size = b.max - b.min;
            int nx = math.clamp((int)math.ceil(size.x / interval), 1, 64), ny = math.clamp((int)math.ceil(size.y / interval), 1, 64);
            float2 W(float lx, float lz) => origin + u * lx + fwd * lz;
            var a = W(b.min.x, b.max.y); var bb = W(b.max.x, b.max.y); // front edge
            var d = W(b.min.x, b.min.y); var c = W(b.max.x, b.min.y);  // back edge
            for (int i = 0; i < nx; i++)
            {
                float t = (i + 0.5f) / nx;
                var front = math.lerp(a, bb, t); var back = math.lerp(d, c, t);
                if (rule == WaterRule.Shoreline)
                {
                    if (!floating && tp.Depth(front) >= DryDepth) return K_WATER;
                    if (tp.Depth(back) < WetDepth) return K_NOWATER;
                    continue;
                }
                for (int j = 0; j < ny; j++)
                {
                    var pt = math.lerp(front, back, (j + 0.5f) / ny);
                    float dep = tp.Depth(pt);
                    if (!float.IsNaN(baseY)) dep = math.min(dep, tp.Height(pt) + dep - baseY); // water surface above the base
                    if (rule == WaterRule.Wet && dep < WetDepth) return K_NOWATER;
                    if (rule == WaterRule.Dry && dep >= DryDepth) return K_WATER;
                }
            }
            return -1;
        }

        static Bounds2? ParseBox(object o)
        {
            var v = new List<float>();
            if (o is List<object> l) { foreach (var x in l) if (x is double dd) v.Add((float)dd); }
            else if (o is string s)
                foreach (var part in s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) v.Add(f);
            if (v.Count != 4) return null;
            return new Bounds2(new float2(math.min(v[0], v[2]), math.min(v[1], v[3])), new float2(math.max(v[0], v[2]), math.max(v[1], v[3])));
        }

        void AddSpecNotes(Dictionary<string, object> r, LotSpec spec)
        {
            if (spec.Prefab == Entity.Null) return;
            switch (spec.Water)
            {
                case WaterRule.Shoreline: r["waterRule"] = "shoreline: the front edge (road side) must be dry and the whole back edge in water (depth >= " + WetDepth + " m), as the game's CheckSurface; no steepness filter"; break;
                case WaterRule.Wet: r["waterRule"] = "floating: the whole object must be in water"; break;
                case WaterRule.None: r["waterRule"] = "none (the game does not check water for this object)"; break;
                default: r["waterRule"] = "dry"; break;
            }
            if (spec.WaterParts.Count > 0)
                r["waterParts"] = spec.WaterParts.Select(w => (object)new Dictionary<string, object>
                {
                    { "prefab", w.Name }, { "rule", w.Rule == WaterRule.Shoreline ? "shoreline" : "floating" }, { "at", XZ(w.Pos) },
                }).ToList();
            if (spec.HasExtent)
                r["footprint"] = new Dictionary<string, object>
                {
                    { "local", new List<object> { Math.Round(spec.Extent.min.x, 1), Math.Round(spec.Extent.min.y, 1), Math.Round(spec.Extent.max.x, 1), Math.Round(spec.Extent.max.y, 1) } },
                    { "geometry", new List<object> { Math.Round(spec.Surface.min.x, 1), Math.Round(spec.Surface.min.y, 1), Math.Round(spec.Surface.max.x, 1), Math.Round(spec.Surface.max.y, 1) } },
                    { "note", "sub-objects/sub-nets/sub-areas reach beyond the building's geometry (object-local x0,z0,x1,z1; +z = front): the city-limits test uses this footprint grown by limitMargin" },
                };
            r["limitMargin"] = spec.LimitMargin;
        }
    }
}
