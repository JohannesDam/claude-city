using System.Collections.Generic;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        /// <summary>GET /unlockinfo?name=X: what unlocks prefab X (its UnlockRequirement entries, recursively one level),
        /// with the requirement's kind (milestone, dev tree node, object built, zone built, citizens, ...) and progress.</summary>
        object UnlockInfo(string name)
        {
            if (string.IsNullOrEmpty(name)) return new ApiError("name required");
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>());
            var matches = new List<Entity>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr) if (PrefabName(e) == name) matches.Add(e);
            if (matches.Count == 0) return new ApiError("no prefab named " + name, 404);
            var res = new List<object>();
            foreach (var e in matches) res.Add(Describe(e, 2));
            return new Dictionary<string, object> { { "prefabs", res } };
        }

        Dictionary<string, object> Describe(Entity e, int depth)
        {
            var d = new Dictionary<string, object> { { "name", PrefabName(e) }, { "locked", IsLocked(e) }, { "kind", RequirementKind(e) } };
            if (em.HasComponent<UnlockRequirementData>(e)) d["progress"] = em.GetComponentData<UnlockRequirementData>(e).m_Progress;
            if (em.HasComponent<ObjectBuiltRequirementData>(e)) d["minimumCount"] = em.GetComponentData<ObjectBuiltRequirementData>(e).m_MinimumCount;
            if (em.HasComponent<StrictObjectBuiltRequirementData>(e))
            {
                var s = em.GetComponentData<StrictObjectBuiltRequirementData>(e);
                d["strictObject"] = s.m_Requirement == Entity.Null ? "" : PrefabName(s.m_Requirement);
                d["minimumCount"] = s.m_MinimumCount;
            }
            if (em.HasComponent<MilestoneData>(e)) d["milestoneIndex"] = em.GetComponentData<MilestoneData>(e).m_Index;
            if (depth > 0 && em.HasBuffer<UnlockRequirement>(e))
            {
                var reqs = new List<object>();
                var buf = em.GetBuffer<UnlockRequirement>(e, true);
                for (int i = 0; i < buf.Length; i++)
                {
                    var r = buf[i].m_Prefab;
                    if (r == e || r == Entity.Null) continue;
                    var rd = Describe(r, depth - 1);
                    rd["flags"] = buf[i].m_Flags.ToString();
                    reqs.Add(rd);
                }
                if (reqs.Count > 0) d["requires"] = reqs;
            }
            return d;
        }

        string RequirementKind(Entity e)
        {
            if (em.HasComponent<MilestoneData>(e)) return "milestone";
            if (em.HasComponent<DevTreeNodeData>(e)) return "devTreeNode";
            if (em.HasComponent<ObjectBuiltRequirementData>(e)) return "objectBuilt";
            if (em.HasComponent<StrictObjectBuiltRequirementData>(e)) return "strictObjectBuilt";
            if (em.HasComponent<ZoneBuiltRequirementData>(e)) return "zoneBuilt";
            if (em.HasComponent<CitizenRequirementData>(e)) return "citizens";
            if (em.HasComponent<ProcessingRequirementData>(e)) return "processing";
            if (em.HasComponent<TransportRequirementData>(e)) return "transport";
            if (em.HasComponent<ForceUnlockRequirementData>(e)) return "forceUnlock";
            if (em.HasComponent<ThemeRequirementData>(e)) return "theme";
            return "prefab";
        }
    }
}
