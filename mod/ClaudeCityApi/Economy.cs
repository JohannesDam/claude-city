using System;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using GCity = Game.City;
using GCit = Game.Citizens;
using GCo = Game.Companies;
using GE = Game.Economy;
using GUI = Game.UI.InGame;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        static readonly string[] EduNames = { "Uneducated", "PoorlyEducated", "Educated", "WellEducated", "HighlyEducated" };

        static List<object> I2(int2 v) => new List<object> { v.x, v.y };
        static int At(NativeArray<int> a, int i) => a.IsCreated && i >= 0 && i < a.Length ? a[i] : 0;
        static NativeArray<int> PrivArray(object sys, string field) => GetField(sys, field) is NativeArray<int> a ? a : default;

        // ------------------------------------------------------------------ /demand

        static string DemandFactorName(int i) => Enum.IsDefined(typeof(DemandFactor), i) ? ((DemandFactor)i).ToString() : "factor" + i;

        /// <summary>Non-zero factors sorted like Game.UI.InGame.FactorInfo (|weight| desc); top = the 5 the tooltip shows.</summary>
        static Dictionary<string, object> FactorDict(NativeArray<int> f, out List<object> top)
        {
            var l = new List<KeyValuePair<int, int>>();
            for (int i = 0; i < f.Length; i++) if (f[i] != 0) l.Add(new KeyValuePair<int, int>(i, f[i]));
            l.Sort((a, b) => { int c = math.abs(b.Value).CompareTo(math.abs(a.Value)); return c != 0 ? c : b.Key.CompareTo(a.Key); });
            var d = new Dictionary<string, object>();
            top = new List<object>();
            foreach (var kv in l)
            {
                var n = DemandFactorName(kv.Key);
                d[n] = kv.Value;
                if (top.Count < 5) top.Add(n);
            }
            return d;
        }

        /// <summary>
        /// GET /demand[?all=1]: per zone type (and residential density) the building demand (0-100), the smoothed
        /// bar the UI draws (0-1), the signed demand factors and the top 5 the in-game tooltip shows; per resource
        /// company/building demand, free properties and storage. all=1 also lists resources with all-zero values.
        /// </summary>
        object Demand(Pending p)
        {
            bool all = p.Q("all", "0") == "1" || p.Q("all", "") == "true";
            var res = Sys<ResidentialDemandSystem>();
            var com = Sys<CommercialDemandSystem>();
            var ind = Sys<IndustrialDemandSystem>();
            var info = world.GetExistingSystemManaged<GUI.CityInfoUISystem>();
            float Bar(string field) => info != null && GetField(info, field) is float f ? f : float.NaN;
            Dictionary<string, object> Zone(int demand, float bar, NativeArray<int> f, int company = int.MinValue)
            {
                var fd = FactorDict(f, out var top);
                var z = new Dictionary<string, object> { { "demand", demand }, { "uiBar", bar } };
                if (company != int.MinValue) z["companyDemand"] = company;
                z["factors"] = fd;
                z["tooltip"] = top;
                return z;
            }

            JobHandle dep;
            var lowF = res.GetLowDensityDemandFactors(out dep); dep.Complete();
            var medF = res.GetMediumDensityDemandFactors(out dep); dep.Complete();
            var highF = res.GetHighDensityDemandFactors(out dep); dep.Complete();
            var bd = res.buildingDemand;
            var residential = new Dictionary<string, object>
            {
                { "householdDemand", res.householdDemand },
                { "low", Zone(bd.x, Bar("m_ResidentialLowDemand"), lowF) },
                { "medium", Zone(bd.y, Bar("m_ResidentialMediumDemand"), medF) },
                { "high", Zone(bd.z, Bar("m_ResidentialHighDemand"), highF) },
            };

            // commercial
            var comF = com.GetDemandFactors(out dep); dep.Complete();
            var comRD = com.GetResourceDemands(out dep); dep.Complete();
            var comBD = com.GetBuildingDemands(out dep); dep.Complete();
            var comCons = com.GetConsumption(out dep); dep.Complete();
            var comFree = PrivArray(com, "m_FreeProperties");
            var comRes = new Dictionary<string, object>();
            var it = GE.ResourceIterator.GetIterator();
            while (it.Next())
            {
                var r = it.resource;
                if (!GE.EconomyUtils.IsCommercialResource(r)) continue;
                int i = GE.EconomyUtils.GetResourceIndex(r);
                int cd = At(comRD, i), bdm = At(comBD, i), fp = At(comFree, i);
                if (!all && cd == 0 && bdm == 0 && fp == 0) continue;
                comRes[r.ToString()] = new Dictionary<string, object> { { "companyDemand", cd }, { "buildingDemand", bdm }, { "freeProperties", fp }, { "consumption", At(comCons, i) } };
            }
            var commercial = Zone(com.buildingDemand, Bar("m_CommercialDemand"), comF, com.companyDemand);
            commercial["resources"] = comRes;

            // industrial / office / storage (one system)
            var indF = ind.GetIndustrialDemandFactors(out dep); dep.Complete();
            var offF = ind.GetOfficeDemandFactors(out dep); dep.Complete();
            var indCD = ind.GetResourceDemands(out dep); dep.Complete();
            var indBD = ind.GetBuildingDemands(out dep); dep.Complete();
            var stCD = ind.GetStorageCompanyDemands(out dep); dep.Complete();
            var stBD = ind.GetStorageBuildingDemands(out dep); dep.Complete();
            var indCons = ind.GetIndustrialResourceDemands(out dep); dep.Complete();
            var indZone = PrivArray(ind, "m_IndustrialZoningDemands");
            var indFree = PrivArray(ind, "m_FreeProperties");
            var stFree = PrivArray(ind, "m_FreeStorages");
            var stCount = PrivArray(ind, "m_Storages");
            var stCap = PrivArray(ind, "m_StorageCapacities");
            var rs = Sys<ResourceSystem>();
            var indRes = new Dictionary<string, object>();
            var offRes = new Dictionary<string, object>();
            it = GE.ResourceIterator.GetIterator();
            while (it.Next())
            {
                var r = it.resource;
                int i = GE.EconomyUtils.GetResourceIndex(r);
                bool office = GE.EconomyUtils.IsOfficeResource(r);
                bool industrial = false;
                try
                {
                    var pe = rs.GetPrefab(r);
                    if (pe != Entity.Null && em.HasComponent<ResourceData>(pe))
                        industrial = GE.EconomyUtils.IsIndustrialResource(em.GetComponentData<ResourceData>(pe), true, false);
                }
                catch { }
                int cd = At(indCD, i), bdm = At(indBD, i), zd = At(indZone, i), scd = At(stCD, i), sbd = At(stBD, i), fp = At(indFree, i), fs = At(stFree, i);
                bool any = cd != 0 || bdm != 0 || zd != 0 || scd != 0 || sbd != 0 || fp != 0 || fs != 0;
                if (!any && !(all && (office || industrial))) continue;
                var d = new Dictionary<string, object>
                {
                    { "companyDemand", cd }, { "buildingDemand", bdm }, { "zoningDemand", zd }, { "freeProperties", fp },
                    { "resourceDemand", At(indCons, i) },
                };
                if (!office)
                {
                    d["storageCompanyDemand"] = scd; d["storageBuildingDemand"] = sbd;
                    d["freeStorages"] = fs; d["storages"] = At(stCount, i); d["storageCapacity"] = At(stCap, i);
                }
                (office ? offRes : indRes)[r.ToString()] = d;
            }
            var industrialZ = Zone(ind.industrialBuildingDemand, Bar("m_IndustrialDemand"), indF, ind.industrialCompanyDemand);
            industrialZ["storage"] = new Dictionary<string, object> { { "buildingDemand", ind.storageBuildingDemand }, { "companyDemand", ind.storageCompanyDemand } };
            industrialZ["resources"] = indRes;
            var officeZ = Zone(ind.officeBuildingDemand, Bar("m_OfficeDemand"), offF, ind.officeCompanyDemand);
            officeZ["resources"] = offRes;

            return new Dictionary<string, object>
            {
                { "note", "demand = building demand 0-100 (what spawns buildings); uiBar = the smoothed bar the game draws (0-1; industrial bar uses max(industrial, storage)); factors = signed weights per DemandFactor (positive raises demand), sorted like the tooltip; tooltip = the 5 the game shows. Resource keys are Game.Economy.Resource names." },
                { "residential", residential },
                { "commercial", commercial },
                { "industrial", industrialZ },
                { "office", officeZ },
            };
        }

        // ------------------------------------------------------------------ /finance and /loan

        static Dictionary<string, object> LoanJson(LoanInfo l) => new Dictionary<string, object>
        {
            { "amount", l.m_Amount }, { "dailyInterestRate", l.m_DailyInterestRate }, { "dailyPayment", l.m_DailyPayment },
        };

        Dictionary<string, object> LoanState()
        {
            var ls = Sys<LoanSystem>();
            var city = Sys<CitySystem>().City;
            var cur = ls.CurrentLoan;
            int credit = ls.Creditworthiness;
            int money = em.HasComponent<GCity.PlayerMoney>(city) ? em.GetComponentData<GCity.PlayerMoney>(city).money : 0;
            return new Dictionary<string, object>
            {
                { "current", LoanJson(cur) },
                { "creditworthiness", credit },
                { "money", money },
                { "allowedTotal", new List<object> { math.max(0, cur.m_Amount - math.max(0, money)), credit } },
                { "canBorrowMore", math.max(0, credit - cur.m_Amount) },
            };
        }

        /// <summary>
        /// GET /finance: the Budget panel's income and expense per source (per month, expenses positive), totals,
        /// balance, money delta, cash, upkeep per city service, and the loan state.
        /// </summary>
        object Finance(Pending p)
        {
            var b = Sys<CityServiceBudgetSystem>();
            var city = Sys<CitySystem>().City;
            var income = new Dictionary<string, object>();
            long ti = 0, te = 0;
            for (int i = 0; i < (int)GCity.IncomeSource.Count; i++)
            {
                int v = b.GetIncome((GCity.IncomeSource)i);
                income[((GCity.IncomeSource)i).ToString()] = v;
                ti += v;
            }
            var expenses = new Dictionary<string, object>();
            for (int i = 0; i < (int)GCity.ExpenseSource.Count; i++)
            {
                int v = b.GetExpense((GCity.ExpenseSource)i);
                expenses[((GCity.ExpenseSource)i).ToString()] = v;
                te += v;
            }
            var svc = new List<Dictionary<string, object>>();
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<ServiceData>(), ComponentType.ReadOnly<PrefabData>());
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    int upkeep = 0;
                    try { b.GetEstimatedServiceBudget(e, out upkeep); } catch { }
                    int pct = b.GetServiceBudget(e);
                    int eff = 0;
                    try { eff = b.GetServiceEfficiency(e, pct); } catch { }
                    if (upkeep == 0 && !p.Query.ContainsKey("all")) continue;
                    svc.Add(new Dictionary<string, object> { { "service", PrefabName(e) }, { "budgetPct", pct }, { "efficiency", eff }, { "upkeep", upkeep } });
                }
            svc.Sort((x, y) => ((int)y["upkeep"]).CompareTo((int)x["upkeep"]));
            int money = em.HasComponent<GCity.PlayerMoney>(city) ? em.GetComponentData<GCity.PlayerMoney>(city).money : 0;
            return new Dictionary<string, object>
            {
                { "note", "per month (Budget panel). expenses are positive amounts; balance = income - expenses. moneyDelta = (income - expenses) / 24 (CityServiceBudgetSystem.GetMoneyDelta). serviceUpkeep = estimated upkeep per city service at its current budget % (ServiceUpkeep total is in expenses)." },
                { "money", money },
                { "income", income },
                { "expenses", expenses },
                { "totalIncome", ti },
                { "totalExpenses", te },
                { "balance", b.GetBalance() },
                { "taxIncome", b.GetTotalTaxIncome() },
                { "moneyDelta", b.GetMoneyDelta() },
                { "serviceUpkeep", svc },
                { "loan", LoanState() },
            };
        }

        /// <summary>
        /// GET /loan[?amount=N]: loan state (+ offer for a total of N). POST /loan {amount: newTotal} | {delta: +-N} |
        /// {repay: true} [, preview: true]: same path as the Loan panel (LoanSystem.RequestLoanOffer clamps to
        /// [current - cash, creditworthiness], then ChangeLoan); waits until the loan entity shows the new amount.
        /// </summary>
        object LoanRequest(Pending p)
        {
            var ls = Sys<LoanSystem>();
            var before = ls.CurrentLoan;
            var a = p.Body;
            bool post = p.Method == "POST";
            int? target = null;
            if (post)
            {
                if (a.Has("amount")) target = a.I("amount");
                else if (a.Has("delta")) target = before.m_Amount + a.I("delta");
                else if (a.B("repay")) target = 0;
            }
            else if (p.Q("amount") != null) target = (int)p.QF("amount", 0);
            var r = LoanState();
            if (target == null)
            {
                if (post) return new ApiError("pass amount (new total loan), delta (+/-), or repay:true");
                return r;
            }
            var offer = ls.RequestLoanOffer(target.Value);
            r["requested"] = target.Value;
            r["offer"] = LoanJson(offer);
            if (offer.m_Amount != target.Value) r["clamped"] = true;
            if (!post || a.B("preview")) { r["preview"] = true; return r; }
            if (offer.m_Amount == before.m_Amount) { r["changed"] = false; return r; }
            ls.ChangeLoan(offer.m_Amount);
            int want = offer.m_Amount;
            return new PollOperation(() => ls.CurrentLoan.m_Amount == want, ok =>
            {
                var s = LoanState();
                s["before"] = LoanJson(before);
                s["requested"] = target.Value;
                s["applied"] = ok;
                if (!ok) s["note"] = "ChangeLoan queued; LoanSystem had not applied it yet when the wait ended";
                return s;
            }, 120);
        }

        // ------------------------------------------------------------------ /fees

        /// <summary>
        /// GET /fees: every service fee (Game.City.PlayerResource) with value, default, max, adjustable, income
        /// estimate, import/export money and the consumption/efficiency/happiness effects the Budget panel shows.
        /// POST /fees {resource, fee} or {fees: {Electricity: 0.3, Water: 0.25}}: as ServiceBudgetUISystem.SetServiceFee
        /// (ServiceFeeSystem.SetFee on the city's ServiceFee buffer); only adjustable fees, clamped to [0, max].
        /// </summary>
        object Fees(Pending p)
        {
            var city = Sys<CitySystem>().City;
            if (!em.HasComponent<GCity.ServiceFee>(city)) return new ApiError("city has no ServiceFee buffer", 409);
            var fs = Sys<ServiceFeeSystem>();
            if (!em.CreateEntityQuery(ComponentType.ReadOnly<ServiceFeeParameterData>()).TryGetSingleton<ServiceFeeParameterData>(out var fp))
                return new ApiError("ServiceFeeParameterData not found", 409);

            var changes = new List<object>();
            if (p.Method == "POST")
            {
                var a = p.Body;
                var set = new List<KeyValuePair<string, object>>();
                if (a.Has("resource")) set.Add(new KeyValuePair<string, object>(a.Str("resource"), a.Raw("fee")));
                if (a.Raw("fees") is Dictionary<string, object> fd) set.AddRange(fd);
                if (set.Count == 0) return new ApiError("pass {resource, fee} or {fees: {Name: value}}");
                var buf = em.GetBuffer<GCity.ServiceFee>(city, false);
                foreach (var kv in set)
                {
                    var c = new Dictionary<string, object> { { "resource", kv.Key } };
                    changes.Add(c);
                    if (!Enum.TryParse<GCity.PlayerResource>(kv.Key ?? "", true, out var res) || res == GCity.PlayerResource.Count || !Enum.IsDefined(typeof(GCity.PlayerResource), res))
                    { c["error"] = "unknown resource; use one of " + string.Join("|", Enumerable.Range(0, (int)GCity.PlayerResource.Count).Select(i => ((GCity.PlayerResource)i).ToString())); continue; }
                    c["resource"] = res.ToString();
                    double want = kv.Value is double dv ? dv : (kv.Value is string sv && double.TryParse(sv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pv) ? pv : double.NaN);
                    if (double.IsNaN(want)) { c["error"] = "fee must be a number"; continue; }
                    if (res == GCity.PlayerResource.Parking) { c["error"] = "the budget UI does not set the parking fee; parking fees are policies (Roadside Parking Fee / Lot Parking Fee)"; continue; }
                    var par = fp.GetFeeParameters(res);
                    if (!par.m_Adjustable) { c["error"] = "fee is not adjustable in the game UI"; continue; }
                    float v = math.clamp((float)want, 0f, par.m_Max);
                    c["before"] = GameFee(res, buf);
                    c["requested"] = want;
                    GameSetFee(res, buf, v);
                    c["fee"] = v;
                    if (Math.Abs(v - want) > 1e-6) c["clamped"] = true;
                }
            }

            em.CreateEntityQuery(ComponentType.ReadOnly<BuildingEfficiencyParameterData>()).TryGetSingleton<BuildingEfficiencyParameterData>(out var effPar);
            bool hasHap = em.CreateEntityQuery(ComponentType.ReadOnly<CitizenHappinessParameterData>()).TryGetSingleton<CitizenHappinessParameterData>(out var hapPar);
            bool hasTrade = em.CreateEntityQuery(ComponentType.ReadOnly<OutsideTradeParameterData>()).TryGetSingleton<OutsideTradeParameterData>(out var tradePar);

            // which city services collect which fee (the Budget panel lists fees under these services)
            var byService = new Dictionary<int, List<object>>();
            var sq = em.CreateEntityQuery(ComponentType.ReadOnly<ServiceData>(), ComponentType.ReadOnly<CollectedCityServiceFeeData>());
            using (var arr = sq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var cb = em.GetBuffer<CollectedCityServiceFeeData>(e, true);
                    for (int i = 0; i < cb.Length; i++)
                    {
                        if (!byService.TryGetValue(cb[i].m_PlayerResource, out var l)) byService[cb[i].m_PlayerResource] = l = new List<object>();
                        var sn = PrefabName(e);
                        if (!l.Contains(sn)) l.Add(sn);
                    }
                }

            var rbuf = em.GetBuffer<GCity.ServiceFee>(city, true);
            var list = new List<object>();
            for (int i = 0; i < (int)GCity.PlayerResource.Count; i++)
            {
                var res = (GCity.PlayerResource)i;
                var par = fp.GetFeeParameters(res);
                float fee = 0;
                bool has = GameTryGetFee(res, rbuf, out fee);
                int3 tot = int3.zero;
                try { tot = fs.GetServiceFees(res); } catch { }
                int incomeInternal = tot.x;
                if (has) try { incomeInternal = fs.GetServiceFeeIncomeEstimate(res, fee); } catch { }
                float rel = par.m_Default > 0 ? fee / par.m_Default : 1f;
                var d = new Dictionary<string, object>
                {
                    { "resource", res.ToString() },
                    { "fee", has ? (object)fee : null },
                    { "default", par.m_Default }, { "min", 0 }, { "max", par.m_Max }, { "adjustable", par.m_Adjustable },
                    { "relativeToDefault", has ? (object)rel : null },
                    { "services", byService.TryGetValue(i, out var sl) ? sl : new List<object>() },
                    { "incomeInternal", incomeInternal }, { "incomeExports", tot.y }, { "expenseImports", -tot.z },
                };
                if (has && par.m_Default > 0)
                {
                    try { d["consumptionMultiplier"] = ServiceFeeSystem.GetConsumptionMultiplier(res, rel, in fp); } catch { }
                    try { d["efficiencyMultiplier"] = ServiceFeeSystem.GetEfficiencyMultiplier(res, rel, in effPar); } catch { }
                    if (hasHap) try { d["happinessEffect"] = ServiceFeeSystem.GetHappinessEffect(res, rel, in hapPar); } catch { }
                }
                if (hasTrade) { try { d["importable"] = tradePar.Importable(res); d["exportable"] = tradePar.Exportable(res); } catch { } }
                list.Add(d);
            }
            var r = new Dictionary<string, object>
            {
                { "note", "fee units as in the Budget panel; income values per month. Only adjustable fees can be set (the game UI shows a slider only for those). consumption/efficiency multipliers and happinessEffect are what the panel shows for the current fee/default ratio." },
                { "fees", list },
            };
            if (changes.Count > 0) r["changes"] = changes;
            return r;
        }

        static float GameFee(GCity.PlayerResource r, DynamicBuffer<GCity.ServiceFee> buf) => ServiceFeeSystem.GetFee(r, buf);
        static bool GameTryGetFee(GCity.PlayerResource r, DynamicBuffer<GCity.ServiceFee> buf, out float fee) { fee = 0; return ServiceFeeSystem.TryGetFee(r, buf, out fee); }
        static void GameSetFee(GCity.PlayerResource r, DynamicBuffer<GCity.ServiceFee> buf, float v) => ServiceFeeSystem.SetFee(r, buf, v);

        // ------------------------------------------------------------------ /taxes2

        static readonly TaxAreaType[] TaxAreas = { TaxAreaType.Residential, TaxAreaType.Commercial, TaxAreaType.Industrial, TaxAreaType.Office };

        /// <summary>Resources the Taxation panel lists for an area (ResourceData + TaxableResourceData, TaxableResource.m_TaxAreas empty = all).</summary>
        List<GE.Resource> TaxableResources(TaxAreaType area)
        {
            var ps = Sys<PrefabSystem>();
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<ResourceData>(), ComponentType.ReadOnly<TaxableResourceData>());
            var l = new List<GE.Resource>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (!ps.TryGetPrefab<ResourcePrefab>(e, out var rp) || rp == null) continue;
                    var tr = rp.GetComponent<TaxableResource>();
                    bool match = tr == null || tr.m_TaxAreas == null || tr.m_TaxAreas.Length == 0 || Array.IndexOf(tr.m_TaxAreas, area) >= 0;
                    if (match) l.Add(GE.EconomyUtils.GetResource(rp.m_Resource));
                }
            l.Sort((x, y) => GE.EconomyUtils.GetResourceIndex(x).CompareTo(GE.EconomyUtils.GetResourceIndex(y)));
            return l;
        }

        static int GetResRate(TaxSystem t, TaxAreaType area, GE.Resource r) =>
            area == TaxAreaType.Commercial ? t.GetCommercialTaxRate(r) : area == TaxAreaType.Industrial ? t.GetIndustrialTaxRate(r) : t.GetOfficeTaxRate(r);

        static void SetResRate(TaxSystem t, TaxAreaType area, GE.Resource r, int v)
        {
            if (area == TaxAreaType.Commercial) t.SetCommercialTaxRate(r, v);
            else if (area == TaxAreaType.Industrial) t.SetIndustrialTaxRate(r, v);
            else t.SetOfficeTaxRate(r, v);
        }

        static int2 AreaLimits(TaxParameterData lim, TaxAreaType a) =>
            a == TaxAreaType.Residential ? lim.m_ResidentialTaxLimits : a == TaxAreaType.Commercial ? lim.m_CommercialTaxLimits : a == TaxAreaType.Industrial ? lim.m_IndustrialTaxLimits : lim.m_OfficeTaxLimits;

        static bool TryNum(object o, out double v)
        {
            v = 0;
            if (o is double d) { v = d; return true; }
            if (o is string s) return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
            return false;
        }

        /// <summary>
        /// GET /taxes2: overall, per-area, per-education-level (residential) and per-resource (commercial,
        /// industrial, office) tax rates with the game's limits and the Taxation panel's income estimates.
        /// POST /taxes2 {overall?, residential?, commercial?, industrial?, office?,
        ///   residentialLevels?: {"0".."4" | "Uneducated"..: rate} or [r0..r4],
        ///   commercialResources?|industrialResources?|officeResources?: {ResourceName: rate}}:
        /// the TaxationUISystem setters (TaxSystem.Set*), clamped to the limits (the game also clamps).
        /// </summary>
        object Taxes2(Pending p)
        {
            var tax = Sys<TaxSystem>();
            var lim = tax.GetTaxParameterData();
            var changes = new List<object>();
            if (p.Method == "POST")
            {
                var a = p.Body;
                tax.Readers.Complete();
                void Log(string what, double req, int before, int after) => changes.Add(new Dictionary<string, object> { { "what", what }, { "requested", req }, { "before", before }, { "after", after } });
                if (a.Has("overall") && TryNum(a.Raw("overall"), out var ov))
                {
                    int b0 = tax.TaxRate;
                    tax.TaxRate = math.clamp((int)Math.Round(ov), lim.m_TotalTaxLimits.x, lim.m_TotalTaxLimits.y);
                    Log("overall", ov, b0, tax.TaxRate);
                }
                foreach (var area in TaxAreas)
                {
                    var key = area.ToString().ToLowerInvariant();
                    if (!a.Has(key) || !TryNum(a.Raw(key), out var av)) continue;
                    var l2 = AreaLimits(lim, area);
                    int b0 = tax.GetTaxRate(area);
                    tax.SetTaxRate(area, math.clamp((int)Math.Round(av), l2.x, l2.y));
                    Log(key, av, b0, tax.GetTaxRate(area));
                }
                // residential per education level
                var levels = new List<KeyValuePair<int, double>>();
                var rl = a.Raw("residentialLevels");
                if (rl is List<object> ll)
                {
                    for (int i = 0; i < ll.Count && i < 5; i++) if (TryNum(ll[i], out var lv)) levels.Add(new KeyValuePair<int, double>(i, lv));
                }
                else if (rl is Dictionary<string, object> ld)
                {
                    foreach (var kv in ld)
                    {
                        int lvl = int.TryParse(kv.Key, out var li) ? li : Array.FindIndex(EduNames, n => string.Equals(n, kv.Key, StringComparison.OrdinalIgnoreCase));
                        if (lvl < 0 || lvl > 4) { changes.Add(new Dictionary<string, object> { { "what", "residentialLevels." + kv.Key }, { "error", "level must be 0-4 or " + string.Join("|", EduNames) } }); continue; }
                        if (TryNum(kv.Value, out var lv)) levels.Add(new KeyValuePair<int, double>(lvl, lv));
                    }
                }
                foreach (var kv in levels)
                {
                    int b0 = tax.GetResidentialTaxRate(kv.Key);
                    tax.SetResidentialTaxRate(kv.Key, math.clamp((int)Math.Round(kv.Value), lim.m_JobLevelTaxLimits.x, lim.m_JobLevelTaxLimits.y));
                    Log("residential." + EduNames[kv.Key], kv.Value, b0, tax.GetResidentialTaxRate(kv.Key));
                }
                // per resource
                foreach (var area in new[] { TaxAreaType.Commercial, TaxAreaType.Industrial, TaxAreaType.Office })
                {
                    var key = area.ToString().ToLowerInvariant() + "Resources";
                    if (!(a.Raw(key) is Dictionary<string, object> rd)) continue;
                    var allowed = TaxableResources(area);
                    foreach (var kv in rd)
                    {
                        if (!Enum.TryParse<GE.Resource>(kv.Key, true, out var r) || !allowed.Contains(r))
                        { changes.Add(new Dictionary<string, object> { { "what", key + "." + kv.Key }, { "error", "not a taxable " + area + " resource; use one of " + string.Join("|", allowed.Select(x => x.ToString())) } }); continue; }
                        if (!TryNum(kv.Value, out var rv)) continue;
                        int b0 = GetResRate(tax, area, r);
                        SetResRate(tax, area, r, math.clamp((int)Math.Round(rv), lim.m_ResourceTaxLimits.x, lim.m_ResourceTaxLimits.y));
                        Log(area.ToString().ToLowerInvariant() + "." + r, rv, b0, GetResRate(tax, area, r));
                    }
                }
                if (changes.Count == 0) return new ApiError("nothing to set; keys: overall, residential, commercial, industrial, office, residentialLevels, commercialResources, industrialResources, officeResources");
            }

            var css = Sys<CityStatisticsSystem>();
            css.CompleteWriters();
            var lookup = css.GetLookup();
            var stats = css.GetBufferLookup<GCity.CityStatistic>(true);
            var areas = new Dictionary<string, object>();
            foreach (var area in TaxAreas)
            {
                int est = 0;
                try { est = tax.GetEstimatedTaxAmount(area, TaxResultType.Any, lookup, stats); } catch { }
                areas[area.ToString()] = new Dictionary<string, object>
                {
                    { "rate", tax.GetTaxRate(area) }, { "subRateRange", I2(tax.GetTaxRateRange(area)) },
                    { "limits", I2(AreaLimits(lim, area)) }, { "estimatedIncome", est },
                };
            }
            var lv2 = new List<object>();
            for (int l = 0; l < 5; l++)
            {
                int est = 0;
                try { est = tax.GetEstimatedResidentialTaxIncome(l, lookup, stats); } catch { }
                lv2.Add(new Dictionary<string, object> { { "level", l }, { "education", EduNames[l] }, { "rate", tax.GetResidentialTaxRate(l) }, { "estimatedIncome", est } });
            }
            var r2 = new Dictionary<string, object>
            {
                { "note", "rates in %; negative = subsidy. Limits are absolute rates (TaxParameterData); the game re-clamps sub-rates when a parent rate moves. estimatedIncome = Taxation panel estimate per month." },
                { "overall", tax.TaxRate },
                { "limits", new Dictionary<string, object> {
                    { "total", I2(lim.m_TotalTaxLimits) }, { "residential", I2(lim.m_ResidentialTaxLimits) }, { "commercial", I2(lim.m_CommercialTaxLimits) },
                    { "industrial", I2(lim.m_IndustrialTaxLimits) }, { "office", I2(lim.m_OfficeTaxLimits) },
                    { "educationLevel", I2(lim.m_JobLevelTaxLimits) }, { "resource", I2(lim.m_ResourceTaxLimits) } } },
                { "areas", areas },
                { "residentialLevels", lv2 },
            };
            foreach (var area in new[] { TaxAreaType.Commercial, TaxAreaType.Industrial, TaxAreaType.Office })
            {
                var rl2 = new List<object>();
                foreach (var r in TaxableResources(area))
                {
                    int est = 0;
                    try
                    {
                        est = area == TaxAreaType.Commercial ? tax.GetEstimatedCommercialTaxIncome(r, lookup, stats)
                            : area == TaxAreaType.Industrial ? tax.GetEstimatedIndustrialTaxIncome(r, lookup, stats)
                            : tax.GetEstimatedOfficeTaxIncome(r, lookup, stats);
                    }
                    catch { }
                    rl2.Add(new Dictionary<string, object> { { "resource", r.ToString() }, { "rate", GetResRate(tax, area, r) }, { "estimatedIncome", est } });
                }
                r2[area.ToString().ToLowerInvariant() + "Resources"] = rl2;
            }
            if (changes.Count > 0) r2["changes"] = changes;
            return r2;
        }

        // ------------------------------------------------------------------ /jobs

        /// <summary>
        /// GET /jobs[?scan=0]: workplaces vs workers per education/job level (CountWorkplacesSystem), citizens and
        /// job seekers per education (CountHouseholdDataSystem), plus an exact citizen scan (unless scan=0) for
        /// unemployed / under-employed / working outside per education level.
        /// </summary>
        object Jobs(Pending p)
        {
            var cw = Sys<CountWorkplacesSystem>();
            var ch = Sys<CountHouseholdDataSystem>();
            var total = cw.GetTotalWorkplaces();
            var free = cw.GetFreeWorkplaces();
            var openTo = cw.GetUnemployedWorkspaceByLevel();
            var hd = ch.GetHouseholdCountData();
            int[] eduCount = { hd.m_UneducatedCount, hd.m_PoorlyEducatedCount, hd.m_EducatedCount, hd.m_WellEducatedCount, hd.m_HighlyEducatedCount };
            int[] seekers = { hd.m_EmployableByEducation0, hd.m_EmployableByEducation1, hd.m_EmployableByEducation2, hd.m_EmployableByEducation3, hd.m_EmployableByEducation4 };

            bool scan = p.Q("scan", "1") != "0";
            var workable = new int[5]; var employed = new int[5]; var outside = new int[5]; var unemployed = new int[5]; var under = new int[5];
            var matrix = new int[5, 5];
            if (scan)
            {
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<GCit.Citizen>(), ComponentType.ReadOnly<GCit.HouseholdMember>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
                em.CompleteDependencyBeforeRO<GCit.Citizen>();
                em.CompleteDependencyBeforeRO<GCit.Worker>();
                em.CompleteDependencyBeforeRO<GCit.HealthProblem>();
                var cH = em.GetComponentTypeHandle<GCit.Citizen>(true);
                var wH = em.GetComponentTypeHandle<GCit.Worker>(true);
                var hH = em.GetComponentTypeHandle<GCit.HealthProblem>(true);
                using (var chunks = q.ToArchetypeChunkArray(Allocator.Temp))
                    foreach (var chunk in chunks)
                    {
                        var cs = chunk.GetNativeArray(ref cH);
                        bool hasW = chunk.Has(ref wH), hasH = chunk.Has(ref hH), student = chunk.Has<GCit.Student>();
                        var ws = hasW ? chunk.GetNativeArray(ref wH) : default;
                        var hs = hasH ? chunk.GetNativeArray(ref hH) : default;
                        for (int i = 0; i < cs.Length; i++)
                        {
                            var c = cs[i];
                            if ((c.m_State & (GCit.CitizenFlags.Tourist | GCit.CitizenFlags.Commuter)) != 0) continue;
                            if ((c.m_State & GCit.CitizenFlags.ValidCitizen) == 0) continue;
                            if (hasH && GCit.CitizenUtils.IsDead(hs[i])) continue;
                            if (!GCit.CitizenUtils.IsWorkableCitizen(c, c.GetAge(), student, false)) continue;
                            int edu = math.clamp(c.GetEducationLevel(), 0, 4);
                            workable[edu]++;
                            if (!hasW) { unemployed[edu]++; continue; }
                            var w = ws[i];
                            if (em.HasComponent<Game.Objects.OutsideConnection>(w.m_Workplace)) { outside[edu]++; continue; }
                            employed[edu]++;
                            matrix[edu, math.clamp((int)w.m_Level, 0, 4)]++;
                            if (w.m_Level < edu) under[edu]++;
                        }
                    }
            }

            var levels = new List<object>();
            for (int l = 0; l < 5; l++)
            {
                var d = new Dictionary<string, object>
                {
                    { "level", l }, { "education", EduNames[l] },
                    { "workplaces", total[l] }, { "freeWorkplaces", free[l] }, { "filledWorkplaces", total[l] - free[l] },
                    { "freeJobsOpenToThisEducation", openTo[l] },
                    { "citizens", eduCount[l] }, { "jobSeekers", seekers[l] },
                };
                if (scan)
                {
                    d["workable"] = workable[l]; d["employedInCity"] = employed[l]; d["employedOutside"] = outside[l];
                    d["unemployed"] = unemployed[l]; d["underEmployed"] = under[l];
                    var row = new Dictionary<string, object>();
                    for (int j = 0; j < 5; j++) if (matrix[l, j] > 0) row[EduNames[j]] = matrix[l, j];
                    d["workersByJobLevel"] = row;
                }
                levels.Add(d);
            }
            var r = new Dictionary<string, object>
            {
                { "note", "workplaces/freeWorkplaces are per JOB level (filled includes commuters from outside). citizens/jobSeekers/unemployed are per EDUCATION level. jobSeekers = game's 'employable': workable citizens without a city job or in a job below their education. freeJobsOpenToThisEducation = free jobs at this level or below (a citizen can take any job at or below their education). Scan counts exclude tourists, commuters, dead and students." },
                { "levels", levels },
                { "totals", new Dictionary<string, object> {
                    { "workplaces", total.TotalCount }, { "freeWorkplaces", free.TotalCount },
                    { "simpleWorkplaces", total.SimpleWorkplacesCount }, { "complexWorkplaces", total.ComplexWorkplacesCount },
                    { "freeSimple", free.SimpleWorkplacesCount }, { "freeComplex", free.ComplexWorkplacesCount },
                    { "workableCitizens", hd.m_WorkableCitizenCount }, { "cityWorkers", hd.m_CityWorkerCount },
                    { "unemployed", hd.Unemployed() }, { "unemploymentRatePct", ch.UnemploymentRate },
                    { "students", hd.m_StudentCount }, { "commuterHouseholds", hd.m_CommuterHouseholdCount },
                    { "homelessHouseholds", hd.m_HomelessHouseholdCount }, { "homelessCitizens", hd.m_HomelessCitizenCount },
                    { "children", hd.m_ChildrenCount }, { "teens", hd.m_TeenCount }, { "adults", hd.m_AdultCount }, { "seniors", hd.m_SeniorCount },
                    { "movingIn", hd.m_MovingInCitizenCount }, { "movingAwayHouseholds", hd.m_MovingAwayHouseholdCount },
                } },
            };
            if (scan)
                r["scanTotals"] = new Dictionary<string, object> { { "workable", workable.Sum() }, { "employedInCity", employed.Sum() }, { "employedOutside", outside.Sum() }, { "unemployed", unemployed.Sum() }, { "underEmployed", under.Sum() } };
            return r;
        }
    }

    /// <summary>Waits (one check per frame) until a condition holds or a frame cap is hit, then builds the result.</summary>
    public class PollOperation : IOperation
    {
        readonly Func<bool> done;
        readonly Func<bool, object> result;
        readonly int maxFrames;
        int frames;

        public PollOperation(Func<bool> done, Func<bool, object> result, int maxFrames = 120)
        {
            this.done = done; this.result = result; this.maxFrames = maxFrames;
        }

        public bool Step(out object r)
        {
            r = null;
            frames++;
            bool ok = false;
            try { ok = frames > 1 && done(); } catch { }
            if (!ok && frames < maxFrames) return false;
            try { r = result(ok); }
            catch (Exception e) { r = new ApiError("result: " + e.Message, 500); }
            return true;
        }
    }
}
