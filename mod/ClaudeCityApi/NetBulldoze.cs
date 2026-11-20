using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using NetEdge = Game.Net.Edge;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Deletes net edges (roads, tracks, power lines, pipes) exactly like the bulldozer
    /// (BulldozeToolSystem.CreateDefinitionsJob.AddEntity for an Edge): a CreationDefinition
    /// { m_Original = edge, m_Flags = Delete } with a NetCourse along the edge's own curve and
    /// nodes. The game's generators then remove orphaned nodes, lanes, sub-objects and zone blocks.
    /// Buildings in Targets (from DeleteObjectJob) are demolished in the same preview.
    /// </summary>
    public class DeleteNetJob : DeleteObjectJob
    {
        public List<Entity> Edges = new List<Entity>();
        public Api Api;
        HashSet<Entity> before;

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            deps = base.Create(tool, ecb, deps);
            var em = tool.EntityManager;
            int n = 0;
            foreach (var e in Edges)
            {
                if (!em.Exists(e) || em.HasComponent<Deleted>(e) || !em.HasComponent<NetEdge>(e) || !em.HasComponent<Curve>(e)) continue;
                var edge = em.GetComponentData<NetEdge>(e);
                var cv = em.GetComponentData<Curve>(e).m_Bezier;
                var d = ecb.CreateEntity();
                ecb.AddComponent(d, default(Updated));
                ecb.AddComponent(d, new NetCourse
                {
                    m_Curve = cv,
                    m_Length = MathUtils.Length(cv),
                    m_FixedIndex = em.HasComponent<Fixed>(e) ? em.GetComponentData<Fixed>(e).m_Index : -1,
                    m_StartPosition = new CoursePos { m_Entity = edge.m_Start, m_Position = cv.a, m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(cv)), m_CourseDelta = 0f },
                    m_EndPosition = new CoursePos { m_Entity = edge.m_End, m_Position = cv.d, m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(cv)), m_CourseDelta = 1f },
                });
                ecb.AddComponent(d, new CreationDefinition { m_Original = e, m_Flags = CreationFlags.Delete });
                n++;
            }
            Result["edgeTargets"] = n;
            if (!DryRun && Api != null) before = Api.SnapshotEdges();
            return deps;
        }

        public override void AfterApply(AgentToolSystem tool)
        {
            base.AfterApply(tool);
            if (before == null) return;
            try
            {
                var em = tool.EntityManager;
                var live = Api.SnapshotEdges();
                var targets = new HashSet<Entity>(Edges);
                Result["deletedEdgeIds"] = Edges.Where(e => !live.Contains(e)).Select(e => (object)Util.Id(e)).ToList();
                var notDeleted = Edges.Where(e => live.Contains(e)).Select(e => (object)Util.Id(e)).ToList();
                if (notDeleted.Count > 0) Result["notDeletedEdgeIds"] = notDeleted;
                // side effects: neighbours the game re-created (merge/split) get new ids
                Result["otherStaleEdgeIds"] = before.Where(e => !live.Contains(e) && !targets.Contains(e)).Take(100).Select(e => (object)Util.Id(e)).ToList();
                Result["newEdgeIds"] = live.Where(e => !before.Contains(e)).Take(100).Select(e => (object)Util.Id(e)).ToList();
            }
            catch (Exception e) { Result["trackError"] = e.Message; }
        }
    }

    public partial class Api
    {
        /// <summary>
        /// POST /bulldoze — net edges and/or buildings, previewed and applied through the bulldozer's own definitions:
        ///   edges:[ids]                       stale ids are skipped and listed in selection.missing
        ///   box:[x0,z0,x1,z1] + nets:"Small Road|Medium Road" (or prefabFilter; "*" = any net, incl. pipes/power lines)
        ///   polyline:[[x,z],...] + tolerance + nets
        ///   box + buildings:true              every top-level building in the box (keepServices:true skips service
        ///                                     buildings; buildingFilter:"A|B" name substrings)
        ///   ids:[building ids]
        ///   maxDelete (edges, default 500), maxBuildings (default 500), dryRun
        /// One call can do a whole phase of a district teardown; the dry run lists edges, buildings, households and jobs.
        /// </summary>
        object BulldozeNets(Args a)
        {
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy", 409);
            bool hasBox = a.Has("box");
            bool netFilter = a.Has("nets") || a.Has("prefabFilter");
            bool wantBuildings = a.B("buildings");
            // box without a net filter selects buildings only
            var edgeArgs = a;
            if (hasBox && !netFilter) { var d = new Dictionary<string, object>(a.RawDict); d.Remove("box"); edgeArgs = new Args(d); }
            bool selectEdges = edgeArgs.Has("edges") || edgeArgs.Has("box") || edgeArgs.Has("polyline");
            if (!selectEdges && !(hasBox && wantBuildings) && a.List("ids") == null)
                return new ApiError("nothing selected: give edges:[ids], nets with box/polyline, buildings:true with box, or ids:[building ids]");
            var sel = selectEdges ? SelectEdges(edgeArgs, null, true) : new EdgeSel();
            if (sel.Error != null) return sel.Error;
            int maxDelete = math.clamp(a.I("maxDelete", 500), 1, 5000);
            int maxBuildings = math.clamp(a.I("maxBuildings", 500), 1, 5000);
            var job = new DeleteNetJob { DryRun = a.B("dryRun"), Api = this };
            var edgesInfo = new List<object>();
            var byPrefab = new Dictionary<string, object>();
            var skipped = new List<object>();
            foreach (var e in sel.Edges)
            {
                if (em.HasComponent<Fixed>(e)) { skipped.Add(new Dictionary<string, object> { { "id", Id(e) }, { "reason", "part of a fixed net group; not supported" } }); continue; }
                job.Edges.Add(e);
                var b = EdgeBrief(e);
                var pn = (b["prefab"] as string) ?? "?";
                byPrefab[pn] = (byPrefab.TryGetValue(pn, out var c) ? (int)c : 0) + 1;
                if (edgesInfo.Count < 300) edgesInfo.Add(b);
            }
            if (job.Edges.Count > maxDelete) return new ApiError("would delete " + job.Edges.Count + " edges (> maxDelete " + maxDelete + "); raise maxDelete if intended") { { "edgesByPrefab", byPrefab } };

            // buildings: explicit ids and/or everything in the box
            var bq = em.CreateEntityQuery(ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var seenB = new HashSet<Entity>();
            var ids = a.List("ids");
            if (ids != null)
                foreach (var id in ids)
                {
                    var b = Resolve(id, bq);
                    if (b == Entity.Null || !em.HasComponent<Building>(b)) return new ApiError("not a building: " + id);
                    if (em.HasComponent<Owner>(b)) return new ApiError("refusing to delete a sub-building: " + id);
                    if (seenB.Add(b)) job.Targets.Add(b);
                }
            int keptServices = 0;
            if (hasBox && wantBuildings)
            {
                if (!TryBox(a.Raw("box"), out var bmin, out var bmax)) return new ApiError("box must be [x0,z0,x1,z1]");
                bool keepServices = a.B("keepServices");
                var bt = NameTokens(a.Str("buildingFilter"));
                using (var arr = bq.ToEntityArray(Allocator.Temp))
                    foreach (var b in arr)
                    {
                        if (em.HasComponent<Owner>(b) || seenB.Contains(b)) continue;
                        if (!InBox(em.GetComponentData<ObjTransform>(b).m_Position.xz, bmin, bmax)) continue;
                        var pf = em.GetComponentData<PrefabRef>(b).m_Prefab;
                        if (keepServices && em.HasComponent<ServiceObjectData>(pf)) { keptServices++; continue; }
                        if (!NameMatches(PrefabName(pf), bt)) continue;
                        seenB.Add(b);
                        job.Targets.Add(b);
                    }
            }
            if (job.Targets.Count > maxBuildings) return new ApiError("would demolish " + job.Targets.Count + " buildings (> maxBuildings " + maxBuildings + "); raise maxBuildings if intended");

            var info = new Dictionary<string, object> { { "edgeCount", job.Edges.Count }, { "edgesByPrefab", byPrefab }, { "edges", edgesInfo }, { "selection", sel.Report() } };
            if (job.Edges.Count > edgesInfo.Count) info["edgesListed"] = edgesInfo.Count;
            if (job.Targets.Count > 0 || wantBuildings) info["buildings"] = BuildingsImpact(job.Targets);
            if (keptServices > 0) info["keptServiceBuildings"] = keptServices;
            if (skipped.Count > 0) info["skipped"] = skipped;
            if (job.Edges.Count == 0 && job.Targets.Count == 0) { info["applied"] = false; info["note"] = "nothing to delete"; return info; }
            if (job.Edges.Count > 0) info["impact"] = EdgeDeletionImpact(new HashSet<Entity>(job.Edges));
            tool.Enqueue(job);
            return new BuildOperation(job, info);
        }

        /// <summary>Buildings to demolish: counts per kind, households/residents, companies, jobs (max workplaces) and current workers.</summary>
        Dictionary<string, object> BuildingsImpact(List<Entity> buildings)
        {
            int households = 0, residents = 0, companies = 0, workplaces = 0, workers = 0;
            var byKind = new Dictionary<string, object>();
            var list = new List<object>();
            foreach (var b in buildings)
            {
                var pf = em.GetComponentData<PrefabRef>(b).m_Prefab;
                string kind;
                if (em.HasComponent<ServiceObjectData>(pf)) kind = "service: " + PrefabName(em.GetComponentData<ServiceObjectData>(pf).m_Service);
                else if (em.HasComponent<SpawnableBuildingData>(pf)) kind = "zoned: " + (PrefabName(em.GetComponentData<SpawnableBuildingData>(pf).m_ZonePrefab) ?? "?");
                else kind = "other";
                byKind[kind] = (byKind.TryGetValue(kind, out var c) ? (int)c : 0) + 1;
                int hh = 0, res = 0, co = 0, wp = 0, wk = 0;
                if (em.HasBuffer<Renter>(b))
                {
                    var rb = em.GetBuffer<Renter>(b, true);
                    for (int i = 0; i < rb.Length; i++)
                    {
                        var r = rb[i].m_Renter;
                        if (em.HasComponent<Game.Citizens.Household>(r))
                        {
                            hh++;
                            if (em.HasBuffer<Game.Citizens.HouseholdCitizen>(r)) res += em.GetBuffer<Game.Citizens.HouseholdCitizen>(r, true).Length;
                        }
                        else if (em.HasComponent<Game.Companies.WorkProvider>(r))
                        {
                            co++;
                            wp += em.GetComponentData<Game.Companies.WorkProvider>(r).m_MaxWorkers;
                            if (em.HasBuffer<Game.Companies.Employee>(r)) wk += em.GetBuffer<Game.Companies.Employee>(r, true).Length;
                        }
                    }
                }
                if (em.HasComponent<Game.Companies.WorkProvider>(b))
                {
                    wp += em.GetComponentData<Game.Companies.WorkProvider>(b).m_MaxWorkers;
                    if (em.HasBuffer<Game.Companies.Employee>(b)) wk += em.GetBuffer<Game.Companies.Employee>(b, true).Length;
                }
                households += hh; residents += res; companies += co; workplaces += wp; workers += wk;
                if (list.Count < 300)
                {
                    var d = new Dictionary<string, object> { { "id", Id(b) }, { "prefab", PrefabName(pf) }, { "kind", kind }, { "pos", F2(em.GetComponentData<ObjTransform>(b).m_Position) } };
                    if (hh > 0) { d["households"] = hh; d["residents"] = res; }
                    if (wp > 0) { d["workplaces"] = wp; d["workers"] = wk; }
                    list.Add(d);
                }
            }
            var r2 = new Dictionary<string, object>
            {
                { "count", buildings.Count }, { "byKind", byKind }, { "households", households }, { "residents", residents },
                { "companies", companies }, { "workplaces", workplaces }, { "workers", workers }, { "list", list },
            };
            if (buildings.Count > list.Count) r2["listed"] = list.Count;
            return r2;
        }

        /// <summary>What else a deletion touches: buildings fronting the edges, zoned cells on their blocks, nodes left without edges.</summary>
        Dictionary<string, object> EdgeDeletionImpact(HashSet<Entity> edges)
        {
            var r = new Dictionary<string, object>();
            if (edges.Count == 0) return r;
            int lose = 0;
            var samples = new List<object>();
            var bq = em.CreateEntityQuery(ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = bq.ToEntityArray(Allocator.Temp))
                foreach (var b in arr)
                {
                    var road = em.GetComponentData<Building>(b).m_RoadEdge;
                    if (road == Entity.Null || !edges.Contains(road)) continue;
                    lose++;
                    if (samples.Count < 20)
                    {
                        var s = new Dictionary<string, object> { { "id", Id(b) }, { "prefab", PrefabName(em.GetComponentData<PrefabRef>(b).m_Prefab) } };
                        if (em.HasComponent<ObjTransform>(b)) s["pos"] = F2(em.GetComponentData<ObjTransform>(b).m_Position);
                        samples.Add(s);
                    }
                }
            r["buildingsFrontingDeletedEdges"] = lose;
            if (samples.Count > 0) r["buildingSamples"] = samples;
            int zoned = 0, occupied = 0;
            var orphans = new HashSet<Entity>();
            foreach (var e in edges)
            {
                if (em.HasBuffer<SubBlock>(e))
                {
                    var sb = em.GetBuffer<SubBlock>(e, true);
                    for (int i = 0; i < sb.Length; i++)
                    {
                        var blk = sb[i].m_SubBlock;
                        if (!em.HasBuffer<Cell>(blk)) continue;
                        var cells = em.GetBuffer<Cell>(blk, true);
                        for (int k = 0; k < cells.Length; k++)
                        {
                            if (cells[k].m_Zone.m_Index == 0 || (cells[k].m_State & CellFlags.Visible) == 0) continue;
                            zoned++;
                            if ((cells[k].m_State & CellFlags.Occupied) != 0) occupied++;
                        }
                    }
                }
                var ed = em.GetComponentData<NetEdge>(e);
                foreach (var n in new[] { ed.m_Start, ed.m_End })
                {
                    if (!em.HasBuffer<ConnectedEdge>(n)) continue;
                    var ce = em.GetBuffer<ConnectedEdge>(n, true);
                    bool all = true;
                    for (int i = 0; i < ce.Length; i++) if (!edges.Contains(ce[i].m_Edge) && !em.HasComponent<Owner>(ce[i].m_Edge)) { all = false; break; }
                    if (all) orphans.Add(n);
                }
            }
            r["zonedCellsOnTheirBlocks"] = zoned;
            r["occupiedZonedCells"] = occupied;
            r["nodesLeftWithoutEdges"] = orphans.Count;
            return r;
        }
    }
}
