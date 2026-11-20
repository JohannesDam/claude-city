using System;
using System.Collections.Generic;
using Game.Common;
using Game.Tools;
using Game.UI.InGame;
using Unity.Collections;
using Unity.Entities;
using GB = Game.Buildings;
using GP = Game.Prefabs;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Waits until a building action shows up in the building's state (policy changes are applied by
    /// Game.Policies.ModifiedSystem from an event entity written to the EndFrameBarrier).
    /// </summary>
    public class BuildingActionOperation : IOperation
    {
        readonly Api api;
        readonly Entity target;
        readonly string action;
        readonly bool want;
        readonly Dictionary<string, object> info;
        int frames;

        public BuildingActionOperation(Api api, Entity target, string action, bool want, Dictionary<string, object> info)
        {
            this.api = api; this.target = target; this.action = action; this.want = want; this.info = info;
        }

        public bool Step(out object result)
        {
            result = null;
            frames++;
            var st = api.ActionState(target);
            bool now = st.TryGetValue(action, out var o) && o is Dictionary<string, object> d && d.TryGetValue("on", out var on) && on is bool b && b;
            if (now != want && frames < 120) return false;
            var r = new Dictionary<string, object>(info) { { "state", st }, { "confirmed", now == want }, { "frames", frames } };
            if (now != want) r["warning"] = "state did not change within 120 frames (is the game paused in a menu?); re-check with GET /buildingaction?id=";
            result = r;
            return true;
        }
    }

    public partial class Api
    {
        Entity PolicyPrefabByName(string exactName)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GP.PolicyData>(), ComponentType.ReadOnly<GP.PrefabData>());
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                    if (string.Equals(PrefabName(e), exactName, StringComparison.Ordinal)) return e;
            return Entity.Null;
        }

        /// <summary>
        /// Same conditions as Game.UI.InGame.ActionsSection (Out of Service / Empty buttons) and
        /// VisualCustomizeSection (Historical toggle).
        /// </summary>
        public Dictionary<string, object> ActionState(Entity e)
        {
            var r = new Dictionary<string, object>();
            if (!em.Exists(e)) return r;
            var prefab = em.HasComponent<GP.PrefabRef>(e) ? em.GetComponentData<GP.PrefabRef>(e).m_Prefab : Entity.Null;
            bool hasBuilding = em.HasComponent<GB.Building>(e);
            var b = hasBuilding ? em.GetComponentData<GB.Building>(e) : default(GB.Building);
            bool isUpgrade = em.HasComponent<GB.ServiceUpgrade>(e);
            bool hasPolicies = em.HasBuffer<Game.Policies.Policy>(e);

            // Out of Service: ActionsSection.OnToggle -> "Out of Service" policy (BuildingOption.Inactive)
            bool oosAllowed = isUpgrade || (hasPolicies && hasBuilding && em.HasComponent<Game.City.CityServiceUpkeep>(e) && em.HasBuffer<GB.Efficiency>(e));
            bool oosOn = (hasBuilding && GB.BuildingUtils.CheckOption(b, GB.BuildingOption.Inactive))
                         || (em.HasComponent<GB.Extension>(e) && (em.GetComponentData<GB.Extension>(e).m_Flags & GB.ExtensionFlags.Disabled) != 0);
            r["outOfService"] = new Dictionary<string, object> { { "allowed", oosAllowed }, { "on", oosOn } };

            // Emptying: only landfills (GarbageFacilityData.m_LongTermStorage) have the Empty button
            bool emptiable = hasPolicies && hasBuilding && prefab != Entity.Null && em.HasComponent<GP.GarbageFacilityData>(prefab)
                             && em.GetComponentData<GP.GarbageFacilityData>(prefab).m_LongTermStorage;
            r["emptying"] = new Dictionary<string, object> { { "allowed", emptiable }, { "on", hasBuilding && GB.BuildingUtils.CheckOption(b, GB.BuildingOption.Empty) } };

            // Historical: zoned (spawnable) non-signature buildings that are not abandoned; freezes leveling
            bool histAllowed = prefab != Entity.Null && em.HasComponent<GP.SpawnableBuildingData>(prefab) && !em.HasComponent<GP.SignatureBuildingData>(prefab)
                               && !em.HasComponent<GB.Abandoned>(e);
            r["historical"] = new Dictionary<string, object> { { "allowed", histAllowed }, { "on", hasBuilding && (b.m_Flags & GB.BuildingFlags.Historical) != 0 } };
            return r;
        }

        /// <summary>
        /// GET /buildingaction?id= : which actions a building allows and their state.
        /// POST /buildingaction {id, action: outOfService|emptying|historical, on: true|false}:
        /// outOfService/emptying go through PoliciesUISystem.SetPolicy(building, "Out of Service"|"Empty" policy, on)
        /// exactly like the info panel buttons (works on service buildings and on individual upgrades);
        /// historical flips BuildingFlags.Historical like VisualCustomizeSection.OnToggleHistorical
        /// (BuildingUpkeepSystem then clamps the condition: no level-up, no level-down/abandonment).
        /// </summary>
        object BuildingAction(Pending p)
        {
            bool post = string.Equals(p.Method, "POST", StringComparison.OrdinalIgnoreCase);
            var a = p.Body;
            object idArg = post ? (a.Raw("id") ?? a.Raw("building")) : (object)(p.Q("id") ?? p.Q("building"));
            if (idArg == null && !post) idArg = a.Raw("id");
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var e = Resolve(idArg, q);
            if (e == Entity.Null || !IsLive(e)) return new ApiError("building not found: " + idArg, 404);
            var prefab = em.HasComponent<GP.PrefabRef>(e) ? PrefabName(em.GetComponentData<GP.PrefabRef>(e).m_Prefab) : null;
            var info = new Dictionary<string, object> { { "id", Id(e) }, { "prefab", prefab } };
            if (em.HasComponent<GB.ServiceUpgrade>(e) && em.HasComponent<Owner>(e)) info["upgradeOf"] = Id(em.GetComponentData<Owner>(e).m_Owner);
            if (!post)
            {
                info["state"] = ActionState(e);
                info["usage"] = "POST /buildingaction {id, action: outOfService|emptying|historical, on: true|false}";
                return info;
            }
            var action = (a.Str("action", "") ?? "").Trim();
            string key = action.Equals("outOfService", StringComparison.OrdinalIgnoreCase) || action.Equals("disable", StringComparison.OrdinalIgnoreCase) ? "outOfService"
                : action.Equals("emptying", StringComparison.OrdinalIgnoreCase) || action.Equals("empty", StringComparison.OrdinalIgnoreCase) ? "emptying"
                : action.Equals("historical", StringComparison.OrdinalIgnoreCase) ? "historical" : null;
            if (key == null) return new ApiError("action must be outOfService|emptying|historical");
            if (!a.Has("on")) return new ApiError("on: true|false required");
            bool on = a.B("on");
            var st = ActionState(e);
            var ks = (Dictionary<string, object>)st[key];
            if (!(bool)ks["allowed"]) return new ApiError(key + " is not available for " + prefab + " (same rule as the in-game info panel)", 409);
            info["action"] = key;
            info["on"] = on;
            info["before"] = ks["on"];
            if ((bool)ks["on"] == on) { info["state"] = st; info["changed"] = false; return info; }

            if (key == "historical")
            {
                var b = em.GetComponentData<GB.Building>(e);
                if (on) b.m_Flags |= GB.BuildingFlags.Historical; else b.m_Flags &= ~GB.BuildingFlags.Historical;
                em.SetComponentData(e, b);
                info["state"] = ActionState(e);
                info["changed"] = true;
                return info;
            }
            var policy = PolicyPrefabByName(key == "outOfService" ? "Out of Service" : "Empty");
            if (policy == Entity.Null) return new ApiError("policy prefab '" + (key == "outOfService" ? "Out of Service" : "Empty") + "' not found", 500);
            Sys<PoliciesUISystem>().SetPolicy(e, policy, on, 0f);
            info["changed"] = true;
            return new BuildingActionOperation(this, e, key, on, info);
        }
    }
}
