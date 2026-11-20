using System;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using GB = Game.Buildings;
using GP = Game.Prefabs;
using GPF = Game.Pathfind;

namespace ClaudeCityBridge
{
    /// <summary>
    /// GET /requests?kind=...  : the game's service request entities (what the dispatchers work on).
    /// POST /requests/reset    : make failed requests retry now (mayor only; dryRun by default).
    ///
    /// How the game handles a request (all *DispatchSystem jobs, e.g. PrisonerTransportDispatchJob):
    /// every dispatch tick of the request's update group SimulationUtils.TickServiceRequest returns true when
    /// ServiceRequest.m_Cooldown == 0 or the SkipCooldown flag is set (and decrements the cooldown otherwise);
    /// then a pathfind for a vehicle source is queued (PathInformation added). When the result has no origin
    /// SimulationUtils.ResetFailedRequest sets m_FailCount+1 and m_Cooldown = 2^min(8, failCount) - 1 ticks,
    /// so after 8 failures a request is retried only every 255 ticks (prisoner transport/hearse: 255 x 256
    /// frames = about 6 in-game hours; garbage/patrol/post/maintenance: about 12 h). The game itself sets
    /// SkipCooldown (DispatchVehiclesJob) to make a request retry immediately.
    /// </summary>
    public partial class Api
    {
        static readonly string[] ReqKinds =
        {
            "prisonerTransport", "hearse", "ambulance", "garbage", "garbageTransfer", "policePatrol", "policeEmergency",
            "fire", "evacuation", "post", "mailTransfer", "maintenance", "transportVehicle", "taxi",
        };

        class ReqRow
        {
            public Entity E;
            public string Kind;
            public Entity Target;
            public object Priority;
            public bool HasSR;
            public ServiceRequest SR;
            public bool Reversed, Dispatched, HasPath;
            public Entity Handler;
            public GPF.PathInformation Path;
            public int TickFrames;
        }

