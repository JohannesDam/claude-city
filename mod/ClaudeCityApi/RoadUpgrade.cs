using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using NetEdge = Game.Net.Edge;
using AgeMask = Game.Tools.AgeMask;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Road upgrades (Trees, Grass, Wide Sidewalk, Sound Barrier, Lighting, Bikelane, ...) on existing
    /// edges, written exactly like NetToolSystem.CreateDefinitionsJob.CreateUpgrade does for a
    /// click/drag with an upgrade prefab:
    ///   CreationDefinition { m_Original = edge, m_Prefab = the edge's OWN prefab,
    ///                        m_Flags = SubElevation|Align (+ Upgrade|Parent when something changes) }
    ///   Upgraded { m_Flags = (old.Upgraded &amp; ~remove) | (add &amp; ~oldFlags) }
    ///   SubReplacement buffer (street-tree species per side), NetCourse along the edge's own curve/nodes.
    /// add/remove come from the upgrade prefab's PlaceableNetData.m_SetUpgradeFlags / m_UnsetUpgradeFlags
    /// (the prefab's flags describe the RIGHT side; the left side uses them mirrored).
    /// </summary>
    public class NetUpgradeJob : AgentToolSystem.BuildJob
    {
        public class Item
        {
            public Entity Edge;
            public CompositionFlags NewFlags;
            public bool Changed;
            public bool SubChanged;
            public List<SubReplacement> SubReplacements = new List<SubReplacement>();
            public CoursePosFlags StartFlags, EndFlags;
        }

        public List<Item> Items = new List<Item>();
        public Api Api; // optional: reports the edge ids after the apply (EdgeRenew.cs)
        HashSet<Entity> before;
        List<KeyValuePair<Entity, Bezier4x3>> requested;

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            var em = tool.EntityManager;
            if (!DryRun && Api != null)
            {
                try { before = Api.SnapshotEdges(); requested = Api.EdgeCurves(Items.Select(it => it.Edge)); }
                catch (Exception ex) { Result["trackError"] = ex.Message; }
            }
            int n = 0;
            foreach (var it in Items)
            {
                var e = it.Edge;
                if (!em.Exists(e) || em.HasComponent<Deleted>(e) || !em.HasComponent<NetEdge>(e)) continue;
                var edge = em.GetComponentData<NetEdge>(e);
                var cv = em.GetComponentData<Curve>(e).m_Bezier;
                var flags = CreationFlags.SubElevation | CreationFlags.Align;
                if (it.Changed || it.SubChanged) flags |= CreationFlags.Upgrade | CreationFlags.Parent;
                var d = ecb.CreateEntity();
                ecb.AddComponent(d, new CreationDefinition
                {
                    m_Original = e,
                    m_Prefab = em.GetComponentData<PrefabRef>(e).m_Prefab,
                    m_Flags = flags,
                });
                ecb.AddComponent(d, new Upgraded { m_Flags = it.NewFlags });
                var buf = ecb.AddBuffer<SubReplacement>(d);
                foreach (var s in it.SubReplacements) buf.Add(s);
                ecb.AddComponent(d, default(Updated));
                var course = new NetCourse
                {
                    m_Curve = cv,
                    m_Length = MathUtils.Length(cv),
                    m_FixedIndex = em.HasComponent<Fixed>(e) ? em.GetComponentData<Fixed>(e).m_Index : -1,
                    m_StartPosition = new CoursePos
                    {
                        m_Entity = edge.m_Start, m_Position = cv.a, m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(cv)),
                        m_CourseDelta = 0f, m_Flags = it.StartFlags, // m_ParentMesh stays 0 like vanilla: keeps the exact curve (no terrain re-fit)
                    },
                    m_EndPosition = new CoursePos
                    {
                        m_Entity = edge.m_End, m_Position = cv.d, m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(cv)),
                        m_CourseDelta = 1f, m_Flags = it.EndFlags,
                    },
                };
                ecb.AddComponent(d, course);
                n++;
            }
            Result["definitions"] = n;
            return deps;
        }

        public override void AfterApply(AgentToolSystem tool)
        {
            if (Api == null || before == null || requested == null) return;
            try { Api.ReportRenewedEdges(requested, before, Result); }
            catch (Exception e) { Result["trackError"] = e.Message; }
        }
    }

    public partial class Api
    {
        // CompositionFlags masks as used by NetToolSystem.AddControlPoints
        const CompositionFlags.General GeneralOptionMask = (CompositionFlags.General)0x380000;     // WideMedian | Primary/SecondaryMiddleBeautification
        const CompositionFlags.Side SideMiddleClearMask = (CompositionFlags.Side)0x40223000;       // cleared from side flags when upgrading the median
        const CompositionFlags.Side SideDirectionalMask = (CompositionFlags.Side)0x1F000000;       // Forbid*Turn/Straight, Add/RemoveCrosswalk (node-end specific)
        const CompositionFlags.General GeneralNodeMask = (CompositionFlags.General)0x1C086F1;      // CompositionFlags.NODE_MASK_GENERAL (traffic lights, stops, roundabouts ...)

        static CompositionFlags Mirror(CompositionFlags f) => new CompositionFlags(f.m_General, f.m_Right, f.m_Left);
        static bool IsEmpty(CompositionFlags f) => f.m_General == 0 && f.m_Left == 0 && f.m_Right == 0;

        static string FlagNames<T>(T value) where T : Enum
        {
            ulong v = Convert.ToUInt64(value);
            if (v == 0) return "";
            var names = new List<string>();
            foreach (T x in Enum.GetValues(typeof(T)))
            {
                ulong b = Convert.ToUInt64(x);
                if (b != 0 && (b & (b - 1)) == 0 && (v & b) == b) names.Add(x.ToString());
            }
            return string.Join("|", names);
        }

        public static object FlagsJson(CompositionFlags f)
        {
            var d = new Dictionary<string, object>();
            if (f.m_General != 0) d["general"] = FlagNames(f.m_General);
            if (f.m_Left != 0) d["left"] = FlagNames(f.m_Left);
            if (f.m_Right != 0) d["right"] = FlagNames(f.m_Right);
            return d;
        }

        bool IsNetUpgradePrefab(Entity e)
        {
            if (!em.HasComponent<PlaceableNetData>(e)) return false;
            var p = em.GetComponentData<PlaceableNetData>(e);
            return !IsEmpty(p.m_SetUpgradeFlags) || !IsEmpty(p.m_UnsetUpgradeFlags);
        }

        /// <summary>GET /roadupgrades: upgrade prefabs with their composition flags.</summary>
        object RoadUpgrades()
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<PlaceableNetData>());
            var res = new List<object>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (!IsNetUpgradePrefab(e)) continue;
                    var p = em.GetComponentData<PlaceableNetData>(e);
                    var set = p.m_SetUpgradeFlags; var unset = p.m_UnsetUpgradeFlags;
                    string kind = (set.m_General & GeneralNodeMask) != 0 ? "node (not supported here)"
                        : ((set.m_Left | set.m_Right | unset.m_Left | unset.m_Right) & SideDirectionalMask) != 0 ? "node-end/directional (not supported here)"
                        : "edge";
                    res.Add(new Dictionary<string, object>
                    {
                        { "name", PrefabName(e) }, { "locked", IsLocked(e) }, { "kind", kind },
                        { "set", FlagsJson(set) }, { "unset", FlagsJson(unset) },
                        { "costPer8m", p.m_DefaultConstructionCost }, { "upkeep", p.m_DefaultUpkeepCost },
                    });
                }
            res.Sort((a, b) => string.Compare((string)((Dictionary<string, object>)a)["name"], (string)((Dictionary<string, object>)b)["name"], StringComparison.Ordinal));
            return new Dictionary<string, object> { { "count", res.Count }, { "upgrades", res } };
        }

        /// <summary>
        /// POST /roadupgrade {edges:[ids], upgrade:"Trees", side:"both|left|right|middle", remove:false,
        ///                    tree:"&lt;tree prefab&gt;", treeAge:"mature", dryRun}
        /// left/right are relative to the edge direction (start -> end node, see /nets).
        /// </summary>
        object RoadUpgrade(Args a)
        {
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy", 409);
            var name = a.Str("upgrade", a.Str("prefab", ""));
            var up = FindPrefab(name, IsNetUpgradePrefab);
            if (up == Entity.Null) return new ApiError("road upgrade prefab not found: " + name + " (GET /roadupgrades lists them)");
            bool remove = a.B("remove");
            if (!remove && IsLocked(up)) return new ApiError(PrefabName(up) + " is still locked");
            var pnd = em.GetComponentData<PlaceableNetData>(up);
            var set = pnd.m_SetUpgradeFlags; var unset = pnd.m_UnsetUpgradeFlags;
            if ((set.m_General & GeneralNodeMask) != 0) return new ApiError(PrefabName(up) + " is a node upgrade (intersection); not supported by /roadupgrade");
            if (((set.m_Left | set.m_Right | unset.m_Left | unset.m_Right) & SideDirectionalMask) != 0)
                return new ApiError(PrefabName(up) + " is a directional node-end upgrade (turn restriction / crosswalk); not supported by /roadupgrade");

            var side = a.Str("side", "both").ToLowerInvariant();
            if (side != "both" && side != "left" && side != "right" && side != "middle") return new ApiError("side must be both|left|right|middle");

            // street-tree species (like placing a tree object onto a road with the object tool)
            bool treeUpgrade = (set.m_General & CompositionFlags.General.SecondaryMiddleBeautification) != 0
                || ((set.m_Left | set.m_Right) & CompositionFlags.Side.SecondaryBeautification) != 0;
            Entity treePrefab = Entity.Null;
            if (a.Has("tree"))
            {
                treePrefab = FindPrefab(a.Str("tree"), e => em.HasComponent<PlaceableObjectData>(e) && em.GetComponentData<PlaceableObjectData>(e).m_SubReplacementType == SubReplacementType.Tree);
                if (treePrefab == Entity.Null) return new ApiError("tree prefab not found (needs a tree object that can be used as street tree): " + a.Str("tree"));
                if (!treeUpgrade) return new ApiError("'tree' only applies to the Trees upgrade");
                if (IsLocked(treePrefab)) return new ApiError(PrefabName(treePrefab) + " is still locked");
            }
            var ageName = a.Str("treeAge", "mature").ToLowerInvariant();
            AgeMask age = ageName == "sapling" ? AgeMask.Sapling : ageName == "young" ? AgeMask.Young : ageName == "elderly" ? AgeMask.Elderly
                : ageName == "mixed" ? AgeMask.Young | AgeMask.Mature | AgeMask.Elderly : AgeMask.Mature;

            // edges:[ids] (stale ids skipped + reported; strict:true restores all-or-nothing) and/or box/polyline (+prefabFilter)
            var sel = SelectEdges(a, IsRoadEdge, false);
            if (sel.Error != null) return sel.Error;
            var edges = sel.Edges;
            // nodes shared by two selected edges are interior points of the "drag" (no IsFirst/IsLast there)
            var nodeUse = new Dictionary<Entity, int>();
            foreach (var e in edges)
            {
                var ed = em.GetComponentData<NetEdge>(e);
                nodeUse[ed.m_Start] = (nodeUse.TryGetValue(ed.m_Start, out var c1) ? c1 : 0) + 1;
                nodeUse[ed.m_End] = (nodeUse.TryGetValue(ed.m_End, out var c2) ? c2 : 0) + 1;
            }

            CompositionFlags.General genBits = set.m_General | unset.m_General;
            CompositionFlags.Side sideBits = set.m_Left | set.m_Right | unset.m_Left | unset.m_Right;

            var job = new NetUpgradeJob { DryRun = a.B("dryRun"), Api = this };
            var report = new List<object>();
            foreach (var e in edges)
            {
                var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                var r = new Dictionary<string, object> { { "id", Id(e) }, { "prefab", PrefabName(prefab) } };
                var sideE = sel.Reversed.Contains(e) ? FlipSide(side) : side; // polyline selection: left/right follow the polyline
                if (sideE != side) r["edgeSide"] = sideE;
                report.Add(r);
                if (!em.HasComponent<NetData>(prefab) || !em.HasComponent<Composition>(e)) { r["skipped"] = "not a net edge with composition"; continue; }
                var nd = em.GetComponentData<NetData>(prefab);
                bool sideOk = (nd.m_SideFlagMask & sideBits) != 0;
                bool genOk = (nd.m_GeneralFlagMask & genBits) != 0;
                bool middle = side == "middle";
                if (middle && !genOk) { r["skipped"] = "this road has no median for " + PrefabName(up); continue; }
                if (!middle && !sideOk)
                {
                    r["skipped"] = genOk ? "this road only supports " + PrefabName(up) + " on the median (side:\"middle\")" : "upgrade not supported by this road";
                    continue;
                }

                // old flags as the tool sees them: Upgraded + crosswalk state of both node ends
                var upgraded = em.HasComponent<Upgraded>(e) ? em.GetComponentData<Upgraded>(e).m_Flags : default(CompositionFlags);
                var old = upgraded;
                var comp = em.GetComponentData<Composition>(e);
                foreach (var nodeComp in new[] { comp.m_StartNode, comp.m_EndNode })
                {
                    if (nodeComp == Entity.Null || !em.HasComponent<NetCompositionData>(nodeComp)) continue;
                    var nf = em.GetComponentData<NetCompositionData>(nodeComp).m_Flags;
                    bool inv = (nf.m_General & CompositionFlags.General.Invert) != 0;
                    var bit = (nf.m_General & CompositionFlags.General.Crosswalk) != 0 ? CompositionFlags.Side.AddCrosswalk : CompositionFlags.Side.RemoveCrosswalk;
                    if (inv) old.m_Left |= bit; else old.m_Right |= bit;
                }

                // add/remove for each requested side, masked like AddControlPoints
                var add = default(CompositionFlags); var rem = default(CompositionFlags);
                var sides = new List<SubReplacementSide>();
                void SideState(bool mirrored, bool isMiddle)
                {
                    var s = mirrored ? Mirror(set) : set;
                    var u = mirrored ? Mirror(unset) : unset;
                    if (isMiddle || !sideOk) { s.m_Left &= ~SideMiddleClearMask; s.m_Right &= ~SideMiddleClearMask; u.m_Left &= ~SideMiddleClearMask; u.m_Right &= ~SideMiddleClearMask; }
                    if (!isMiddle || !genOk) { s.m_General &= ~GeneralOptionMask; u.m_General &= ~GeneralOptionMask; }
                    if (remove)
                    {
                        u.m_General = 0; u.m_Left &= CompositionFlags.Side.RemoveCrosswalk; u.m_Right &= CompositionFlags.Side.RemoveCrosswalk;
                        add |= u; rem |= s;
                    }
                    else { add |= s; rem |= u; }
                    sides.Add(isMiddle ? SubReplacementSide.Middle : mirrored ? SubReplacementSide.Left : SubReplacementSide.Right);
                }
                if (middle) SideState(false, true);
                else
                {
                    if (sideE == "right" || sideE == "both") SideState(false, false);
                    if (sideE == "left" || sideE == "both") SideState(true, false);
                }

                var item = new NetUpgradeJob.Item { Edge = e };
                item.NewFlags = (upgraded & ~rem) | (add & ~old);
                item.Changed = ((old & ~rem) | add) != old;

                // street-tree species: entries of the affected sides are replaced (or dropped -> default trees)
                if (em.HasBuffer<SubReplacement>(e))
                {
                    var existing = em.GetBuffer<SubReplacement>(e, true);
                    for (int i = 0; i < existing.Length; i++)
                    {
                        var s = existing[i];
                        if (treeUpgrade && s.m_Type == SubReplacementType.Tree && sides.Contains(s.m_Side))
                        {
                            if (treePrefab == Entity.Null || remove || s.m_Prefab != treePrefab) item.SubChanged = true;
                            continue;
                        }
                        item.SubReplacements.Add(s);
                    }
                }
                if (treeUpgrade && treePrefab != Entity.Null && !remove)
                    foreach (var s in sides)
                    {
                        item.SubReplacements.Add(new SubReplacement { m_Prefab = treePrefab, m_Type = SubReplacementType.Tree, m_Side = s, m_AgeMask = age });
                        if (!item.SubChanged)
                        {
                            bool had = false;
                            if (em.HasBuffer<SubReplacement>(e))
                            {
                                var ex = em.GetBuffer<SubReplacement>(e, true);
                                for (int i = 0; i < ex.Length; i++) if (ex[i].m_Type == SubReplacementType.Tree && ex[i].m_Side == s && ex[i].m_Prefab == treePrefab) had = true;
                            }
                            if (!had) item.SubChanged = true;
                        }
                    }

                var ed = em.GetComponentData<NetEdge>(e);
                item.StartFlags = nodeUse[ed.m_Start] > 1 ? default(CoursePosFlags) : CoursePosFlags.IsFirst;
                item.EndFlags = nodeUse[ed.m_End] > 1 ? default(CoursePosFlags) : CoursePosFlags.IsLast;

                r["before"] = FlagsJson(upgraded);
                r["after"] = FlagsJson(item.NewFlags);
                if (!item.Changed && !item.SubChanged) { r["skipped"] = remove ? "upgrade not present" : "already has this upgrade"; continue; }
                r["changes"] = true;
                job.Items.Add(item);
            }
            var info = new Dictionary<string, object> { { "upgrade", PrefabName(up) }, { "side", side }, { "remove", remove }, { "edges", report }, { "selection", sel.Report() } };
            if (treePrefab != Entity.Null) { info["tree"] = PrefabName(treePrefab); info["treeAge"] = age.ToString(); }
            if (job.Items.Count == 0) { info["applied"] = false; info["note"] = "nothing to change"; return info; }
            tool.Enqueue(job);
            return new BuildOperation(job, info);
        }
    }
}
