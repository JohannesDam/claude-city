using System;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using GB = Game.Buildings;
using GC = Game.Companies;
using GE = Game.Economy;
using GO = Game.Objects;
using GP = Game.Prefabs;
using GV = Game.Vehicles;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        /// <summary>Where a trade partner sits: outside connection (by mode), storage (warehouse / cargo station) or a local company.</summary>
        Dictionary<string, object> PartnerRef(Entity partner, float3 from)
        {
            if (partner == Entity.Null || !em.Exists(partner)) return null;
            var d = new Dictionary<string, object> { { "id", Id(partner) } };
            Entity place = partner;
            if (em.HasComponent<GB.PropertyRenter>(partner)) place = em.GetComponentData<GB.PropertyRenter>(partner).m_Property;
            var top = TopBuilding(place);
            if (top != Entity.Null) place = top;
            string kind;
            if (em.HasComponent<GO.OutsideConnection>(place) || em.HasComponent<GO.OutsideConnection>(partner))
            {
                var pf = PrefabOf(place) ?? "";
                kind = "outside connection (" + (pf.IndexOf("Train", StringComparison.OrdinalIgnoreCase) >= 0 ? "train" : pf.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0 ? "ship"
                    : pf.IndexOf("Airplane", StringComparison.OrdinalIgnoreCase) >= 0 ? "air" : "road") + ")";
            }
            else if (em.HasComponent<GB.CargoTransportStation>(place)) kind = "cargo station";
            else if (em.HasComponent<GC.StorageCompany>(partner)) kind = "warehouse";
            else if (em.HasComponent<GC.IndustrialCompany>(partner)) kind = "local industry";
            else if (em.HasComponent<GC.CommercialCompany>(partner)) kind = "local shop";
            else kind = "other";
            d["kind"] = kind;
            d["prefab"] = PrefabOf(place);
            if (place != partner) d["building"] = Id(place);
            if (TryPos(place, out var p)) { d["pos"] = F2(p); d["distance"] = Math.Round(math.distance(p.xz, from.xz), 0); }
            return d;
        }

        /// <summary>
        /// GET /companies?kind=industrial|commercial|all&amp;x&amp;z&amp;radius&amp;limit=50&amp;problems=1&amp;resource=Wood
        /// Companies with their process (output, inputs), stock per resource, the No Inputs counter and icons, what
        /// they are buying now, their last input purchase (partner kind: outside connection by mode, cargo station,
        /// warehouse, local industry; distance), trades running with outside connections (CurrentTrading: resource,
        /// amount, mode) and delivery trucks heading to them. summary: companies short of inputs by resource and where
        /// inputs come from. problems=1 lists only companies with No Inputs; resource= filters by input or output.
        /// </summary>
        object Companies(Pending p)
        {
            var kind = (p.Q("kind", "industrial") ?? "industrial").ToLowerInvariant();
            float x = p.QF("x", 0), z = p.QF("z", 0), radius = p.QF("radius", 1e6f);
            int limit = (int)math.clamp(p.QF("limit", 50), 1, 1000);
            bool problemsOnly = p.Q("problems", "0") == "1" || p.Q("problems", "") == "true";
            var resFilter = p.Q("resource");
            ComponentType kindType;
            switch (kind)
            {
                case "industrial": kindType = ComponentType.ReadOnly<GC.IndustrialCompany>(); break;
                case "commercial": kindType = ComponentType.ReadOnly<GC.CommercialCompany>(); break;
                case "all": kindType = ComponentType.ReadOnly<GC.CompanyData>(); break;
                default: return new ApiError("kind must be industrial|commercial|all");
            }
            var icons = IconsByOwner();

            // delivery trucks heading to each building, by resource
            var trucksTo = new Dictionary<Entity, Dictionary<string, int>>();
            var tq = em.CreateEntityQuery(ComponentType.ReadOnly<GV.DeliveryTruck>(), ComponentType.ReadOnly<Target>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = tq.ToEntityArray(Allocator.Temp))
                foreach (var v in arr)
                {
                    var t = em.GetComponentData<Target>(v).m_Target;
                    if (t == Entity.Null) continue;
                    var dt = em.GetComponentData<GV.DeliveryTruck>(v);
                    if ((dt.m_State & GV.DeliveryTruckFlags.Returning) != 0) continue;
                    if (!trucksTo.TryGetValue(t, out var c)) trucksTo[t] = c = new Dictionary<string, int>();
                    Count(c, ResName(dt.m_Resource), dt.m_Amount);
                }

            var q = em.CreateEntityQuery(kindType, ComponentType.ReadOnly<GC.CompanyData>(), ComponentType.ReadOnly<GB.PropertyRenter>(), ComponentType.ReadOnly<GP.PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var rows = new List<KeyValuePair<float, Dictionary<string, object>>>();
            int total = 0, noInputs = 0;
            var shortBy = new Dictionary<string, int>();            // input resource of companies with No Inputs
            var partnerKinds = new Dictionary<string, int>();       // "resource <- kind" of the last purchase
            var tradingByMode = new Dictionary<string, int>();      // CurrentTrading amount by outside connection mode
            var center = new float2(x, z);
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var c in arr)
                {
                    var b = em.GetComponentData<GB.PropertyRenter>(c).m_Property;
                    if (b == Entity.Null || !em.Exists(b) || !em.HasComponent<ObjTransform>(b)) continue;
                    var bpos = em.GetComponentData<ObjTransform>(b).m_Position;
                    float dist = math.distance(bpos.xz, center);
                    if (dist > radius) continue;
                    var cpf = em.GetComponentData<GP.PrefabRef>(c).m_Prefab;
                    var ipd = em.HasComponent<GP.IndustrialProcessData>(cpf) ? em.GetComponentData<GP.IndustrialProcessData>(cpf) : default;
                    var inputs = new List<GE.Resource>();
                    if (ipd.m_Input1.m_Resource != GE.Resource.NoResource) inputs.Add(ipd.m_Input1.m_Resource);
                    if (ipd.m_Input2.m_Resource != GE.Resource.NoResource) inputs.Add(ipd.m_Input2.m_Resource);
                    if (resFilter != null && !inputs.Concat(new[] { ipd.m_Output.m_Resource }).Any(r => string.Equals(ResName(r), resFilter, StringComparison.OrdinalIgnoreCase))) continue;
                    var bic = IconList(icons, b);
                    int counter = em.HasComponent<GC.CompanyNotifications>(c) ? em.GetComponentData<GC.CompanyNotifications>(c).m_NoInputCounter : 0;
                    bool noIn = (bic != null && bic.Any(o => o.ToString().IndexOf("No Input", StringComparison.OrdinalIgnoreCase) >= 0));
                    total++;
                    var stock = new Dictionary<GE.Resource, int>();
                    if (em.HasBuffer<GE.Resources>(c))
                    {
                        var rb = em.GetBuffer<GE.Resources>(c, true);
                        for (int i = 0; i < rb.Length; i++) stock[rb[i].m_Resource] = (stock.TryGetValue(rb[i].m_Resource, out var s0) ? s0 : 0) + rb[i].m_Amount;
                    }
                    if (noIn)
                    {
                        noInputs++;
                        foreach (var r in inputs) if (!stock.TryGetValue(r, out var have) || have <= 0) Count(shortBy, ResName(r));
                    }
                    Dictionary<string, object> last = null;
                    if (em.HasComponent<GC.BuyingCompany>(c))
                    {
                        var bc = em.GetComponentData<GC.BuyingCompany>(c);
                        last = PartnerRef(bc.m_LastTradePartner, bpos);
                        if (last != null && inputs.Count > 0) Count(partnerKinds, (string)last["kind"]);
                    }
                    var trading = new List<object>();
                    if (em.HasBuffer<GC.CurrentTrading>(c))
                    {
                        var ct = em.GetBuffer<GC.CurrentTrading>(c, true);
                        for (int i = 0; i < ct.Length; i++)
                        {
                            Count(tradingByMode, FlagNames(ct[i].m_OutsideConnectionType), ct[i].m_TradingResourceAmount);
                            if (trading.Count < 6) trading.Add(new Dictionary<string, object> { { "resource", ResName(ct[i].m_TradingResource) }, { "amount", ct[i].m_TradingResourceAmount }, { "via", FlagNames(ct[i].m_OutsideConnectionType) } });
                        }
                    }
                    if (problemsOnly && !noIn) continue;

                    var d = new Dictionary<string, object>
                    {
                        { "id", Id(c) }, { "company", PrefabName(cpf) }, { "building", Id(b) }, { "buildingPrefab", PrefabOf(b) }, { "pos", F2(bpos) },
                    };
                    if (radius < 1e5f) d["distance"] = Math.Round(dist, 0);
                    if (em.HasComponent<GC.StorageCompany>(c)) d["storage"] = true;
                    if (ipd.m_Output.m_Resource != GE.Resource.NoResource)
                        d["output"] = new Dictionary<string, object> { { "resource", ResName(ipd.m_Output.m_Resource) }, { "perUnit", ipd.m_Output.m_Amount }, { "stock", stock.TryGetValue(ipd.m_Output.m_Resource, out var os) ? os : 0 } };
                    var inRows = new List<object>();
                    var tradeCost = em.HasBuffer<GC.TradeCost>(c) ? em.GetBuffer<GC.TradeCost>(c, true).AsNativeArray().ToArray() : new GC.TradeCost[0];
                    foreach (var r in inputs)
                    {
                        var stack = r == ipd.m_Input1.m_Resource ? ipd.m_Input1 : ipd.m_Input2;
                        var ir = new Dictionary<string, object> { { "resource", ResName(r) }, { "perUnit", stack.m_Amount }, { "stock", stock.TryGetValue(r, out var s1) ? s1 : 0 } };
                        foreach (var tc in tradeCost) if (tc.m_Resource == r) { ir["buyCost"] = Math.Round(tc.m_BuyCost, 2); break; }
                        inRows.Add(ir);
                    }
                    if (inRows.Count > 0) d["inputs"] = inRows;
                    var other = stock.Where(kv => kv.Value != 0 && kv.Key != ipd.m_Output.m_Resource && !inputs.Contains(kv.Key)).ToDictionary(kv => ResName(kv.Key), kv => (object)kv.Value);
                    if (other.Count > 0) d["otherStock"] = other;
                    if (bic != null) d["icons"] = bic;
                    if (counter != 0) d["noInputCounter"] = counter;
                    if (em.HasComponent<GC.ResourceBuyer>(c))
                    {
                        var rbuy = em.GetComponentData<GC.ResourceBuyer>(c);
                        d["buyingNow"] = new Dictionary<string, object> { { "resource", ResName(rbuy.m_ResourceNeeded) }, { "amount", rbuy.m_AmountNeeded } };
                    }
                    if (last != null) d["lastPurchaseFrom"] = last;
                    if (em.HasComponent<GC.BuyingCompany>(c)) d["meanInputTrip"] = Math.Round(em.GetComponentData<GC.BuyingCompany>(c).m_MeanInputTripLength, 0);
                    if (trading.Count > 0) d["tradingWithOutside"] = trading;
                    if (trucksTo.TryGetValue(b, out var tt)) d["trucksComing"] = Ordered(tt);
                    else if (trucksTo.TryGetValue(c, out var tt2)) d["trucksComing"] = Ordered(tt2);
                    if (em.HasComponent<GC.Profitability>(c)) d["profitability"] = (int)em.GetComponentData<GC.Profitability>(c).m_Profitability;
                    if (em.HasComponent<GC.ResourceExporter>(c))
                    {
                        var ex = em.GetComponentData<GC.ResourceExporter>(c);
                        d["exporting"] = new Dictionary<string, object> { { "resource", ResName(ex.m_Resource) }, { "amount", ex.m_Amount } };
                    }
                    rows.Add(new KeyValuePair<float, Dictionary<string, object>>(radius < 1e5f ? dist : (noIn ? 0 : 1), d));
                }
            rows.Sort((a, b) => a.Key.CompareTo(b.Key));
            return new Dictionary<string, object>
            {
                { "kind", kind }, { "count", total }, { "withNoInputs", noInputs },
                { "noInputsByMissingResource", Ordered(shortBy) },
                { "lastInputPurchaseFrom", Ordered(partnerKinds) },
                { "tradingWithOutsideByMode", Ordered(tradingByMode) },
                { "listed", Math.Min(rows.Count, limit) },
                { "companies", rows.Take(limit).Select(r => (object)r.Value).ToList() },
                { "note", "inputs[].stock = what the company holds now; lastPurchaseFrom = BuyingCompany.m_LastTradePartner (kind: outside connection by mode, cargo station, warehouse, local industry/shop); tradingWithOutside = CurrentTrading (running trades with outside connections, via = Road|Train|Air|Ship); trucksComing = delivery trucks (not returning) targeting the building, amount by resource" },
            };
        }
    }
}
