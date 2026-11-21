using System;
using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using GB = Game.Buildings;
using GO = Game.Objects;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// The extractor building that the vanilla object tool attaches to a specialized-industry hub.
    ///
    /// An "... Area Placeholder" prefab (PlaceholderBuildingData, m_Type ExtractorBuilding) is only the hub and the
    /// owner of the work area. When the player places one, ObjectToolSystem.UpdateDefinitions runs
    /// FindAttachmentBuildingJob: it picks a level-1 spawnable building of the placeholder's zone
    /// (BuildingSpawnGroupData = ZoneData.m_ZoneType of PlaceholderBuildingData.m_ZonePrefab, e.g. "Industrial Ore")
    /// whose lot fits the placeholder lot, and hands it to CreateDefinitions as AttachmentData {m_Entity, m_Offset}.
    /// CreateDefinitionsJob.Execute then creates that building too (UpdateObject with parent = the placeholder
    /// prefab: CreationFlags.Attach, m_Attached), and AttachSystem resolves Attached.m_Parent to the placed hub.
    ///
    /// That attached building is the extractor property: PropertyUtils.ExtractorFindCompanyJob (run by
    /// IndustrialFindPropertySystem) only rents a free ExtractorProperty that has Game.Objects.Attached
    /// (Evaluate returns -8 otherwise) and uses Attached.m_Parent (the hub) for the work area's resource
    /// concentration and to restrict m_AllowedManufactured. A bare placeholder is on the market but never rented:
    /// no company, no workers, nothing extracted. The core ObjectJob/SeededObjectJob passed an empty
    /// AttachmentData, so every hub placed through the API stayed bare.
    /// </summary>
    internal static class ExtractorAttachment
    {
        /// <summary>
        /// FindAttachmentBuildingJob.Execute, same candidates, same random draws (RandomSeed.GetRandom(2000000)).
        /// Returns false when the prefab is not a placeholder building or no spawnable fits.
        /// </summary>
        public static bool TryPick(EntityManager em, Entity placeholderPrefab, RandomSeed seed, out ObjectToolBaseSystem.AttachmentData data, out string note)
        {
            data = default;
            note = null;
            if (placeholderPrefab == Entity.Null || !em.HasComponent<PlaceholderBuildingData>(placeholderPrefab) || !em.HasComponent<BuildingData>(placeholderPrefab)) return false;
            var ph = em.GetComponentData<PlaceholderBuildingData>(placeholderPrefab);
            if (ph.m_ZonePrefab == Entity.Null || !em.HasComponent<ZoneData>(ph.m_ZonePrefab)) { note = "placeholder has no zone prefab"; return false; }
            var zoneType = em.GetComponentData<ZoneData>(ph.m_ZonePrefab).m_ZoneType;
            var phData = em.GetComponentData<BuildingData>(placeholderPrefab);
            int2 lotSize = phData.m_LotSize;
            var corners = new bool2((phData.m_Flags & BuildingFlags.LeftAccess) != 0, (phData.m_Flags & BuildingFlags.RightAccess) != 0);

            // ObjectToolSystem.m_BuildingQuery: BuildingData, SpawnableBuildingData, BuildingSpawnGroupData, PrefabData
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingData>(), ComponentType.ReadOnly<SpawnableBuildingData>(),
                ComponentType.ReadOnly<BuildingSpawnGroupData>(), ComponentType.ReadOnly<PrefabData>());
            q.ResetFilter();
            q.SetSharedComponentFilter(new BuildingSpawnGroupData(zoneType));
            try
            {
                var random = seed.GetRandom(2000000);
                Entity best = Entity.Null;
                BuildingData bestData = default;
                float bestScore = 0f;
                int candidates = 0;
                using (var arr = q.ToEntityArray(Allocator.Temp))
                    foreach (var e in arr)
                    {
                        if (em.GetComponentData<SpawnableBuildingData>(e).m_Level != 1) continue;
                        var bd = em.GetComponentData<BuildingData>(e);
                        int2 lot = bd.m_LotSize;
                        var c = new bool2((bd.m_Flags & BuildingFlags.LeftAccess) != 0, (bd.m_Flags & BuildingFlags.RightAccess) != 0);
                        if (!math.all(lot <= lotSize)) continue;
                        candidates++;
                        int2 rest = math.select(lotSize - lot, new int2(0), lot == lotSize - 1);
                        float score = lot.x * lot.y * random.NextFloat(1f, 1.05f);
                        score += rest.x * lot.y * random.NextFloat(0.95f, 1f);
                        score += lotSize.x * rest.y * random.NextFloat(0.55f, 0.6f);
                        score /= lotSize.x * lotSize.y;
                        score *= math.csum(math.select(new float2(0.01f), new float2(0.5f), corners == c));
                        if (score > bestScore) { best = e; bestData = bd; bestScore = score; }
                    }
                if (best == Entity.Null) { note = "no level-1 spawnable of zone type " + zoneType.m_Index + " fits lot " + lotSize.x + "x" + lotSize.y + " (" + candidates + " candidates)"; return false; }
                data.m_Entity = best;
                data.m_Offset = new float3(0f, 0f, (lotSize.y - bestData.m_LotSize.y) * 4f);
                return true;
            }
            finally { q.ResetFilter(); }
        }
    }

    public partial class Api
    {
        /// <summary>Extractor hub (placeholder) status: its facing angle, the attached extractor building and the company renting it.</summary>
        internal Dictionary<string, object> ExtractorHubStatus(Entity hub)
        {
            var d = new Dictionary<string, object>();
            if (hub == Entity.Null || !em.Exists(hub) || !em.HasComponent<PrefabRef>(hub)) return d;
            var hp = em.GetComponentData<PrefabRef>(hub).m_Prefab;
            if (!em.HasComponent<PlaceholderBuildingData>(hp)) return d;
            if (em.HasComponent<ObjTransform>(hub))
            {
                var fwd = math.mul(em.GetComponentData<ObjTransform>(hub).m_Rotation, new float3(0, 0, 1));
                d["angle"] = Math.Round(math.degrees(math.atan2(fwd.x, fwd.z)), 2); // POST /building {x, z, angle} convention
            }
            // the attached extractor building: Attachment.m_Attached on the hub, else any building whose Attached.m_Parent is the hub
            Entity att = Entity.Null;
            if (em.HasComponent<GO.Attachment>(hub)) att = em.GetComponentData<GO.Attachment>(hub).m_Attached;
            if (att == Entity.Null || !IsLive(att))
            {
                att = Entity.Null;
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<GO.Attached>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
                using (var arr = q.ToEntityArray(Allocator.Temp))
                    foreach (var e in arr)
                        if (em.GetComponentData<GO.Attached>(e).m_Parent == hub) { att = e; break; }
            }
            if (att == Entity.Null)
            {
                d["extractorBuilding"] = null;
                d["problem"] = "bare hub: no attached extractor building, so no extractor company can rent it (placed without AttachmentData). Bulldoze and place it again with the fixed POST /building, then redraw the work area.";
                return d;
            }
            var b = new Dictionary<string, object> { { "id", Id(att) }, { "prefab", PrefabOf(att) } };
            Entity company = Entity.Null;
            if (em.HasBuffer<GB.Renter>(att))
            {
                var rb = em.GetBuffer<GB.Renter>(att, true);
                for (int i = 0; i < rb.Length; i++)
                    if (em.Exists(rb[i].m_Renter) && em.HasComponent<Game.Companies.CompanyData>(rb[i].m_Renter)) { company = rb[i].m_Renter; break; }
            }
            b["onMarket"] = em.HasComponent<GB.PropertyOnMarket>(att);
            if (company != Entity.Null) b["company"] = new Dictionary<string, object> { { "id", Id(company) }, { "prefab", PrefabOf(company) } };
            else b["company"] = null;
            d["extractorBuilding"] = b;
            return d;
        }
    }
}
