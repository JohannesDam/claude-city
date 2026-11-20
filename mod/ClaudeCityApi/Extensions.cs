using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Colossal.Mathematics;
using Game.Common;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using GB = Game.Buildings;
using GP = Game.Prefabs;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Places a service upgrade / extension on an existing service building exactly like the vanilla
    /// tools do, through ObjectToolBaseSystem.CreateDefinitions with owner = the building:
    /// <list type="bullet">
    /// <item>upgrades WITHOUT PlaceableObjectData ("Extra Garage" style, fixed position) are what
    /// UpgradeToolSystem places: control point = LocalToWorld(owner transform, BuildingExtensionData.m_Position)
    /// (owner position if the prefab has no BuildingExtensionData), rotation = owner rotation;</item>
    /// <item>upgrades WITH PlaceableObjectData (classroom extensions, wings, storage) are what
    /// ObjectToolSystem places in Mode.Upgrade: the control point is snapped to one side of the owner's
    /// lot (SnapJob.CheckSnapLine, Snap.OwnerSide), facing the owner.</item>
    /// </list>
    /// CreateDefinitionsJob then writes an update definition for the owner (upgrade = true), the
    /// extension's CreationDefinition + ObjectDefinition + OwnerDefinition, its sub-objects/nets/areas
    /// and clear areas; validation, cost, XP and InstalledUpgrade bookkeeping are the game's own.
    /// CreateDefinitions is protected; it is invoked by reflection on the core AgentToolSystem so no
    /// core change (and no game restart) is needed.
    /// </summary>
    public class ExtensionJob : AgentToolSystem.BuildJob
    {
        public Api Api;
        public Entity Prefab;
        public Entity Owner;
        public float3 Position;
        public quaternion Rotation;
        public Entity Placed = Entity.Null;
        HashSet<Entity> before;
        NativeList<ControlPoint> points;
        NativeReference<ObjectToolBaseSystem.AttachmentData> attachment;
        static MethodInfo createDefs;

        /// <summary>ObjectToolBaseSystem.CreateDefinitions with an owner (the core helper always passes Entity.Null).</summary>
        public static JobHandle CreateDefinitionsWithOwner(AgentToolSystem tool, Entity prefab, Entity owner,
            NativeList<ControlPoint> pts, NativeReference<ObjectToolBaseSystem.AttachmentData> att, JobHandle deps)
        {
            if (createDefs == null)
                createDefs = typeof(ObjectToolBaseSystem).GetMethod("CreateDefinitions", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (createDefs == null) throw new MissingMethodException("ObjectToolBaseSystem", "CreateDefinitions");
            var args = new object[]
            {
                prefab,                       // objectPrefab
                Entity.Null,                  // transformPrefab
                Entity.Null,                  // brushPrefab
                owner,                        // owner  (ObjectToolSystem: m_UpgradingObject, UpgradeToolSystem: m_UpgradingObject)
                Entity.Null,                  // original (moving)
                Entity.Null,                  // laneEditor
                tool.CityConfig.defaultTheme, // theme
                pts, att,
                false,                        // editorMode
                tool.CityConfig.leftHandTraffic,
                false, false,                 // removing, stamping
                0f, 0f, 0f,                   // brush size/angle/strength
                0f,                           // distance
                0.1f,                         // deltaTime
                RandomSeed.Next(),
                Snap.Upright,                 // only read in editor mode
                AgeMask.Sapling,
                false,                        // decorationMode
                default(PlacementOverrides),
                deps,
            };
            try { return (JobHandle)createDefs.Invoke(tool, args); }
            catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
        }

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            if (!Api.IsLive(Owner)) { Result["error"] = "owner building no longer exists"; return deps; }
            before = Api.InstalledUpgradeSet(Owner);
            points = new NativeList<ControlPoint>(1, Allocator.Persistent);
            attachment = new NativeReference<ObjectToolBaseSystem.AttachmentData>(Allocator.Persistent);
            var fwd = math.mul(Rotation, new float3(0, 0, 1));
            points.Add(new ControlPoint
            {
                m_Position = Position,
                m_HitPosition = Position,
                m_Rotation = Rotation,
                m_Direction = math.normalizesafe(fwd.xz),
                m_HitDirection = new float3(0, 1, 0),
                m_OriginalEntity = Entity.Null,   // CheckSnapLine clears it; UpgradeToolSystem leaves it null
                m_ElementIndex = new int2(-1, -1),
            });
            var h = CreateDefinitionsWithOwner(tool, Prefab, Owner, points, attachment, deps);
            h.Complete();
            return h;
        }

        public override void AfterApply(AgentToolSystem tool)
        {
            var em = tool.EntityManager;
            foreach (var u in Api.InstalledUpgradeSet(Owner))
            {
                if (before != null && before.Contains(u)) continue;
                if (em.HasComponent<GP.PrefabRef>(u) && em.GetComponentData<GP.PrefabRef>(u).m_Prefab == Prefab) { Placed = u; break; }
            }
            Result["upgradeId"] = Placed == Entity.Null ? null : Api.Id(Placed);
            try { Result["ownerAfter"] = Api.CombinedServiceStats(Owner); } catch (Exception e) { Result["ownerAfterError"] = e.Message; }
        }

        public override void Dispose()
        {
            if (points.IsCreated) points.Dispose();
            if (attachment.IsCreated) attachment.Dispose();
        }
    }

    /// <summary>
    /// Runs one or more ExtensionJobs: with several candidate slots (side=auto) each is previewed as a
    /// dry run until one validates without errors; then (unless dryRun) that slot is built for real.
    /// </summary>
    public class ExtensionOperation : IOperation
    {
        readonly Api api;
        readonly AgentToolSystem tool;
        readonly List<Api.ExtSlot> slots;
        readonly bool dryRun;
        readonly Func<Api.ExtSlot, bool, ExtensionJob> make;
        readonly Dictionary<string, object> info;
        readonly List<object> tried = new List<object>();
        readonly int started = Environment.TickCount;
        readonly int timeoutMs;
        ExtensionJob current;
        int index;
        bool applying;

        public ExtensionOperation(Api api, AgentToolSystem tool, List<Api.ExtSlot> slots, bool dryRun, Func<Api.ExtSlot, bool, ExtensionJob> make, Dictionary<string, object> info)
        {
            this.api = api; this.tool = tool; this.slots = slots; this.dryRun = dryRun; this.make = make; this.info = info;
            timeoutMs = 30000 + 4000 * slots.Count; // HTTP waits 60 s unless ?timeout= is given
            // a single slot is run directly (no separate probe)
            current = make(slots[0], slots.Count == 1 ? dryRun : true);
            applying = slots.Count == 1 && !dryRun;
            tool.Enqueue(current);
        }

        static bool Ok(ExtensionJob j) =>
            !j.Result.ContainsKey("error") && j.Result.TryGetValue("errors", out var e) && e is int ei && ei == 0
            && j.Result.TryGetValue("previewEntities", out var p) && p is int pi && pi > 0;

        public bool Step(out object result)
        {
            result = null;
            if (!current.Done)
            {
                if (Environment.TickCount - started > timeoutMs) { result = new ApiError("extension build timed out (is the game running and in-game?)", 504); return true; }
                return false;
            }
            var slot = slots[index];
            var summary = new Dictionary<string, object> { { "slot", slot.Label }, { "ok", Ok(current) } };
            foreach (var k in new[] { "cost", "errors", "warnings", "problems", "applied", "error", "warning" })
                if (current.Result.TryGetValue(k, out var v)) summary[k] = v;
            tried.Add(summary);

            if (applying || (slots.Count == 1))
            {
                result = Finish(current, slot);
                return true;
            }
            if (Ok(current))
            {
                if (dryRun) { result = Finish(current, slot); return true; }
                applying = true;
                current = make(slot, false);
                tool.Enqueue(current);
                return false;
            }
            index++;
            if (index >= slots.Count)
            {
                var r = Finish(current, slot);
                r["error"] = "no side of the owner lot validated for this extension (see tried[].problems)";
                result = r;
                return true;
            }
            current = make(slots[index], true);
            tool.Enqueue(current);
            return false;
        }

        Dictionary<string, object> Finish(ExtensionJob j, Api.ExtSlot slot)
        {
            var r = new Dictionary<string, object>(j.Result);
            foreach (var kv in info) r[kv.Key] = kv.Value;
            r["slot"] = slot.Label;
            r["pos"] = Api.F2(slot.Pos);
            r["facing"] = Api.F2(math.mul(slot.Rot, new float3(0, 0, 1)));
            r["dryRun"] = dryRun;
            if (tried.Count > 1) r["tried"] = tried;
            return r;
        }
    }

    public partial class Api
    {
        /// <summary>One candidate placement of an extension around its owner.</summary>
        public class ExtSlot
        {
            public string Side;
            public string Label;
            public float3 Pos;
            public quaternion Rot;
            public float Along;   // metres from the start of the side line (after snapping)
        }

        public HashSet<Entity> InstalledUpgradeSet(Entity owner)
        {
            var set = new HashSet<Entity>();
            if (em.Exists(owner) && em.HasBuffer<GB.InstalledUpgrade>(owner))
            {
                var buf = em.GetBuffer<GB.InstalledUpgrade>(owner, true);
                for (int i = 0; i < buf.Length; i++) set.Add(buf[i].m_Upgrade);
            }
            return set;
        }

        /// <summary>What the vanilla tools treat as the upgradable object: the parent of an Attached object, the owner of an upgrade.</summary>
        Entity UpgradableOwner(Entity e)
        {
            if (em.HasComponent<Game.Objects.Attached>(e))
            {
                var p = em.GetComponentData<Game.Objects.Attached>(e).m_Parent;
                if (p != Entity.Null && em.HasComponent<GB.Building>(p)) e = p;
            }
            if (em.HasComponent<GB.ServiceUpgrade>(e) && em.HasComponent<Owner>(e))
                e = em.GetComponentData<Owner>(e).m_Owner;
            return e;
        }

        /// <summary>Upgrade and module prefabs a building prefab offers (UpgradeMenuUISystem.BindUpgrades).</summary>
        List<KeyValuePair<Entity, bool>> OfferedUpgrades(Entity ownerPrefab)
        {
            var list = new List<KeyValuePair<Entity, bool>>();
            if (em.HasBuffer<GP.BuildingUpgradeElement>(ownerPrefab))
            {
                var b = em.GetBuffer<GP.BuildingUpgradeElement>(ownerPrefab, true);
                for (int i = 0; i < b.Length; i++) list.Add(new KeyValuePair<Entity, bool>(b[i].m_Upgrade, false));
            }
            if (em.HasBuffer<GP.BuildingModule>(ownerPrefab))
            {
                var b = em.GetBuffer<GP.BuildingModule>(ownerPrefab, true);
                for (int i = 0; i < b.Length; i++) list.Add(new KeyValuePair<Entity, bool>(b[i].m_Module, true));
            }
            return list;
        }

        /// <summary>UpgradeMenuUISystem.ForbidMultipleUpgrades: fixed extensions and m_ForbidMultiple upgrades exist once per owner.</summary>
        bool ForbidMultiple(Entity upgradePrefab)
        {
            if (em.HasComponent<GP.BuildingExtensionData>(upgradePrefab)) return true;
            return em.HasComponent<GP.ServiceUpgradeData>(upgradePrefab) && em.GetComponentData<GP.ServiceUpgradeData>(upgradePrefab).m_ForbidMultiple;
        }

        int CountInstalled(Entity owner, Entity upgradePrefab)
        {
            int n = 0;
            foreach (var u in InstalledUpgradeSet(owner))
                if (em.Exists(u) && em.HasComponent<GP.PrefabRef>(u) && em.GetComponentData<GP.PrefabRef>(u).m_Prefab == upgradePrefab) n++;
            return n;
        }

        static readonly string[] SideNames = { "back", "left", "front", "right" };

        /// <summary>
        /// Snap slots on the owner lot sides exactly like ObjectToolSystem.SnapJob (Snap.OwnerSide +
        /// CheckSnapLine): the line runs along a side of the owner lot grown by the extension depth,
        /// positions snap to the 8 m grid, are clamped by m_MaxPlacementOffset and the extension faces the owner.
        /// </summary>
        public List<ExtSlot> PlaceableSlots(Entity owner, Entity upgradePrefab, string side, float? offset, float2? near)
        {
            var ot = em.GetComponentData<ObjTransform>(owner);
            var ownerPrefab = em.GetComponentData<GP.PrefabRef>(owner).m_Prefab;
            int2 ownerLot = em.HasComponent<GP.BuildingData>(ownerPrefab) ? em.GetComponentData<GP.BuildingData>(ownerPrefab).m_LotSize : new int2(1, 1);
            int2 ext = em.GetComponentData<GP.BuildingData>(upgradePrefab).m_LotSize;
            var q = GB.BuildingUtils.CalculateCorners(ot, ownerLot + ext.y).xz;
            int maxOffset = ext.x - 1;
            if (em.HasComponent<GP.ServiceUpgradeData>(upgradePrefab))
            {
                var sud = em.GetComponentData<GP.ServiceUpgradeData>(upgradePrefab);
                if (sud.m_MaxPlacementOffset >= 0) maxOffset = sud.m_MaxPlacementOffset;
            }
            var lines = new[] { new Line2(q.a, q.b), new Line2(q.b, q.c), new Line2(q.c, q.d), new Line2(q.d, q.a) };
            var angles = new[] { 0f, math.PI * 0.5f, math.PI, math.PI * 1.5f };
            float half = ((ext.x - ext.y) & 1) != 0 ? 4f : 0f;
            float range = math.min(2 * maxOffset - ext.y - ext.x, ext.y - ext.x) * 4f;

            ExtSlot SnapOn(int i, float2 target, string label)
            {
                var line = lines[i];
                MathUtils.Distance(line, target, out float t);
                float len = math.max(0.001f, math.distance(line.a, line.b));
                float s = t * len;
                s = MathUtils.Snap(s + half, 8f) - half;
                s = math.clamp(s, -range, len + range);
                var xz = MathUtils.Position(line, s / len);
                var dir = math.mul(math.mul(ot.m_Rotation, quaternion.RotateY(angles[i])), new float3(0, 0, 1)).xz;
                return new ExtSlot
                {
                    Side = SideNames[i], Label = label, Along = s,
                    Pos = new float3(xz.x, ot.m_Position.y, xz.y),
                    Rot = ToolUtils.CalculateRotation(dir),
                };
            }

            var res = new List<ExtSlot>();
            if (near.HasValue)
            {
                // like the tool under the mouse: snap onto every side, keep the closest result
                ExtSlot best = null; float bd = float.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    var c = SnapOn(i, near.Value, SideNames[i] + "@xz");
                    var d = math.distance(c.Pos.xz, near.Value);
                    if (d < bd) { bd = d; best = c; }
                }
                res.Add(best);
                return res;
            }
            IEnumerable<int> sides;
            int si = Array.IndexOf(SideNames, side ?? "auto");
            if (si >= 0) sides = new[] { si };
            else sides = new[] { 0, 1, 3, 2 }; // auto: back, left, right, front (front usually faces the road)
            foreach (var i in sides)
            {
                var line = lines[i];
                var dir = math.normalizesafe(line.b - line.a);
                var mid = (line.a + line.b) * 0.5f;
                res.Add(SnapOn(i, mid + dir * (offset ?? 0f), SideNames[i] + (offset.HasValue ? "@" + offset.Value : "")));
            }
            if (si < 0 && !offset.HasValue)
            {
                // auto: then both ends of each side (the clamp keeps them on the allowed range)
                foreach (var i in new[] { 0, 1, 3, 2 })
                {
                    res.Add(SnapOn(i, lines[i].a, SideNames[i] + "@start"));
                    res.Add(SnapOn(i, lines[i].b, SideNames[i] + "@end"));
                }
            }
            // drop duplicates (short sides clamp to the same spot)
            var uniq = new List<ExtSlot>();
            foreach (var s in res)
                if (!uniq.Any(u => math.distance(u.Pos.xz, s.Pos.xz) < 0.5f && math.abs(math.dot(u.Rot.value, s.Rot.value)) > 0.999f)) uniq.Add(s);
            return uniq;
        }

        ExtSlot FixedSlot(Entity owner, Entity upgradePrefab)
        {
            // UpgradeToolSystem.CreateTempObject
            var ot = em.GetComponentData<ObjTransform>(owner);
            var pos = ot.m_Position;
            if (em.HasComponent<GP.BuildingExtensionData>(upgradePrefab))
                pos = Game.Objects.ObjectUtils.LocalToWorld(ot, em.GetComponentData<GP.BuildingExtensionData>(upgradePrefab).m_Position);
            return new ExtSlot { Side = "fixed", Label = "fixed", Pos = pos, Rot = ot.m_Rotation };
        }

        // ------------------------------------------------------------------ prefab data summaries

        static Dictionary<string, object> StructFields(object boxed)
        {
            var d = new Dictionary<string, object>();
            foreach (var f in boxed.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var v = f.GetValue(boxed);
                var name = f.Name.StartsWith("m_") ? f.Name.Substring(2) : f.Name;
                name = name.Length > 0 ? char.ToLowerInvariant(name[0]) + name.Substring(1) : name;
                switch (v)
                {
                    case Entity _: break;
                    case bool b: if (b) d[name] = true; break;
                    case float fl: if (math.abs(fl) > 1e-6f) d[name] = Math.Round(fl, 3); break;
                    case double db: if (Math.Abs(db) > 1e-9) d[name] = Math.Round(db, 3); break;
                    case int i: if (i != 0) d[name] = i; break;
                    case uint u: if (u != 0) d[name] = u; break;
                    case long l: if (l != 0) d[name] = l; break;
                    case short sh: if (sh != 0) d[name] = (int)sh; break;
                    case byte by: if (by != 0) d[name] = (int)by; break;
                    case Enum en: if (Convert.ToInt64(en) != 0) d[name] = en.ToString(); break;
                    default: if (v != null) { var s = v.ToString(); if (!string.IsNullOrEmpty(s) && s != "0") d[name] = s; } break;
                }
            }
            return d;
        }

        void PrefabPart<T>(Dictionary<string, object> d, Entity prefab) where T : unmanaged, IComponentData
        {
            if (!em.HasComponent<T>(prefab)) return;
            var f = StructFields(em.GetComponentData<T>(prefab));
            if (f.Count > 0) d[typeof(T).Name] = f;
        }

        void CombinedPart<T>(Dictionary<string, object> d, Entity e, Entity prefab) where T : unmanaged, IComponentData, GP.ICombineData<T>
        {
            if (!GP.UpgradeUtils.TryGetCombinedComponent(em, e, prefab, out T v)) return;
            var f = StructFields(v);
            if (f.Count > 0) d[typeof(T).Name] = f;
        }

        /// <summary>The service-relevant data of an upgrade prefab (what it adds when combined with the owner).</summary>
        Dictionary<string, object> UpgradeEffects(Entity p)
        {
            var d = new Dictionary<string, object>();
            PrefabPart<GP.SchoolData>(d, p); PrefabPart<GP.HospitalData>(d, p); PrefabPart<GP.PoliceStationData>(d, p);
            PrefabPart<GP.PrisonData>(d, p); PrefabPart<GP.FireStationData>(d, p); PrefabPart<GP.GarbageFacilityData>(d, p);
            PrefabPart<GP.DeathcareFacilityData>(d, p); PrefabPart<GP.PostFacilityData>(d, p); PrefabPart<GP.TransportDepotData>(d, p);
            PrefabPart<GP.MaintenanceDepotData>(d, p); PrefabPart<GP.PowerPlantData>(d, p); PrefabPart<GP.BatteryData>(d, p);
            PrefabPart<GP.EmergencyGeneratorData>(d, p); PrefabPart<GP.WaterPumpingStationData>(d, p); PrefabPart<GP.SewageOutletData>(d, p);
            PrefabPart<GP.WastewaterTreatmentPlantData>(d, p); PrefabPart<GP.TelecomFacilityData>(d, p); PrefabPart<GP.EmergencyShelterData>(d, p);
            PrefabPart<GP.ParkData>(d, p); PrefabPart<GP.ParkingFacilityData>(d, p); PrefabPart<GP.TransportStationData>(d, p);
            PrefabPart<GP.WorkplaceData>(d, p); PrefabPart<GP.AttractionData>(d, p); PrefabPart<GP.PollutionData>(d, p);
            PrefabPart<GP.ConsumptionData>(d, p); PrefabPart<GP.GarbagePoweredData>(d, p); PrefabPart<GP.SolarPoweredData>(d, p);
            PrefabPart<GP.WindPoweredData>(d, p); PrefabPart<GP.GroundWaterPoweredData>(d, p); PrefabPart<GP.WaterPoweredData>(d, p);
            PrefabPart<GP.StorageCompanyData>(d, p);
            return d;
        }

        /// <summary>Owner prefab data combined with all installed (and enabled) upgrades (UpgradeUtils.TryGetCombinedComponent).</summary>
        public Dictionary<string, object> CombinedServiceStats(Entity e)
        {
            var d = new Dictionary<string, object>();
            if (!em.Exists(e) || !em.HasComponent<GP.PrefabRef>(e)) return d;
            var p = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
            CombinedPart<GP.SchoolData>(d, e, p); CombinedPart<GP.HospitalData>(d, e, p); CombinedPart<GP.PoliceStationData>(d, e, p);
            CombinedPart<GP.PrisonData>(d, e, p); CombinedPart<GP.FireStationData>(d, e, p); CombinedPart<GP.GarbageFacilityData>(d, e, p);
            CombinedPart<GP.DeathcareFacilityData>(d, e, p); CombinedPart<GP.PostFacilityData>(d, e, p); CombinedPart<GP.TransportDepotData>(d, e, p);
            CombinedPart<GP.MaintenanceDepotData>(d, e, p); CombinedPart<GP.PowerPlantData>(d, e, p); CombinedPart<GP.BatteryData>(d, e, p);
            CombinedPart<GP.EmergencyGeneratorData>(d, e, p); CombinedPart<GP.WaterPumpingStationData>(d, e, p); CombinedPart<GP.SewageOutletData>(d, e, p);
            CombinedPart<GP.WastewaterTreatmentPlantData>(d, e, p); CombinedPart<GP.TelecomFacilityData>(d, e, p); CombinedPart<GP.EmergencyShelterData>(d, e, p);
            CombinedPart<GP.ParkData>(d, e, p); CombinedPart<GP.ParkingFacilityData>(d, e, p); CombinedPart<GP.TransportStationData>(d, e, p);
            CombinedPart<GP.WorkplaceData>(d, e, p); CombinedPart<GP.GarbagePoweredData>(d, e, p); CombinedPart<GP.SolarPoweredData>(d, e, p);
            CombinedPart<GP.WindPoweredData>(d, e, p);
            return d;
        }

        /// <summary>Cost the vanilla UI shows: the placeable construction cost, else ServiceUpgradeData.m_UpgradeCost.</summary>
        long UpgradeCost(Entity p)
        {
            long c = 0;
            if (em.HasComponent<GP.PlaceableObjectData>(p)) c = em.GetComponentData<GP.PlaceableObjectData>(p).m_ConstructionCost;
            if (c == 0 && em.HasComponent<GP.ServiceUpgradeData>(p)) c = em.GetComponentData<GP.ServiceUpgradeData>(p).m_UpgradeCost;
            return c;
        }

        Dictionary<string, object> OfferJson(Entity owner, Entity up, bool module, bool withSlots)
        {
            bool placeable = em.HasComponent<GP.PlaceableObjectData>(up);
            bool locked = IsLocked(up);
            bool forbid = ForbidMultiple(up);
            int built = owner == Entity.Null ? 0 : CountInstalled(owner, up);
            var d = new Dictionary<string, object>
            {
                { "name", PrefabName(up) },
                { "kind", module ? "module" : placeable ? "placeable" : "fixed" },
                { "locked", locked },
                { "cost", UpgradeCost(up) },
                { "built", built },
                { "forbidMultiple", forbid },
            };
            if (em.HasComponent<GP.ServiceUpgradeData>(up))
            {
                var sud = em.GetComponentData<GP.ServiceUpgradeData>(up);
                d["xp"] = sud.m_XPReward;
                if (sud.m_UpgradeCost != 0) d["upgradeCost"] = sud.m_UpgradeCost;
                if (placeable) { d["maxPlacementOffset"] = sud.m_MaxPlacementOffset; d["maxPlacementDistance"] = Math.Round(sud.m_MaxPlacementDistance, 1); }
            }
            if (em.HasComponent<GP.BuildingData>(up)) { var l = em.GetComponentData<GP.BuildingData>(up).m_LotSize; d["lot"] = new List<object> { l.x, l.y }; }
            if (em.HasComponent<GP.ConsumptionData>(up)) d["upkeep"] = em.GetComponentData<GP.ConsumptionData>(up).m_Upkeep;
            var eff = UpgradeEffects(up);
            eff.Remove("ConsumptionData");
            if (eff.Count > 0) d["effects"] = eff;
            string why = locked ? "locked" : (forbid && built > 0) ? "already built (one per building)" : null;
            if (owner != Entity.Null && why == null && em.HasComponent<Destroyed>(owner)) why = "owner destroyed";
            d["canBuild"] = why == null;
            if (why != null) d["reason"] = why;
            if (withSlots && owner != Entity.Null && placeable && em.HasComponent<GP.BuildingData>(up))
                d["sides"] = PlaceableSlots(owner, up, "auto", null, null).Where(s => !s.Label.Contains("@"))
                    .Select(s => (object)new Dictionary<string, object> { { "side", s.Side }, { "pos", F2(s.Pos) } }).ToList();
            return d;
        }

        // ------------------------------------------------------------------ GET /extensions

        /// <summary>
        /// GET /extensions?owner=id : the building's installed upgrades, every upgrade/module its prefab
        /// offers (lock state, cost, xp, upkeep, lot, effects, one-per-building rule, side slots) and the
        /// owner's data combined with its upgrades.
        /// GET /extensions[?filter=School&amp;onlyBuildable=1&amp;limit=200] : city-wide list of service buildings
        /// with installed and still-buildable upgrades.
        /// </summary>
        object Extensions(Pending p)
        {
            var bq = em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.ReadOnly<ObjTransform>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var ownerArg = p.Q("owner") ?? p.Q("id");
            if (ownerArg != null)
            {
                var e = Resolve(ownerArg, bq);
                if (e == Entity.Null) return new ApiError("building not found: " + ownerArg, 404);
                var owner = UpgradableOwner(e);
                var op = em.GetComponentData<GP.PrefabRef>(owner).m_Prefab;
                var tr = em.GetComponentData<ObjTransform>(owner);
                var r = new Dictionary<string, object>
                {
                    { "owner", Id(owner) }, { "prefab", PrefabName(op) }, { "pos", F2(tr.m_Position) },
                    { "facing", F2(math.mul(tr.m_Rotation, new float3(0, 0, 1))) },
                };
                if (owner != e) r["resolvedFrom"] = Id(e);
                if (em.HasComponent<GP.BuildingData>(op)) { var l = em.GetComponentData<GP.BuildingData>(op).m_LotSize; r["lot"] = new List<object> { l.x, l.y }; }
                if (!em.HasBuffer<GB.InstalledUpgrade>(owner)) r["note"] = "this building has no InstalledUpgrade buffer; the vanilla upgrade tools refuse it";
                var inst = new List<object>();
                foreach (var u in InstalledUpgradeSet(owner))
                {
                    if (!em.Exists(u) || !em.HasComponent<GP.PrefabRef>(u)) continue;
                    var ud = new Dictionary<string, object> { { "id", Id(u) }, { "prefab", PrefabName(em.GetComponentData<GP.PrefabRef>(u).m_Prefab) } };
                    if (em.HasComponent<ObjTransform>(u)) ud["pos"] = F2(em.GetComponentData<ObjTransform>(u).m_Position);
                    ud["outOfService"] = UpgradeDisabled(owner, u);
                    inst.Add(ud);
                }
                r["installed"] = inst;
                r["offered"] = OfferedUpgrades(op).Select(kv => (object)OfferJson(owner, kv.Key, kv.Value, true)).ToList();
                r["combined"] = CombinedServiceStats(owner);
                r["usage"] = "POST /extension {owner, prefab, side: auto|back|left|right|front, offset (m along the side), or x,z (snap to the nearest side), dryRun}. 'fixed' upgrades need no side.";
                return r;
            }

            string filter = p.Q("filter");
            bool onlyBuildable = p.Q("onlyBuildable", "0") == "1" || p.Q("onlyBuildable", "") == "true";
            int limit = (int)math.clamp(p.QF("limit", 200f), 1f, 2000f);
            var list = new List<object>();
            int total = 0;
            using (var arr = bq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (em.HasComponent<GB.ServiceUpgrade>(e) || em.HasComponent<Owner>(e)) continue;
                    var op = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
                    var offered = OfferedUpgrades(op);
                    if (offered.Count == 0) continue;
                    var name = PrefabName(op);
                    if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var buildable = new List<object>();
                    foreach (var kv in offered)
                    {
                        var up = kv.Key;
                        if (IsLocked(up)) continue;
                        if (ForbidMultiple(up) && CountInstalled(e, up) > 0) continue;
                        buildable.Add(PrefabName(up) + " (" + UpgradeCost(up) + ")");
                    }
                    if (onlyBuildable && buildable.Count == 0) continue;
                    total++;
                    if (list.Count >= limit) continue;
                    list.Add(new Dictionary<string, object>
                    {
                        { "id", Id(e) }, { "prefab", name }, { "pos", F2(em.GetComponentData<ObjTransform>(e).m_Position) },
                        { "installed", InstalledUpgradeSet(e).Where(u => em.Exists(u) && em.HasComponent<GP.PrefabRef>(u)).Select(u => (object)PrefabName(em.GetComponentData<GP.PrefabRef>(u).m_Prefab)).ToList() },
                        { "buildable", buildable },
                    });
                }
            return new Dictionary<string, object> { { "count", total }, { "buildings", list }, { "hint", "GET /extensions?owner=<id> for costs, effects and side slots" } };
        }

        bool UpgradeDisabled(Entity owner, Entity upgrade)
        {
            if (em.HasComponent<GB.Extension>(upgrade) && (em.GetComponentData<GB.Extension>(upgrade).m_Flags & GB.ExtensionFlags.Disabled) != 0) return true;
            if (em.HasComponent<GB.Building>(upgrade) && GB.BuildingUtils.CheckOption(em.GetComponentData<GB.Building>(upgrade), GB.BuildingOption.Inactive)) return true;
            if (em.HasBuffer<GB.InstalledUpgrade>(owner))
            {
                var b = em.GetBuffer<GB.InstalledUpgrade>(owner, true);
                for (int i = 0; i < b.Length; i++)
                    if (b[i].m_Upgrade == upgrade) return GB.BuildingUtils.CheckOption(b[i], GB.BuildingOption.Inactive);
            }
            return false;
        }

        // ------------------------------------------------------------------ POST /extension

        /// <summary>
        /// POST /extension {owner: id, prefab: name or part of it (from GET /extensions?owner=),
        /// side: auto|back|left|right|front, offset: metres along the side from its centre, or x,z (snap
        /// to the nearest side like the mouse would), dryRun}. Fixed upgrades ignore side.
        /// side=auto previews the side centres (back, left, right, front) and then their ends as dry runs
        /// and builds the first that validates.
        /// </summary>
        object PlaceExtension(Args a)
        {
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy", 409);
            var bq = em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var e = Resolve(a.Raw("owner") ?? a.Raw("id") ?? a.Raw("building"), bq);
            if (e == Entity.Null) return new ApiError("owner building not found: " + (a.Raw("owner") ?? a.Raw("id")), 404);
            var owner = UpgradableOwner(e);
            if (!em.HasComponent<ObjTransform>(owner)) return new ApiError("owner has no transform");
            if (em.HasComponent<Destroyed>(owner)) return new ApiError("owner building is destroyed (rebuild it first)");
            var op = em.GetComponentData<GP.PrefabRef>(owner).m_Prefab;
            var offered = OfferedUpgrades(op);
            if (offered.Count == 0) return new ApiError(PrefabName(op) + " offers no upgrades");
            var want = a.Str("prefab", a.Str("upgrade", ""));
            Entity up = Entity.Null;
            foreach (var kv in offered) if (string.Equals(PrefabName(kv.Key), want, StringComparison.OrdinalIgnoreCase)) { up = kv.Key; break; }
            if (up == Entity.Null && want.Length > 0)
                foreach (var kv in offered) if (PrefabName(kv.Key).IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) { up = kv.Key; break; }
            if (up == Entity.Null)
                return new ApiError("'" + want + "' is not offered by " + PrefabName(op) + "; offered: " + string.Join(", ", offered.Select(kv => PrefabName(kv.Key))));
            if (IsLocked(up)) return new ApiError(PrefabName(up) + " is still locked");
            if (!em.HasComponent<GP.ServiceUpgradeData>(up))
                return new ApiError(PrefabName(up) + " is a building module without ServiceUpgradeData; the vanilla tools would not place it as an upgrade (not supported)");
            if (ForbidMultiple(up) && CountInstalled(owner, up) > 0) return new ApiError(PrefabName(up) + " is already built on this building (one per building)", 409);
            if (!em.HasBuffer<GB.InstalledUpgrade>(owner))
                return new ApiError(PrefabName(op) + " has no InstalledUpgrade buffer (not upgradable)");

            bool placeable = em.HasComponent<GP.PlaceableObjectData>(up);
            List<ExtSlot> slots;
            if (!placeable) slots = new List<ExtSlot> { FixedSlot(owner, up) };
            else
            {
                if (!em.HasComponent<GP.BuildingData>(up)) return new ApiError(PrefabName(up) + " has PlaceableObjectData but no BuildingData; cannot compute a side slot");
                float2? near = a.Has("x") && a.Has("z") ? new float2(a.F("x"), a.F("z")) : (float2?)null;
                float? offset = a.Has("offset") ? a.F("offset") : (float?)null;
                var side = (a.Str("side", "auto") ?? "auto").ToLowerInvariant();
                if (side != "auto" && Array.IndexOf(SideNames, side) < 0) return new ApiError("side must be auto|back|left|right|front");
                slots = PlaceableSlots(owner, up, side, offset, near);
                int maxTries = (int)math.clamp(a.I("maxTries", 12), 1, 12);
                if (slots.Count > maxTries) slots = slots.GetRange(0, maxTries);
            }
            bool dry = a.B("dryRun");
            var info = new Dictionary<string, object>
            {
                { "owner", Id(owner) }, { "ownerPrefab", PrefabName(op) }, { "prefab", PrefabName(up) },
                { "kind", placeable ? "placeable" : "fixed" }, { "listCost", UpgradeCost(up) },
            };
            if (owner != e) info["resolvedFrom"] = Id(e);
            var capturedUp = up; var capturedOwner = owner;
            Func<ExtSlot, bool, ExtensionJob> make = (s, d) => new ExtensionJob { Api = this, Prefab = capturedUp, Owner = capturedOwner, Position = s.Pos, Rotation = s.Rot, DryRun = d };
            return new ExtensionOperation(this, tool, slots, dry, make, info);
        }
    }
}
