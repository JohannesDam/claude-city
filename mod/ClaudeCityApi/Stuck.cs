using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using GB = Game.Buildings;
using GCit = Game.Citizens;
using GN = Game.Net;
using GP = Game.Prefabs;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        /// <summary>Best-effort world position of an entity (object, net edge, citizen in a building, household).</summary>
        bool TryPos(Entity e, out float3 pos)
        {
            pos = default;
            for (int hop = 0; hop < 4 && e != Entity.Null && em.Exists(e); hop++)
            {
                if (em.HasComponent<ObjTransform>(e)) { pos = em.GetComponentData<ObjTransform>(e).m_Position; return true; }
                if (em.HasComponent<GN.Curve>(e)) { pos = MathUtils.Position(em.GetComponentData<GN.Curve>(e).m_Bezier, 0.5f); return true; }
                if (em.HasComponent<GN.Node>(e)) { pos = em.GetComponentData<GN.Node>(e).m_Position; return true; }
                if (em.HasComponent<GCit.CurrentBuilding>(e)) { e = em.GetComponentData<GCit.CurrentBuilding>(e).m_CurrentBuilding; continue; }
                if (em.HasComponent<GCit.HouseholdMember>(e)) { e = em.GetComponentData<GCit.HouseholdMember>(e).m_Household; continue; }
                if (em.HasComponent<GB.PropertyRenter>(e)) { e = em.GetComponentData<GB.PropertyRenter>(e).m_Property; continue; }
                break;
            }
            return false;
        }

        class StuckCell
        {
            public int Count;
            public readonly Dictionary<string, int> By = new Dictionary<string, int>();
            public readonly List<string> Samples = new List<string>();
        }

        static void AddCell(Dictionary<int2, StuckCell> cells, float3 pos, float cell, string by, Entity sample, int max = 3)
        {
            var k = new int2((int)math.floor(pos.x / cell), (int)math.floor(pos.z / cell));
            if (!cells.TryGetValue(k, out var c)) cells[k] = c = new StuckCell();
            c.Count++;
            if (by != null) c.By[by] = c.By.TryGetValue(by, out var n) ? n + 1 : 1;
            if (c.Samples.Count < max && sample != Entity.Null) c.Samples.Add(Id(sample));
        }

        static List<object> TopCells(Dictionary<int2, StuckCell> cells, float cell, int limit) =>
            cells.OrderByDescending(kv => kv.Value.Count).Take(limit).Select(kv => (object)new Dictionary<string, object>
            {
                { "centre", new List<object> { Math.Round((kv.Key.x + 0.5) * cell), Math.Round((kv.Key.y + 0.5) * cell) } },
                { "count", kv.Value.Count },
                { "by", kv.Value.By.OrderByDescending(x => x.Value).Take(6).ToDictionary(x => x.Key, x => (object)x.Value) },
                { "samples", kv.Value.Samples },
            }).ToList();

        void FailedRequests<T>(string name, Func<T, Entity> target, Dictionary<int2, StuckCell> cells, float cell, Dictionary<string, object> counts)
            where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<ServiceRequest>(), ComponentType.Exclude<Deleted>());
            int failing = 0, unplaced = 0;
            using (var ents = q.ToEntityArray(Allocator.Temp))
            using (var reqs = q.ToComponentDataArray<ServiceRequest>(Allocator.Temp))
            using (var data = q.ToComponentDataArray<T>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    var sr = reqs[i];
                    if ((sr.m_Flags & ServiceRequestFlags.Reversed) != 0 || sr.m_FailCount == 0) continue;
                    failing++;
                    if (TryPos(target(data[i]), out var pos)) AddCell(cells, pos, cell, name, target(data[i]));
                    else unplaced++;
                }
            if (failing > 0) counts[name] = unplaced > 0 ? (object)new Dictionary<string, object> { { "failing", failing }, { "noPosition", unplaced } } : failing;
        }

        /// <summary>
        /// GET /stuck?cell=256&amp;limit=12 : where connectivity is broken.
        /// citizens: Citizen.m_PenaltyCounter &gt; 0 (CitizenTravelPurposeSystem / TripNeededSystem set 255
        /// when a citizen is in a building without road access or a trip finds no path; it feeds
        /// the TrafficPenalty happiness factor) grouped by home cell and by the building they are in;
        /// buildingsWithoutRoad: top-level buildings with Building.m_RoadEdge == Null;
        /// failedPaths: path owners (vehicles, creatures) with PathFlags.Failed/Stuck grouped by target cell;
        /// failedRequests: service requests with ServiceRequest.m_FailCount &gt; 0 grouped by target cell.
        /// </summary>
        object Stuck(Pending p)
        {
            float cell = math.clamp(p.QF("cell", 256f), 32f, 4000f);
            int limit = (int)math.clamp(p.QF("limit", 12f), 1f, 100f);
            var r = new Dictionary<string, object> { { "cell", cell } };

            // --- citizens with a traffic penalty
            {
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<GCit.Citizen>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
                var homeCells = new Dictionary<int2, StuckCell>();
                var inBuilding = new Dictionary<Entity, int>();
                int pen = 0, high = 0, total = 0, noHome = 0;
                using (var ents = q.ToEntityArray(Allocator.Temp))
                using (var cits = q.ToComponentDataArray<GCit.Citizen>(Allocator.Temp))
                    for (int i = 0; i < ents.Length; i++)
                    {
                        total++;
                        var c = cits[i];
                        if (c.m_PenaltyCounter == 0) continue;
                        pen++;
                        if (c.m_PenaltyCounter >= 128) high++;
                        var e = ents[i];
                        Entity home = Entity.Null;
                        if (em.HasComponent<GCit.HouseholdMember>(e))
                        {
                            var hh = em.GetComponentData<GCit.HouseholdMember>(e).m_Household;
                            if (em.HasComponent<GB.PropertyRenter>(hh)) home = em.GetComponentData<GB.PropertyRenter>(hh).m_Property;
                        }
                        if (home != Entity.Null && em.HasComponent<ObjTransform>(home))
                        {
                            string kind = "home";
                            if (em.HasComponent<GB.Building>(home) && em.GetComponentData<GB.Building>(home).m_RoadEdge == Entity.Null) kind = "home has no road";
                            AddCell(homeCells, em.GetComponentData<ObjTransform>(home).m_Position, cell, kind, home);
                        }
                        else noHome++;
                        if (em.HasComponent<GCit.CurrentBuilding>(e))
                        {
                            var cb = em.GetComponentData<GCit.CurrentBuilding>(e).m_CurrentBuilding;
                            if (cb != Entity.Null) inBuilding[cb] = inBuilding.TryGetValue(cb, out var n) ? n + 1 : 1;
                        }
                    }
                r["citizens"] = new Dictionary<string, object>
                {
                    { "total", total }, { "withPenalty", pen }, { "penaltyAbove128", high }, { "withPenaltyNoHome", noHome },
                    { "byHomeCell", TopCells(homeCells, cell, limit) },
                    { "inBuilding", inBuilding.OrderByDescending(kv => kv.Value).Take(limit).Select(kv =>
                        {
                            var d = new Dictionary<string, object> { { "id", Id(kv.Key) }, { "count", kv.Value } };
                            if (em.HasComponent<GP.PrefabRef>(kv.Key)) d["prefab"] = PrefabName(em.GetComponentData<GP.PrefabRef>(kv.Key).m_Prefab);
                            if (TryPos(kv.Key, out var bp)) d["pos"] = F2(bp);
                            if (em.HasComponent<GB.Building>(kv.Key)) d["roadConnected"] = em.GetComponentData<GB.Building>(kv.Key).m_RoadEdge != Entity.Null;
                            return (object)d;
                        }).ToList() },
                };
            }

            // --- buildings without road access
            {
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.ReadOnly<GP.PrefabRef>(),
                    ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Owner>());
                var cells = new Dictionary<int2, StuckCell>();
                var byPrefab = new Dictionary<string, int>();
                int n = 0, abandoned = 0;
                var list = new List<object>();
                using (var ents = q.ToEntityArray(Allocator.Temp))
                    foreach (var e in ents)
                    {
                        if (em.GetComponentData<GB.Building>(e).m_RoadEdge != Entity.Null) continue;
                        var prefab = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
                        if (em.HasComponent<Game.Objects.OutsideConnection>(e)) continue;
                        n++;
                        if (em.HasComponent<GB.Abandoned>(e)) abandoned++;
                        string kind = em.HasComponent<GP.SpawnableBuildingData>(prefab)
                            ? "zoned:" + (PrefabName(em.GetComponentData<GP.SpawnableBuildingData>(prefab).m_ZonePrefab) ?? "?")
                            : em.HasComponent<GP.ServiceObjectData>(prefab) ? "service" : "other";
                        var pn = PrefabName(prefab);
                        byPrefab[pn] = byPrefab.TryGetValue(pn, out var c) ? c + 1 : 1;
                        var pos = em.GetComponentData<ObjTransform>(e).m_Position;
                        AddCell(cells, pos, cell, kind, e);
                        if (list.Count < 40 && kind != "other")
                            list.Add(new Dictionary<string, object> { { "id", Id(e) }, { "prefab", pn }, { "kind", kind }, { "pos", F2(pos) } });
                    }
                r["buildingsWithoutRoad"] = new Dictionary<string, object>
                {
                    { "count", n }, { "abandoned", abandoned },
                    { "byPrefab", byPrefab.OrderByDescending(kv => kv.Value).Take(15).ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                    { "byCell", TopCells(cells, cell, limit) },
                    { "examples", list },
                };
            }

            // --- path owners whose pathfinding failed
            {
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Pathfind.PathOwner>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
                var cells = new Dictionary<int2, StuckCell>();
                int failed = 0, stuck = 0, total = 0;
                var byKind = new Dictionary<string, int>();
                using (var ents = q.ToEntityArray(Allocator.Temp))
                using (var pos = q.ToComponentDataArray<Game.Pathfind.PathOwner>(Allocator.Temp))
                    for (int i = 0; i < ents.Length; i++)
                    {
                        total++;
                        var st = pos[i].m_State;
                        bool f = (st & Game.Pathfind.PathFlags.Failed) != 0, s = (st & Game.Pathfind.PathFlags.Stuck) != 0;
                        if (!f && !s) continue;
                        if (f) failed++;
                        if (s) stuck++;
                        var e = ents[i];
                        string kind = em.HasComponent<Game.Vehicles.Vehicle>(e) ? "vehicle" : em.HasComponent<Game.Creatures.Creature>(e) ? "pedestrian" : "other";
                        if (em.HasComponent<GP.PrefabRef>(e) && kind == "vehicle") kind = "vehicle:" + PrefabName(em.GetComponentData<GP.PrefabRef>(e).m_Prefab);
                        byKind[kind] = byKind.TryGetValue(kind, out var c) ? c + 1 : 1;
                        var tgt = em.HasComponent<Target>(e) ? em.GetComponentData<Target>(e).m_Target : Entity.Null;
                        if (TryPos(tgt, out var tp)) AddCell(cells, tp, cell, f ? "failed" : "stuck", tgt);
                    }
                r["failedPaths"] = new Dictionary<string, object>
                {
                    { "pathOwners", total }, { "failed", failed }, { "stuck", stuck },
                    { "byKind", byKind.OrderByDescending(kv => kv.Value).Take(15).ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                    { "byTargetCell", TopCells(cells, cell, limit) },
                };
            }

            // --- service requests that failed pathfinding (dispatchers could not reach the target)
            {
                var cells = new Dictionary<int2, StuckCell>();
                var counts = new Dictionary<string, object>();
                FailedRequests<GarbageCollectionRequest>("garbage", x => x.m_Target, cells, cell, counts);
                FailedRequests<MaintenanceRequest>("maintenance", x => x.m_Target, cells, cell, counts);
                FailedRequests<PostVanRequest>("post", x => x.m_Target, cells, cell, counts);
                FailedRequests<FireRescueRequest>("fire", x => x.m_Target, cells, cell, counts);
                FailedRequests<PolicePatrolRequest>("policePatrol", x => x.m_Target, cells, cell, counts);
                FailedRequests<PoliceEmergencyRequest>("policeEmergency", x => x.m_Site, cells, cell, counts);
                FailedRequests<PrisonerTransportRequest>("prisonerTransport", x => x.m_Target, cells, cell, counts);
                FailedRequests<HealthcareRequest>("healthcare", x => x.m_Citizen, cells, cell, counts);
                r["failedRequests"] = new Dictionary<string, object> { { "byService", counts }, { "byTargetCell", TopCells(cells, cell, limit) } };
            }
            r["legend"] = "byCell centre = cell centre in world metres (x, z); samples are entity ids; check a cell with GET /map?x&z or GET /nets?x&z";
            return r;
        }
    }
}
