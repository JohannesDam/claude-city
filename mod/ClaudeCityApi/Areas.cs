using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using GA = Game.Areas;
using GB = Game.Buildings;
using GPol = Game.Policies;
using PSubArea = Game.Prefabs.SubArea;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// A closed area polygon written exactly like AreaToolSystem.CreateDefinitionsJob.Edit() does in
    /// game mode when the player completes a polygon (State.Create):
    /// <list type="bullet">
    /// <item>new area (district): CreationDefinition { m_Prefab } + Updated + a Node buffer with one
    /// node per control point, elevation float.MinValue ("follow the terrain"), and the first point
    /// repeated at the end with the identical position. GenerateAreasSystem drops that closing node
    /// and sets AreaFlags.Complete, exactly as when the player clicks the first node again.</item>
    /// <item>extractor work area: the in-game flow after placing an extractor main building is
    /// ObjectToolSystem.Apply -> AreaToolSystem.recreate = the building's lot sub-area. The definition
    /// then gets m_Original = that lot, CreationFlags.Recreate and (Owner of the original, a building
    /// with a Transform, game mode) m_Owner = the building. ApplyAreasSystem copies the new nodes
    /// into the existing lot, so the lot keeps its entity id and its Owner/SubArea link.</item>
    /// </list>
    /// Validation (shape, overlap, city limits, LotData.m_MaxRadius around the owner), cost and
    /// resource evaluation (AreaResourceSystem also runs on the temporary preview) are the game's own.
    /// </summary>
    public class AreaJob : AgentToolSystem.BuildJob
    {
        public Api Api;
        public Entity Prefab;
        public Entity Original = Entity.Null;   // lot to recreate (extractor) or Null (new area)
        public Entity Owner = Entity.Null;      // owner building of Original
        public List<float3> Points = new List<float3>();
        public string Name;
        public Entity Placed = Entity.Null;
        HashSet<Entity> before;

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            if (Original != Entity.Null && !Api.IsLive(Original)) { Result["error"] = "the work area to redraw no longer exists"; return deps; }
            if (Owner != Entity.Null && !Api.IsLive(Owner)) { Result["error"] = "owner building no longer exists"; return deps; }
            if (Points.Count < 3) { Result["error"] = "need at least 3 points"; return deps; }
            before = Api.AreaEntitiesOf(Prefab);
            var def = new CreationDefinition { m_Prefab = Prefab };
            if (Original != Entity.Null)
            {
                def.m_Original = Original;
                def.m_Flags |= CreationFlags.Recreate;
                def.m_Owner = Owner;
            }
            var e = ecb.CreateEntity();
            var nodes = ecb.AddBuffer<GA.Node>(e);
            foreach (var p in Points) nodes.Add(new GA.Node(p, float.MinValue));
            nodes.Add(new GA.Node(Points[0], float.MinValue)); // closing node == first node -> Complete
            ecb.AddComponent(e, def);
            ecb.AddComponent(e, default(Updated));
            return deps;
        }

        public override void AfterApply(AgentToolSystem tool)
        {
            var em = tool.EntityManager;
            if (Original != Entity.Null) Placed = Original;
            else
            {
                var c = float2.zero;
                foreach (var p in Points) c += p.xz;
                c /= Points.Count;
                float best = float.MaxValue;
                foreach (var a in Api.AreaEntitiesOf(Prefab))
                {
                    if (before != null && before.Contains(a)) continue;
                    var ac = em.HasComponent<GA.Geometry>(a) ? em.GetComponentData<GA.Geometry>(a).m_CenterPosition.xz : c;
                    var d = math.distance(ac, c);
                    if (d < best) { best = d; Placed = a; }
                }
            }
            Result["areaId"] = Placed == Entity.Null ? null : Api.Id(Placed);
            if (Placed == Entity.Null) return;
            // the name is set by AreaOperation (bridge update): AfterApply runs in the tool update, where
            // NameSystem.SetCustomName cannot get its EndFrameBarrier command buffer (SafeCommandBufferSystem
            // throws "Trying to create EntityCommandBuffer when it's not allowed!" until AllowBarrier runs)
            NamePending = !string.IsNullOrWhiteSpace(Name) && em.HasComponent<GA.District>(Placed);
            try { Result["area"] = Api.DescribeArea(Placed, 64, null); } catch (Exception ex) { Result["describeError"] = ex.Message; }
        }

        /// <summary>Set by AfterApply when a district name still has to be applied (AreaOperation does it).</summary>
        public bool NamePending;
    }

    /// <summary>Waits for an AreaJob; while the game shows the temporary preview it snapshots the temp
    /// area (size, completeness, the extractor resource the game computed for it).</summary>
    public class AreaOperation : IOperation
    {
        readonly Api api;
        readonly AreaJob job;
        readonly Dictionary<string, object> info;
        readonly int started = Environment.TickCount;
        readonly bool allowDemolish;
        Dictionary<string, object> preview;
        string blocked;
        int nameTries;

        public AreaOperation(Api api, AreaJob job, Dictionary<string, object> info, bool allowDemolish = true)
        { this.api = api; this.job = job; this.info = info; this.allowDemolish = allowDemolish; }

        public bool Step(out object result)
        {
            result = null;
            if (!job.Done)
            {
                if (job.State == 1 || job.State == 5)
                {
                    try { var s = api.SnapshotTempArea(job); if (s != null) preview = s; }
                    catch (Exception e) { preview = new Dictionary<string, object> { { "snapshotError", e.Message } }; }
                    // like the build Safety wrapper: still before the tool evaluates, so nothing gets applied
                    if (!allowDemolish && !job.DryRun && job.State == 1 && preview != null && preview.TryGetValue("wouldDelete", out var wd) && wd is List<object> l && l.Count > 0)
                    {
                        job.DryRun = true;
                        blocked = "the area would delete " + l.Count + " object(s); pass allowDemolish:true to accept";
                    }
                }
                if (Environment.TickCount - started > 30000) { result = new ApiError("area build timed out (is the game running and in-game?)", 504); return true; }
                return false;
            }
            if (job.NamePending && !TrySetName()) return false; // retried on the next frames (bounded)
            var r = new Dictionary<string, object>(info);
            foreach (var kv in job.Result) r[kv.Key] = kv.Value;
            if (preview != null) r["preview"] = preview;
            if (blocked != null) r["blocked"] = blocked;
            result = r;
            return true;
        }

        /// <summary>Applies the district name like the info panel rename (NameSystem.SetCustomName) from the bridge update;
        /// true when finished (set, or given up after a few frames with nameError).</summary>
        bool TrySetName()
        {
            try
            {
                api.SetAreaName(job.Placed, job.Name);
                job.NamePending = false;
                job.Result.Remove("nameError");
                job.Result["nameSet"] = job.Name;
                try { job.Result["area"] = api.DescribeArea(job.Placed, 64, null); } catch (Exception ex) { job.Result["describeError"] = ex.Message; }
                return true;
            }
            catch (Exception ex)
            {
                job.Result["nameError"] = ex.Message;
                if (++nameTries < 30) return false;
                job.NamePending = false;
                return true;
            }
        }
    }

    /// <summary>Waits a few frames for an end-of-frame deletion and confirms it.</summary>
    public class AreaDeleteOperation : IOperation
    {
        readonly Api api;
        readonly Entity target;
        readonly Dictionary<string, object> info;
        int frames;
        public AreaDeleteOperation(Api api, Entity target, Dictionary<string, object> info) { this.api = api; this.target = target; this.info = info; }
        public bool Step(out object result)
        {
            result = null;
            if (++frames < 8) return false;
            var r = new Dictionary<string, object>(info);
            bool gone = !api.World.EntityManager.Exists(target) || api.World.EntityManager.HasComponent<Deleted>(target);
            r["deleted"] = gone;
            if (!gone) r["warning"] = "entity still exists after 8 frames";
            result = r;
            return true;
        }
    }

    public partial class Api
    {
        // ------------------------------------------------------------------ helpers

        static float2 AreaPt(object o)
        {
            var l = o as List<object>;
            if (l == null || l.Count < 2 || !(l[0] is double) || !(l[1] is double)) throw new ArgumentException("point must be [x, z]");
            return new float2((float)(double)l[0], (float)(double)l[1]);
        }

        static List<object> Poly(IList<float2> pts) => pts.Select(p => (object)new List<object> { Math.Round(p.x, 1), Math.Round(p.y, 1) }).ToList();

        /// <summary>Signed shoelace area (positive = counter-clockwise in x/z).</summary>
        static float PolyArea(IList<float2> pts)
        {
            double s = 0;
            for (int i = 0; i < pts.Count; i++) { var a = pts[i]; var b = pts[(i + 1) % pts.Count]; s += (double)a.x * b.y - (double)b.x * a.y; }
            return (float)(s * 0.5);
        }

        static bool PointInPoly(IList<float2> poly, float2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        static bool SegmentsCross(float2 a, float2 b, float2 c, float2 d)
        {
            float Cross(float2 o, float2 p, float2 q) => (p.x - o.x) * (q.y - o.y) - (p.y - o.y) * (q.x - o.x);
            float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        static bool SelfIntersects(IList<float2> poly)
        {
            int n = poly.Count;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (j == i + 1 || (i == 0 && j == n - 1)) continue; // adjacent edges
                    if (SegmentsCross(poly[i], poly[(i + 1) % n], poly[j], poly[(j + 1) % n])) return true;
                }
            return false;
        }

        /// <summary>
        /// Polygon [n0, n1, u...] like the in-game redraw (seeded with the lot's building edge n0-n1).
        /// The caller's corners are rotated/reversed to the simple (non-crossing) order with the shortest
        /// perimeter. Left unchanged when the corners already contain n0/n1 or enclose both.
        /// </summary>
        static List<float2> WithBuildingEdge(List<float2> user, float2 n0, float2 n1, out string how)
        {
            if (user.Any(q => math.distance(q, n0) < 1f || math.distance(q, n1) < 1f)) { how = "already in polygon"; return user; }
            if (PointInPoly(user, n0) && PointInPoly(user, n1)) { how = "edge inside polygon, polygon unchanged"; return user; }
            List<float2> best = null;
            float bestPerim = float.MaxValue;
            for (int rev = 0; rev < 2; rev++)
            {
                var seq = rev == 0 ? user : Enumerable.Reverse(user).ToList();
                for (int r = 0; r < seq.Count; r++)
                {
                    var cand = new List<float2> { n0, n1 };
                    for (int i = 0; i < seq.Count; i++) cand.Add(seq[(r + i) % seq.Count]);
                    if (SelfIntersects(cand)) continue;
                    float per = 0;
                    for (int i = 0; i < cand.Count; i++) per += math.distance(cand[i], cand[(i + 1) % cand.Count]);
                    if (per < bestPerim) { bestPerim = per; best = cand; }
                }
            }
            if (best == null) { how = "no simple polygon with the edge, polygon unchanged"; return user; }
            how = "inserted";
            return best;
        }

        /// <summary>Owned (non-Native) map tiles as polygons with bounds.</summary>
        public List<KeyValuePair<Bounds2, float2[]>> OwnedTilePolys()
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GA.MapTile>(), ComponentType.ReadOnly<GA.Node>(), ComponentType.Exclude<Native>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var res = new List<KeyValuePair<Bounds2, float2[]>>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var t in arr)
                {
                    var nodes = em.GetBuffer<GA.Node>(t, true);
                    var pts = new float2[nodes.Length];
                    var b = new Bounds2(new float2(float.MaxValue), new float2(float.MinValue));
                    for (int i = 0; i < nodes.Length; i++) { pts[i] = nodes[i].m_Position.xz; b.min = math.min(b.min, pts[i]); b.max = math.max(b.max, pts[i]); }
                    res.Add(new KeyValuePair<Bounds2, float2[]>(b, pts));
                }
            return res;
        }

        static bool InOwned(List<KeyValuePair<Bounds2, float2[]>> tiles, float2 p)
        {
            foreach (var t in tiles)
            {
                var b = t.Key;
                if (p.x < b.min.x || p.x > b.max.x || p.y < b.min.y || p.y > b.max.y) continue;
                if (PointInPoly(t.Value, p)) return true;
            }
            return false;
        }

        /// <summary>Live areas (not temp/deleted) of one prefab.</summary>
        public HashSet<Entity> AreaEntitiesOf(Entity prefab)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GA.Area>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var res = new HashSet<Entity>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr) if (em.GetComponentData<PrefabRef>(e).m_Prefab == prefab) res.Add(e);
            return res;
        }

        /// <summary>AreasConfigurationPrefab.m_DefaultDistrictPrefab (what DistrictsSection.ToggleDistrictTool uses).</summary>
        Entity DefaultDistrictPrefab()
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<AreasConfigurationData>());
            using (var arr = q.ToComponentDataArray<AreasConfigurationData>(Allocator.Temp))
                if (arr.Length > 0 && arr[0].m_DefaultDistrictPrefab != Entity.Null) return arr[0].m_DefaultDistrictPrefab;
            return FindPrefab("District", e => em.HasComponent<DistrictData>(e) && em.HasComponent<AreaGeometryData>(e));
        }

        Entity ResolveAreaPrefab(string name)
        {
            if (string.IsNullOrEmpty(name) || string.Equals(name, "district", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "districts", StringComparison.OrdinalIgnoreCase))
                return DefaultDistrictPrefab();
            return FindPrefab(name, e => em.HasComponent<AreaGeometryData>(e) && em.HasComponent<AreaData>(e));
        }

        /// <summary>The extractor work-area lot(s) of a building (its Game.Areas.SubArea buffer, fallback: areas owned by it).</summary>
        List<Entity> ExtractorLotsOf(Entity building)
        {
            var res = new List<Entity>();
            if (em.HasBuffer<GA.SubArea>(building))
            {
                var subs = em.GetBuffer<GA.SubArea>(building, true);
                for (int i = 0; i < subs.Length; i++)
                {
                    var a = subs[i].m_Area;
                    if (!IsLive(a) || !em.HasComponent<PrefabRef>(a)) continue;
                    if (em.HasComponent<GA.Extractor>(a) || em.HasComponent<ExtractorAreaData>(em.GetComponentData<PrefabRef>(a).m_Prefab)) res.Add(a);
                }
            }
            if (res.Count == 0)
            {
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<GA.Extractor>(), ComponentType.ReadOnly<Owner>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
                using (var arr = q.ToEntityArray(Allocator.Temp))
                    foreach (var a in arr) if (em.GetComponentData<Owner>(a).m_Owner == building) res.Add(a);
            }
            return res;
        }

        public void SetAreaName(Entity e, string name)
        {
            var ns = world.GetExistingSystemManaged<Game.UI.NameSystem>();
            if (ns == null) throw new InvalidOperationException("NameSystem not available");
            ns.SetCustomName(e, name); // what TitleSection.OnRename does
        }

        static string FeatureLabel(GA.MapFeature f)
        {
            switch (f)
            {
                case GA.MapFeature.FertileLand: return "fertility";
                case GA.MapFeature.Forest: return "forest";
                case GA.MapFeature.Ore: return "ore";
                case GA.MapFeature.Oil: return "oil";
                case GA.MapFeature.Fish: return "fish";
                case GA.MapFeature.GroundWater: return "groundwater";
                case GA.MapFeature.SurfaceWater: return "surfacewater";
                default: return f.ToString();
            }
        }

        Dictionary<string, object> ExtractorJson(Entity area)
        {
            var d = new Dictionary<string, object>();
            if (em.HasComponent<GA.Extractor>(area))
            {
                var x = em.GetComponentData<GA.Extractor>(area);
                d["resourceAmount"] = Math.Round(x.m_ResourceAmount, 1);
                d["maxConcentration"] = Math.Round(x.m_MaxConcentration, 3);
                d["extractedAmount"] = Math.Round(x.m_ExtractedAmount, 1);
                d["workAmount"] = Math.Round(x.m_WorkAmount, 1);
                d["harvestedAmount"] = Math.Round(x.m_HarvestedAmount, 1);
                d["totalExtracted"] = Math.Round(x.m_TotalExtracted, 1);
                d["workType"] = x.m_WorkType.ToString();
            }
            if (em.HasBuffer<GA.WoodResource>(area)) d["trees"] = em.GetBuffer<GA.WoodResource>(area, true).Length;
            if (em.HasBuffer<GA.MapFeatureElement>(area))
            {
                var mf = em.GetBuffer<GA.MapFeatureElement>(area, true);
                var f = new Dictionary<string, object>();
                for (int i = 0; i < mf.Length && i < (int)GA.MapFeature.Count; i++)
                    if (mf[i].m_Amount != 0) f[FeatureLabel((GA.MapFeature)i)] = Math.Round(mf[i].m_Amount, 1);
                if (f.Count > 0) d["features"] = f;
            }
            return d;
        }

        /// <summary>One area as JSON. districtBuildings: optional building count per district.</summary>
        public Dictionary<string, object> DescribeArea(Entity e, int maxNodes, Dictionary<Entity, int> districtBuildings)
        {
            var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
            string kind = em.HasComponent<GA.District>(e) ? "district" : em.HasComponent<GA.Extractor>(e) || em.HasComponent<ExtractorAreaData>(prefab) ? "extractor" : em.HasComponent<GA.Lot>(e) ? "lot" : "other";
            var d = new Dictionary<string, object> { { "id", Id(e) }, { "prefab", PrefabName(prefab) }, { "kind", kind } };
            var nodes = em.GetBuffer<GA.Node>(e, true);
            var pts = new List<float2>();
            for (int i = 0; i < nodes.Length; i++) pts.Add(nodes[i].m_Position.xz);
            d["nodes"] = pts.Count;
            if (maxNodes > 0 && pts.Count > maxNodes)
            {
                int step = (int)math.ceil(pts.Count / (float)maxNodes);
                var s = new List<float2>();
                for (int i = 0; i < pts.Count; i += step) s.Add(pts[i]);
                d["polygon"] = Poly(s);
                d["polygonSimplified"] = true;
            }
            else d["polygon"] = Poly(pts);
            if (em.HasComponent<GA.Geometry>(e))
            {
                var g = em.GetComponentData<GA.Geometry>(e);
                d["size"] = Math.Round(g.m_SurfaceArea);
                d["center"] = F2(g.m_CenterPosition);
            }
            else d["size"] = Math.Round(math.abs(PolyArea(pts)));
            if (em.HasComponent<GA.Area>(e)) d["complete"] = (em.GetComponentData<GA.Area>(e).m_Flags & GA.AreaFlags.Complete) != 0;
            if (em.HasComponent<Owner>(e))
            {
                var o = em.GetComponentData<Owner>(e).m_Owner;
                d["owner"] = Id(o);
                if (em.Exists(o) && em.HasComponent<PrefabRef>(o))
                {
                    var op = em.GetComponentData<PrefabRef>(o).m_Prefab;
                    d["ownerPrefab"] = PrefabName(op);
                    if (em.HasComponent<ObjTransform>(o)) d["ownerPos"] = F2(em.GetComponentData<ObjTransform>(o).m_Position);
                    if (em.HasComponent<BuildingPropertyData>(op))
                    {
                        var r = em.GetComponentData<BuildingPropertyData>(op).m_AllowedManufactured;
                        if (r != Game.Economy.Resource.NoResource) d["ownerProduces"] = r.ToString();
                    }
                }
            }
            if (kind == "district")
            {
                d["name"] = EntityLabel(e);
                var dist = em.GetComponentData<GA.District>(e);
                d["options"] = MaskNames<GA.DistrictOption>(dist.m_OptionMask);
                var pol = new List<object>();
                if (em.HasBuffer<GPol.Policy>(e))
                {
                    var buf = em.GetBuffer<GPol.Policy>(e, true);
                    for (int i = 0; i < buf.Length; i++)
                    {
                        if ((buf[i].m_Flags & GPol.PolicyFlags.Active) == 0) continue;
                        var pn = PrefabName(buf[i].m_Policy);
                        pol.Add(new Dictionary<string, object> { { "name", pn }, { "title", Loc("Policy.TITLE[" + pn + "]") }, { "adjustment", Math.Round(buf[i].m_Adjustment, 2) } });
                    }
                }
                d["policies"] = pol;
                if (districtBuildings != null) d["buildings"] = districtBuildings.TryGetValue(e, out var n) ? n : 0;
            }
            if (kind == "extractor" || kind == "lot")
            {
                if (em.HasComponent<ExtractorAreaData>(prefab))
                {
                    var ed = em.GetComponentData<ExtractorAreaData>(prefab);
                    d["mapFeature"] = FeatureLabel(ed.m_MapFeature);
                }
                if (em.HasComponent<LotData>(prefab)) d["maxRadius"] = em.GetComponentData<LotData>(prefab).m_MaxRadius;
                foreach (var kv in ExtractorJson(e)) d[kv.Key] = kv.Value;
            }
            return d;
        }

        /// <summary>Called by AreaOperation every frame while the preview exists.</summary>
        public Dictionary<string, object> SnapshotTempArea(AreaJob job)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GA.Area>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<GA.Node>(), ComponentType.Exclude<Deleted>());
            using (var arr = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in arr)
                {
                    var t = em.GetComponentData<Temp>(e);
                    bool match = job.Original != Entity.Null
                        ? t.m_Original == job.Original
                        : t.m_Original == Entity.Null && (t.m_Flags & TempFlags.Create) != 0 && em.GetComponentData<PrefabRef>(e).m_Prefab == job.Prefab;
                    if (!match) continue;
                    var d = new Dictionary<string, object>();
                    if (em.HasComponent<GA.Geometry>(e)) d["size"] = Math.Round(em.GetComponentData<GA.Geometry>(e).m_SurfaceArea);
                    d["nodes"] = em.GetBuffer<GA.Node>(e, true).Length;
                    d["complete"] = (em.GetComponentData<GA.Area>(e).m_Flags & GA.AreaFlags.Complete) != 0;
                    d["error"] = em.HasComponent<Error>(e);
                    d["warning"] = em.HasComponent<Warning>(e);
                    if (em.HasComponent<Owner>(e)) d["owner"] = Id(em.GetComponentData<Owner>(e).m_Owner);
                    foreach (var kv in ExtractorJson(e)) d[kv.Key] = kv.Value;
                    // anything else the apply would delete (buildings/objects cleared by the area)
                    var dq = em.CreateEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>());
                    var del = new List<object>();
                    using (var all = dq.ToEntityArray(Allocator.Temp))
                        foreach (var x in all)
                        {
                            var tx = em.GetComponentData<Temp>(x);
                            if ((tx.m_Flags & TempFlags.Delete) == 0 || x == e) continue;
                            if (del.Count < 30) del.Add(new Dictionary<string, object> { { "id", tx.m_Original == Entity.Null ? Id(x) : Id(tx.m_Original) }, { "prefab", PrefabName(em.GetComponentData<PrefabRef>(x).m_Prefab) }, { "building", em.HasComponent<GB.Building>(x) } });
                        }
                    d["wouldDelete"] = del;
                    return d;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ POST /area

        /// <summary>
        /// POST /area {prefab, points:[[x,z],...], owner?, name?, keepBuildingEdge?, dryRun}:
        /// district (prefab "district"/default district prefab, optional name) or extractor work area
        /// (owner = extractor main building id, or the id of its work area; prefab optional).
        /// </summary>
        object AreaCreate(Pending p)
        {
            if (p.Method != "POST")
                return new ApiError("use POST /area {prefab:'district'|<area prefab>, points:[[x,z],...], owner?:<building id>, name?, keepBuildingEdge?, dryRun?}; GET /extractors lists area prefabs, GET /areas existing areas", 405);
            var a = p.Body;
            var tool = Sys<AgentToolSystem>();
            if (tool.Busy) return new ApiError("agent tool is busy with another build", 409);
            var raw = a.List("points");
            if (raw == null || raw.Count < 3) return new ApiError("points: need at least 3 [x, z] corners");
            var pts = new List<float2>();
            try { foreach (var o in raw) pts.Add(AreaPt(o)); }
            catch (ArgumentException e) { return new ApiError(e.Message); }
            // closing point given explicitly -> drop it (we add the game's closing node ourselves)
            if (pts.Count > 3 && math.distance(pts[0], pts[pts.Count - 1]) < 1f) pts.RemoveAt(pts.Count - 1);
            var dedup = new List<float2>();
            foreach (var q in pts) if (dedup.Count == 0 || math.distance(dedup[dedup.Count - 1], q) >= 1f) dedup.Add(q);
            int dropped = pts.Count - dedup.Count;
            pts = dedup;
            if (pts.Count < 3) return new ApiError("need at least 3 distinct corners (>= 1 m apart)");
            if (pts.Count > 128) return new ApiError("too many corners (max 128)");

            var info = new Dictionary<string, object>();
            Entity prefab, original = Entity.Null, owner = Entity.Null;
            string prefabArg = a.Str("prefab");
            if (a.Has("owner"))
            {
                var anyQ = em.CreateEntityQuery(new EntityQueryDesc
                {
                    Any = new[] { ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<GA.Area>() },
                    None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
                });
                var o = Resolve(a.Raw("owner"), anyQ);
                if (o == Entity.Null || !IsLive(o) || !em.HasComponent<PrefabRef>(o) || !(em.HasComponent<GA.Area>(o) || em.HasComponent<GB.Building>(o)))
                    return new ApiError("owner not found (need a building or work-area id): " + a.Raw("owner"), 404);
                if (em.HasComponent<GA.Area>(o))
                {
                    if (!em.HasComponent<Owner>(o)) return new ApiError("area " + Id(o) + " has no owner building; pass the extractor main building id");
                    original = o;
                    owner = em.GetComponentData<Owner>(o).m_Owner;
                }
                else
                {
                    owner = o;
                    var lots = ExtractorLotsOf(o);
                    var bp = PrefabName(em.GetComponentData<PrefabRef>(o).m_Prefab);
                    if (lots.Count == 0) return new ApiError("building " + Id(o) + " (" + bp + ") has no extractor work area; place an extractor main building first (GET /extractors lists them, e.g. 'Agriculture Area Placeholder - Grain')");
                    if (lots.Count > 1 && !string.IsNullOrEmpty(prefabArg))
                        original = lots.FirstOrDefault(l => string.Equals(PrefabName(em.GetComponentData<PrefabRef>(l).m_Prefab), prefabArg, StringComparison.OrdinalIgnoreCase));
                    if (original == Entity.Null) original = lots[0];
                    if (lots.Count > 1) info["workAreasOfOwner"] = lots.Select(l => (object)Id(l)).ToList();
                }
                prefab = em.GetComponentData<PrefabRef>(original).m_Prefab;
                if (!string.IsNullOrEmpty(prefabArg) && !string.Equals(prefabArg, "extractor", StringComparison.OrdinalIgnoreCase)
                    && PrefabName(prefab).IndexOf(prefabArg, StringComparison.OrdinalIgnoreCase) < 0)
                    return new ApiError("owner's work area prefab is '" + PrefabName(prefab) + "', not '" + prefabArg + "' (omit prefab to use it)");
                if (em.HasComponent<LotData>(prefab) && !em.GetComponentData<LotData>(prefab).m_AllowEditing)
                    info["note"] = "LotData.m_AllowEditing is false for this prefab (the in-game 'edit lot' button is hidden); the redraw path is the one used right after placing the building";
                info["kind"] = "extractor";
                info["workArea"] = Id(original);
                info["owner"] = Id(owner);
                info["ownerPrefab"] = owner != Entity.Null && em.HasComponent<PrefabRef>(owner) ? PrefabName(em.GetComponentData<PrefabRef>(owner).m_Prefab) : null;
                try { info["before"] = DescribeArea(original, 32, null); } catch { }
                if (owner != Entity.Null && em.HasComponent<ObjTransform>(owner) && em.HasComponent<LotData>(prefab))
                {
                    var op = em.GetComponentData<ObjTransform>(owner).m_Position.xz;
                    float maxR = em.GetComponentData<LotData>(prefab).m_MaxRadius;
                    var far = new List<object>();
                    for (int i = 0; i < pts.Count; i++) { var dd = math.distance(pts[i], op); if (dd > maxR) far.Add(new List<object> { i, Math.Round(dd) }); }
                    info["maxRadius"] = maxR;
                    if (far.Count > 0) info["cornersBeyondMaxRadius"] = far; // game reports ExceedsLotLimits
                }
                if (a.B("keepBuildingEdge", true))
                {
                    // the in-game redraw (AreaToolSystem with recreate) is seeded with nodes 0 and 1 of the
                    // existing lot, so the drawn work area always keeps that edge at the building
                    var on = em.GetBuffer<GA.Node>(original, true);
                    if (on.Length >= 2)
                    {
                        var n0 = on[0].m_Position.xz; var n1 = on[1].m_Position.xz;
                        pts = WithBuildingEdge(pts, n0, n1, out var how);
                        info["buildingEdge"] = new Dictionary<string, object> { { "edge", Poly(new[] { n0, n1 }) }, { "result", how } };
                    }
                }
            }
            else
            {
                prefab = ResolveAreaPrefab(prefabArg);
                if (prefab == Entity.Null) return new ApiError("area prefab not found: " + prefabArg + " (use 'district', or GET /extractors)");
                if (em.HasComponent<ExtractorAreaData>(prefab) || em.HasComponent<LotData>(prefab))
                    return new ApiError("'" + PrefabName(prefab) + "' is a building work area: pass owner = the extractor main building id (place one first, GET /extractors)");
                if (!em.HasComponent<DistrictData>(prefab))
                    return new ApiError("only districts and extractor work areas are supported (prefab " + PrefabName(prefab) + ")");
                if (IsLocked(prefab)) return new ApiError(PrefabName(prefab) + " is still locked");
                info["kind"] = "district";
                if (a.Has("name")) info["name"] = a.Str("name");
            }

            var tiles = OwnedTilePolys();
            int outside = pts.Count(q => !InOwned(tiles, q));
            if (outside > 0) info["cornersOutsideOwnedTiles"] = outside;
            if (SelfIntersects(pts)) info["shapeWarning"] = "polygon edges cross each other; the game will reject it (InvalidShape)";
            if (dropped > 0) info["droppedDuplicateCorners"] = dropped;

            var job = new AreaJob { Api = this, Prefab = prefab, Original = original, Owner = owner, DryRun = a.B("dryRun"), Name = a.Str("name") };
            foreach (var q in pts) { var w = new float3(q.x, 0, q.y); w.y = TerrainHeight(w); job.Points.Add(w); }
            info["prefab"] = PrefabName(prefab);
            info["corners"] = pts.Count;
            info["polygon"] = Poly(pts);
            info["polygonArea"] = Math.Round(math.abs(PolyArea(pts)));
            if (em.HasComponent<AreaGeometryData>(prefab)) info["geometryFlags"] = em.GetComponentData<AreaGeometryData>(prefab).m_Flags.ToString();
            tool.Enqueue(job);
            return new AreaOperation(this, job, info, a.B("allowDemolish", true));
        }

        // ------------------------------------------------------------------ GET /areas

        /// <summary>GET /areas?kind=district|extractor|all|lot|any[&amp;x&amp;z&amp;radius][&amp;maxNodes=24][&amp;limit=300]
        /// (all = districts + extractor work areas; lot = other building lots; any = both)</summary>
        object AreaList(Pending p)
        {
            var kind = p.Q("kind", "all").ToLowerInvariant();
            float x = p.QF("x", 0), z = p.QF("z", 0), radius = p.QF("radius", 1e7f);
            int maxNodes = (int)p.QF("maxNodes", 24);
            int limit = (int)p.QF("limit", 300);
            var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<GA.Area>(), ComponentType.ReadOnly<GA.Node>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<GA.MapTile>() },
            });
            // building count per district
            var perDistrict = new Dictionary<Entity, int>();
            if (kind == "all" || kind == "district" || kind == "any")
            {
                var bq = em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<GA.CurrentDistrict>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
                using (var cds = bq.ToComponentDataArray<GA.CurrentDistrict>(Allocator.Temp))
                    foreach (var cd in cds) if (cd.m_District != Entity.Null) perDistrict[cd.m_District] = perDistrict.TryGetValue(cd.m_District, out var c) ? c + 1 : 1;
            }
            var res = new List<object>();
            var counts = new Dictionary<string, object>();
            int total = 0;
            using (var arr = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in arr)
                {
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    string k = em.HasComponent<GA.District>(e) ? "district" : em.HasComponent<GA.Extractor>(e) || em.HasComponent<ExtractorAreaData>(prefab) ? "extractor" : em.HasComponent<GA.Lot>(e) ? "lot" : null;
                    if (k == null) continue; // surfaces, spaces
                    // all = districts + extractor work areas; lot = other building lots; any = everything
                    bool want = kind == "any" || kind == k || (kind == "all" && k != "lot");
                    if (!want) continue;
                    if (radius < 1e6f)
                    {
                        var nb = em.GetBuffer<GA.Node>(e, true);
                        if (!em.HasComponent<GA.Geometry>(e) && nb.Length == 0) continue;
                        var c = em.HasComponent<GA.Geometry>(e) ? em.GetComponentData<GA.Geometry>(e).m_CenterPosition.xz : nb[0].m_Position.xz;
                        if (math.distance(c, new float2(x, z)) > radius) continue;
                    }
                    counts[k] = (counts.TryGetValue(k, out var n) ? (int)n : 0) + 1;
                    total++;
                    if (res.Count < limit) res.Add(DescribeArea(e, maxNodes, perDistrict));
                }
            }
            return new Dictionary<string, object>
            {
                { "kind", kind }, { "count", total }, { "byKind", counts }, { "truncated", total > res.Count }, { "areas", res },
                { "note", "size in m2; polygon = [x,z] corners (simplified to maxNodes); district policies = active ones (GET /policies?district=<id> for all)" },
            };
        }

        // ------------------------------------------------------------------ POST /area/delete

        /// <summary>
        /// POST /area/delete {id, dryRun?}: deletes a district the way the info panel's delete button does
        /// (ActionsSection.OnDelete: EndFrameBarrier command buffer AddComponent&lt;Deleted&gt;; the panel offers
        /// delete only for districts among areas). Extractor work areas belong to their building.
        /// </summary>
        object AreaDelete(Pending p)
        {
            if (p.Method != "POST") return new ApiError("use POST /area/delete {id, dryRun?}", 405);
            var a = p.Body;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GA.Area>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var e = Resolve(a.Raw("id"), q);
            if (e == Entity.Null || !IsLive(e) || !em.HasComponent<GA.Area>(e)) return new ApiError("area not found: " + a.Raw("id"), 404);
            if (!em.HasComponent<GA.District>(e))
            {
                if (em.HasComponent<Owner>(e))
                    return new ApiError("area " + Id(e) + " is a work area of building " + Id(em.GetComponentData<Owner>(e).m_Owner) + "; redraw it with POST /area {owner} or bulldoze the building (POST /bulldoze)");
                return new ApiError("only districts can be deleted this way (the in-game delete button is offered for districts)");
            }
            if (Sys<AgentToolSystem>().Busy) return new ApiError("agent tool is busy with another build", 409);
            var info = new Dictionary<string, object> { { "id", Id(e) }, { "area", DescribeArea(e, 16, null) } };
            if (a.B("dryRun")) { info["dryRun"] = true; info["deleted"] = false; return info; }
            var ecb = world.GetOrCreateSystemManaged<Game.EndFrameBarrier>().CreateCommandBuffer();
            ecb.AddComponent<Deleted>(e);
            return new AreaDeleteOperation(this, e, info);
        }

        // ------------------------------------------------------------------ POST /district/name

        /// <summary>POST /district/name {id, name}: NameSystem.SetCustomName like the info panel rename; empty name resets.</summary>
        object DistrictRename(Pending p)
        {
            if (p.Method != "POST") return new ApiError("use POST /district/name {id, name}", 405);
            var a = p.Body;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GA.District>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var s = a.Raw("id")?.ToString();
            var e = Resolve(a.Raw("id"), q);
            if ((e == Entity.Null || !em.HasComponent<GA.District>(e)) && s != null)
            {
                // also accept the current name
                using (var arr = q.ToEntityArray(Allocator.Temp))
                    foreach (var d in arr) if (string.Equals(EntityLabel(d), s, StringComparison.OrdinalIgnoreCase)) { e = d; break; }
            }
            if (e == Entity.Null || !IsLive(e) || !em.HasComponent<GA.District>(e)) return new ApiError("district not found: " + s, 404);
            var before = EntityLabel(e);
            var name = a.Str("name", "");
            SetAreaName(e, name);
            return new Dictionary<string, object> { { "id", Id(e) }, { "before", before }, { "name", string.IsNullOrWhiteSpace(name) ? null : name }, { "label", EntityLabel(e) } };
        }
    }
}
