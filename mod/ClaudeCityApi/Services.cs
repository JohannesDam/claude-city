using System;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using GA = Game.Areas;
using GB = Game.Buildings;
using GCit = Game.Citizens;
using GCo = Game.Companies;
using GE = Game.Economy;
using GP = Game.Prefabs;
using GV = Game.Vehicles;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        // ------------------------------------------------------------------ /services

        static readonly string[] ServiceKinds = { "garbage", "deathcare", "healthcare", "police", "fire", "education", "post", "park" };

        /// <summary>One service building with the numbers the game's own info panels show.</summary>
        class SvcInfo
        {
            public string Kind, Type, Prefab;
            public Entity Entity;
            public float3 Pos;
            public float Efficiency = 1f;
            public int VehInUse, VehOwned, VehUsable, VehCapacity;
            public long Stored, StoreCap;
            public float Rate, RateCap;
            public int Users, UserCap;
            public string UsersName;
            public readonly List<string> Status = new List<string>();
            public readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        }

        /// <summary>
        /// GET /services?kind=garbage|deathcare|healthcare|police|fire|education|post|park|all&amp;x&amp;z&amp;radius&amp;brief=1:
        /// per service building efficiency, vehicles in use vs usable vs capacity, storage, processing,
        /// patients/students/prisoners, a status list; totals per kind plus city-wide demand (production,
        /// waiting dispatch requests, buildings over the warning limit) and a short diagnosis.
        /// </summary>
        object Services(Pending p)
        {
            var kind = (p.Q("kind", "all") ?? "all").ToLowerInvariant();
            var kinds = kind == "all" ? ServiceKinds : new[] { kind };
            foreach (var k in kinds)
                if (Array.IndexOf(ServiceKinds, k) < 0) return new ApiError("kind must be one of " + string.Join("|", ServiceKinds) + "|all");
            float x = p.QF("x", 0f), z = p.QF("z", 0f), radius = p.QF("radius", 1e7f);
            bool brief = p.Q("brief", "0") == "1" || p.Q("brief", "") == "true";
            int limit = (int)math.clamp(p.QF("limit", 300f), 1f, 2000f);

            var res = new Dictionary<string, object>();
            foreach (var k in kinds)
            {
                var list = CollectServices(k).Where(s => math.distance(s.Pos.xz, new float2(x, z)) <= radius).ToList();
                var totals = ServiceTotals(list);
                var demand = ServiceDemand(k);
                var block = new Dictionary<string, object>
                {
                    { "totals", totals },
                    { "demand", demand },
                    { "diagnosis", Diagnose(k, list, demand) },
                };
                if (!brief)
                    block["buildings"] = list.OrderByDescending(s => s.Status.Count(st => st != "ok"))
                        .Take(limit).Select(SvcToJson).Cast<object>().ToList();
                res[k] = block;
            }
            return new Dictionary<string, object>
            {
                { "note", "vehicles: inUse = owned vehicles not parked; usable = capacity scaled by efficiency (what the dispatcher may send); capacity = prefab + upgrades. Rates use the units of the in-game info panels." },
                { "kinds", res },
            };
        }

        Dictionary<string, object> SvcToJson(SvcInfo s)
        {
            var d = new Dictionary<string, object>
            {
                { "id", Id(s.Entity) }, { "prefab", s.Prefab }, { "type", s.Type }, { "pos", F2(s.Pos) },
                { "efficiency", Math.Round(s.Efficiency, 2) },
                { "status", s.Status.Count == 0 ? new List<string> { "ok" } : s.Status },
            };
            if (s.VehCapacity > 0 || s.VehOwned > 0)
                d["vehicles"] = new Dictionary<string, object> { { "inUse", s.VehInUse }, { "usable", s.VehUsable }, { "capacity", s.VehCapacity } };
            if (s.StoreCap > 0 || s.Stored > 0)
                d["storage"] = new Dictionary<string, object> { { "stored", s.Stored }, { "capacity", s.StoreCap }, { "pct", s.StoreCap > 0 ? Math.Round(100.0 * s.Stored / s.StoreCap, 1) : 0.0 } };
            if (s.RateCap > 0)
                d["processing"] = new Dictionary<string, object> { { "rate", Math.Round(s.Rate, 1) }, { "capacity", Math.Round(s.RateCap, 1) } };
            if (s.UsersName != null)
                d[s.UsersName] = new Dictionary<string, object> { { "count", s.Users }, { "capacity", s.UserCap } };
            foreach (var kv in s.Data) d[kv.Key] = kv.Value;
            return d;
        }

        Dictionary<string, object> ServiceTotals(List<SvcInfo> list)
        {
            var status = new Dictionary<string, int>();
            foreach (var s in list)
                foreach (var st in s.Status) status[st] = status.TryGetValue(st, out var c) ? c + 1 : 1;
            int okCount = list.Count(s => s.Status.Count == 0);
            if (okCount > 0) status["ok"] = okCount;
            var t = new Dictionary<string, object>
            {
                { "buildings", list.Count },
                { "vehicles", new Dictionary<string, object> { { "inUse", list.Sum(s => s.VehInUse) }, { "usable", list.Sum(s => s.VehUsable) }, { "capacity", list.Sum(s => s.VehCapacity) } } },
                { "statusCounts", status.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "avgEfficiency", list.Count > 0 ? Math.Round(list.Average(s => s.Efficiency), 2) : 0.0 },
            };
            long stored = list.Sum(s => s.Stored), cap = list.Sum(s => s.StoreCap);
            if (cap > 0 || stored > 0) t["storage"] = new Dictionary<string, object> { { "stored", stored }, { "capacity", cap }, { "pct", cap > 0 ? Math.Round(100.0 * stored / cap, 1) : 0.0 } };
            float rate = list.Sum(s => s.Rate), rateCap = list.Sum(s => s.RateCap);
            if (rateCap > 0) t["processing"] = new Dictionary<string, object> { { "rate", Math.Round(rate, 1) }, { "capacity", Math.Round(rateCap, 1) }, { "effectiveCapacity", Math.Round(list.Sum(s => s.RateCap * s.Efficiency), 1) } };
            foreach (var g in list.Where(s => s.UsersName != null).GroupBy(s => s.UsersName))
                t[g.Key] = new Dictionary<string, object> { { "count", g.Sum(s => s.Users) }, { "capacity", g.Sum(s => s.UserCap) } };
            var types = list.GroupBy(s => s.Type ?? "?").ToDictionary(g => g.Key, g => (object)g.Count());
            if (types.Count > 1) t["byType"] = types;
            return t;
        }

        // ---- collection

        List<SvcInfo> CollectServices(string kind)
        {
            var res = new List<SvcInfo>();
            switch (kind)
            {
                case "garbage": foreach (var e in Facilities<GB.GarbageFacility>()) res.Add(GarbageInfo(e)); break;
                case "deathcare": foreach (var e in Facilities<GB.DeathcareFacility>()) res.Add(DeathcareInfo(e)); break;
                case "healthcare": foreach (var e in Facilities<GB.Hospital>()) res.Add(HospitalInfo(e)); break;
                case "police":
                    foreach (var e in Facilities<GB.PoliceStation>()) res.Add(PoliceInfo(e));
                    foreach (var e in Facilities<GB.Prison>()) res.Add(PrisonInfo(e));
                    break;
                case "fire": foreach (var e in Facilities<GB.FireStation>()) res.Add(FireInfo(e)); break;
                case "education": foreach (var e in Facilities<GB.School>()) res.Add(SchoolInfo(e)); break;
                case "post": foreach (var e in Facilities<GB.PostFacility>()) res.Add(PostInfo(e)); break;
                case "park":
                    foreach (var e in Facilities<GB.Park>()) res.Add(ParkInfo(e));
                    foreach (var e in Facilities<GB.MaintenanceDepot>()) { var s = DepotInfo(e); if (s != null) res.Add(s); }
                    break;
            }
            return res;
        }

        List<Entity> Facilities<T>() where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<GP.PrefabRef>(),
                ComponentType.ReadOnly<ObjTransform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var list = new List<Entity>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                    if (!em.HasComponent<GB.ServiceUpgrade>(e)) list.Add(e); // upgrades are folded into their owner's stats
            return list;
        }

        /// <summary>Common part: prefab, position, efficiency (+ factors below 100%), workers, road, districts, coverage.</summary>
        SvcInfo BaseInfo(Entity e, string kind, out Entity prefab, out float vehEff)
        {
            prefab = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
            var s = new SvcInfo { Kind = kind, Entity = e, Prefab = PrefabName(prefab), Pos = em.GetComponentData<ObjTransform>(e).m_Position };
            vehEff = 1f;
            if (em.HasBuffer<GB.Efficiency>(e))
            {
                var eb = em.GetBuffer<GB.Efficiency>(e, true);
                s.Efficiency = GB.BuildingUtils.GetEfficiency(eb);
                vehEff = math.min(math.min(s.Efficiency, 1f), GB.BuildingUtils.GetImmediateEfficiency(eb)); // as VehicleUIUtils.GetAvailableVehicles
                var factors = new Dictionary<string, object>();
                for (int i = 0; i < eb.Length; i++)
                    if (math.abs(eb[i].m_Efficiency - 1f) > 0.005f) factors[eb[i].m_Factor.ToString()] = Math.Round(eb[i].m_Efficiency, 2);
                if (factors.Count > 0) s.Data["efficiencyFactors"] = factors;
            }
            if (em.HasComponent<GCo.WorkProvider>(e))
            {
                int max = em.GetComponentData<GCo.WorkProvider>(e).m_MaxWorkers;
                int n = em.HasBuffer<GCo.Employee>(e) ? em.GetBuffer<GCo.Employee>(e, true).Length : 0;
                s.Data["workers"] = new List<object> { n, max };
                if (max > 0 && n < max / 2) s.Status.Add("understaffed");
            }
            var b = em.GetComponentData<GB.Building>(e);
            if (b.m_RoadEdge == Entity.Null) s.Status.Add("no road access");
            if (em.HasComponent<Destroyed>(e)) s.Status.Add("destroyed");
            if (em.HasBuffer<GA.ServiceDistrict>(e))
            {
                int nd = em.GetBuffer<GA.ServiceDistrict>(e, true).Length;
                if (nd > 0) s.Data["restrictedToDistricts"] = nd;
            }
            if (em.HasComponent<GP.CoverageData>(prefab))
            {
                var cd = em.GetComponentData<GP.CoverageData>(prefab);
                s.Data["coverage"] = new Dictionary<string, object> { { "service", cd.m_Service.ToString() }, { "range", Math.Round(cd.m_Range) }, { "capacity", Math.Round(cd.m_Capacity) }, { "magnitude", Math.Round(cd.m_Magnitude, 2) } };
            }
            if (s.Efficiency <= 0.001f) s.Status.Add("not working (efficiency 0)");
            else if (s.Efficiency < 0.6f) s.Status.Add("low efficiency");
            return s;
        }

        T Combined<T>(Entity e, Entity prefab) where T : unmanaged, IComponentData, GP.ICombineData<T>
        {
            T d;
            GP.UpgradeUtils.TryGetCombinedComponent(em, e, prefab, out d);
            return d;
        }

        /// <summary>Count owned vehicles that have component <paramref name="ct"/>; inUse = not parked.</summary>
        void CountVehicles(SvcInfo s, ComponentType ct, int capacity, float vehEff, string label = null)
        {
            int owned = 0, moving = 0;
            if (em.HasBuffer<GV.OwnedVehicle>(s.Entity))
            {
                var buf = em.GetBuffer<GV.OwnedVehicle>(s.Entity, true);
                for (int i = 0; i < buf.Length; i++)
                {
                    var v = buf[i].m_Vehicle;
                    if (!em.Exists(v) || !em.HasComponent(v, ct)) continue;
                    owned++;
                    if (!em.HasComponent<GV.ParkedCar>(v)) moving++;
                }
            }
            int usable = GB.BuildingUtils.GetVehicleCapacity(vehEff, capacity);
            if (label == null)
            {
                s.VehOwned += owned; s.VehInUse += moving; s.VehCapacity += capacity; s.VehUsable += usable;
            }
            else if (capacity > 0 || owned > 0)
                s.Data[label] = new Dictionary<string, object> { { "inUse", moving }, { "usable", usable }, { "capacity", capacity } };
        }

        int Buf<T>(Entity e) where T : unmanaged, IBufferElementData => em.HasBuffer<T>(e) ? em.GetBuffer<T>(e, true).Length : 0;

        SvcInfo GarbageInfo(Entity e)
        {
            var s = BaseInfo(e, "garbage", out var prefab, out var ve);
            var gf = em.GetComponentData<GB.GarbageFacility>(e);
            var gd = Combined<GP.GarbageFacilityData>(e, prefab);
            s.Type = gd.m_IndustrialWasteOnly ? "hazardous" : gd.m_LongTermStorage ? "landfill"
                : em.HasComponent<GB.ElectricityProducer>(e) ? "incinerator" : em.HasComponent<GB.ResourceProducer>(e) ? "recycling" : "processing";
            // stored garbage: like GarbageSection (resources + storage sub-areas of landfills)
            s.StoreCap = gd.m_GarbageCapacity;
            if (em.HasBuffer<GE.Resources>(e)) s.Stored += GE.EconomyUtils.GetResources(GE.Resource.Garbage, em.GetBuffer<GE.Resources>(e, true));
            if (em.HasBuffer<GA.SubArea>(e))
            {
                var subs = em.GetBuffer<GA.SubArea>(e, true);
                for (int i = 0; i < subs.Length; i++)
                {
                    var a = subs[i].m_Area;
                    if (!em.HasComponent<GA.Storage>(a) || !em.HasComponent<GP.PrefabRef>(a)) continue;
                    var ap = em.GetComponentData<GP.PrefabRef>(a).m_Prefab;
                    if (!em.HasComponent<GP.StorageAreaData>(ap) || !em.HasComponent<GA.Geometry>(a)) continue;
                    s.StoreCap += GA.AreaUtils.CalculateStorageCapacity(em.GetComponentData<GA.Geometry>(a), em.GetComponentData<GP.StorageAreaData>(ap));
                    s.Stored += em.GetComponentData<GA.Storage>(a).m_Amount;
                }
            }
            s.Rate = gf.m_ProcessingRate;
            s.RateCap = gd.m_ProcessingSpeed;
            CountVehicles(s, ComponentType.ReadOnly<GV.GarbageTruck>(), gd.m_VehicleCapacity, ve);
            CountVehicles(s, ComponentType.ReadOnly<GV.DeliveryTruck>(), gd.m_TransportCapacity, ve, "transferTrucks");
            var f = gf.m_Flags;
            if ((f & GB.GarbageFacilityFlags.IsFull) != 0) s.Status.Add("full");
            else if (s.StoreCap > 0 && s.Stored >= 0.9 * s.StoreCap) s.Status.Add("nearly full");
            if (gd.m_VehicleCapacity > 0 && (f & GB.GarbageFacilityFlags.HasAvailableGarbageTrucks) == 0) s.Status.Add("no trucks available");
            s.Data["flags"] = f.ToString();
            return s;
        }

        SvcInfo DeathcareInfo(Entity e)
        {
            var s = BaseInfo(e, "deathcare", out var prefab, out var ve);
            var df = em.GetComponentData<GB.DeathcareFacility>(e);
            var dd = Combined<GP.DeathcareFacilityData>(e, prefab);
            s.Type = dd.m_LongTermStorage ? "cemetery" : "crematorium";
            s.UsersName = "bodies";
            s.Users = df.m_LongTermStoredCount + Buf<GB.Patient>(e); // as DeathcareSection
            s.UserCap = dd.m_StorageCapacity;
            s.RateCap = dd.m_ProcessingRate;
            s.Rate = s.Users > 0 ? dd.m_ProcessingRate * s.Efficiency : 0f;
            CountVehicles(s, ComponentType.ReadOnly<GV.Hearse>(), dd.m_HearseCapacity, ve);
            var f = df.m_Flags;
            if ((f & GB.DeathcareFacilityFlags.IsFull) != 0 || (s.UserCap > 0 && s.Users >= s.UserCap)) s.Status.Add("full");
            else if (s.UserCap > 0 && s.Users >= 0.9 * s.UserCap) s.Status.Add("nearly full");
            if (dd.m_HearseCapacity > 0 && (f & GB.DeathcareFacilityFlags.HasAvailableHearses) == 0) s.Status.Add("no hearses available");
            s.Data["flags"] = f.ToString();
            return s;
        }

        SvcInfo HospitalInfo(Entity e)
        {
            var s = BaseInfo(e, "healthcare", out var prefab, out var ve);
            var h = em.GetComponentData<GB.Hospital>(e);
            var hd = Combined<GP.HospitalData>(e, prefab);
            s.Type = hd.m_PatientCapacity >= 200 ? "hospital" : "clinic";
            s.UsersName = "patients";
            s.Users = Buf<GB.Patient>(e);
            s.UserCap = hd.m_PatientCapacity;
            CountVehicles(s, ComponentType.ReadOnly<GV.Ambulance>(), hd.m_AmbulanceCapacity + hd.m_MedicalHelicopterCapacity, ve);
            s.Data["treatmentBonus"] = (int)h.m_TreatmentBonus;
            var f = h.m_Flags;
            if (hd.m_PatientCapacity > 0 && (f & GB.HospitalFlags.HasRoomForPatients) == 0) s.Status.Add("full");
            if (hd.m_AmbulanceCapacity > 0 && (f & GB.HospitalFlags.HasAvailableAmbulances) == 0) s.Status.Add("no ambulances available");
            s.Data["flags"] = f.ToString();
            return s;
        }

        SvcInfo PoliceInfo(Entity e)
        {
            var s = BaseInfo(e, "police", out var prefab, out var ve);
            var ps = em.GetComponentData<GB.PoliceStation>(e);
            var pd = Combined<GP.PoliceStationData>(e, prefab);
            s.Type = "station";
            s.UsersName = "jailed";
            s.Users = Buf<GB.Occupant>(e);
            s.UserCap = pd.m_JailCapacity;
            CountVehicles(s, ComponentType.ReadOnly<GV.PoliceCar>(), pd.m_PatrolCarCapacity + pd.m_PoliceHelicopterCapacity, ve);
            s.Data["purpose"] = pd.m_PurposeMask.ToString();
            var f = ps.m_Flags;
            if (pd.m_PatrolCarCapacity > 0 && (f & GB.PoliceStationFlags.HasAvailablePatrolCars) == 0) s.Status.Add("no patrol cars available");
            if (s.UserCap > 0 && s.Users >= s.UserCap) s.Status.Add("jail full");
            if ((f & GB.PoliceStationFlags.NeedPrisonerTransport) != 0) s.Status.Add("waiting for prison van");
            s.Data["flags"] = f.ToString();
            return s;
        }

        SvcInfo PrisonInfo(Entity e)
        {
            var s = BaseInfo(e, "police", out var prefab, out var ve);
            var pr = em.GetComponentData<GB.Prison>(e);
            var pd = Combined<GP.PrisonData>(e, prefab);
            s.Type = "prison";
            s.UsersName = "prisoners";
            s.Users = Buf<GB.Occupant>(e);
            s.UserCap = pd.m_PrisonerCapacity;
            CountVehicles(s, ComponentType.ReadOnly<GV.PrisonerTransport>(), pd.m_PrisonVanCapacity, ve);
            var f = pr.m_Flags;
            if ((f & GB.PrisonFlags.HasPrisonerSpace) == 0) s.Status.Add("full");
            if (pd.m_PrisonVanCapacity > 0 && (f & GB.PrisonFlags.HasAvailablePrisonVans) == 0) s.Status.Add("no prison vans available");
            s.Data["flags"] = f.ToString();
            return s;
        }

        SvcInfo FireInfo(Entity e)
        {
            var s = BaseInfo(e, "fire", out var prefab, out var ve);
            var fs = em.GetComponentData<GB.FireStation>(e);
            var fd = Combined<GP.FireStationData>(e, prefab);
            s.Type = "station";
            CountVehicles(s, ComponentType.ReadOnly<GV.FireEngine>(), fd.m_FireEngineCapacity + fd.m_FireHelicopterCapacity, ve);
            var f = fs.m_Flags;
            if (fd.m_FireEngineCapacity > 0 && (f & GB.FireStationFlags.HasAvailableFireEngines) == 0) s.Status.Add("no fire engines available");
            s.Data["flags"] = f.ToString();
            return s;
        }

        SvcInfo SchoolInfo(Entity e)
        {
            var s = BaseInfo(e, "education", out var prefab, out var ve);
            var sd = Combined<GP.SchoolData>(e, prefab);
            s.Type = sd.m_EducationLevel == 1 ? "elementary" : sd.m_EducationLevel == 2 ? "highschool" : sd.m_EducationLevel == 3 ? "college" : sd.m_EducationLevel == 4 ? "university" : "level" + sd.m_EducationLevel;
            s.UsersName = "students";
            s.Users = Buf<GB.Student>(e);
            s.UserCap = sd.m_StudentCapacity;
            if (em.HasComponent<GB.School>(e))
            {
                var sc = em.GetComponentData<GB.School>(e);
                s.Data["graduationTime"] = Math.Round(sc.m_AverageGraduationTime, 2);
                s.Data["failProbability"] = Math.Round(sc.m_AverageFailProbability, 3);
            }
            if (s.UserCap > 0 && s.Users >= s.UserCap) s.Status.Add("full");
            else if (s.UserCap > 0 && s.Users >= 0.9 * s.UserCap) s.Status.Add("nearly full");
            return s;
        }

        SvcInfo PostInfo(Entity e)
        {
            var s = BaseInfo(e, "post", out var prefab, out var ve);
            var pf = em.GetComponentData<GB.PostFacility>(e);
            var pd = Combined<GP.PostFacilityData>(e, prefab);
            s.Type = pd.m_SortingRate > 0 ? "sorting" : "post office";
            int unsorted = 0, local = 0, outgoing = 0;
            if (em.HasBuffer<GE.Resources>(e))
            {
                var rb = em.GetBuffer<GE.Resources>(e, true);
                unsorted = GE.EconomyUtils.GetResources(GE.Resource.UnsortedMail, rb);
                local = GE.EconomyUtils.GetResources(GE.Resource.LocalMail, rb);
                outgoing = GE.EconomyUtils.GetResources(GE.Resource.OutgoingMail, rb);
            }
            if (em.HasComponent<Game.Routes.MailBox>(e)) unsorted += em.GetComponentData<Game.Routes.MailBox>(e).m_MailAmount;
            s.Stored = unsorted + local + outgoing;
            s.StoreCap = pd.m_MailCapacity;
            s.Data["mail"] = new Dictionary<string, object> { { "unsorted", unsorted }, { "local", local }, { "outgoing", outgoing } };
            if (pd.m_SortingRate > 0) { s.RateCap = pd.m_SortingRate; s.Rate = (pd.m_SortingRate * pf.m_ProcessingFactor + 50) / 100; }
            CountVehicles(s, ComponentType.ReadOnly<GV.PostVan>(), pd.m_PostVanCapacity, ve);
            CountVehicles(s, ComponentType.ReadOnly<GV.DeliveryTruck>(), pd.m_PostTruckCapacity, ve, "postTrucks");
            if (s.StoreCap > 0 && s.Stored >= 0.9 * s.StoreCap) s.Status.Add("nearly full");
            if (pd.m_PostVanCapacity > 0 && s.VehInUse >= s.VehUsable) s.Status.Add("no vans available");
            s.Data["flags"] = pf.m_Flags.ToString();
            return s;
        }

        SvcInfo ParkInfo(Entity e)
        {
            var s = BaseInfo(e, "park", out var prefab, out var ve);
            s.Status.Remove("understaffed");
            var pk = em.GetComponentData<GB.Park>(e);
            var pd = Combined<GP.ParkData>(e, prefab);
            s.Type = "park";
            if (pd.m_MaintenancePool > 0)
            {
                float m = (float)pk.m_Maintenance / pd.m_MaintenancePool;
                s.Data["maintenance"] = Math.Round(m, 2);
                if (m < 0.3f) s.Status.Add("needs maintenance");
            }
            return s;
        }

        SvcInfo DepotInfo(Entity e)
        {
            var prefab0 = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
            var md0 = Combined<GP.MaintenanceDepotData>(e, prefab0);
            if ((md0.m_MaintenanceType & MaintenanceType.Park) == 0) return null;
            var s = BaseInfo(e, "park", out var prefab, out var ve);
            s.Type = "maintenance depot";
            CountVehicles(s, ComponentType.ReadOnly<GV.MaintenanceVehicle>(), md0.m_VehicleCapacity, ve);
            if (md0.m_VehicleCapacity > 0 && s.VehInUse >= s.VehUsable) s.Status.Add("no vehicles available");
            s.Data["maintenanceType"] = md0.m_MaintenanceType.ToString();
            return s;
        }

        // ---- city-wide demand

        /// <summary>Count service requests of type T: waiting (no vehicle assigned yet), dispatched, and failing (pathfinding failed at least once).</summary>
        Dictionary<string, object> RequestStats<T>(Func<Entity, bool> filter = null) where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>(), ComponentType.Exclude<Deleted>());
            int waiting = 0, dispatched = 0, failing = 0;
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var r in arr)
                {
                    if (filter != null && !filter(r)) continue;
                    if (em.HasComponent<ServiceRequest>(r))
                    {
                        var sr = em.GetComponentData<ServiceRequest>(r);
                        if ((sr.m_Flags & ServiceRequestFlags.Reversed) != 0) continue; // facility-side "looking for work" request
                        if (sr.m_FailCount > 0) failing++;
                    }
                    if (em.HasComponent<Dispatched>(r)) dispatched++; else waiting++;
                }
            return new Dictionary<string, object> { { "waiting", waiting }, { "dispatched", dispatched }, { "failedPathfind", failing } };
        }

        bool TrySingleton<T>(out T value) where T : unmanaged, IComponentData
        {
            value = default;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>());
            using (var arr = q.ToComponentDataArray<T>(Allocator.Temp))
            {
                if (arr.Length == 0) return false;
                value = arr[0];
                return true;
            }
        }

        Dictionary<string, object> ServiceDemand(string kind)
        {
            var d = new Dictionary<string, object>();
            try
            {
                switch (kind)
                {
                    case "garbage":
                        {
                            d["production"] = Sys<GarbageAccumulationSystem>().garbageAccumulation;
                            d["requests"] = RequestStats<GarbageCollectionRequest>();
                            if (TrySingleton<GP.GarbageParameterData>(out var gp))
                            {
                                var q = em.CreateEntityQuery(ComponentType.ReadOnly<GB.GarbageProducer>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
                                int overWarning = 0, overRequest = 0, resPenalized = 0, resTotal = 0, penaltySum = 0;
                                using (var ents = q.ToEntityArray(Allocator.Temp))
                                using (var arr = q.ToComponentDataArray<GB.GarbageProducer>(Allocator.Temp))
                                    for (int i = 0; i < arr.Length; i++)
                                    {
                                        int g = arr[i].m_Garbage;
                                        if (g > gp.m_WarningGarbageLimit) overWarning++;
                                        if (g > gp.m_RequestGarbageLimit) overRequest++;
                                        if (!em.HasComponent<GB.ResidentialProperty>(ents[i])) continue;
                                        resTotal++;
                                        // CitizenHappinessSystem.GetGarbageBonuses: -min(10, max(0, (garbage - baseline) / step))
                                        int pen = gp.m_HappinessEffectStep > 0 ? math.min(10, math.max(0, (g - gp.m_HappinessEffectBaseline) / gp.m_HappinessEffectStep)) : 0;
                                        if (pen > 0) { resPenalized++; penaltySum += pen; }
                                    }
                                d["producers"] = q.CalculateEntityCount();
                                d["overWarningLimit"] = overWarning;
                                d["wantPickup"] = overRequest;
                                d["residentialWithHappinessPenalty"] = new Dictionary<string, object> { { "buildings", resPenalized }, { "of", resTotal }, { "avgPenalty", resPenalized > 0 ? Math.Round((double)penaltySum / resPenalized, 1) : 0.0 } };
                                d["limits"] = new Dictionary<string, object> { { "request", gp.m_RequestGarbageLimit }, { "warning", gp.m_WarningGarbageLimit }, { "happinessBaseline", gp.m_HappinessEffectBaseline }, { "happinessStep", gp.m_HappinessEffectStep } };
                            }
                            break;
                        }
                    case "deathcare":
                    case "healthcare":
                        {
                            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GCit.HealthProblem>(), ComponentType.Exclude<Deleted>());
                            int dead = 0, deadWaiting = 0, sick = 0, injured = 0, needTransport = 0, noHealthcare = 0;
                            using (var arr = q.ToComponentDataArray<GCit.HealthProblem>(Allocator.Temp))
                                foreach (var hp in arr)
                                {
                                    var f = hp.m_Flags;
                                    if ((f & GCit.HealthProblemFlags.Dead) != 0) { dead++; if ((f & GCit.HealthProblemFlags.RequireTransport) != 0) deadWaiting++; continue; }
                                    if ((f & GCit.HealthProblemFlags.Sick) != 0) sick++;
                                    if ((f & GCit.HealthProblemFlags.Injured) != 0) injured++;
                                    if ((f & GCit.HealthProblemFlags.RequireTransport) != 0) needTransport++;
                                    if ((f & GCit.HealthProblemFlags.NoHealthcare) != 0) noHealthcare++;
                                }
                            if (kind == "deathcare")
                            {
                                d["deadCitizens"] = dead;
                                d["deadAwaitingHearse"] = deadWaiting;
                                d["requests"] = RequestStats<HealthcareRequest>(r => em.GetComponentData<HealthcareRequest>(r).m_Type == HealthcareRequestType.Hearse);
                                try { d["deathRate"] = Sys<CityStatisticsSystem>().GetStatisticValue(Game.City.StatisticType.DeathRate, 0); } catch { }
                            }
                            else
                            {
                                d["sick"] = sick; d["injured"] = injured; d["awaitingAmbulance"] = needTransport; d["noHealthcareFlag"] = noHealthcare;
                                d["requests"] = RequestStats<HealthcareRequest>(r => em.GetComponentData<HealthcareRequest>(r).m_Type == HealthcareRequestType.Ambulance);
                            }
                            break;
                        }
                    case "police":
                        d["patrolRequests"] = RequestStats<PolicePatrolRequest>();
                        d["emergencyRequests"] = RequestStats<PoliceEmergencyRequest>();
                        d["prisonerTransportRequests"] = RequestStats<PrisonerTransportRequest>();
                        break;
                    case "fire":
                        d["requests"] = RequestStats<FireRescueRequest>();
                        d["buildingsOnFire"] = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Events.OnFire>(), ComponentType.Exclude<Deleted>()).CalculateEntityCount();
                        break;
                    case "post":
                        d["vanRequests"] = RequestStats<PostVanRequest>();
                        d["transferRequests"] = RequestStats<MailTransferRequest>();
                        break;
                    case "park":
                        d["maintenanceRequests"] = RequestStats<MaintenanceRequest>();
                        break;
                    case "education":
                        break;
                }
            }
            catch (Exception ex) { d["error"] = ex.GetType().Name + ": " + ex.Message; }
            return d;
        }


        static int DInt(Dictionary<string, object> d, string k, string sub = null)
        {
            if (d == null || !d.TryGetValue(k, out var v)) return 0;
            if (sub != null) return v is Dictionary<string, object> dd && dd.TryGetValue(sub, out var w) && w is int wi ? wi : 0;
            return v is int i ? i : v is long l ? (int)l : 0;
        }

        /// <summary>Plain-language hints: saturated fleets, full storage, processing below production, waiting requests.</summary>
        List<object> Diagnose(string kind, List<SvcInfo> list, Dictionary<string, object> demand)
        {
            var h = new List<object>();
            if (list.Count == 0) { h.Add("no " + kind + " buildings"); return h; }
            int inUse = list.Sum(s => s.VehInUse), usable = list.Sum(s => s.VehUsable), cap = list.Sum(s => s.VehCapacity);
            if (usable > 0 && inUse >= 0.85 * usable)
                h.Add($"fleet saturated: {inUse}/{usable} usable vehicles out (capacity {cap}) - add a facility or vehicle upgrade near the hotspots");
            if (cap > usable + 1)
                h.Add($"{cap - usable} vehicles unusable because of efficiency < 100% (budget, workers, power/water) - fix efficiency first");
            var full = list.Where(s => s.Status.Contains("full")).ToList();
            if (full.Count > 0) h.Add($"{full.Count}/{list.Count} buildings full: " + string.Join(", ", full.Take(6).Select(s => s.Prefab + " " + Id(s.Entity))));
            var noVeh = list.Where(s => s.Status.Any(st => st.StartsWith("no ") && st.EndsWith("available"))).ToList();
            if (noVeh.Count > 0) h.Add($"{noVeh.Count} buildings have no free vehicles");
            var lowEff = list.Where(s => s.Efficiency < 0.6f).ToList();
            if (lowEff.Count > 0) h.Add($"{lowEff.Count} buildings below 60% efficiency (see efficiencyFactors)");
            var noRoad = list.Count(s => s.Status.Contains("no road access"));
            if (noRoad > 0) h.Add($"{noRoad} buildings without road access");
            string reqKey = demand.ContainsKey("requests") ? "requests" : demand.ContainsKey("vanRequests") ? "vanRequests" : demand.ContainsKey("patrolRequests") ? "patrolRequests" : null;
            if (reqKey != null)
            {
                int waiting = DInt(demand, reqKey, "waiting"), failed = DInt(demand, reqKey, "failedPathfind");
                if (waiting > 0) h.Add($"{waiting} {reqKey} waiting for a vehicle ({usable - inUse} usable vehicles idle)");
                if (failed > 0) h.Add($"{failed} {reqKey} failed pathfinding at least once (unreachable / too far / no facility serving that district)");
            }
            if (kind == "garbage")
            {
                var prod = demand.TryGetValue("production", out var pv) ? Convert.ToDouble(pv) : 0;
                double proc = list.Sum(s => s.RateCap * s.Efficiency);
                long stored = list.Sum(s => s.Stored), scap = list.Sum(s => s.StoreCap);
                if (prod > 0 && proc < prod)
                    h.Add($"processing capacity {Math.Round(proc)} below production {Math.Round(prod)}: landfills fill up; add incinerator/recycling");
                if (scap > 0 && stored > 0.8 * scap) h.Add($"landfill storage {Math.Round(100.0 * stored / scap)}% used");
            }
            if (kind == "deathcare")
            {
                long bodies = list.Sum(s => (long)s.Users), bcap = list.Sum(s => (long)s.UserCap);
                if (bcap > 0 && bodies > 0.8 * bcap) h.Add($"body storage {bodies}/{bcap} used - add cemetery/crematorium");
                if (DInt(demand, "deadAwaitingHearse") > 0) h.Add($"{DInt(demand, "deadAwaitingHearse")} dead citizens awaiting a hearse");
            }
            if (kind == "education")
                foreach (var g in list.GroupBy(s => s.Type))
                {
                    int st = g.Sum(s => s.Users), sc = g.Sum(s => s.UserCap);
                    if (sc > 0 && st >= 0.9 * sc) h.Add($"{g.Key}: {st}/{sc} seats used");
                }
            if (h.Count == 0) h.Add("no obvious capacity problem");
            return h;
        }
    }
}
