using System;
using System.Collections.Generic;
using System.Linq;
using Game.City;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        /// <summary>
        /// GET /devtree: development points and every development-tree node (service, cost, locked,
        /// whether it can be bought now, required nodes, and the prefabs it unlocks).
        /// POST /devtree {"node": name}: buy that node with development points (DevTreeSystem.Purchase).
        /// </summary>
        object DevTree(Pending p)
        {
            var sys = Sys<DevTreeSystem>();
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<DevTreeNodeData>(), ComponentType.ReadOnly<PrefabData>());
            Dictionary<string, Entity> byName = new Dictionary<string, Entity>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr) byName[PrefabName(e)] = e;

            if (p.Method == "POST")
            {
                var name = p.Body.Str("node");
                if (string.IsNullOrEmpty(name) || !byName.TryGetValue(name, out var node)) return new ApiError("unknown dev tree node " + name);
                int before = sys.points;
                bool wasLocked = IsLocked(node);
                sys.Purchase(node);
                return new Dictionary<string, object>
                {
                    { "node", name }, { "cost", em.GetComponentData<DevTreeNodeData>(node).m_Cost },
                    { "pointsBefore", before }, { "pointsAfter", sys.points }, { "wasLocked", wasLocked },
                    { "purchased", wasLocked && sys.points < before },
                };
            }

            // prefabs unlocked by each node
            var unlocks = new Dictionary<Entity, List<string>>();
            var uq = em.CreateEntityQuery(ComponentType.ReadOnly<UnlockRequirement>(), ComponentType.ReadOnly<PrefabData>());
            using (var arr = uq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var buf = em.GetBuffer<UnlockRequirement>(e, true);
                    for (int i = 0; i < buf.Length; i++)
                    {
                        var r = buf[i].m_Prefab;
                        if (r == e || !em.HasComponent<DevTreeNodeData>(r)) continue;
                        if (!unlocks.TryGetValue(r, out var l)) unlocks[r] = l = new List<string>();
                        l.Add(PrefabName(e));
                    }
                }

            int points = sys.points;
            var nodes = new List<object>();
            foreach (var kv in byName.OrderBy(k => k.Key))
            {
                var e = kv.Value;
                var nd = em.GetComponentData<DevTreeNodeData>(e);
                var reqs = new List<object>();
                bool reqsMet = true;
                if (em.HasBuffer<DevTreeNodeRequirement>(e))
                {
                    var rb = em.GetBuffer<DevTreeNodeRequirement>(e, true);
                    for (int i = 0; i < rb.Length; i++)
                    {
                        if (rb[i].m_Node == Entity.Null) continue;
                        reqs.Add(PrefabName(rb[i].m_Node));
                        if (IsLocked(rb[i].m_Node)) reqsMet = false;
                    }
                }
                bool locked = IsLocked(e);
                bool serviceOpen = nd.m_Service == Entity.Null || !IsLocked(nd.m_Service);
                var d = new Dictionary<string, object>
                {
                    { "name", kv.Key }, { "service", nd.m_Service == Entity.Null ? "" : PrefabName(nd.m_Service) },
                    { "cost", nd.m_Cost }, { "locked", locked },
                    { "buyable", locked && serviceOpen && reqsMet && nd.m_Cost <= points },
                };
                if (reqs.Count > 0) d["requires"] = reqs;
                if (unlocks.TryGetValue(e, out var ul)) d["unlocks"] = ul;
                nodes.Add(d);
            }
            return new Dictionary<string, object> { { "points", points }, { "nodes", nodes } };
        }
    }
}
