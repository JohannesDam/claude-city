using System;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using GA = Game.Areas;
using GB = Game.Buildings;
using PSubArea = Game.Prefabs.SubArea;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        /// <summary>
        /// GET /extractors[?filter=][&amp;all=true]: extractor main buildings (placeable building prefabs whose prefab
        /// SubArea list contains an area with ExtractorAreaData) with their work-area prefab(s), plus the
        /// district prefab(s). Placing the main building (POST /building) creates its default work area;
        /// POST /area {owner} redraws it.
        /// </summary>
        object Extractors(Pending p)
        {
            var filter = p.Q("filter");
            bool all = p.Q("all", "false") == "true"; // also spawnable variants without a toolbar entry
            // placed counts
            var placed = new Dictionary<Entity, int>();
            var bq = em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var prs = bq.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                foreach (var pr in prs) placed[pr.m_Prefab] = placed.TryGetValue(pr.m_Prefab, out var c) ? c + 1 : 1;
            var areaCounts = new Dictionary<Entity, int>();
            var aq = em.CreateEntityQuery(ComponentType.ReadOnly<GA.Area>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<GA.MapTile>());
            using (var prs = aq.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                foreach (var pr in prs) areaCounts[pr.m_Prefab] = areaCounts.TryGetValue(pr.m_Prefab, out var c) ? c + 1 : 1;

            var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<BuildingData>(), ComponentType.ReadOnly<PSubArea>());
            var res = new List<object>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in arr)
                {
                    var subs = em.GetBuffer<PSubArea>(e, true);
                    var areas = new List<object>();
                    bool hasNodes = em.HasBuffer<SubAreaNode>(e);
                    for (int i = 0; i < subs.Length; i++)
                    {
                        var ap = subs[i].m_Prefab;
                        if (ap == Entity.Null || !em.HasComponent<ExtractorAreaData>(ap)) continue;
                        var ed = em.GetComponentData<ExtractorAreaData>(ap);
                        var d = new Dictionary<string, object>
                        {
                            { "prefab", PrefabName(ap) },
                            { "resource", FeatureLabel(ed.m_MapFeature) },
                            { "requireNaturalResource", ed.m_RequireNaturalResource },
                            { "workAmountFactor", ed.m_WorkAmountFactor },
                            { "objectSpawnFactor", ed.m_ObjectSpawnFactor },
                            { "maxObjectArea", ed.m_MaxObjectArea },
                            { "existing", areaCounts.TryGetValue(ap, out var ac) ? ac : 0 },
                        };
                        if (em.HasComponent<AreaGeometryData>(ap)) d["geometryFlags"] = em.GetComponentData<AreaGeometryData>(ap).m_Flags.ToString();
                        if (em.HasComponent<LotData>(ap))
                        {
                            var ld = em.GetComponentData<LotData>(ap);
                            d["maxRadius"] = ld.m_MaxRadius;
                            d["allowOverlap"] = ld.m_AllowOverlap;
                            d["onWater"] = ld.m_OnWater;
                            d["allowEditing"] = ld.m_AllowEditing;
                        }
                        if (hasNodes)
                        {
                            var nodes = em.GetBuffer<SubAreaNode>(e, true);
                            var r = subs[i].m_NodeRange;
                            if (r.x >= 0 && r.y <= nodes.Length && r.y - r.x >= 3)
                            {
                                var pts = new List<float2>();
                                for (int k = r.x; k < r.y; k++) pts.Add(nodes[k].m_Position.xz);
                                d["defaultSize"] = Math.Round(math.abs(PolyArea(pts)));
                                d["defaultPolygonLocal"] = Poly(pts);
                            }
                        }
                        areas.Add(d);
                    }
                    if (areas.Count == 0) continue;
                    if (!all && !(em.HasComponent<UIObjectData>(e) && em.HasComponent<PlaceableObjectData>(e))) continue;
                    var name = PrefabName(e);
                    if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                        && !areas.Any(x => ((string)((Dictionary<string, object>)x)["prefab"]).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                                        || ((string)((Dictionary<string, object>)x)["resource"]).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    var m = new Dictionary<string, object> { { "name", name }, { "locked", IsLocked(e) } };
                    if (em.HasComponent<UIObjectData>(e))
                    {
                        var ui = em.GetComponentData<UIObjectData>(e);
                        if (ui.m_Group != Entity.Null) m["group"] = PrefabName(ui.m_Group);
                    }
                    else m["placeable"] = false;
                    if (em.HasComponent<PlaceableObjectData>(e)) m["cost"] = em.GetComponentData<PlaceableObjectData>(e).m_ConstructionCost;
                    var bd = em.GetComponentData<BuildingData>(e);
                    m["lot"] = new List<object> { bd.m_LotSize.x, bd.m_LotSize.y };
                    if (em.HasComponent<PlaceholderBuildingData>(e))
                    {
                        var ph = em.GetComponentData<PlaceholderBuildingData>(e);
                        m["placeholder"] = ph.m_Type.ToString();
                        if (ph.m_ZonePrefab != Entity.Null) m["zone"] = PrefabName(ph.m_ZonePrefab);
                    }
                    if (em.HasComponent<BuildingPropertyData>(e))
                    {
                        var bp = em.GetComponentData<BuildingPropertyData>(e);
                        if (bp.m_AllowedManufactured != Game.Economy.Resource.NoResource) m["produces"] = bp.m_AllowedManufactured.ToString();
                    }
                    if (em.HasComponent<ServiceObjectData>(e)) m["service"] = PrefabName(em.GetComponentData<ServiceObjectData>(e).m_Service);
                    m["placed"] = placed.TryGetValue(e, out var pc) ? pc : 0;
                    m["workAreas"] = areas;
                    res.Add(m);
                }
            }
            res = res.OrderBy(x => (bool)((Dictionary<string, object>)x)["locked"]).ThenBy(x => ((Dictionary<string, object>)x).ContainsKey("group") ? 0 : 1).ThenBy(x => (string)((Dictionary<string, object>)x)["name"]).ToList();

            // district prefabs
            var def = DefaultDistrictPrefab();
            var dq = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<DistrictData>(), ComponentType.ReadOnly<AreaGeometryData>());
            var dl = new List<object>();
            using (var arr = dq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                    dl.Add(new Dictionary<string, object> { { "prefab", PrefabName(e) }, { "locked", IsLocked(e) }, { "default", e == def }, { "existing", areaCounts.TryGetValue(e, out var c) ? c : 0 } });

            // every extractor work-area prefab (also those no listed building references)
            var wq = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<ExtractorAreaData>());
            var wl = new List<object>();
            using (var arr = wq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var w = new Dictionary<string, object> { { "prefab", PrefabName(e) }, { "resource", FeatureLabel(em.GetComponentData<ExtractorAreaData>(e).m_MapFeature) }, { "existing", areaCounts.TryGetValue(e, out var c) ? c : 0 } };
                    if (em.HasComponent<LotData>(e)) w["maxRadius"] = em.GetComponentData<LotData>(e).m_MaxRadius;
                    wl.Add(w);
                }

            return new Dictionary<string, object>
            {
                { "count", res.Count },
                { "extractors", res },
                { "districtPrefabs", dl },
                { "workAreaPrefabs", wl },
                { "howTo", "1) POST /building {prefab:<extractor name>, x, z, angle | edge,t,side} places the main building with its default work area; 2) GET /resources to pick a rich spot within maxRadius of the building; 3) POST /area {owner:<building id>, points:[[x,z],...], dryRun:true} previews the redrawn work area (preview.resourceAmount), then without dryRun to apply. Districts: POST /area {prefab:'district', points, name}." },
            };
        }
    }
}
