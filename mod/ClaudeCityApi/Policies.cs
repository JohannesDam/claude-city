using System;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using GA = Game.Areas;
using GB = Game.Buildings;
using GCity = Game.City;
using GPol = Game.Policies;
using GR = Game.Routes;
using GUI = Game.UI.InGame;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        // ------------------------------------------------------------------ helpers

        static string Loc(string key)
        {
            try
            {
                var d = GameManager.instance?.localizationManager?.activeDictionary;
                if (d != null && d.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
            }
            catch { }
            return null;
        }

        string EntityLabel(Entity e)
        {
            try { var n = world.GetExistingSystemManaged<Game.UI.NameSystem>()?.GetRenderedLabelName(e); if (!string.IsNullOrEmpty(n)) return n; } catch { }
            return null;
        }

        /// <summary>city | district | building | route, from the option/modifier components the PoliciesUISystem queries use.</summary>
        string PolicyScope(Entity p)
        {
            if (em.HasComponent<CityOptionData>(p) || em.HasComponent<CityModifierData>(p)) return "city";
            if (em.HasComponent<DistrictOptionData>(p) || em.HasComponent<DistrictModifierData>(p)) return "district";
            if (em.HasComponent<BuildingOptionData>(p) || em.HasComponent<BuildingModifierData>(p)) return "building";
            if (em.HasComponent<RouteOptionData>(p) || em.HasComponent<RouteModifierData>(p)) return "route";
            return "other";
        }

        static List<object> MaskNames<T>(uint mask) where T : struct, Enum
        {
            var l = new List<object>();
            for (int i = 0; i < 32; i++)
                if ((mask & (1u << i)) != 0) l.Add(Enum.IsDefined(typeof(T), i) ? Enum.GetName(typeof(T), i) : "option" + i);
            return l;
        }

        List<object> PolicyEffects(Entity p)
        {
            var l = new List<object>();
            if (em.HasBuffer<CityModifierData>(p))
                foreach (var m in em.GetBuffer<CityModifierData>(p, true))
                    l.Add(new Dictionary<string, object> { { "cityModifier", m.m_Type.ToString() }, { "mode", m.m_Mode.ToString() }, { "range", new List<object> { m.m_Range.min, m.m_Range.max } } });
            if (em.HasBuffer<DistrictModifierData>(p))
                foreach (var m in em.GetBuffer<DistrictModifierData>(p, true))
                    l.Add(new Dictionary<string, object> { { "districtModifier", m.m_Type.ToString() }, { "mode", m.m_Mode.ToString() }, { "range", new List<object> { m.m_Range.min, m.m_Range.max } } });
            if (em.HasBuffer<BuildingModifierData>(p))
                foreach (var m in em.GetBuffer<BuildingModifierData>(p, true))
                    l.Add(new Dictionary<string, object> { { "buildingModifier", m.m_Type.ToString() }, { "mode", m.m_Mode.ToString() }, { "range", new List<object> { m.m_Range.min, m.m_Range.max } } });
            if (em.HasBuffer<RouteModifierData>(p))
                foreach (var m in em.GetBuffer<RouteModifierData>(p, true))
                    l.Add(new Dictionary<string, object> { { "routeModifier", m.m_Type.ToString() }, { "mode", m.m_Mode.ToString() }, { "range", new List<object> { m.m_Range.min, m.m_Range.max } } });
            if (em.HasComponent<CityOptionData>(p)) l.Add(new Dictionary<string, object> { { "cityOptions", MaskNames<GCity.CityOption>(em.GetComponentData<CityOptionData>(p).m_OptionMask) } });
            if (em.HasComponent<DistrictOptionData>(p)) l.Add(new Dictionary<string, object> { { "districtOptions", MaskNames<GA.DistrictOption>(em.GetComponentData<DistrictOptionData>(p).m_OptionMask) } });
            if (em.HasComponent<BuildingOptionData>(p)) l.Add(new Dictionary<string, object> { { "buildingOptions", MaskNames<GB.BuildingOption>(em.GetComponentData<BuildingOptionData>(p).m_OptionMask) } });
            if (em.HasComponent<RouteOptionData>(p)) l.Add(new Dictionary<string, object> { { "routeOptions", MaskNames<GR.RouteOption>(em.GetComponentData<RouteOptionData>(p).m_OptionMask) } });
            return l;
        }

        /// <summary>Active flag and adjustment of a policy on a target (city, district, building, route).</summary>
        bool PolicyState(Entity target, Entity policy, out float adjustment)
        {
            adjustment = 0;
            if (target == Entity.Null || !em.Exists(target) || !em.HasBuffer<GPol.Policy>(target)) return false;
            var buf = em.GetBuffer<GPol.Policy>(target, true);
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].m_Policy == policy)
                {
                    adjustment = buf[i].m_Adjustment;
                    return (buf[i].m_Flags & GPol.PolicyFlags.Active) != 0;
                }
            return false;
        }

        List<Entity> Districts()
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GA.District>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = q.ToEntityArray(Allocator.Temp)) return arr.ToArray().ToList();
        }

        /// <summary>District by "index:version", bare index, or (custom/rendered) name.</summary>
        Entity FindDistrict(string s)
        {
            if (string.IsNullOrEmpty(s)) return Entity.Null;
            var all = Districts();
            var parts = s.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var i) && int.TryParse(parts[1], out var v))
            {
                var e = new Entity { Index = i, Version = v };
                return all.Contains(e) ? e : Entity.Null;
            }
            if (int.TryParse(s, out var idx)) { var e = all.FirstOrDefault(x => x.Index == idx); if (e != Entity.Null) return e; }
            foreach (var d in all) if (string.Equals(EntityLabel(d), s, StringComparison.OrdinalIgnoreCase)) return d;
            foreach (var d in all) { var n = EntityLabel(d); if (n != null && n.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return d; }
            return Entity.Null;
        }

        /// <summary>PoliciesUISystem.FilterPolicy (private): whether the vanilla UI offers this policy on this target. null = unknown.</summary>
        bool? UiOffersPolicy(GUI.PoliciesUISystem ui, Entity policy, Entity target)
        {
            try
            {
                if (ui == null || !Sys<PrefabSystem>().TryGetPrefab<PolicyPrefab>(policy, out var pp) || pp == null) return null;
                var m = typeof(GUI.PoliciesUISystem).GetMethod("FilterPolicy", Any);
                if (m == null || m.GetParameters().Length != 3) return null;
                return (bool)m.Invoke(ui, new object[] { policy, pp, target });
            }
            catch (Exception e) { Log.Warn("FilterPolicy: " + e.Message); return null; }
        }

        Entity FindPolicy(string name)
        {
            if (string.IsNullOrEmpty(name)) return Entity.Null;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<PolicyData>(), ComponentType.ReadOnly<PrefabData>());
            Entity contains = Entity.Null;
            using (var arr = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in arr)
                {
                    var pn = PrefabName(e);
                    var title = Loc("Policy.TITLE[" + pn + "]");
                    if (string.Equals(pn, name, StringComparison.OrdinalIgnoreCase) || string.Equals(title, name, StringComparison.OrdinalIgnoreCase)) return e;
                    if (contains == Entity.Null && ((pn != null && pn.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) || (title != null && title.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))) contains = e;
                }
            }
            return contains;
        }

        Dictionary<string, object> SliderJson(Entity p)
        {
            if (!em.HasComponent<PolicySliderData>(p)) return null;
            var s = em.GetComponentData<PolicySliderData>(p);
            return new Dictionary<string, object>
            {
                { "min", s.m_Range.min }, { "max", s.m_Range.max }, { "default", s.m_Default }, { "step", s.m_Step },
                { "unit", Enum.IsDefined(typeof(PolicySliderUnit), s.m_Unit) ? ((PolicySliderUnit)s.m_Unit).ToString() : s.m_Unit.ToString() },
            };
        }

        Dictionary<string, object> PolicyInfo(Entity p, PrefabSystem ps)
        {
            var pn = PrefabName(p);
            bool locked = IsLocked(p);
            var d = new Dictionary<string, object>
            {
                { "name", pn },
                { "title", Loc("Policy.TITLE[" + pn + "]") },
                { "description", Loc("Policy.DESCRIPTION[" + pn + "]") },
                { "scope", PolicyScope(p) },
                { "locked", locked },
            };
            if (ps.TryGetPrefab<PolicyPrefab>(p, out var pp) && pp != null)
            {
                d["category"] = pp.m_Category.ToString();
                if (pp.m_Visibility == PolicyVisibility.HideFromPolicyList) d["hidden"] = true;
            }
            if (locked) { try { d["requiredMilestone"] = GUI.ProgressionUtils.GetRequiredMilestone(em, p); } catch { } }
            var sl = SliderJson(p);
            if (sl != null) d["slider"] = sl;
            d["effects"] = PolicyEffects(p);
            return d;
        }

        // ------------------------------------------------------------------ GET /policies

        /// <summary>
        /// GET /policies[?scope=city|district|building|route|all][&amp;district=idOrName][&amp;target=entityId]:
        /// every policy prefab with title, description, scope, category, lock state (+ required milestone),
        /// slider range, effects (modifier types/ranges, options) and its state: on the city for city policies,
        /// on the given district/target, otherwise where it is active (districts, buildings, routes).
        /// </summary>
        object PolicyList(Pending p)
        {
            var scope = (p.Q("scope", "all") ?? "all").ToLowerInvariant();
            var ps = Sys<PrefabSystem>();
            var city = Sys<CitySystem>().City;
            Entity district = Entity.Null, target = Entity.Null;
            if (p.Q("district") != null)
            {
                district = FindDistrict(p.Q("district"));
                if (district == Entity.Null) return new ApiError("district not found: " + p.Q("district"), 404);
            }
            if (p.Q("target") != null)
            {
                target = Resolve(p.Q("target"), em.CreateEntityQuery(ComponentType.ReadOnly<GPol.Policy>()));
                if (target == Entity.Null) return new ApiError("target not found: " + p.Q("target"), 404);
            }

            // where non-city policies are active (only when no explicit target)
            var activeOn = new Dictionary<Entity, List<object>>();
            if (district == Entity.Null && target == Entity.Null)
            {
                void Collect(EntityQuery q, string kind)
                {
                    using (var arr = q.ToEntityArray(Allocator.Temp))
                        foreach (var e in arr)
                        {
                            var buf = em.GetBuffer<GPol.Policy>(e, true);
                            for (int i = 0; i < buf.Length; i++)
                            {
                                if ((buf[i].m_Flags & GPol.PolicyFlags.Active) == 0) continue;
                                if (!activeOn.TryGetValue(buf[i].m_Policy, out var l)) activeOn[buf[i].m_Policy] = l = new List<object>();
                                if (l.Count >= 30) continue;
                                var x = new Dictionary<string, object> { { "kind", kind }, { "id", Id(e) }, { "value", buf[i].m_Adjustment } };
                                var n = kind == "district" || kind == "route" ? EntityLabel(e) : (em.HasComponent<PrefabRef>(e) ? PrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab) : null);
                                if (n != null) x["name"] = n;
                                l.Add(x);
                            }
                        }
                }
                Collect(em.CreateEntityQuery(ComponentType.ReadOnly<GA.District>(), ComponentType.ReadOnly<GPol.Policy>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()), "district");
                Collect(em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<GPol.Policy>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()), "building");
                Collect(em.CreateEntityQuery(ComponentType.ReadOnly<GR.Route>(), ComponentType.ReadOnly<GPol.Policy>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()), "route");
            }

            var uiSys = world.GetExistingSystemManaged<GUI.PoliciesUISystem>();
            var list = new List<Dictionary<string, object>>();
            var q2 = em.CreateEntityQuery(ComponentType.ReadOnly<PolicyData>(), ComponentType.ReadOnly<PrefabData>());
            using (var arr = q2.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var sc = PolicyScope(e);
                    if (scope != "all" && scope != sc) continue;
                    var d = PolicyInfo(e, ps);
                    Entity on = sc == "city" ? city : sc == "district" ? district : target;
                    if (on != Entity.Null)
                    {
                        bool act = PolicyState(on, e, out var adj);
                        d["active"] = act;
                        if (em.HasComponent<PolicySliderData>(e)) d["value"] = act ? adj : em.GetComponentData<PolicySliderData>(e).m_Default;
                        if (sc != "city") d["target"] = Id(on);
                        if (sc == "building" || sc == "route") { var ok = UiOffersPolicy(uiSys, e, on); if (ok != null) d["offeredForTarget"] = ok.Value; }
                    }
                    else if (activeOn.TryGetValue(e, out var where)) d["activeOn"] = where;
                    list.Add(d);
                }
            var order = new Dictionary<string, int> { { "city", 0 }, { "district", 1 }, { "building", 2 }, { "route", 3 }, { "other", 4 } };
            list.Sort((a, b) =>
            {
                int c = order[(string)a["scope"]].CompareTo(order[(string)b["scope"]]);
                return c != 0 ? c : string.Compare((string)a["name"], (string)b["name"], StringComparison.Ordinal);
            });
            var r = new Dictionary<string, object>
            {
                { "note", "scope from the option/modifier components (city policies live on the City entity; district/building/route policies on those entities). value = slider adjustment (or its default when inactive). Set with POST /policy." },
                { "count", list.Count },
                { "policies", list },
                { "districts", Districts().Select(dd => (object)new Dictionary<string, object> { { "id", Id(dd) }, { "name", EntityLabel(dd) } }).ToList() },
            };
            if (district != Entity.Null) r["district"] = new Dictionary<string, object> { { "id", Id(district) }, { "name", EntityLabel(district) } };
            return r;
        }

        // ------------------------------------------------------------------ POST /policy

        /// <summary>
        /// POST /policy {name, active?, value?, district?, target?}: toggles/adjusts a policy through
        /// PoliciesUISystem.SetCityPolicy (city) or SetPolicy(target, ...) (district/building/route), which queue a
        /// Game.Policies.Modify event exactly like the UI; waits until the Policy buffer shows the new state.
        /// name = prefab name or in-game title; value is clamped to the slider range and snapped to its step;
        /// active defaults to true when only value is given. Locked and hidden policies are refused.
        /// </summary>
        object PolicySet(Pending p)
        {
            if (p.Method != "POST") return new ApiError("use POST /policy {name, active, value?, district?, target?}; GET /policies lists them", 405);
            var a = p.Body;
            var policy = FindPolicy(a.Str("name") ?? a.Str("policy"));
            if (policy == Entity.Null) return new ApiError("policy not found: " + (a.Str("name") ?? a.Str("policy")) + " (GET /policies lists names and titles)", 404);
            var ui = world.GetExistingSystemManaged<GUI.PoliciesUISystem>();
            if (ui == null) return new ApiError("PoliciesUISystem not available", 409);
            var ps = Sys<PrefabSystem>();
            var info = PolicyInfo(policy, ps);
            var scope = (string)info["scope"];
            if ((bool)info["locked"]) { var e1 = new ApiError("policy is locked", 409); e1["policy"] = info; return e1; }
            // route policies are hidden from the policy list but the line info panel sets them the same way
            // (VehicleCountSection/TicketPriceSection -> PoliciesUISystem.SetPolicy(route, policy, true, adjustment))
            bool linePanel = scope == "route";
            if (info.ContainsKey("hidden") && !linePanel) return new ApiError("policy is hidden from the policy lists (not settable from the UI)", 409);

            Entity target;
            if (scope == "city") target = Sys<CitySystem>().City;
            else if (scope == "district")
            {
                var ds = a.Str("district") ?? a.Str("target");
                if (ds == null) return new ApiError("district policy: pass district (id or name); GET /policies lists districts");
                target = FindDistrict(ds);
                if (target == Entity.Null) return new ApiError("district not found: " + ds, 404);
            }
            else if (scope == "building" || scope == "route")
            {
                var ts = a.Str("target");
                if (ts == null) return new ApiError(scope + " policy: pass target (entity id \"index:version\")");
                target = Resolve(a.Raw("target"), em.CreateEntityQuery(ComponentType.ReadOnly<GPol.Policy>()));
                if (target == Entity.Null || !em.Exists(target)) return new ApiError("target not found: " + ts, 404);
                if (scope == "building" && !em.HasComponent<GB.Building>(target)) return new ApiError("target is not a building");
                if (scope == "route" && !em.HasComponent<GR.Route>(target)) return new ApiError("target is not a route");
            }
            else return new ApiError("policy has no city/district/building/route scope");
            if (!em.HasBuffer<GPol.Policy>(target)) return new ApiError("target has no Policy buffer (cannot carry policies)", 409);
            var offered = linePanel ? (bool?)null : UiOffersPolicy(ui, policy, target);
            if (offered == false) return new ApiError("the game UI does not offer this policy for this target (PoliciesUISystem.FilterPolicy)", 409);

            bool wasActive = PolicyState(target, policy, out var oldAdj);
            bool hasSlider = em.HasComponent<PolicySliderData>(policy);
            if (!a.Has("active") && !a.Has("value")) return new ApiError("pass active (true|false) and/or value");
            bool active = a.Has("active") ? a.B("active") : true;
            float adj = oldAdj;
            object requested = null;
            if (hasSlider)
            {
                var s = em.GetComponentData<PolicySliderData>(policy);
                float v = a.Has("value") ? a.F("value") : (wasActive ? oldAdj : s.m_Default);
                requested = a.Has("value") ? (object)a.Num("value") : null;
                v = math.clamp(v, s.m_Range.min, s.m_Range.max);
                if (s.m_Step > 0) v = math.clamp(s.m_Range.min + math.round((v - s.m_Range.min) / s.m_Step) * s.m_Step, s.m_Range.min, s.m_Range.max);
                adj = v;
            }
            else if (a.Has("value")) requested = a.Num("value");

            if (scope == "city") ui.SetCityPolicy(policy, active, adj);
            else ui.SetPolicy(target, policy, active, adj);

            var tgt = target;
            float wantAdj = adj;
            bool wantActive = active;
            return new PollOperation(() =>
            {
                bool now = PolicyState(tgt, policy, out var na);
                return now == wantActive && (!wantActive || !hasSlider || math.abs(na - wantAdj) < 1e-4f);
            }, ok =>
            {
                bool now = PolicyState(tgt, policy, out var na);
                var r = new Dictionary<string, object>
                {
                    { "policy", PrefabName(policy) }, { "title", info["title"] }, { "scope", scope },
                    { "target", Id(tgt) }, { "targetName", scope == "city" ? CityName : EntityLabel(tgt) },
                    { "before", new Dictionary<string, object> { { "active", wasActive }, { "value", oldAdj } } },
                    { "requested", new Dictionary<string, object> { { "active", wantActive }, { "value", requested } } },
                    { "sent", new Dictionary<string, object> { { "active", wantActive }, { "value", wantAdj } } },
                    { "after", new Dictionary<string, object> { { "active", now }, { "value", na } } },
                    { "confirmed", ok },
                };
                if (hasSlider) r["slider"] = info["slider"];
                if (!ok) r["note"] = "Modify event queued via the UI system; the Policy buffer did not show it yet (paused simulation?). Re-check with GET /policies.";
                return r;
            }, 120);
        }

        // ------------------------------------------------------------------ GET /modifiers

        static Dictionary<string, object> ModJson(string type, float2 delta) => new Dictionary<string, object>
        {
            { "type", type }, { "absolute", delta.x }, { "relative", delta.y }, { "relativePct", Math.Round(delta.y * 100.0, 2) },
        };

        /// <summary>
        /// GET /modifiers: the City entity's CityModifier buffer (what is in effect: value' = (value + absolute) *
        /// (1 + relative)), active city options, the sources (active city policies and CityEffectProvider
        /// buildings with their prefab modifier data and efficiency), and per district its DistrictModifier
        /// buffer, options and active policies.
        /// </summary>
        object Modifiers(Pending p)
        {
            var city = Sys<CitySystem>().City;
            var mods = new List<object>();
            if (em.HasBuffer<GCity.CityModifier>(city))
            {
                var buf = em.GetBuffer<GCity.CityModifier>(city, true);
                for (int i = 0; i < buf.Length; i++)
                {
                    var dlt = buf[i].m_Delta;
                    if (dlt.x == 0 && dlt.y == 0) continue;
                    mods.Add(ModJson(Enum.IsDefined(typeof(GCity.CityModifierType), i) ? ((GCity.CityModifierType)i).ToString() : "type" + i, dlt));
                }
            }
            var cityOptions = new List<object>();
            if (em.HasComponent<GCity.City>(city))
            {
                var c = em.GetComponentData<GCity.City>(city);
                foreach (GCity.CityOption o in Enum.GetValues(typeof(GCity.CityOption)))
                    if (GCity.CityUtils.CheckOption(c, o)) cityOptions.Add(o.ToString());
            }

            // sources: active city policies
            var polSrc = new List<object>();
            if (em.HasBuffer<GPol.Policy>(city))
            {
                var pb = em.GetBuffer<GPol.Policy>(city, true);
                for (int i = 0; i < pb.Length; i++)
                {
                    if ((pb[i].m_Flags & GPol.PolicyFlags.Active) == 0) continue;
                    var pe = pb[i].m_Policy;
                    var pn = PrefabName(pe);
                    var d = new Dictionary<string, object> { { "policy", pn }, { "title", Loc("Policy.TITLE[" + pn + "]") }, { "effects", PolicyEffects(pe) } };
                    if (em.HasComponent<PolicySliderData>(pe)) d["value"] = pb[i].m_Adjustment;
                    polSrc.Add(d);
                }
            }

            // sources: buildings providing city effects (City Hall, banks, universities, research...)
            var bySrc = new Dictionary<string, Dictionary<string, object>>();
            var bq = em.CreateEntityQuery(ComponentType.ReadOnly<GB.CityEffectProvider>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Common.Destroyed>(), ComponentType.Exclude<Temp>());
            using (var arr = bq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    var effects = new List<object>();
                    void AddFrom(Entity pf, string via)
                    {
                        if (!em.HasBuffer<CityModifierData>(pf)) return;
                        foreach (var m in em.GetBuffer<CityModifierData>(pf, true))
                        {
                            var x = new Dictionary<string, object> { { "cityModifier", m.m_Type.ToString() }, { "mode", m.m_Mode.ToString() }, { "range", new List<object> { m.m_Range.min, m.m_Range.max } } };
                            if (via != null) x["upgrade"] = via;
                            effects.Add(x);
                        }
                    }
                    AddFrom(prefab, null);
                    if (em.HasBuffer<GB.InstalledUpgrade>(e))
                        foreach (var u in em.GetBuffer<GB.InstalledUpgrade>(e, true))
                            if (em.Exists(u.m_Upgrade) && em.HasComponent<PrefabRef>(u.m_Upgrade))
                            {
                                var up = em.GetComponentData<PrefabRef>(u.m_Upgrade).m_Prefab;
                                AddFrom(up, PrefabName(up));
                            }
                    float eff = 1f;
                    if (em.HasBuffer<GB.Efficiency>(e)) eff = GB.BuildingUtils.GetEfficiency(em.GetBuffer<GB.Efficiency>(e, true));
                    var key = PrefabName(prefab) + "|" + string.Join(",", effects.Select(x => Json.Write(x)));
                    if (!bySrc.TryGetValue(key, out var g))
                        bySrc[key] = g = new Dictionary<string, object> { { "prefab", PrefabName(prefab) }, { "count", 0 }, { "ids", new List<object>() }, { "efficiency", new List<object>() }, { "effects", effects } };
                    g["count"] = (int)g["count"] + 1;
                    if (((List<object>)g["ids"]).Count < 10) { ((List<object>)g["ids"]).Add(Id(e)); ((List<object>)g["efficiency"]).Add(Math.Round(eff, 2)); }
                }

            // districts
            var dl = new List<object>();
            foreach (var d in Districts())
            {
                var dd = new Dictionary<string, object> { { "id", Id(d) }, { "name", EntityLabel(d) } };
                var dm = new List<object>();
                if (em.HasBuffer<GA.DistrictModifier>(d))
                {
                    var buf = em.GetBuffer<GA.DistrictModifier>(d, true);
                    for (int i = 0; i < buf.Length; i++)
                    {
                        var dlt = buf[i].m_Delta;
                        if (dlt.x == 0 && dlt.y == 0) continue;
                        dm.Add(ModJson(Enum.IsDefined(typeof(GA.DistrictModifierType), i) ? ((GA.DistrictModifierType)i).ToString() : "type" + i, dlt));
                    }
                }
                dd["modifiers"] = dm;
                var opts = new List<object>();
                if (em.HasComponent<GA.District>(d))
                {
                    var dc = em.GetComponentData<GA.District>(d);
                    foreach (GA.DistrictOption o in Enum.GetValues(typeof(GA.DistrictOption)))
                        if (GA.AreaUtils.CheckOption(dc, o)) opts.Add(o.ToString());
                }
                dd["options"] = opts;
                var dp = new List<object>();
                if (em.HasBuffer<GPol.Policy>(d))
                    foreach (var pol in em.GetBuffer<GPol.Policy>(d, true))
                        if ((pol.m_Flags & GPol.PolicyFlags.Active) != 0)
                        {
                            var x = new Dictionary<string, object> { { "policy", PrefabName(pol.m_Policy) } };
                            if (em.HasComponent<PolicySliderData>(pol.m_Policy)) x["value"] = pol.m_Adjustment;
                            dp.Add(x);
                        }
                dd["policies"] = dp;
                dl.Add(dd);
            }

            return new Dictionary<string, object>
            {
                { "note", "CityModifier entries apply as value' = (value + absolute) * (1 + relative). Sources: active city policies (slider maps its range onto the modifier range) and CityEffectProvider buildings (prefab + installed upgrades, scaled by building efficiency). Local (radius) building effects are not included." },
                { "city", new Dictionary<string, object> { { "modifiers", mods }, { "options", cityOptions } } },
                { "sources", new Dictionary<string, object> { { "policies", polSrc }, { "buildings", bySrc.Values.Cast<object>().ToList() } } },
                { "districts", dl },
            };
        }
    }
}
