using System;
using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Demolish top-level buildings/objects like the bulldozer: a Delete definition with the
    /// object's own transform. Sub-objects, sub-nets and lots are left to the game's generators.
    /// </summary>
    public class DeleteObjectJob : AgentToolSystem.BuildJob
    {
        public List<Entity> Targets = new List<Entity>();

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            var em = tool.EntityManager;
            int n = 0;
            foreach (var e in Targets)
            {
                if (!em.Exists(e) || em.HasComponent<Deleted>(e) || !em.HasComponent<ObjTransform>(e) || !em.HasComponent<PrefabRef>(e)) continue;
                var tr = em.GetComponentData<ObjTransform>(e);
                var d = ecb.CreateEntity();
                ecb.AddComponent(d, new CreationDefinition { m_Original = e, m_Prefab = em.GetComponentData<PrefabRef>(e).m_Prefab, m_Flags = CreationFlags.Delete });
                ecb.AddComponent(d, default(Updated));
                ecb.AddComponent(d, new ObjectDefinition
                {
                    m_Position = tr.m_Position, m_Rotation = tr.m_Rotation,
                    m_LocalPosition = tr.m_Position, m_LocalRotation = tr.m_Rotation,
                    m_ParentMesh = -1, m_Probability = 100, m_PrefabSubIndex = -1,
                });
                n++;
            }
            Result["targets"] = n;
            return deps;
        }
    }

    public partial class Api
    {
        /// <summary>POST /bulldoze {ids:[...], dryRun}: demolish buildings (not roads).</summary>
        object Bulldoze(Args a)
        {
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy", 409);
            var ids = a.List("ids");
            if (ids == null || ids.Count == 0) return new ApiError("ids required");
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var job = new DeleteObjectJob { DryRun = a.B("dryRun") };
            var names = new List<object>();
            foreach (var id in ids)
            {
                var e = Resolve(id, q);
                if (e == Entity.Null || !em.HasComponent<Game.Buildings.Building>(e)) return new ApiError("not a building: " + id);
                if (em.HasComponent<Owner>(e)) return new ApiError("refusing to delete a sub-building: " + id);
                job.Targets.Add(e);
                names.Add(PrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab));
            }
            tool.Enqueue(job);
            return new BuildOperation(job, new Dictionary<string, object> { { "buildings", names } });
        }
    }
}