        void ScanReq<T>(string kind, List<ReqRow> rows, int tickFrames, Func<T, Entity> target, Func<T, object> prio, Func<T, bool> filter = null)
            where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>(), ComponentType.Exclude<Deleted>());
            using (var ents = q.ToEntityArray(Allocator.Temp))
            using (var data = q.ToComponentDataArray<T>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    if (filter != null && !filter(data[i])) continue;
                    var e = ents[i];
                    var r = new ReqRow { E = e, Kind = kind, Target = target(data[i]), Priority = prio(data[i]), TickFrames = tickFrames };
                    if (em.HasComponent<ServiceRequest>(e))
                    {
                        r.HasSR = true;
                        r.SR = em.GetComponentData<ServiceRequest>(e);
                        r.Reversed = (r.SR.m_Flags & ServiceRequestFlags.Reversed) != 0;
                    }
                    if (em.HasComponent<Dispatched>(e)) { r.Dispatched = true; r.Handler = em.GetComponentData<Dispatched>(e).m_Handler; }
                    if (em.HasComponent<GPF.PathInformation>(e)) { r.HasPath = true; r.Path = em.GetComponentData<GPF.PathInformation>(e); }
                    rows.Add(r);
                }
        }

        /// <summary>Tick period (sim frames) = update groups x 16 frames, from each dispatch system's OnUpdate.</summary>
        List<ReqRow> ScanRequests(string kind)
        {
            var l = new List<ReqRow>();
            switch (kind)
            {
                case "prisonerTransport": ScanReq<PrisonerTransportRequest>(kind, l, 256, r => r.m_Target, r => r.m_Priority); break;
                case "hearse": ScanReq<HealthcareRequest>(kind, l, 256, r => r.m_Citizen, r => null, r => r.m_Type == HealthcareRequestType.Hearse); break;
                case "ambulance": ScanReq<HealthcareRequest>(kind, l, 256, r => r.m_Citizen, r => null, r => r.m_Type == HealthcareRequestType.Ambulance); break;
                case "garbage": ScanReq<GarbageCollectionRequest>(kind, l, 512, r => r.m_Target, r => r.m_Priority); break;
                case "garbageTransfer": ScanReq<GarbageTransferRequest>(kind, l, 128, r => r.m_Facility, r => Math.Round(r.m_Priority, 2)); break;
                case "policePatrol": ScanReq<PolicePatrolRequest>(kind, l, 512, r => r.m_Target, r => Math.Round(r.m_Priority, 2)); break;
                case "policeEmergency": ScanReq<PoliceEmergencyRequest>(kind, l, 64, r => r.m_Site != Entity.Null ? r.m_Site : r.m_Target, r => Math.Round(r.m_Priority, 2)); break;
                case "fire": ScanReq<FireRescueRequest>(kind, l, 64, r => r.m_Target, r => Math.Round(r.m_Priority, 2)); break;
                case "evacuation": ScanReq<EvacuationRequest>(kind, l, 64, r => r.m_Target, r => Math.Round(r.m_Priority, 2)); break;
                case "post": ScanReq<PostVanRequest>(kind, l, 512, r => r.m_Target, r => (int)r.m_Priority); break;
                case "mailTransfer": ScanReq<MailTransferRequest>(kind, l, 128, r => r.m_Facility, r => Math.Round(r.m_Priority, 2)); break;
                case "maintenance": ScanReq<MaintenanceRequest>(kind, l, 512, r => r.m_Target, r => r.m_Priority); break;
                case "transportVehicle": ScanReq<TransportVehicleRequest>(kind, l, 128, r => r.m_Route, r => Math.Round(r.m_Priority, 2)); break;
                case "taxi": ScanReq<TaxiRequest>(kind, l, 256, r => r.m_Seeker, r => r.m_Priority); break;
            }
            return l;
        }

        static string ReqState(ReqRow r)
        {
            if (r.Reversed) return "reversed";
            if (r.Dispatched) return "dispatched";
            if (r.HasPath) return "pathfinding";
            if (r.HasSR && r.SR.m_Cooldown > 0 && (r.SR.m_Flags & ServiceRequestFlags.SkipCooldown) == 0) return "cooldown";
            return "waiting";
        }

        /// <summary>Lower bound of the age implied by the fail count: each failed attempt was followed by its cooldown (2^min(8,k) ticks incl. the attempt).</summary>
        static double MinAgeHours(int failCount, int tickFrames)
        {
            long ticks = 0;
            for (int k = 1; k <= failCount; k++) ticks += 1L << math.min(8, k);
            return ticks * (double)tickFrames / TimeSystem.kTicksPerDay * 24.0;
        }

        Dictionary<string, object> DescribeEntity(Entity t)
        {
            if (t == Entity.Null) return null;
            var d = new Dictionary<string, object> { { "id", Id(t) } };
            if (!em.Exists(t)) { d["gone"] = true; return d; }
            if (em.HasComponent<GP.PrefabRef>(t)) d["prefab"] = PrefabName(em.GetComponentData<GP.PrefabRef>(t).m_Prefab);
            if (TryPos(t, out var pos)) d["pos"] = F2(pos);
            if (em.HasComponent<Owner>(t))
            {
                var o = em.GetComponentData<Owner>(t).m_Owner;
                if (o != Entity.Null && em.Exists(o) && em.HasComponent<GP.PrefabRef>(o)) d["owner"] = Id(o) + " " + PrefabName(em.GetComponentData<GP.PrefabRef>(o).m_Prefab);
            }
            if (em.HasComponent<GB.Building>(t) && em.GetComponentData<GB.Building>(t).m_RoadEdge == Entity.Null) d["noRoadAccess"] = true;
            return d;
        }

        Dictionary<string, object> ReqJson(ReqRow r)
        {
            var st = ReqState(r);
            var d = new Dictionary<string, object> { { "id", Id(r.E) }, { "state", st } };
            if (r.Kind != null) d["kind"] = r.Kind;
            var t = DescribeEntity(r.Target);
            if (t != null) d["target"] = t;
            if (r.Priority != null) d["priority"] = r.Priority;
            if (r.HasSR)
            {
                d["failCount"] = (int)r.SR.m_FailCount;
                d["cooldownTicks"] = (int)r.SR.m_Cooldown;
                if (st == "cooldown") d["nextRetryInGameMinutes"] = Math.Round(r.SR.m_Cooldown * (double)r.TickFrames / TimeSystem.kTicksPerDay * 1440.0, 1);
                if (r.SR.m_FailCount > 0) d["minAgeGameHours"] = Math.Round(MinAgeHours(r.SR.m_FailCount, r.TickFrames), 1);
                if ((r.SR.m_Flags & ServiceRequestFlags.SkipCooldown) != 0) d["skipCooldown"] = true;
            }
            if (r.Dispatched) d["handler"] = DescribeEntity(r.Handler);
            if (r.HasPath)
                d["path"] = new Dictionary<string, object>
                {
                    { "origin", r.Path.m_Origin == Entity.Null ? null : Id(r.Path.m_Origin) },
                    { "destination", r.Path.m_Destination == Entity.Null ? null : Id(r.Path.m_Destination) },
                    { "state", r.Path.m_State.ToString() }, { "distance", Math.Round(r.Path.m_Distance) },
                };
            return d;
        }

        /// <summary>GET /requests?kind=prisonerTransport|hearse|...|all&amp;onlyFailed=1&amp;limit=50&amp;includeReversed=0</summary>
        object Requests(Pending p)
        {
            var kind = p.Q("kind", "all");
            var kinds = kind == "all" ? ReqKinds : kind.Split(',').Select(k => k.Trim()).ToArray();
            foreach (var k in kinds)
                if (Array.IndexOf(ReqKinds, k) < 0) return new ApiError("kind must be one of " + string.Join("|", ReqKinds) + "|all (comma list allowed)");
            bool onlyFailed = p.Q("onlyFailed", "0") == "1" || p.Q("onlyFailed", "") == "true";
            bool includeReversed = p.Q("includeReversed", "0") == "1" || p.Q("includeReversed", "") == "true";
            int limit = (int)math.clamp(p.QF("limit", kind == "all" ? 0f : 50f), 0f, 1000f);
            var res = new Dictionary<string, object>();
            foreach (var k in kinds)
            {
                var rows = ScanRequests(k);
                var states = new Dictionary<string, int>();
                var hist = new Dictionary<string, int> { { "0", 0 }, { "1-3", 0 }, { "4-7", 0 }, { "8-15", 0 }, { "16+", 0 } };
                int failed = 0;
                foreach (var r in rows)
                {
                    var st = ReqState(r);
                    states[st] = states.TryGetValue(st, out var c) ? c + 1 : 1;
                    if (r.Reversed || !r.HasSR) continue;
                    int fc = r.SR.m_FailCount;
                    if (fc > 0) failed++;
                    hist[fc == 0 ? "0" : fc <= 3 ? "1-3" : fc <= 7 ? "4-7" : fc <= 15 ? "8-15" : "16+"]++;
                }
                var block = new Dictionary<string, object>
                {
                    { "total", rows.Count }, { "byState", states }, { "failed", failed }, { "failCountHistogram", hist },
                    { "tickGameMinutes", Math.Round(rows.Count > 0 ? rows[0].TickFrames * 1440.0 / TimeSystem.kTicksPerDay : 0, 2) },
                };
                if (limit > 0 || kind != "all")
                {
                    var sel = rows.Where(r => (includeReversed || !r.Reversed) && (!onlyFailed || (r.HasSR && r.SR.m_FailCount > 0)))
                        .OrderByDescending(r => r.HasSR ? r.SR.m_FailCount : 0).ThenBy(r => ReqState(r)).Take(math.max(limit, 1)).ToList();
                    block["requests"] = sel.Select(r => { var j = ReqJson(r); j.Remove("kind"); return (object)j; }).ToList();
                    // targets with most failing requests (e.g. one unreachable police station)
                    var byTarget = rows.Where(r => !r.Reversed && r.HasSR && r.SR.m_FailCount > 0).GroupBy(r => r.Target)
                        .OrderByDescending(g => g.Count()).Take(10)
                        .Select(g => (object)new Dictionary<string, object> { { "target", DescribeEntity(g.Key) }, { "failedRequests", g.Count() }, { "maxFailCount", g.Max(r => (int)r.SR.m_FailCount) } }).ToList();
                    if (byTarget.Count > 0) block["failedByTarget"] = byTarget;
                }
                res[k] = block;
            }
            return new Dictionary<string, object>
            {
                { "kinds", res },
                { "legend", "state: waiting = will be tried at the next dispatch tick; cooldown = failed before, retried after cooldownTicks dispatch ticks (tickGameMinutes each; cooldown = 2^min(8,failCount)-1); pathfinding = a vehicle search is in flight; dispatched = a vehicle/facility took it (handler); reversed = a facility's own 'looking for work' request. failCount = failed vehicle searches (no reachable facility with a free vehicle). minAgeGameHours = lower bound implied by failCount. Make failed ones retry now: POST /requests/reset {kind, onlyFailed:true, dryRun:true} (mayor only)." },
            };
        }

        /// <summary>
        /// POST /requests/reset {kind, onlyFailed:true, dryRun:true, mode:"retry"|"recreate", ids:[...], target:"id", limit}
        /// retry (default): ServiceRequest.m_FailCount = 0, m_Cooldown = 0, flag SkipCooldown -&gt; the dispatcher
        ///   searches again at the request's next dispatch tick (the game's own SkipCooldown mechanism); nothing is
        ///   created or deleted.
        /// recreate (prisonerTransport only): destroy the request entity; PoliceStationAISystem.RequestPrisonerTransport
        ///   issues a fresh one (fail count 0) at the station's next tick because PoliceStation.m_PrisonerTransportRequest
        ///   no longer has a ServiceRequest (exactly what the game does when a request's target becomes invalid).
        /// Never touches dispatched requests, requests with a pathfind in flight, or reversed (facility) requests.
        /// </summary>
        object RequestsReset(Pending p)
        {
            if (!string.Equals(p.Method, "POST", StringComparison.OrdinalIgnoreCase))
                return new ApiError("use POST with a JSON body {\"kind\":\"prisonerTransport\",\"onlyFailed\":true,\"dryRun\":true}", 405);
            var a = p.Body;
            var kind = a.Str("kind");
            if (kind == null || Array.IndexOf(ReqKinds, kind) < 0) return new ApiError("kind required, one of " + string.Join("|", ReqKinds));
            bool onlyFailed = a.B("onlyFailed", true);
            bool dryRun = a.B("dryRun", true);
            var mode = a.Str("mode", "retry");
            if (mode != "retry" && mode != "recreate") return new ApiError("mode must be retry|recreate");
            if (mode == "recreate" && kind != "prisonerTransport") return new ApiError("mode recreate is only supported for prisonerTransport (owner verified to re-issue the request); use mode retry");
            int limit = math.clamp(a.I("limit", 1000), 1, 5000);
            HashSet<string> ids = null;
            var idl = a.List("ids");
            if (idl != null) ids = new HashSet<string>(idl.Where(x => x != null).Select(x => x is double dd ? ((long)dd).ToString() : x.ToString()));
            var target = a.Str("target");

            var rows = ScanRequests(kind);
            var skipped = new Dictionary<string, int> { { "reversed", 0 }, { "dispatched", 0 }, { "pathfinding", 0 }, { "notFailed", 0 }, { "filtered", 0 }, { "noServiceRequest", 0 } };
            var matched = new List<ReqRow>();
            foreach (var r in rows)
            {
                if (ids != null && !ids.Contains(Id(r.E)) && !ids.Contains(r.E.Index.ToString())) { skipped["filtered"]++; continue; }
                if (target != null && Id(r.Target) != target && r.Target.Index.ToString() != target) { skipped["filtered"]++; continue; }
                if (!r.HasSR) { skipped["noServiceRequest"]++; continue; }
                if (r.Reversed) { skipped["reversed"]++; continue; }
                if (r.Dispatched) { skipped["dispatched"]++; continue; }
                if (r.HasPath) { skipped["pathfinding"]++; continue; }
                if (onlyFailed && r.SR.m_FailCount == 0) { skipped["notFailed"]++; continue; }
                matched.Add(r);
                if (matched.Count >= limit) break;
            }
            var list = matched.Select(r => (object)ReqJson(r)).ToList();
            int changed = 0;
            var errors = new List<object>();
            if (!dryRun)
            {
                foreach (var r in matched)
                {
                    try
                    {
                        if (!em.Exists(r.E) || em.HasComponent<Dispatched>(r.E) || em.HasComponent<GPF.PathInformation>(r.E)) continue;
                        if (mode == "retry")
                        {
                            var sr = em.GetComponentData<ServiceRequest>(r.E);
                            sr.m_FailCount = 0;
                            sr.m_Cooldown = 0;
                            sr.m_Flags |= ServiceRequestFlags.SkipCooldown;
                            em.SetComponentData(r.E, sr);
                        }
                        else em.DestroyEntity(r.E);
                        changed++;
                    }
                    catch (Exception ex) { errors.Add(Id(r.E) + ": " + ex.Message); }
                }
            }
            int tick = rows.Count > 0 ? rows[0].TickFrames : 256;
            var res = new Dictionary<string, object>
            {
                { "kind", kind }, { "mode", mode }, { "dryRun", dryRun }, { "onlyFailed", onlyFailed },
                { "scanned", rows.Count }, { "matched", matched.Count }, { "changed", changed }, { "skipped", skipped },
                { "requests", list.Take(100).ToList() },
                { "expect", mode == "retry"
                    ? $"each matched request searches for a vehicle at its next dispatch tick (within {Math.Round(tick * 1440.0 / TimeSystem.kTicksPerDay, 1)} game minutes); check GET /requests?kind={kind} after ~1 game hour: state dispatched = fixed; failCount 1+ again = still no reachable facility with a free vehicle (road/district problem)"
                    : "the destroyed requests are re-issued by their police stations at the stations' next tick with failCount 0; check GET /requests?kind=prisonerTransport and GET /services?kind=police after ~1 game hour" },
            };
            if (errors.Count > 0) res["errors"] = errors;
            if (dryRun) res["note"] = "dry run: nothing changed. Send dryRun:false to apply (mayor only).";
            return res;
        }
    }
}
