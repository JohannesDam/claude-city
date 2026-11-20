using System.Collections.Generic;
using System.Linq;
using Game.Buildings;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Demolition checks for service upgrades / extensions without a preview (GET /extensions, GET /education,
    /// and the slot order of POST /extension side=auto).
    ///
    /// The game's preview does not flag growables under a new extension (no Overlap Existing warning, the
    /// build validates with errors = warnings = 0), yet once the extension stands its lot occupies their
    /// zone cells and ZoneCheckSystem condemns them. So the check is geometric: the extension's base
    /// (ObjectGeometryData bounds = lot minus 0.2 m per side) against growable lots, the same test as the
    /// Safety wrapper's "geometry" check (Demolish.cs GrowablesUnder).
    /// </summary>
    public partial class Api
    {
        /// <summary>All growable (zoned) lots of the city in one grid, for many footprint checks per request.</summary>
        ObstSet GrowableIndex()
        {
            var polys = new List<KeyValuePair<float2[], Entity>>();
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            var geoCache = new Dictionary<Entity, ObjectGeometryData?>();
            var q = GrowableQuery();
            using (var ents = q.ToEntityArray(Allocator.TempJob))
            using (var trs = q.ToComponentDataArray<ObjTransform>(Allocator.TempJob))
            using (var prs = q.ToComponentDataArray<PrefabRef>(Allocator.TempJob))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    var pf = prs[i].m_Prefab;
                    if (!geoCache.TryGetValue(pf, out var g))
                    {
                        g = em.HasComponent<ObjectGeometryData>(pf) ? em.GetComponentData<ObjectGeometryData>(pf) : (ObjectGeometryData?)null;
                        geoCache[pf] = g;
                    }
                    if (g == null || !RemovedWhenOverlapped(g.Value)) continue;
                    var corners = BaseCorners(trs[i].m_Position.xz, trs[i].m_Rotation, g.Value.m_Bounds);
                    foreach (var c in corners) { mn = math.min(mn, c); mx = math.max(mx, c); }
                    polys.Add(new KeyValuePair<float2[], Entity>(corners, ents[i]));
                }
            }
            if (polys.Count == 0) return new ObstSet(new float2(0f), new float2(1f), 64f);
            var set = new ObstSet(mn - 1f, mx + 1f, 64f);
            foreach (var kv in polys) set.AddPoly(kv.Key, K_ZONED, kv.Value, false);
            return set;
        }

        /// <summary>One candidate extension slot with the growables its lot would remove.</summary>
        sealed class SlotCheck
        {
            public ExtSlot Slot;
            public List<Entity> Hits;
            public int Renters;
        }

        int RenterCount(Entity e) => em.Exists(e) && em.HasBuffer<Renter>(e) ? em.GetBuffer<Renter>(e, true).Length : 0;

        /// <summary>Growables an extension placed at the slot would remove (its base overlaps their lots).</summary>
        List<Entity> SlotGrowables(ObstSet idx, Entity upgradePrefab, ExtSlot s)
        {
            var res = new List<Entity>();
            if (idx == null || !em.HasComponent<ObjectGeometryData>(upgradePrefab)) return res;
            var g = em.GetComponentData<ObjectGeometryData>(upgradePrefab);
            // circular bases are checked as their bounding square (conservative)
            var r = ObjRect(new ObjTransform { m_Position = s.Pos, m_Rotation = s.Rot }, g.m_Bounds);
            foreach (var o in idx.AllHits(ref r, OverlapEps)) if (!res.Contains(o.Src)) res.Add(o.Src);
            return res;
        }

        /// <summary>The slots POST /extension side=auto would try (or the fixed slot), each with its growable hits.</summary>
        List<SlotCheck> CheckSlots(ObstSet idx, Entity owner, Entity up)
        {
            List<ExtSlot> slots;
            if (!em.HasComponent<PlaceableObjectData>(up)) slots = new List<ExtSlot> { FixedSlot(owner, up) };
            else if (em.HasComponent<BuildingData>(up)) slots = PlaceableSlots(owner, up, "auto", null, null);
            else slots = new List<ExtSlot>();
            var res = new List<SlotCheck>();
            foreach (var s in slots)
            {
                var hits = SlotGrowables(idx, up, s);
                s.Demolishes = hits.Count;
                res.Add(new SlotCheck { Slot = s, Hits = hits, Renters = hits.Sum(RenterCount) });
            }
            return res;
        }

        /// <summary>True when no slot is free: every candidate would remove zoned buildings (false when there are no slots at all).</summary>
        static bool AllSlotsDemolish(List<SlotCheck> checks) => checks.Count > 0 && checks.All(c => c.Hits.Count > 0);

        /// <summary>Text for a list entry whose every slot demolishes: the least destructive slot.</summary>
        static string DemolishSummary(List<SlotCheck> checks)
        {
            var best = checks.OrderBy(c => c.Hits.Count).ThenBy(c => c.Renters).First();
            return "every slot demolishes zoned buildings (least: " + best.Hits.Count + " building(s), " + best.Renters + " renter(s) at " + best.Slot.Label + ")";
        }
    }
}
