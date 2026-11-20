using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Notifications;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using NetEdge = Game.Net.Edge;
using NetNode = Game.Net.Node;
using ObjTransform = Game.Objects.Transform;
using GeoFlags = Game.Objects.GeometryFlags;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Which existing buildings a build would remove, and what blocks it.
    ///
    /// How the game decides (Game.dll, read with tools/ildump):
    ///  * Growable (zoned, spawnable, non-signature) building prefabs carry ObjectGeometryData flags
    ///    Overridable | DeleteOverridden (BuildingInitializeSystem); service/signature buildings do not.
    ///  * Validation (Game.Objects/Game.Net ValidationHelpers.ObjectIterator.CheckOverlap): a new object or
    ///    net that overlaps such a building produces ErrorData { OverlapExisting, severity Warning,
    ///    m_TempEntity = our preview entity, m_PermanentEntity = the growable } instead of an Error. So the
    ///    build validates (errors = 0) and the growable is removed. Anything else that overlaps is
    ///    severity Error with m_PermanentEntity = the blocker.
    ///  * ValidationSystem.ProcessValidationResultsJob turns each ErrorData into a notification icon:
    ///    Owner = the temp entity, Target = m_PermanentEntity, PrefabRef = the error notification
    ///    ("Overlap Existing", ...), Icon.m_Priority = Warning (100) / Error (200).
    ///  * After apply, growables whose lot cells are no longer valid get Condemned (Game.Buildings.
    ///    ZoneCheckSystem) and CondemnedBuildingSystem deletes them a little later.
    ///
    /// So during the preview we read the icons the game itself created: Warning icons whose Target is an
    /// Overridable+DeleteOverridden object are "wouldDemolish" (via "preview"); Error/Warning icons with
    /// any other Target are "conflicts". As a cross-check (and for /findspot, /freeland, which have no
    /// preview) the same overlap is computed geometrically: growable lots (ObjectGeometryData bounds =
    /// lot minus 0.2 m per side) against the new footprint (via "geometry").
    /// </summary>
    public partial class Api
    {
        const int K_ZONED = 8; // obstacle kind for growable lots (KindNames[8] = "zonedBuilding", FindSpot.cs)
        const GeoFlags OverrideDelete = GeoFlags.Overridable | GeoFlags.DeleteOverridden;

        // ------------------------------------------------------------------ classification

        static bool RemovedWhenOverlapped(ObjectGeometryData g) => (g.m_Flags & OverrideDelete) == OverrideDelete;

        /// <summary>True for objects the game deletes instead of failing when a build overlaps them (growables).</summary>
        bool RemovedWhenOverlapped(Entity e)
        {
            if (e == Entity.Null || !em.Exists(e) || !em.HasComponent<PrefabRef>(e)) return false;
            var pf = em.GetComponentData<PrefabRef>(e).m_Prefab;
            return em.HasComponent<ObjectGeometryData>(pf) && RemovedWhenOverlapped(em.GetComponentData<ObjectGeometryData>(pf));
        }

        string DemolishKind(Entity e)
        {
            var pf = em.HasComponent<PrefabRef>(e) ? em.GetComponentData<PrefabRef>(e).m_Prefab : Entity.Null;
            if (pf != Entity.Null && em.HasComponent<SpawnableBuildingData>(pf)) return "zoned";
            if (pf != Entity.Null && em.HasComponent<ServiceObjectData>(pf)) return "service";
            return "other";
        }

        /// <summary>Coarse kind of any entity, for conflict reports.</summary>
        string EntityKind(Entity e)
        {
            if (!em.Exists(e)) return "gone";
            var pf = em.HasComponent<PrefabRef>(e) ? em.GetComponentData<PrefabRef>(e).m_Prefab : Entity.Null;
            if (em.HasComponent<Building>(e))
                return pf != Entity.Null && em.HasComponent<SpawnableBuildingData>(pf) ? "zoned"
                    : pf != Entity.Null && em.HasComponent<ServiceObjectData>(pf) ? "service" : "building";
            if (em.HasComponent<NetEdge>(e)) return em.HasComponent<Road>(e) ? "road" : "net";
            if (em.HasComponent<NetNode>(e)) return "node";
            if (em.HasComponent<Game.Objects.Tree>(e)) return "tree";
            if (em.HasComponent<Game.Areas.Area>(e)) return "area";
            if (em.HasComponent<ObjTransform>(e)) return "object";
            return "other";
        }

        Entity TopOwner(Entity e)
        {
            for (int i = 0; i < 8 && e != Entity.Null && em.HasComponent<Owner>(e); i++)
            {
                var o = em.GetComponentData<Owner>(e).m_Owner;
                if (o == Entity.Null || !em.Exists(o) || !em.HasComponent<PrefabRef>(o)) break;
                e = o;
            }
            return e;
        }

        object EntityPos(Entity e)
        {
            if (em.HasComponent<ObjTransform>(e)) return F2(em.GetComponentData<ObjTransform>(e).m_Position);
            if (em.HasComponent<Curve>(e)) return F2(MathUtils.Position(em.GetComponentData<Curve>(e).m_Bezier, 0.5f));
            if (em.HasComponent<NetNode>(e)) return F2(em.GetComponentData<NetNode>(e).m_Position);
            return null;
        }

        Dictionary<string, object> DemolishItem(Entity e)
        {
            var d = new Dictionary<string, object> { { "id", Id(e) } };
            if (!em.Exists(e)) return d;
            var pf = em.HasComponent<PrefabRef>(e) ? em.GetComponentData<PrefabRef>(e).m_Prefab : Entity.Null;
            d["prefab"] = PrefabName(pf);
            d["kind"] = DemolishKind(e);
            d["center"] = EntityPos(e);
            if (pf != Entity.Null && em.HasComponent<SpawnableBuildingData>(pf))
            {
                var sb = em.GetComponentData<SpawnableBuildingData>(pf);
                d["zone"] = PrefabName(sb.m_ZonePrefab);
                d["level"] = (int)sb.m_Level;
            }
            if (em.HasBuffer<Renter>(e)) d["renters"] = em.GetBuffer<Renter>(e, true).Length;
            if (em.HasComponent<Abandoned>(e)) d["state"] = "abandoned";
            else if (em.HasComponent<Condemned>(e)) d["state"] = "condemned";
            else if (em.HasComponent<Destroyed>(e)) d["state"] = "destroyed";
            return d;
        }

        Dictionary<string, object> Ref(Entity e)
        {
            var pf = em.Exists(e) && em.HasComponent<PrefabRef>(e) ? em.GetComponentData<PrefabRef>(e).m_Prefab : Entity.Null;
            return new Dictionary<string, object> { { "id", Id(e) }, { "prefab", PrefabName(pf) }, { "kind", EntityKind(e) } };
        }

        // ------------------------------------------------------------------ preview snapshot (game signal)

        sealed class PreviewSnapshot
        {
            public int Temps;
            public readonly List<Entity> Demolish = new List<Entity>();
            public readonly List<Dictionary<string, object>> Conflicts = new List<Dictionary<string, object>>();
            public readonly List<string> ConflictKeys = new List<string>();
        }

        /// <summary>
        /// Reads the validation icons of the current tool preview. Must run while the preview (Temp
        /// entities) exists. Item dictionaries are cached by entity so they survive the entity's removal.
        /// </summary>
        EntityQuery snapTempQ, snapIconQ; // created once per plugin load (snapshots run every preview frame)
        bool snapQueries;

        PreviewSnapshot SnapshotPreview(Dictionary<Entity, Dictionary<string, object>> itemCache)
        {
            if (!snapQueries)
            {
                snapTempQ = em.CreateEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.Exclude<Deleted>());
                snapIconQ = em.CreateEntityQuery(ComponentType.ReadOnly<Icon>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>());
                snapQueries = true;
            }
            var s = new PreviewSnapshot();
            s.Temps = snapTempQ.CalculateEntityCount();
            if (s.Temps == 0) return s;
            var q = snapIconQ;
            var seenD = new HashSet<Entity>();
            var seenC = new HashSet<string>();
            using (var icons = q.ToEntityArray(Allocator.Temp))
            using (var owners = q.ToComponentDataArray<Owner>(Allocator.Temp))
            {
                for (int i = 0; i < icons.Length; i++)
                {
                    var owner = owners[i].m_Owner;
                    if (owner == Entity.Null || !em.Exists(owner) || !em.HasComponent<Temp>(owner)) continue; // not from the preview
                    var icon = icons[i];
                    var ic = em.GetComponentData<Icon>(icon);
                    if (ic.m_Priority < IconPriority.Warning) continue;
                    var target = em.HasComponent<Target>(icon) ? em.GetComponentData<Target>(icon).m_Target : Entity.Null;
                    bool live = target != Entity.Null && em.Exists(target) && !em.HasComponent<Deleted>(target);
                    string err = PrefabName(em.GetComponentData<PrefabRef>(icon).m_Prefab) ?? "?";
                    if (live && ic.m_Priority < IconPriority.Error && !em.HasComponent<Temp>(target))
                    {
                        var top = RemovedWhenOverlapped(target) ? target : TopOwner(target);
                        if (RemovedWhenOverlapped(top))
                        {
                            if (seenD.Add(top))
                            {
                                s.Demolish.Add(top);
                                if (!itemCache.ContainsKey(top)) itemCache[top] = DemolishItem(top);
                            }
                            continue;
                        }
                    }
                    // target-less errors other than overlaps (no road access, steep slope, ...) are already in 'problems'
                    if (!live && err.IndexOf("Overlap", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var c = ConflictItem(ic, owner, live ? target : Entity.Null, err);
                    var key = err + "|" + (c.TryGetValue("id", out var cid) ? cid : null) + "|" + ((Dictionary<string, object>)c["by"])["prefab"];
                    if (seenC.Add(key)) { s.Conflicts.Add(c); s.ConflictKeys.Add(key); }
                }
            }
            return s;
        }

        Dictionary<string, object> ConflictItem(Icon ic, Entity owner, Entity target, string err)
        {
            var d = new Dictionary<string, object>
            {
                { "error", err },
                { "severity", ic.m_Priority >= IconPriority.Error ? "error" : "warning" },
                { "at", F2(ic.m_Location) },
            };
            if (target != Entity.Null)
            {
                var t = target;
                if (em.HasComponent<Temp>(t))
                {
                    // the other side is part of the preview itself: a copy of an existing entity or a new piece
                    var orig = em.GetComponentData<Temp>(t).m_Original;
                    d["previewPart"] = true;
                    if (orig != Entity.Null && em.Exists(orig)) t = orig;
                }
                d["id"] = em.HasComponent<Temp>(t) ? null : Id(t);
                var pf = em.HasComponent<PrefabRef>(t) ? em.GetComponentData<PrefabRef>(t).m_Prefab : Entity.Null;
                d["prefab"] = PrefabName(pf);
                d["kind"] = EntityKind(t);
                var pos = EntityPos(t);
                if (pos != null) d["center"] = pos;
                var top = TopOwner(t);
                if (top != t) d["owner"] = Ref(top);
            }
            else d["id"] = null;
            // which part of the new build reported it
            var by = new Dictionary<string, object>();
            var opf = em.HasComponent<PrefabRef>(owner) ? em.GetComponentData<PrefabRef>(owner).m_Prefab : Entity.Null;
            by["prefab"] = PrefabName(opf);
            var ot = em.GetComponentData<Temp>(owner);
            if (ot.m_Original != Entity.Null) by["original"] = Id(ot.m_Original);
            d["by"] = by;
            return d;
        }

        // ------------------------------------------------------------------ geometry (cross-check, findspot/freeland)

        EntityQuery GrowableQuery() => em.CreateEntityQuery(ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.ReadOnly<PrefabRef>(),
            ComponentType.Exclude<Owner>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Hidden>());

        /// <summary>Base rectangle of an object (its ObjectGeometryData bounds; for buildings the lot minus 0.2 m per side).</summary>
        static LotRect ObjRect(ObjTransform tr, Bounds3 b)
        {
            var u = math.normalizesafe(math.mul(tr.m_Rotation, new float3(1, 0, 0)).xz);
            var v = math.normalizesafe(math.mul(tr.m_Rotation, new float3(0, 0, 1)).xz);
            var r = new LotRect { U = u, V = v, Ex = (b.max.x - b.min.x) * 0.5f, Ez = (b.max.z - b.min.z) * 0.5f };
            r.C = tr.m_Position.xz + u * ((b.min.x + b.max.x) * 0.5f) + v * ((b.min.z + b.max.z) * 0.5f);
            var ext = math.abs(u) * r.Ex + math.abs(v) * r.Ez;
            r.Min = r.C - ext; r.Max = r.C + ext;
            return r;
        }

        static float2[] BaseCorners(float2 pos, quaternion rot, Bounds3 b) => new[]
        {
            pos + math.mul(rot, new float3(b.min.x, 0, b.min.z)).xz,
            pos + math.mul(rot, new float3(b.min.x, 0, b.max.z)).xz,
            pos + math.mul(rot, new float3(b.max.x, 0, b.max.z)).xz,
            pos + math.mul(rot, new float3(b.max.x, 0, b.min.z)).xz,
        };

        /// <summary>
        /// /findspot, /freeland: growable lots as obstacles (kind zonedBuilding), or with growables=free
        /// kept aside in set.Growables so candidates can report how many they would demolish.
        /// Called from CollectObstacles; AddObjects skips them because they are Overridable.
        /// </summary>
        void AddGrowableObstacles(ObstSet set, CollisionMask bmask, Dictionary<Entity, ObjectGeometryData?> geoCache, bool free)
        {
            const float objReach = 400f;
            if (free && set.Growables == null) set.Growables = new ObstSet(set.AreaMin, set.AreaMax, 32f);
            var min = set.AreaMin; var max = set.AreaMax;
            var q = GrowableQuery();
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
                    if (g == null || !RemovedWhenOverlapped(g.Value)) continue;
                    var geo = g.Value;
                    var e = ents[i];
                    var m = em.HasComponent<Game.Objects.Elevation>(e)
                        ? Game.Objects.ObjectUtils.GetCollisionMask(geo, em.GetComponentData<Game.Objects.Elevation>(e), true)
                        : Game.Objects.ObjectUtils.GetCollisionMask(geo, true);
                    if ((m & bmask) == 0) continue;
                    var corners = BaseCorners(pos, trs[i].m_Rotation, geo.m_Bounds);
                    if (free) set.Growables.AddPoly(corners, K_ZONED, e, false);
                    else set.AddPoly(corners, K_ZONED, e, false);
                }
            }
        }

        static bool GrowablesFree(Pending p) => string.Equals(p.Q("growables", "obstacle"), "free", StringComparison.OrdinalIgnoreCase);

        /// <summary>growables=free: how many growable lots a candidate rectangle overlaps.</summary>
        void AddGrowableHits(Dictionary<string, object> d, ObstSet obs, ref LotRect r)
        {
            if (obs.Growables == null) return;
            var hits = obs.Growables.AllHits(ref r, OverlapEps);
            if (hits.Count == 0) return;
            var ids = new List<object>();
            foreach (var o in hits) { var id = Id(o.Src); if (!ids.Contains(id) && ids.Count < 12) ids.Add(id); }
            d["wouldDemolish"] = hits.Select(o => o.Src).Distinct().Count();
            d["wouldDemolishIds"] = ids;
        }

        void AddGrowableHits(Dictionary<string, object> d, ObstSet obs, float2 mn, float2 mx)
        {
            var r = new LotRect { C = (mn + mx) * 0.5f, U = new float2(1, 0), V = new float2(0, 1), Ex = (mx.x - mn.x) * 0.5f, Ez = (mx.y - mn.y) * 0.5f, Min = mn, Max = mx };
            AddGrowableHits(d, obs, ref r);
        }

        static void NoteGrowables(Dictionary<string, object> r, ObstSet obs)
        {
            bool free = obs.Growables != null;
            r["growables"] = free ? "free" : "obstacle";
            if (free) r["growablesNote"] = "growables=free: zoned (growable) buildings are treated as free land; 'wouldDemolish' on a candidate = how many the game would remove there";
            else if (r.TryGetValue("hint", out var h) && h is string hs)
                r["hint"] = hs + "; zoned (growable) buildings count as obstacles ('zonedBuilding'): growables=free lets the search build over them (the game then demolishes them)";
        }

        sealed partial class ObstSet
        {
            /// <summary>growables=free: growable lots, kept out of the obstacle grid, only for counting.</summary>
            public ObstSet Growables;

            /// <summary>Every obstacle the rectangle overlaps.</summary>
            public List<Obst> AllHits(ref LotRect r, float eps)
            {
                var res = new List<Obst>();
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
                            if (Overlaps(ref r, o, eps)) res.Add(o);
                        }
                    }
                return res;
            }
        }

        /// <summary>Ground footprint of a queued build, as polygons/circles in an ObstSet (null = not computable).</summary>
        sealed class Footprint
        {
            public ObstSet Set;        // null when the build has no geometric footprint check (area only)
            public float2 Min, Max;    // area of the build (for condemnedAfter)
            public string What;
        }

        /// <summary>Base of an object placed at pos/rot: its ObjectGeometryData bounds (a circle for circular objects). False without geometry.</summary>
        bool ObjectBase(Entity prefab, float3 position, quaternion rotation, List<float2[]> polys, List<KeyValuePair<float2, float>> circles)
        {
            if (!em.HasComponent<ObjectGeometryData>(prefab)) return false;
            var g = em.GetComponentData<ObjectGeometryData>(prefab);
            var pos = position.xz;
            if ((g.m_Flags & GeoFlags.Circular) != 0)
            {
                var b = g.m_Bounds;
                var cc = pos + math.mul(rotation, new float3((b.min.x + b.max.x) * 0.5f, 0, (b.min.z + b.max.z) * 0.5f)).xz;
                circles.Add(new KeyValuePair<float2, float>(cc, math.max(g.m_Size.x, g.m_Size.z) * 0.5f));
            }
            else polys.Add(BaseCorners(pos, rotation, g.m_Bounds));
            return true;
        }

        Footprint FootprintOf(AgentToolSystem.BuildJob job)
        {
            var polys = new List<float2[]>();
            var circles = new List<KeyValuePair<float2, float>>();
            var curves = new List<KeyValuePair<Bezier4x3, float>>();
            string what = null;
            bool area = false;
            if (job is ObjectJob oj)
            {
                if (ObjectBase(oj.Prefab, oj.Position, oj.Rotation, polys, circles)) what = "building lot";
            }
            else if (job is ExtensionJob xj)
            {
                if (ObjectBase(xj.Prefab, xj.Position, xj.Rotation, polys, circles)) what = "extension lot";
            }
            else if (job is NetJob nj)
            {
                var pf = nj.Prefab;
                bool ground = em.HasComponent<RoadData>(pf) || em.HasComponent<TrackData>(pf) || em.HasComponent<PathwayData>(pf);
                if (ground && em.HasComponent<NetGeometryData>(pf) && nj.Points.Count >= 2)
                {
                    float hw = em.GetComponentData<NetGeometryData>(pf).m_DefaultWidth * 0.5f;
                    for (int i = 0; i < nj.Points.Count; i++)
                    {
                        var p = nj.Points[i].m_Position.xz;
                        circles.Add(new KeyValuePair<float2, float>(p, hw));
                        if (i == 0) continue;
                        var a = nj.Points[i - 1].m_Position.xz;
                        var dir = math.normalizesafe(p - a);
                        var left = new float2(-dir.y, dir.x) * hw;
                        polys.Add(new[] { a + left, p + left, p - left, a - left });
                    }
                    what = "road strip (prefab default width)";
                }
            }
            else if (job is ReplaceJob rj)
            {
                float hw = em.HasComponent<NetGeometryData>(rj.Prefab) ? em.GetComponentData<NetGeometryData>(rj.Prefab).m_DefaultWidth * 0.5f : 0f;
                foreach (var e in rj.Edges)
                    if (em.Exists(e) && em.HasComponent<Curve>(e)) curves.Add(new KeyValuePair<Bezier4x3, float>(em.GetComponentData<Curve>(e).m_Bezier, hw));
                if (hw > 0 && curves.Count > 0) what = "replaced road strip (new prefab default width)";
                else area = true;
            }
            else if (job is NetUpgradeJob uj)
            {
                foreach (var it in uj.Items)
                    if (em.Exists(it.Edge) && em.HasComponent<Curve>(it.Edge)) curves.Add(new KeyValuePair<Bezier4x3, float>(em.GetComponentData<Curve>(it.Edge).m_Bezier, 8f));
                area = true; // same road width: no growable overlap check, area only
            }
            if (polys.Count == 0 && circles.Count == 0 && curves.Count == 0) return null;

            float2 mn = float.MaxValue, mx = float.MinValue;
            foreach (var poly in polys) foreach (var v in poly) { mn = math.min(mn, v); mx = math.max(mx, v); }
            foreach (var c in circles) { mn = math.min(mn, c.Key - c.Value); mx = math.max(mx, c.Key + c.Value); }
            foreach (var c in curves)
            {
                var b = c.Key;
                mn = math.min(mn, math.min(math.min(b.a.xz, b.b.xz), math.min(b.c.xz, b.d.xz)) - c.Value);
                mx = math.max(mx, math.max(math.max(b.a.xz, b.b.xz), math.max(b.c.xz, b.d.xz)) + c.Value);
            }
            var fp = new Footprint { Min = mn, Max = mx, What = what };
            if (area || what == null) return fp;
            var set = new ObstSet(mn - 1f, mx + 1f, 32f);
            foreach (var poly in polys) set.AddPoly(poly, K_ROAD, Entity.Null, false);
            foreach (var c in circles) if (c.Value > 0.05f) set.AddCircle(c.Key, c.Value, K_ROAD, Entity.Null);
            foreach (var c in curves) AddStripNoCaps(set, c.Key, c.Value);
            fp.Set = set;
            return fp;
        }

        /// <summary>
        /// Like FindSpot's AddCurveStrip but without the round node caps: a replaced segment widens only
        /// alongside its own curve; lots diagonally past its nodes are left to the game's preview.
        /// </summary>
        static void AddStripNoCaps(ObstSet set, Bezier4x3 cv, float hw)
        {
            int n = IsStraight(cv) ? 1 : math.clamp((int)math.ceil(ApproxLen(cv) / 4f), 1, 64);
            float2 prevL = 0, prevR = 0;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                var pt = MathUtils.Position(cv, t).xz;
                var dir = math.normalizesafe(MathUtils.Tangent(cv, t).xz);
                var left = new float2(-dir.y, dir.x) * hw;
                if (i > 0) set.AddPoly(new[] { prevL, pt + left, pt - left, prevR }, K_ROAD, Entity.Null, false);
                prevL = pt + left; prevR = pt - left;
            }
        }

        /// <summary>Growables whose lot overlaps the footprint (same lot shrink and penetration tolerance as the game).</summary>
        List<Entity> GrowablesUnder(Footprint fp)
        {
            var res = new List<Entity>();
            if (fp?.Set == null) return res;
            var set = fp.Set;
            var geoCache = new Dictionary<Entity, ObjectGeometryData?>();
            var q = GrowableQuery();
            using (var ents = q.ToEntityArray(Allocator.TempJob))
            using (var trs = q.ToComponentDataArray<ObjTransform>(Allocator.TempJob))
            using (var prs = q.ToComponentDataArray<PrefabRef>(Allocator.TempJob))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    var pos = trs[i].m_Position.xz;
                    if (pos.x < set.AreaMin.x - 200f || pos.x > set.AreaMax.x + 200f || pos.y < set.AreaMin.y - 200f || pos.y > set.AreaMax.y + 200f) continue;
                    var pf = prs[i].m_Prefab;
                    if (!geoCache.TryGetValue(pf, out var g))
                    {
                        g = em.HasComponent<ObjectGeometryData>(pf) ? em.GetComponentData<ObjectGeometryData>(pf) : (ObjectGeometryData?)null;
                        geoCache[pf] = g;
                    }
                    if (g == null || !RemovedWhenOverlapped(g.Value)) continue;
                    var r = ObjRect(trs[i], g.Value.m_Bounds);
                    if (!set.InArea(r.Min, r.Max)) continue;
                    if (set.FirstHit(ref r, OverlapEps) != null) res.Add(ents[i]);
                }
            }
            return res;
        }

        HashSet<Entity> CondemnedSet()
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<Condemned>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = q.ToEntityArray(Allocator.Temp)) return new HashSet<Entity>(arr);
        }

        string DemolishStatus(Entity e) =>
            !em.Exists(e) || em.HasComponent<Deleted>(e) ? "removed" : em.HasComponent<Condemned>(e) ? "condemned" : "standing";

        // ------------------------------------------------------------------ wrapping builds

        static readonly FieldInfo BuildOpJobField = typeof(BuildOperation).GetField("job", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Dispatch wrapper for /building, /road, /upgrade, /roadupgrade: adds wouldDemolish, conflicts and
        /// (opt-in, allowDemolish:false) refuses to apply a build that would remove buildings.
        /// Non-build results (errors, "nothing to change") pass through unchanged.
        /// </summary>
        object Safety(object r, Args a)
        {
            if (!(r is BuildOperation op)) return r;
            var job = BuildOpJobField?.GetValue(op) as AgentToolSystem.BuildJob;
            if (job == null) { Log.Warn("Safety: BuildOperation.job not readable; wouldDemolish disabled"); return r; }
            return new SafeBuildOperation(this, job, op, a.B("allowDemolish", true));
        }

        /// <summary>Wraps a BuildOperation: snapshots the preview every frame and merges the findings into the result.</summary>
        sealed class SafeBuildOperation : IOperation
        {
            readonly IOperation inner;
            readonly DemolishWatch watch;

            public SafeBuildOperation(Api api, AgentToolSystem.BuildJob job, IOperation inner, bool allowDemolish)
            {
                this.inner = inner;
                watch = new DemolishWatch(api, job, allowDemolish);
            }

            public bool Step(out object result)
            {
                watch.Observe();
                if (!inner.Step(out result)) return false;
                if (result is ApiError || !(result is Dictionary<string, object> r)) return true;
                try { watch.Merge(r); } catch (Exception e) { r["safetyError"] = e.GetType().Name + ": " + e.Message; }
                return true;
            }
        }

        /// <summary>
        /// What one queued build job would remove: the footprint's growable lots (computed before the tool
        /// runs) plus the game's own preview icons (Observe() every frame while the job previews). With
        /// allowDemolish=false a real build is switched to a dry run before the tool evaluates it, so nothing
        /// is applied. Used by SafeBuildOperation (/building, /road, ...) and ExtensionOperation (/extension).
        /// </summary>
        internal sealed class DemolishWatch
        {
            readonly Api api;
            readonly AgentToolSystem.BuildJob job;
            readonly bool allowDemolish;
            readonly Footprint fp;
            readonly List<Entity> geometryHits;
            readonly HashSet<Entity> condemnedBefore;
            readonly Dictionary<Entity, Dictionary<string, object>> items = new Dictionary<Entity, Dictionary<string, object>>();
            PreviewSnapshot last;                     // latest snapshot with preview entities
            readonly List<Entity> demolish = new List<Entity>();   // union over snapshots of the settled preview
            readonly List<Dictionary<string, object>> conflicts = new List<Dictionary<string, object>>();
            readonly HashSet<string> conflictKeys = new HashSet<string>();
            int snapshots, settled;
            string blocked;

            /// <summary>Replaces the default note on via=geometry entries (e.g. extensions, whose preview never flags growables).</summary>
            public string GeometryNote;

            /// <summary>Growable lots under the footprint by our calculation (known before the tool runs).</summary>
            public int GeometryHitCount => geometryHits.Count;

            public DemolishWatch(Api api, AgentToolSystem.BuildJob job, bool allowDemolish)
            {
                this.api = api; this.job = job; this.allowDemolish = allowDemolish;
                try
                {
                    fp = api.FootprintOf(job);
                    geometryHits = api.GrowablesUnder(fp);
                    foreach (var e in geometryHits) items[e] = api.DemolishItem(e);
                }
                catch (Exception e) { Log.Warn("Safety footprint: " + e.Message); geometryHits = new List<Entity>(); }
                condemnedBefore = job.DryRun ? null : api.CondemnedSet();
                if (!allowDemolish && !job.DryRun && geometryHits.Count > 0)
                {
                    job.DryRun = true; // still before the tool evaluates: the preview runs, nothing is applied
                    blocked = "lot overlap with " + geometryHits.Count + " zoned building(s)";
                }
            }

            /// <summary>Call every frame while the job runs (cheap no-op outside the preview states).</summary>
            public void Observe()
            {
                if (job.Done || !(job.State == 1 || job.State == 5) || job.Frames < 4) return;
                try
                {
                    var s = api.SnapshotPreview(items);
                    if (s.Temps > 0)
                    {
                        last = s; snapshots++;
                        // the tool applies once the temp count was stable for 5 frames; union what the
                        // settled preview reported (icons are re-created when validation re-runs)
                        if (job.StableFrames >= 2 && job.Frames >= AgentToolSystem.MinPreviewFrames / 2)
                        {
                            settled++;
                            foreach (var e in s.Demolish) if (!demolish.Contains(e)) demolish.Add(e);
                            for (int i = 0; i < s.Conflicts.Count; i++) if (conflictKeys.Add(s.ConflictKeys[i])) conflicts.Add(s.Conflicts[i]);
                        }
                    }
                    if (!allowDemolish && !job.DryRun && s.Demolish.Count > 0)
                    {
                        job.DryRun = true; // the tool has not evaluated yet (State 1 or 5): it will clear instead of apply
                        blocked = "the game's preview would remove " + s.Demolish.Count + " building(s)";
                    }
                }
                catch (Exception e) { Log.Warn("Safety snapshot: " + e.Message); }
            }

            /// <summary>Adds wouldDemolish, conflicts, demolishCheck (and blocked / condemnedAfter) to a finished job's result.</summary>
            public void Merge(Dictionary<string, object> r)
            {
                bool applied = r.TryGetValue("applied", out var ap) && ap is bool b && b;
                var geo = new HashSet<Entity>(geometryHits);
                var list = new List<object>();
                var listed = new HashSet<Entity>();
                bool geometryOnly = false;
                Action<Entity, string> add = (e, via) =>
                {
                    if (!listed.Add(e)) return;
                    var d = new Dictionary<string, object>(items.TryGetValue(e, out var it) ? it : api.DemolishItem(e));
                    d["via"] = via;
                    if (applied) d["status"] = api.DemolishStatus(e); // after apply + settle: removed | condemned | standing
                    list.Add(d);
                };
                bool preview = last != null;
                if (preview && settled == 0) { demolish.AddRange(last.Demolish); conflicts.AddRange(last.Conflicts); } // preview never settled (300-frame timeout)
                foreach (var e in demolish) add(e, geo.Contains(e) ? "both" : "preview");
                foreach (var e in geometryHits)
                {
                    if (listed.Contains(e)) continue;
                    add(e, "geometry");
                    geometryOnly = true;
                }
                r["wouldDemolish"] = list;
                r["conflicts"] = conflicts.Cast<object>().ToList();
                r["demolishCheck"] = new Dictionary<string, object>
                {
                    { "preview", preview }, { "previewSnapshots", snapshots }, { "settledSnapshots", settled },
                    { "geometry", fp?.Set != null ? fp.What : "none" },
                };
                if (geometryOnly)
                    r["wouldDemolishNote"] = GeometryNote ?? (preview
                        ? "via=geometry: the lot overlaps the new footprint by our calculation but the game's preview did not flag it (bridge/tunnel height, intersection shape or width approximation); via=preview/both is what the game removes"
                        : "no preview was captured; entries come from the lot-overlap calculation only");
                if (blocked != null)
                {
                    r["blocked"] = blocked + " (allowDemolish=false); nothing was built";
                    r["applied"] = false;
                }
                if (applied && condemnedBefore != null)
                {
                    // growables the preview did not predict but that lost their zoning (e.g. zone blocks rebuilt
                    // along a replaced road): condemned now, demolished by the game a little later
                    var extra = new List<object>();
                    float2 mn = fp != null ? fp.Min - 48f : float.MinValue, mx = fp != null ? fp.Max + 48f : float.MaxValue;
                    foreach (var e in api.CondemnedSet())
                    {
                        if (condemnedBefore.Contains(e) || listed.Contains(e)) continue;
                        if (api.em.HasComponent<ObjTransform>(e))
                        {
                            var p = api.em.GetComponentData<ObjTransform>(e).m_Position.xz;
                            if (p.x < mn.x || p.y < mn.y || p.x > mx.x || p.y > mx.y) continue;
                        }
                        if (extra.Count < 50) extra.Add(api.DemolishItem(e));
                    }
                    if (extra.Count > 0) r["condemnedAfter"] = extra;
                }
            }
        }
    }
}
