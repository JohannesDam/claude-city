using System;
using System.Reflection;
using Game.Common;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace ClaudeCityBridge
{
    /// <summary>
    /// ObjectJob with a reproducible random seed.
    ///
    /// The core ObjectJob calls ObjectToolBaseSystem.CreateDefinitions with RandomSeed.Next(), a fresh seed per job.
    /// The seed picks the building's random variation (sub-objects, sub-nets, sub-areas, props), so a dry run and the
    /// real build of the same request preview different variants: IndustrialManufacturingSignature07 at the same
    /// edge/t/side gave 1063, 1082 and 1064 preview entities in three runs, and only the 1082-entity variant reported
    /// "Invalid Shape" (dry run), while the real build applied its own variant with 0 errors. (The apply path does not
    /// ignore errors: each job applies exactly the preview it evaluated, AgentToolSystem state 5.) The vanilla object
    /// tool keeps its seed while the player moves the preview and re-rolls it only after placing, so what the player
    /// sees is what gets built; this job does the same across requests: the seed is derived from prefab, position
    /// and rotation (or given as "seed"), so a dry run and the real build of the same request generate the same variant.
    /// </summary>
    public class SeededObjectJob : ObjectJob
    {
        public uint Seed;
        NativeList<ControlPoint> pts;
        NativeReference<ObjectToolBaseSystem.AttachmentData> att;

        static readonly FieldInfo SeedField = typeof(RandomSeed).GetField("m_Seed", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        static readonly MethodInfo CreateDefs = typeof(ObjectToolBaseSystem).GetMethod("CreateDefinitions", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        /// <summary>Stable seed for a placement: FNV-1a over the prefab name, the position (0.1 m) and the facing (0.1 degree).</summary>
        public static uint SeedFor(string prefabName, float3 pos, quaternion rot)
        {
            uint h = 2166136261;
            void Mix(int v) { unchecked { for (int i = 0; i < 4; i++) { h ^= (uint)(v >> (8 * i)) & 0xff; h *= 16777619; } } }
            foreach (var ch in prefabName ?? "") Mix(ch);
            Mix((int)math.round(pos.x * 10f));
            Mix((int)math.round(pos.z * 10f));
            var fwd = math.mul(rot, new float3(0, 0, 1));
            Mix((int)math.round(math.degrees(math.atan2(fwd.x, fwd.z)) * 10f));
            return h == 0 ? 1u : h;
        }

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            if (CreateDefs == null) { Result["seedNote"] = "CreateDefinitions not found; random seed"; return base.Create(tool, ecb, deps); }
            pts = new NativeList<ControlPoint>(1, Allocator.Persistent);
            att = new NativeReference<ObjectToolBaseSystem.AttachmentData>(Allocator.Persistent);
            var fwd = math.mul(Rotation, new float3(0, 0, 1));
            pts.Add(new ControlPoint
            {
                m_Position = Position,
                m_HitPosition = Position,
                m_Rotation = Rotation,
                m_Direction = math.normalizesafe(fwd.xz),
                m_HitDirection = new float3(0, 1, 0),
                m_OriginalEntity = Entity.Null,
                m_ElementIndex = new int2(-1, -1),
            });
            object boxed = default(RandomSeed); // m_Seed is private
            SeedField?.SetValue(boxed, Seed);
            var seed = SeedField != null ? (RandomSeed)boxed : RandomSeed.Next();
            // the same arguments as AgentToolSystem.CreateObjectDefinitions, except the seed
            var args = new object[]
            {
                Prefab, Entity.Null, Entity.Null, Entity.Null, Entity.Null, Entity.Null,
                tool.CityConfig.defaultTheme, pts, att, false, tool.CityConfig.leftHandTraffic, false, false,
                0f, 0f, 0f, 0f, 0.1f, seed, Snap.Upright, AgeMask.Sapling, false, default(PlacementOverrides), deps,
            };
            var h = (JobHandle)CreateDefs.Invoke(tool, args);
            h.Complete();
            Result["seed"] = SeedField != null ? (object)(double)Seed : "random (RandomSeed.m_Seed not found)";
            return h;
        }

        public override void Dispose()
        {
            if (pts.IsCreated) pts.Dispose();
            if (att.IsCreated) att.Dispose();
            base.Dispose();
        }
    }
}
