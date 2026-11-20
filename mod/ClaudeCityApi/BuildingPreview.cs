using System;
using System.Collections.Generic;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// BuildOperation for POST /building that also reads the preview: the temporary building's road
    /// connection. RoadConnectionSystem connects Temp buildings too (FindRoadConnectionJob handles Temp),
    /// filling Building.m_RoadEdge, the field the real call reports as roadConnected after the apply; so a
    /// dry run (or a refused / failed build) reports roadConnected from the preview.
    /// IOperation is re-implemented so this stays a BuildOperation for the Safety wrapper.
    /// </summary>
    public class BuildingOperation : BuildOperation, IOperation
    {
        readonly Api api;
        readonly ObjectJob job;
        bool seen, roadIsPreview;
        Entity road;

        public BuildingOperation(Api api, ObjectJob job, Dictionary<string, object> extra) : base(job, extra) { this.api = api; this.job = job; }

        public new bool Step(out object result)
        {
            if (!job.Done && (job.State == 1 || job.State == 5) && job.Frames >= 4)
            {
                try { if (api.PreviewBuildingRoad(job.Prefab, job.Position, out var r, out var rp)) { seen = true; road = r; roadIsPreview = rp; } }
                catch (Exception e) { Log.Warn("preview road connection: " + e.Message); }
            }
            if (!base.Step(out result)) return false;
            // placedId is only set after a real apply (FindPlaced reads the built building instead)
            if (seen && result is Dictionary<string, object> d && !d.ContainsKey("placedId"))
            {
                d["roadConnected"] = road != Entity.Null;
                d["roadConnectedVia"] = "preview";
                if (road != Entity.Null && !roadIsPreview) d["roadEdge"] = Api.Id(road);
            }
            return true;
        }
    }

    public partial class Api
    {
        EntityQuery previewBuildingQ;
        bool previewBuildingQReady;

        /// <summary>The preview (Temp) building of this prefab nearest pos and its road edge (Temp copies mapped to their original).</summary>
        internal bool PreviewBuildingRoad(Entity prefab, float3 pos, out Entity road, out bool roadIsPreview)
        {
            road = Entity.Null;
            roadIsPreview = false;
            if (!previewBuildingQReady)
            {
                previewBuildingQ = em.CreateEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<ObjTransform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Owner>());
                previewBuildingQReady = true;
            }
            Entity best = Entity.Null; float bd = 30f;
            using (var arr = previewBuildingQ.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (em.GetComponentData<PrefabRef>(e).m_Prefab != prefab) continue;
                    float d = math.distance(em.GetComponentData<ObjTransform>(e).m_Position.xz, pos.xz);
                    if (d < bd) { bd = d; best = e; }
                }
            if (best == Entity.Null) return false;
            road = em.GetComponentData<Building>(best).m_RoadEdge;
            if (road != Entity.Null && em.HasComponent<Temp>(road))
            {
                var orig = em.GetComponentData<Temp>(road).m_Original;
                if (orig != Entity.Null) road = orig;
                else roadIsPreview = true; // a road that only exists in the preview
            }
            return true;
        }

        /// <summary>The 4 lot corners [x, z] of a building (BuildingData lot) or object base (geometry bounds) at pos/rot, in order around it.</summary>
        internal List<object> LotCorners(Entity prefab, float3 pos, quaternion rot)
        {
            float2[] c;
            if (em.HasComponent<BuildingData>(prefab))
            {
                var q = BuildingUtils.CalculateCorners(new ObjTransform(pos, rot), em.GetComponentData<BuildingData>(prefab).m_LotSize);
                c = new[] { q.a.xz, q.b.xz, q.c.xz, q.d.xz };
            }
            else if (em.HasComponent<ObjectGeometryData>(prefab)) c = BaseCorners(pos.xz, rot, em.GetComponentData<ObjectGeometryData>(prefab).m_Bounds);
            else return null;
            var res = new List<object>();
            foreach (var v in c) res.Add(new List<object> { Math.Round(v.x, 1), Math.Round(v.y, 1) });
            return res;
        }
    }
}
