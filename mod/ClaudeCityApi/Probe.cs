using System;
using System.Collections.Generic;
using System.Linq;
using Game.Buildings;
using Game.Common;
using Unity.Entities;
using Unity.Mathematics;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Watches one candidate build of a ProbeOperation: the game's preview (Api.DemolishWatch: what it would
    /// demolish, conflicts, problems), demolition limits (households / companies / buildings of the zoned
    /// buildings it would remove) and, for buildings, the preview's road connection. A real build that breaks a
    /// limit (or has no road connection with requireRoad) is switched to a dry run before the tool evaluates it,
    /// exactly like allowDemolish=false does for /building, so it is never applied.
    /// </summary>
    internal sealed class ProbeGuard
    {
        public sealed class Limits
        {
            public int MaxHouseholds = 0, MaxCompanies = 0, MaxBuildings = -1; // -1 = no limit
            public Dictionary<string, object> ToJson() => new Dictionary<string, object>
            {
                { "maxHouseholds", MaxHouseholds }, { "maxCompanies", MaxCompanies }, { "maxBuildings", MaxBuildings < 0 ? null : (object)MaxBuildings },
            };
        }

        readonly Api api;
        readonly AgentToolSystem.BuildJob job;
        readonly Api.DemolishWatch watch;
        readonly Limits limits;
        readonly Entity roadPrefab; readonly float3 roadPos; readonly bool requireRoad;
        readonly Dictionary<Entity, int2> impact = new Dictionary<Entity, int2>(); // building -> (households, companies)
        bool seenRoad, roadIsPreview;
        Entity road;
        string limitReason;

        /// <param name="roadPrefab">a building prefab: watch the preview building's road connection (Entity.Null for nets)</param>
        public ProbeGuard(Api api, AgentToolSystem.BuildJob job, Limits limits, Entity roadPrefab, float3 roadPos, bool requireRoad)
        {
            this.api = api; this.job = job; this.limits = limits;
            this.roadPrefab = roadPrefab; this.roadPos = roadPos; this.requireRoad = requireRoad && roadPrefab != Entity.Null;
            // limits are ours: the watch itself never blocks (allowDemolish=true), Block() does
            watch = new Api.DemolishWatch(api, job, true);
            CheckLimits(); // the footprint's growable lots are known before the tool runs
        }

        /// <summary>Households and companies living/working in a building (cached: it may be gone after the apply).</summary>
        int2 ImpactOf(Entity b)
        {
            if (impact.TryGetValue(b, out var v)) return v;
            var em = api.World.EntityManager;
            int hh = 0, co = 0;
            if (em.Exists(b) && em.HasBuffer<Renter>(b))
            {
                var rb = em.GetBuffer<Renter>(b, true);
                for (int i = 0; i < rb.Length; i++)
                {
                    var r = rb[i].m_Renter;
                    if (em.HasComponent<Game.Citizens.Household>(r)) hh++;
                    else if (em.HasComponent<Game.Companies.WorkProvider>(r) || em.HasComponent<Game.Companies.CompanyData>(r)) co++;
                }
            }
            v = new int2(hh, co);
            if (em.Exists(b)) impact[b] = v;
            return v;
        }

        public void Totals(out int buildings, out int households, out int companies, out List<Entity> list)
        {
            list = watch.Current();
            households = 0; companies = 0;
            foreach (var b in list) { var v = ImpactOf(b); households += v.x; companies += v.y; }
            buildings = list.Count;
        }

        string OverLimit()
        {
            Totals(out int nb, out int hh, out int co, out _);
            if (limits.MaxBuildings >= 0 && nb > limits.MaxBuildings) return "would demolish " + nb + " building(s) (maxBuildings " + limits.MaxBuildings + ")";
            if (hh > limits.MaxHouseholds) return "would demolish homes of " + hh + " household(s) (maxHouseholds " + limits.MaxHouseholds + ")";
            if (co > limits.MaxCompanies) return "would demolish " + co + " company/companies (maxCompanies " + limits.MaxCompanies + ")";
            return null;
        }

        void CheckLimits()
        {
            var why = OverLimit();
            if (why == null) return;
            limitReason = why;
            watch.Block(why);
        }

        /// <summary>Call every frame while the job runs.</summary>
        public void Observe()
        {
            if (job.Done) return;
            watch.Observe();
            if (!(job.State == 1 || job.State == 5) || job.Frames < 4) return;
            if (roadPrefab != Entity.Null)
            {
                try { if (api.PreviewBuildingRoad(roadPrefab, roadPos, out var r, out var rp)) { seenRoad = true; road = r; roadIsPreview = rp; } }
                catch (Exception e) { Log.Warn("probe road connection: " + e.Message); }
            }
            CheckLimits();
            // State 5: the tool evaluates on its next update; the road connection of the settled preview is final
            if (job.State == 5 && requireRoad && seenRoad && road == Entity.Null && !job.DryRun)
                watch.Block("the preview building has no road connection (requireRoad)");
        }

        /// <summary>Adds wouldDemolish/conflicts/problemDetails (DemolishWatch), demolition totals and the road connection to a finished job's result.</summary>
        public void Merge(Dictionary<string, object> r)
        {
            Totals(out int nb, out int hh, out int co, out var list);
            try { watch.Merge(r); } catch (Exception e) { r["safetyError"] = e.GetType().Name + ": " + e.Message; }
            r["demolition"] = new Dictionary<string, object>
            {
                { "buildings", nb }, { "households", hh }, { "companies", co },
                { "ids", list.Take(12).Select(e => (object)Api.Id(e)).ToList() },
            };
            if (seenRoad && !r.ContainsKey("placedId"))
            {
                r["roadConnected"] = road != Entity.Null;
                r["roadConnectedVia"] = "preview";
                if (road != Entity.Null && !roadIsPreview) r["roadEdge"] = Api.Id(road);
            }
        }

        /// <summary>Passes when the preview validated (0 errors), demolition is within the limits and (requireRoad) it is road-connected.</summary>
        public bool Judge(Dictionary<string, object> r, out string reason)
        {
            reason = null;
            if (r.TryGetValue("error", out var err) && err != null) { reason = "error: " + err; return false; }
            int errors = r.TryGetValue("errors", out var e) && e is int ei ? ei : -1;
            if (errors != 0)
            {
                var probs = r.TryGetValue("problems", out var p) && p is List<object> pl ? string.Join(", ", pl) : null;
                reason = errors < 0 ? "no preview evaluated" : errors + " error(s)" + (probs != null ? ": " + probs : "");
                return false;
            }
            if (!(r.TryGetValue("previewEntities", out var pe) && pe is int pi && pi > 0)) { reason = "nothing was generated"; return false; }
            var over = limitReason ?? OverLimit();
            if (over != null) { reason = over; return false; }
            if (requireRoad && r.TryGetValue("roadConnected", out var rc) && rc is bool rcb && !rcb) { reason = "not road-connected (requireRoad)"; return false; }
            if (r.TryGetValue("blocked", out var bl) && bl != null) { reason = "blocked: " + bl; return false; }
            return true;
        }
    }

    /// <summary>
    /// Previews candidate builds one after another in ONE request (POST /place, POST /road with fit) and builds
    /// the first that passes its ProbeGuard. Not a dry run: every candidate is queued as a real build whose guard
    /// turns it into a dry run before the tool applies it when it would break a limit; one with errors is cleared
    /// by the tool itself. So the passing preview IS the build (no second preview). dryRun: each candidate is a
    /// dry run, stopping at the first pass. all: every candidate is previewed (dry), ranked, and (unless dryRun)
    /// the best is then built (one more preview). New candidates are not started once the HTTP wait (?timeout=,
    /// default 60 s) is nearly used up; a real build still in its preview at the deadline is switched to a dry run.
    /// </summary>
    internal sealed class ProbeOperation : IOperation
    {
        internal sealed class Probe
        {
            public Dictionary<string, object> Info;                    // what the candidate is (reported in candidates[])
            public Func<bool, AgentToolSystem.BuildJob> Make;           // dryRun -> a fresh job
            public Func<AgentToolSystem.BuildJob, ProbeGuard> Guard;    // created before the job is queued
            public Func<Dictionary<string, object>, double> RankOf;     // lower = better among passing candidates (all)
            internal Dictionary<string, object> Result; internal bool Pass, Tried; internal string Reason; internal double Rank;
        }

        readonly AgentToolSystem tool;
        readonly List<Probe> probes;
        readonly bool dryRun, all;
        readonly Dictionary<string, object> extra;
        readonly Action<Dictionary<string, object>, Probe> finishing;
        readonly int started = Environment.TickCount;
        readonly int deadlineMs;
        int index = -1, buildIndex = -1;
        AgentToolSystem.BuildJob job;
        ProbeGuard guard;
        bool jobIsReal;

        public ProbeOperation(AgentToolSystem tool, List<Probe> probes, bool dryRun, bool all, int httpTimeoutMs,
            Dictionary<string, object> extra, Action<Dictionary<string, object>, Probe> finishing = null)
        {
            if (probes == null || probes.Count == 0) throw new ArgumentException("ProbeOperation needs at least one candidate");
            this.tool = tool; this.probes = probes; this.dryRun = dryRun; this.all = all; this.extra = extra; this.finishing = finishing;
            deadlineMs = math.max(8000, httpTimeoutMs - 4000);
            Next();
        }

        int Elapsed => Environment.TickCount - started;

        /// <summary>Starts the next untried candidate; false when none is left (or no time).</summary>
        bool Next()
        {
            if (index + 1 >= probes.Count) return false;
            if (index >= 0 && Elapsed > deadlineMs - 2500) return false;
            index++;
            Start(index, dryRun || all);
            return true;
        }

        void Start(int i, bool dry)
        {
            job = probes[i].Make(dry);
            jobIsReal = !dry;
            guard = probes[i].Guard(job); // before the tool sees the job: a real build over the limits becomes a dry run
            tool.Enqueue(job);
        }

        public bool Step(out object result)
        {
            result = null;
            if (!job.Done)
            {
                guard.Observe();
                bool applying = !job.DryRun && job.State >= 6 && job.State < 20; // applied, settling: wait for its report
                if (Elapsed > deadlineMs && !applying)
                {
                    // the HTTP wait is about to end: never apply a build nobody hears about
                    if (!job.DryRun && job.State <= 5) job.DryRun = true;
                    result = Finish(null, "timed out after " + Elapsed + " ms (pass ?timeout= with more ms, or fewer candidates)", true);
                    return true;
                }
                return false;
            }
            var pr = buildIndex >= 0 ? probes[buildIndex] : probes[index];
            var r = new Dictionary<string, object>(job.Result);
            guard.Merge(r);
            bool pass = guard.Judge(r, out var reason);
            bool applied = r.TryGetValue("applied", out var ap) && ap is bool apb && apb;
            if (jobIsReal && pass && !applied) { pass = false; reason = "validated but not applied"; }
            if (jobIsReal && applied && !pass) reason = (reason ?? "") + " (but the tool applied it)";
            pr.Result = r; pr.Pass = pass || (jobIsReal && applied); pr.Reason = reason; pr.Tried = true;
            pr.Rank = pr.Pass && pr.RankOf != null ? pr.RankOf(r) : 0;

            if (buildIndex >= 0) { result = Finish(pr, pr.Pass ? null : "the best candidate failed when built: " + reason, !pr.Pass); return true; }
            if (pr.Pass && !all) { result = Finish(pr, null); return true; }
            if (Next()) return false;

            // every candidate tried (or out of time)
            var best = probes.Where(x => x.Tried && x.Pass).OrderBy(x => x.Rank).FirstOrDefault();
            if (best == null) { result = Finish(null, "no candidate passed (see candidates[].reason)", true); return true; }
            if (dryRun) { result = Finish(best, null); return true; }
            if (Elapsed > deadlineMs - 2500) { result = Finish(best, "no time left to build the best candidate (only its dry-run result)", true); return true; }
            buildIndex = probes.IndexOf(best);
            Start(buildIndex, false);
            return false;
        }

        static Dictionary<string, object> Summary(Probe p)
        {
            var d = new Dictionary<string, object>(p.Info);
            var r = p.Result;
            d["pass"] = p.Pass;
            if (p.Reason != null) d["reason"] = p.Reason;
            if (r == null) return d;
            foreach (var k in new[] { "applied", "cost", "errors", "warnings", "problems", "roadConnected", "blocked", "demolition", "previewFrames", "seed" })
                if (r.TryGetValue(k, out var v) && v != null) d[k] = v;
            if (r.TryGetValue("problemDetails", out var pd) && pd is List<object> pdl && pdl.Count > 0) d["problemDetails"] = pdl.Take(4).ToList();
            if (r.TryGetValue("conflicts", out var cf) && cf is List<object> cfl && cfl.Count > 0)
            {
                d["conflictCount"] = cfl.Count;
                d["conflicts"] = cfl.Take(3).ToList();
            }
            return d;
        }

        Dictionary<string, object> Finish(Probe chosen, string note, bool isError = false)
        {
            var res = new Dictionary<string, object>();
            foreach (var kv in extra) res[kv.Key] = kv.Value;
            bool applied = chosen?.Result != null && chosen.Result.TryGetValue("applied", out var ap) && ap is bool b && b;
            res["applied"] = applied;
            res["dryRun"] = dryRun;
            res["mode"] = all ? "all" : "first";
            // ranked: passing candidates first (best first), then the failures in the order tried
            var tried = probes.Where(x => x.Tried).ToList();
            var ranked = tried.Where(x => x.Pass).OrderBy(x => x.Rank).Concat(tried.Where(x => !x.Pass)).ToList();
            res["candidates"] = ranked.Select(x => (object)Summary(x)).ToList();
            res["chosen"] = chosen == null ? null : Summary(chosen);
            res["result"] = chosen?.Result;
            res["tried"] = tried.Count;
            res["planned"] = probes.Count;
            res["passed"] = tried.Count(x => x.Pass);
            res["ms"] = Elapsed;
            if (note != null) res[isError ? "error" : "note"] = note;
            if (tried.Count < probes.Count && chosen == null) res["untried"] = probes.Count - tried.Count;
            try { finishing?.Invoke(res, chosen); } catch (Exception e) { res["finishError"] = e.Message; }
            return res;
        }
    }
}
