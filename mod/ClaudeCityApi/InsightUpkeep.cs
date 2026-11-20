using System;
using System.Collections.Generic;
using System.Linq;
using Game.City;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using GB = Game.Buildings;
using GCo = Game.Companies;
using GE = Game.Economy;
using GP = Game.Prefabs;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Monthly upkeep as the game computes it:
    /// - per building: Game.UI.InGame.UpkeepSection (info panel) / CityServiceBudgetJob (the charge): every
    ///   ServiceUpkeepData of the prefab and of each installed upgrade; money items through
    ///   CityServiceUpkeepSystem.CalculateUpkeep (x service budget %), CityModifier
    ///   CityServiceBuildingBaseUpkeepCost, x0.1 when the building/upgrade is out of service;
    ///   material items at EconomyUtils.GetMarketPrice (x ServiceUsage when ScaleWithUsage, x upgrade
    ///   UpkeepModifierData); plus employee wages (GetUpkeepOfEmployeeWage: sum of
    ///   EconomyParameterData.GetWage(level, cityServiceJob) over the Employee buffer) x budget %.
    /// - per prefab: what the build menu shows (PrefabUISystem.UpkeepPropertyBinderSystem: money x budget %,
    ///   money+materials) plus wages at full staff (EconomyUtils.CalculateNumberOfWorkplaces(maxWorkers,
    ///   complexity, level 1) x GetWage) so the real monthly cost is visible before building.
    /// - map tiles: MapTilePurchaseSystem price/upkeep formula for one tile.
    /// </summary>
    public partial class Api
    {
        class UpkeepCtx
        {
            public int Frame = -1;
            public Entity BudgetEntity;
            public bool HasBudget, HasEcon;
            public Entity City; // city modifiers are read fresh each time (a cached DynamicBuffer dies on any structural change)
            public GP.EconomyParameterData Econ;
            public GP.ResourcePrefabs ResPrefabs;
            public bool HasRes;
            public readonly Dictionary<GE.Resource, float> Prices = new Dictionary<GE.Resource, float>();
        }

        UpkeepCtx upkeepCtx;

        /// <summary>Per-call context (budget singleton, city modifiers, economy parameters); rebuilt once per frame.</summary>
        UpkeepCtx UpCtx()
        {
            int f = UnityEngine.Time.frameCount;
            if (upkeepCtx != null && upkeepCtx.Frame == f && (!upkeepCtx.HasBudget || em.Exists(upkeepCtx.BudgetEntity))) return upkeepCtx;
            var c = new UpkeepCtx { Frame = f };
            var bq = em.CreateEntityQuery(ComponentType.ReadOnly<ServiceBudgetData>());
            using (var arr = bq.ToEntityArray(Allocator.Temp))
                if (arr.Length > 0) { c.BudgetEntity = arr[0]; c.HasBudget = true; }
            c.City = Sys<CitySystem>().City;
            c.HasEcon = TrySingleton<GP.EconomyParameterData>(out c.Econ);
            try { c.ResPrefabs = Sys<GP.ResourceSystem>().GetPrefabs(); c.HasRes = true; } catch { }
            upkeepCtx = c;
            return c;
        }

        /// <summary>CityModifier CityServiceBuildingBaseUpkeepCost (policies), as UpkeepSection / CityServiceBudgetJob.</summary>
        float BaseUpkeepModifier(UpkeepCtx c, float v)
        {
            if (c.City != Entity.Null && em.Exists(c.City) && em.HasBuffer<CityModifier>(c.City))
                CityUtils.ApplyModifier(ref v, em.GetBuffer<CityModifier>(c.City, true), CityModifierType.CityServiceBuildingBaseUpkeepCost);
            return v;
        }

        float MarketPrice(UpkeepCtx c, GE.Resource r)
        {
            if (c.Prices.TryGetValue(r, out var p)) return p;
            p = 0f;
            if (c.HasRes) { try { p = GE.EconomyUtils.GetMarketPrice(r, c.ResPrefabs, em); } catch { } }
            c.Prices[r] = p;
            return p;
        }

        int BudgetPct(Entity prefab)
        {
            var c = UpCtx();
            if (!c.HasBudget || !em.HasComponent<GP.ServiceObjectData>(prefab)) return 100;
            var svc = em.GetComponentData<GP.ServiceObjectData>(prefab).m_Service;
            var buf = em.GetBuffer<ServiceBudgetData>(c.BudgetEntity, true);
            for (int i = 0; i < buf.Length; i++) if (buf[i].m_Service == svc) return buf[i].m_Budget;
            return 100;
        }

        int MoneyUpkeep(int amount, Entity mainPrefab)
        {
            var c = UpCtx();
            return c.HasBudget ? CityServiceUpkeepSystem.CalculateUpkeep(amount, mainPrefab, c.BudgetEntity, em) : amount;
        }

        /// <summary>Resource multipliers from installed (active) upgrades, as CityServiceUpkeepSystem.GetUpkeepModifierData.</summary>
        Dictionary<GE.Resource, float> UpkeepModifiers(Entity e)
        {
            var d = new Dictionary<GE.Resource, float>();
            if (!em.HasBuffer<GB.InstalledUpgrade>(e)) return d;
            var ups = em.GetBuffer<GB.InstalledUpgrade>(e, true);
            for (int i = 0; i < ups.Length; i++)
            {
                if (GB.BuildingUtils.CheckOption(ups[i], GB.BuildingOption.Inactive)) continue;
                var u = ups[i].m_Upgrade;
                if (!em.HasComponent<GP.PrefabRef>(u)) continue;
                var up = em.GetComponentData<GP.PrefabRef>(u).m_Prefab;
                if (!em.HasBuffer<GP.UpkeepModifierData>(up)) continue;
                var mb = em.GetBuffer<GP.UpkeepModifierData>(up, true);
                for (int k = 0; k < mb.Length; k++)
                    d[mb[k].m_Resource] = (d.TryGetValue(mb[k].m_Resource, out var m) ? m : 1f) * mb[k].m_Multiplier;
            }
            return d;
        }

        /// <summary>
        /// Monthly upkeep of one service building (main building + upgrades + wages), exactly as the
        /// building's info panel totals it. Null when the prefab has no upkeep and no workplaces.
        /// </summary>
        Dictionary<string, object> BuildingUpkeep(Entity e, Entity prefab)
        {
            bool hasItems = em.HasBuffer<GP.ServiceUpkeepData>(prefab), hasWork = em.HasComponent<GP.WorkplaceData>(prefab);
            if (!hasItems && !hasWork && !em.HasBuffer<GB.InstalledUpgrade>(e)) return null;
            var c = UpCtx();
            bool inactive = false;
            if (em.HasComponent<GB.Building>(e) && GB.BuildingUtils.CheckOption(em.GetComponentData<GB.Building>(e), GB.BuildingOption.Inactive)) inactive = true;
            else if (em.HasComponent<GB.Extension>(e) && (em.GetComponentData<GB.Extension>(e).m_Flags & GB.ExtensionFlags.Disabled) != 0) inactive = true;
            var mods = UpkeepModifiers(e);
            float usage = em.HasComponent<GB.ServiceUsage>(e) ? em.GetComponentData<GB.ServiceUsage>(e).m_Usage : -1f;
            long basePrice = 0, resPrice = 0, wagePrice = 0;
            var resources = new Dictionary<GE.Resource, long[]>();
            int upgrades = 0;
            bool usageScaled = false;

            void Items(DynamicBuffer<GP.ServiceUpkeepData> buf, bool inactiveUpgrade)
            {
                for (int i = 0; i < buf.Length; i++)
                {
                    var r = buf[i].m_Upkeep.m_Resource;
                    int amount = buf[i].m_Upkeep.m_Amount;
                    if (c.HasBudget && r == GE.Resource.Money)
                    {
                        // CityServiceBudgetJob (what is charged) scales usage-dependent money items too; the info panel does not
                        if (buf[i].m_ScaleWithUsage && usage >= 0f) { amount = (int)(amount * usage); usageScaled = true; }
                        float price = BaseUpkeepModifier(c, MoneyUpkeep(amount, prefab));
                        if (inactive || inactiveUpgrade) price *= 0.1f;
                        basePrice += (int)math.round(price);
                    }
                    else if (!inactiveUpgrade)
                    {
                        float mult = mods.TryGetValue(r, out var m) ? m : 1f;
                        int amt = (int)math.round(amount * mult);
                        int price = (int)math.round(amt * MarketPrice(c, r));
                        if (buf[i].m_ScaleWithUsage && usage >= 0f) { amt = (int)(amt * usage); price = (int)(price * usage); }
                        if (amt == 0 || price == 0) continue;
                        if (!resources.TryGetValue(r, out var acc)) resources[r] = acc = new long[2];
                        acc[0] += amt; acc[1] += price;
                        resPrice += price;
                    }
                }
            }

            if (hasItems) Items(em.GetBuffer<GP.ServiceUpkeepData>(prefab, true), false);
            if (em.HasBuffer<GB.InstalledUpgrade>(e))
            {
                var ups = em.GetBuffer<GB.InstalledUpgrade>(e, true);
                for (int i = 0; i < ups.Length; i++)
                {
                    var u = ups[i].m_Upgrade;
                    if (!em.HasComponent<GP.PrefabRef>(u)) continue;
                    var up = em.GetComponentData<GP.PrefabRef>(u).m_Prefab;
                    if (!em.HasBuffer<GP.ServiceUpkeepData>(up)) continue;
                    upgrades++;
                    // re-read the upgrade list after GetBuffer on another entity is not needed: no structural change here
                    Items(em.GetBuffer<GP.ServiceUpkeepData>(up, true), GB.BuildingUtils.CheckOption(ups[i], GB.BuildingOption.Inactive));
                }
            }
            int wageRaw = 0;
            if (hasWork && c.HasEcon && !inactive && em.HasBuffer<GCo.Employee>(e))
            {
                var emp = em.GetBuffer<GCo.Employee>(e, true);
                for (int i = 0; i < emp.Length; i++) wageRaw += c.Econ.GetWage(emp[i].m_Level, true);
                wagePrice = MoneyUpkeep(wageRaw, prefab);
            }
            var d = new Dictionary<string, object>
            {
                { "perMonth", basePrice + resPrice + wagePrice },
                { "base", basePrice },
                { "wages", wagePrice },
                { "budgetPct", BudgetPct(prefab) },
            };
            if (resPrice > 0)
            {
                d["materials"] = resPrice;
                d["materialItems"] = resources.Select(kv => (object)new Dictionary<string, object> { { "resource", kv.Key.ToString() }, { "amount", kv.Value[0] }, { "cost", kv.Value[1] } }).ToList();
            }
            if (upgrades > 0) d["upgradesWithUpkeep"] = upgrades;
            if (usageScaled) d["usage"] = Math.Round(usage, 2);
            if (inactive) d["outOfService"] = true;
            return d;
        }

        /// <summary>Upkeep of a prefab before it is built: build-menu numbers + wages at full staff.</summary>
        Dictionary<string, object> PrefabUpkeep(Entity prefab)
        {
            var c = UpCtx();
            int money = 0, menuWithMaterials = 0;
            long materials = 0;
            if (em.HasBuffer<GP.ServiceUpkeepData>(prefab))
            {
                var buf = em.GetBuffer<GP.ServiceUpkeepData>(prefab, true);
                for (int i = 0; i < buf.Length; i++)
                {
                    var r = buf[i].m_Upkeep.m_Resource;
                    int amount = buf[i].m_Upkeep.m_Amount;
                    if (c.HasBudget && r == GE.Resource.Money) money += MoneyUpkeep(amount, prefab);
                    else materials += (int)math.round(amount * MarketPrice(c, r));
                }
            }
            menuWithMaterials = money + (int)materials;
            float baseMod = BaseUpkeepModifier(c, money);
            int wages = 0, workers = 0;
            if (em.HasComponent<GP.WorkplaceData>(prefab) && c.HasEcon)
            {
                var wd = em.GetComponentData<GP.WorkplaceData>(prefab);
                workers = wd.m_MaxWorkers;
                if (wd.m_MaxWorkers > 0)
                {
                    var wp = GE.EconomyUtils.CalculateNumberOfWorkplaces(wd.m_MaxWorkers, wd.m_Complexity, 1);
                    int raw = 0;
                    for (int l = 0; l < 5; l++) raw += wp[l] * c.Econ.GetWage(l, true);
                    wages = MoneyUpkeep(raw, prefab);
                }
            }
            if (money == 0 && materials == 0 && wages == 0) return null;
            var d = new Dictionary<string, object>
            {
                { "perMonth", (int)math.round(baseMod) + materials + wages },
                { "base", (int)math.round(baseMod) },
                { "wagesFullStaff", wages },
                { "menu", menuWithMaterials },
                { "budgetPct", BudgetPct(prefab) },
            };
            if (materials > 0) d["materials"] = materials;
            if (workers > 0) d["maxWorkers"] = workers;
            return d;
        }

        /// <summary>Hook for /prefabs: add "upkeep" to a services prefab row.</summary>
        void AddPrefabUpkeep(Dictionary<string, object> d, Entity prefab)
        {
            try { var u = PrefabUpkeep(prefab); if (u != null) d["upkeep"] = u; }
            catch (Exception ex) { d["upkeepError"] = ex.GetType().Name + ": " + ex.Message; }
        }

        // ------------------------------------------------------------------ map tiles

        class TileQuoteCtx
        {
            public bool Ok;
            public string Error;
            public int Owned, CurrentUpkeep, Money, Available;
            public float OwnedValue, MultNow, MultNext;
            public bool UpkeepEnabled;
            public double[] Baseline;
        }

        TileQuoteCtx TileQuoteContext()
        {
            var c = new TileQuoteCtx();
            try
            {
                var mtp = Sys<MapTilePurchaseSystem>();
                c.Owned = Convert.ToInt32(Invoke(mtp, "CalculateOwnedTiles"));
                c.OwnedValue = Convert.ToSingle(Invoke(mtp, "CalculateOwnedTilesCost", false));
                c.CurrentUpkeep = mtp.CalculateOwnedTilesUpkeep();
                c.MultNow = mtp.GetMapTileUpkeepCostMultiplier(c.Owned);
                c.MultNext = mtp.GetMapTileUpkeepCostMultiplier(c.Owned + 1);
                c.UpkeepEnabled = mtp.GetMapTileUpkeepEnabled();
                c.Available = mtp.GetAvailableTiles();
                c.Money = Sys<CitySystem>().moneyAmount;
                c.Baseline = typeof(MapTilePurchaseSystem).GetField("kMapFeatureBaselineModifiers", Any)?.GetValue(null) as double[];
                c.Ok = true;
            }
            catch (Exception ex) { c.Error = ex.GetType().Name + ": " + ex.Message; }
            return c;
        }

        /// <summary>Purchase value of one tile: sum over map features amount x baseline x 10 x MapFeatureData.m_Cost x TilePurchaseCostFactor.</summary>
        float TileValue(Entity tile, TileQuoteCtx c)
        {
            if (!em.HasBuffer<Game.Areas.MapFeatureElement>(tile) || !em.HasComponent<GP.PrefabRef>(tile)) return 0f;
            var pf = em.GetComponentData<GP.PrefabRef>(tile).m_Prefab;
            if (!em.HasBuffer<GP.MapFeatureData>(pf) || !em.HasComponent<GP.TilePurchaseCostFactor>(pf)) return 0f;
            float factor = em.GetComponentData<GP.TilePurchaseCostFactor>(pf).m_Amount;
            var feats = em.GetBuffer<Game.Areas.MapFeatureElement>(tile, true);
            var costs = em.GetBuffer<GP.MapFeatureData>(pf, true);
            float v = 0f;
            for (int i = 0; i < feats.Length && i < costs.Length; i++)
            {
                double bl = c.Baseline != null && i < c.Baseline.Length ? c.Baseline[i] : 1.0;
                v += (float)(feats[i].m_Amount * bl * 10.0 * costs[i].m_Cost * factor);
            }
            return v;
        }

        /// <summary>Hook for /tiles candidates: price and upkeep change if this single tile were bought now.</summary>
        void AddTileQuote(Dictionary<string, object> d, Entity tile, TileQuoteCtx c)
        {
            if (c == null || !c.Ok) return;
            try
            {
                float value = TileValue(tile, c);
                int price = (int)math.round(value * c.Owned); // MapTilePurchaseSystem.UpdateStatus for a 1-tile selection
                int after = (int)math.round((c.OwnedValue + value) * c.MultNext);
                d["price"] = price;
                d["upkeepIncrease"] = c.UpkeepEnabled ? after - c.CurrentUpkeep : 0;
                d["affordable"] = price <= c.Money;
            }
            catch (Exception ex) { d["quoteError"] = ex.GetType().Name + ": " + ex.Message; }
        }

        Dictionary<string, object> TileUpkeepSummary(TileQuoteCtx c)
        {
            if (c == null) return null;
            if (!c.Ok) return new Dictionary<string, object> { { "error", c.Error } };
            return new Dictionary<string, object>
            {
                { "currentPerMonth", c.CurrentUpkeep }, { "enabled", c.UpkeepEnabled }, { "ownedTiles", c.Owned },
                { "multiplierNow", Math.Round(c.MultNow, 4) }, { "multiplierAfterOneMore", Math.Round(c.MultNext, 4) },
                { "note", "candidates[].price = value x owned tiles (MapTilePurchaseSystem, one tile at a time; buying several at once costs more for the later ones); upkeepIncrease = new monthly tile upkeep minus current (CalculateOwnedTilesUpkeep formula). Prices rise with every tile owned." },
            };
        }
    }
}
