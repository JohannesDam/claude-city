using System;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using GO = Game.Objects;
using GP = Game.Prefabs;
using GPF = Game.Pathfind;
using GR = Game.Routes;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Changes an existing line the way RouteToolSystem (State.Modify/Remove) does: one CreationDefinition with
    /// m_Original = the route, the route's own prefab and colour, and a WaypointDefinition buffer listing every
    /// waypoint of the new line in order. Kept waypoints carry m_Original = their waypoint entity (position and
    /// connection unchanged, like GetWaypointDefinition(Entity)); a moved stop keeps m_Original and gets the new stop
    /// as m_Connection; an inserted stop has no m_Original; a removed stop is left out. GenerateRoutesSystem makes the
    /// Temp route (Complete, TempFlags.Modify) and ApplyRoutesSystem updates the original route on apply.
    /// </summary>
    public class LineEditJob : AgentToolSystem.BuildJob
    {
        public class Entry
        {
            public Entity Waypoint;          // existing waypoint (kept or moved), Entity.Null for an inserted stop
            public Entity CurrentStop;       // stop the waypoint is connected to now
            public int Index = -1;           // index in the current waypoint list
            public Func<Entity> Stop;        // null = keep the waypoint unchanged; else the (new) stop
            public float3? Proxy;            // dry run: plain waypoint where a not-yet-built stop would be
            public string Label;
        }

        public Api Api;
        public Entity Route;
        public List<Entry> Entries = new List<Entry>();

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            var em = tool.EntityManager;
            if (!Api.IsLive(Route)) { Result["error"] = "line no longer exists"; return deps; }
            var defs = new List<GR.WaypointDefinition>();
            var missing = new List<object>();
            int proxies = 0;
            foreach (var en in Entries)
            {
                if (en.Stop == null)
                {
                    var wp = en.Waypoint;
                    if (!em.Exists(wp) || !em.HasComponent<GR.Position>(wp)) { missing.Add(en.Label); continue; }
                    var def = new GR.WaypointDefinition(em.GetComponentData<GR.Position>(wp).m_Position) { m_Original = wp };
                    if (em.HasComponent<GR.Connected>(wp)) def.m_Connection = em.GetComponentData<GR.Connected>(wp).m_Connected;
                    defs.Add(def);
                    continue;
                }
                var s = en.Stop();
                if (s != Entity.Null && Api.IsLive(s) && em.HasComponent<ObjTransform>(s))
                    defs.Add(new GR.WaypointDefinition(em.GetComponentData<ObjTransform>(s).m_Position) { m_Connection = s, m_Original = en.Waypoint });
                else if (DryRun && en.Proxy.HasValue) { defs.Add(new GR.WaypointDefinition(en.Proxy.Value) { m_Original = en.Waypoint }); proxies++; }
                else missing.Add(en.Label);
            }
            if (missing.Count > 0) { Result["error"] = "stops missing (not created?)"; Result["missing"] = missing; return deps; }
            // the same stop twice in a row is one stop
            for (int i = defs.Count - 1; i > 0; i--)
                if (defs[i].m_Connection != Entity.Null && defs[i].m_Connection == defs[i - 1].m_Connection) defs.RemoveAt(i);
            if (defs.Count > 2 && defs[0].m_Connection != Entity.Null && defs[0].m_Connection == defs[defs.Count - 1].m_Connection) defs.RemoveAt(defs.Count - 1);
            if (defs.Count < 2) { Result["error"] = "a line needs at least 2 different stops"; return deps; }

            var prefab = em.GetComponentData<GP.PrefabRef>(Route).m_Prefab;
            var e = ecb.CreateEntity();
            ecb.AddComponent(e, new CreationDefinition { m_Prefab = prefab, m_Original = Route });
            if (em.HasComponent<GR.Color>(Route)) ecb.AddComponent(e, new ColorDefinition { m_Color = em.GetComponentData<GR.Color>(Route).m_Color });
            ecb.AddComponent(e, default(Updated));
            var buf = ecb.AddBuffer<GR.WaypointDefinition>(e);
            foreach (var d in defs) buf.Add(d);
            Result["waypointsPlanned"] = defs.Count;
            Result["stopsPlanned"] = defs.Select(d => d.m_Connection == Entity.Null ? (object)("waypoint " + Math.Round(d.m_Position.x) + "," + Math.Round(d.m_Position.z)) : Api.Id(d.m_Connection)).ToList();
            if (proxies > 0) Result["proxyWaypoints"] = proxies;
            return deps;
        }

        public override void AfterApply(AgentToolSystem tool)
        {
            var em = tool.EntityManager;
            Result["lineId"] = Api.Id(Route);
            if (!em.Exists(Route) || !em.HasBuffer<GR.RouteWaypoint>(Route)) return;
            var wps = em.GetBuffer<GR.RouteWaypoint>(Route, true);
            var l = new List<object>();
            for (int i = 0; i < wps.Length; i++)
                l.Add(em.HasComponent<GR.Connected>(wps[i].m_Waypoint) ? Api.Id(em.GetComponentData<GR.Connected>(wps[i].m_Waypoint).m_Connected) : "waypoint");
            Result["stopsAfter"] = l;
        }
    }

    /// <summary>
    /// Watches the Temp route a LineJob/LineEditJob previews and reads each segment's path (PathOwner /
    /// PathInformation, m_Distance &lt; 0 = failed, the test RoutePathReadySystem uses for the "Pathfind Failed"
    /// icon). While a segment is still pending it keeps the tool in the preview (resets StableFrames, at most
    /// MaxHoldFrames). A real build whose preview has more failed segments than the live line had is switched to a
    /// dry run before the tool applies it (unless allowBroken).
    /// </summary>
    public class LinePathWatch
    {
        public const int MaxHoldFrames = 240;
        readonly Api api;
        readonly AgentToolSystem.BuildJob job;
        readonly Entity prefab, original;
        readonly bool allowBroken;
        public int BaselineFailed;
        public List<object> Rows;
        public int Failed, Pending, Snapshots;
        public string Blocked;
        int failStreak;

        public LinePathWatch(Api api, AgentToolSystem.BuildJob job, Entity prefab, Entity original, bool allowBroken)
        {
            this.api = api; this.job = job; this.prefab = prefab; this.original = original; this.allowBroken = allowBroken;
        }

        public void Observe()
        {
            if (job.Done || !(job.State == 1 || job.State == 5) || job.Frames < 2) return;
            if (job.Result.ContainsKey("error")) return;
            try
            {
                var route = api.FindTempRoute(prefab, original);
                if (route == Entity.Null)
                {
                    if (job.State == 1 && job.Frames < 40) job.StableFrames = 0; // definitions not turned into a route yet
                    return;
                }
                var rows = new List<object>();
                if (!api.LineSegments(route, rows, out var f, out var p)) return;
                Rows = rows; Failed = f; Pending = p; Snapshots++;
                // a failure must hold for a few frames (a path can be re-requested once the waypoints find their lanes)
                failStreak = p == 0 && f > BaselineFailed ? failStreak + 1 : 0;
                bool hold = p > 0 || (f > BaselineFailed && failStreak < 3);
                if (hold && job.State == 1 && job.Frames < MaxHoldFrames) job.StableFrames = 0; // wait for the pathfinder
                bool settled = p == 0 && (job.State == 5 || (job.StableFrames >= 2 && job.Frames >= AgentToolSystem.MinPreviewFrames - 2));
                if (settled && !allowBroken && !job.DryRun && f > BaselineFailed && (failStreak >= 3 || job.State == 5))
                {
                    job.DryRun = true; // the tool has not evaluated yet: it clears instead of applying
                    Blocked = f + " segment(s) of the previewed line have no path (live line before: " + BaselineFailed + "); nothing was built for the line. Pass allowBroken:true to build it anyway";
                }
            }
            catch (Exception e) { Log.Warn("LinePathWatch: " + e.Message); }
        }

        public Dictionary<string, object> Report()
        {
            var d = new Dictionary<string, object>
            {
                { "checked", Snapshots > 0 }, { "failedSegments", Failed }, { "pendingSegments", Pending },
                { "ok", Snapshots > 0 && Failed == 0 && Pending == 0 },
            };
            if (Rows != null) d["segments"] = Rows;
            if (Snapshots == 0) d["note"] = "no preview route was seen (nothing generated?)";
            else if (Pending > 0) d["note"] = "some segments were still being pathfound when the preview ended (unknown)";
            else if (Failed > 0) d["note"] = "failed = no vehicle path from that stop to the next (the game shows Pathfind Failed). Trams cannot turn back at a dead-end track; give end stops a loop. Trains need a track route in the allowed directions";
            if (BaselineFailed > 0) d["failedBefore"] = BaselineFailed;
            if (Blocked != null) d["blocked"] = Blocked;
            return d;
        }
    }

    /// <summary>Waits a few frames until a deleted line is gone.</summary>
    public class LineDeleteOperation : IOperation
    {
        readonly Api api; readonly Entity route; readonly Dictionary<string, object> info;
        readonly int started = Environment.TickCount;
        public LineDeleteOperation(Api api, Entity route, Dictionary<string, object> info) { this.api = api; this.route = route; this.info = info; }
        public bool Step(out object result)
        {
            result = null;
            bool gone = !api.IsLive(route);
            if (!gone && Environment.TickCount - started < 5000) return false;
            info["deleted"] = gone;
            if (!gone) info["warning"] = "the line still exists after 5 s (game paused in a menu?)";
            result = info;
            return true;
        }
    }

    public partial class Api
    {
        // ------------------------------------------------------------------ segment path state

        /// <summary>Path state of one route segment: ok | pending | failed, with length and duration when known.</summary>
        public Dictionary<string, object> SegmentStatus(Entity seg)
        {
            var d = new Dictionary<string, object>();
            var st = PathState(seg, d);
            d.Remove("pathRemaining");
            if (st == "ok")
            {
                int elems = em.HasBuffer<GPF.PathElement>(seg) ? em.GetBuffer<GPF.PathElement>(seg, true).Length : 0;
                if (elems == 0 && !d.ContainsKey("pathLength")) st = "pending";
            }
            if (d.TryGetValue("pathLength", out var pl)) { d.Remove("pathLength"); d["length"] = pl; }
            if (d.TryGetValue("pathDuration", out var pd)) { d.Remove("pathDuration"); d["duration"] = pd; }
            d["status"] = st;
            return d;
        }

        string WaypointLabel(Entity wp)
        {
            if (em.HasComponent<GR.Connected>(wp))
            {
                var s = em.GetComponentData<GR.Connected>(wp).m_Connected;
                if (s != Entity.Null) return Id(s);
            }
            if (!em.HasComponent<GR.Position>(wp)) return "waypoint";
            var p = em.GetComponentData<GR.Position>(wp).m_Position;
            return "waypoint " + Math.Round(p.x) + "," + Math.Round(p.z);
        }

        /// <summary>Per-segment path state of a route (segment i runs from waypoint i to waypoint i+1). Returns false without segments.</summary>
        public bool LineSegments(Entity route, List<object> rows, out int failed, out int pending)
        {
            failed = 0; pending = 0;
            if (!em.Exists(route) || !em.HasBuffer<GR.RouteSegment>(route) || !em.HasBuffer<GR.RouteWaypoint>(route)) return false;
            var segs = em.GetBuffer<GR.RouteSegment>(route, true).AsNativeArray().ToArray();
            var wps = em.GetBuffer<GR.RouteWaypoint>(route, true).AsNativeArray().ToArray();
            if (segs.Length == 0) return false;
            for (int i = 0; i < segs.Length; i++)
            {
                var seg = segs[i].m_Segment;
                Dictionary<string, object> s;
                if (seg == Entity.Null || !em.Exists(seg)) s = new Dictionary<string, object> { { "status", "pending" } };
                else s = SegmentStatus(seg);
                var st = (string)s["status"];
                if (st == "failed" || st == "stuck") failed++;
                else if (st == "pending") pending++;
                if (rows != null)
                {
                    var r = new Dictionary<string, object> { { "i", i } };
                    foreach (var kv in s) r[kv.Key] = kv.Value;
                    if (wps.Length > 0)
                    {
                        r["from"] = WaypointLabel(wps[i % wps.Length].m_Waypoint);
                        r["to"] = WaypointLabel(wps[(i + 1) % wps.Length].m_Waypoint);
                    }
                    rows.Add(r);
                }
            }
            return true;
        }

        /// <summary>The Temp route the tool previews for a definition (new line: m_Original = Null; edit: the line).</summary>
        public Entity FindTempRoute(Entity prefab, Entity original)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GR.Route>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.Exclude<Deleted>());
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (em.GetComponentData<GP.PrefabRef>(e).m_Prefab != prefab) continue;
                    if (em.GetComponentData<Temp>(e).m_Original != original) continue;
                    return e;
                }
            return Entity.Null;
        }

        // ------------------------------------------------------------------ stops for lines of any type

        public class StopPlan
        {
            public GP.TransportType Type;
            public bool Cargo;
            public Entity StopPrefab;   // placeable road stop prefab (bus, tram, ...); Null = stops belong to stations
            public bool Dry;
            public EntityQuery StopQuery;
            public readonly List<AgentToolSystem.BuildJob> Jobs = new List<AgentToolSystem.BuildJob>();
            public readonly List<KeyValuePair<float3, StopJob>> Planned = new List<KeyValuePair<float3, StopJob>>(); // the same new stop listed twice -> one job
        }

        public class PlannedStop
        {
            public Func<Entity> Resolve;
            public float3? Proxy;
            public Dictionary<string, object> Report;
        }

        /// <summary>Whether a stop entity can serve a line of this type: same TransportType, cargo/passenger stop (outside connections serve both).</summary>
        string CheckStopType(Entity s, GP.TransportType type, bool cargo)
        {
            if (!em.HasComponent<GR.TransportStop>(s) || !em.HasComponent<GP.PrefabRef>(s)) return "not a transport stop";
            var pf = em.GetComponentData<GP.PrefabRef>(s).m_Prefab;
            if (!em.HasComponent<GP.TransportStopData>(pf)) return "stop has no TransportStopData";
            var t = em.GetComponentData<GP.TransportStopData>(pf);
            if (t.m_TransportType != type) return "is a " + t.m_TransportType + " stop, not " + type;
            if (em.HasComponent<GO.OutsideConnection>(s)) return null;
            if (cargo && !t.m_CargoTransport) return "is a passenger stop (a cargo line needs a cargo stop)";
            if (!cargo && !t.m_PassengerTransport) return "is a cargo stop (a passenger line needs a passenger stop)";
            return null;
        }

        /// <summary>A stop id, or a building (station, outside connection) id resolved to its first fitting stop.</summary>
        string StopFor(Entity e, GP.TransportType type, bool cargo, out Entity stop)
        {
            stop = Entity.Null;
            if (em.HasComponent<GR.TransportStop>(e))
            {
                var err = CheckStopType(e, type, cargo);
                if (err != null) return err;
                stop = e;
                return null;
            }
            var top = TopBuilding(e);
            if (top == Entity.Null) top = e;
            var cands = StopsOf(top).Where(s => CheckStopType(s, type, cargo) == null).ToList();
            if (cands.Count == 0) return (PrefabOf(top) ?? Id(top)) + " has no " + type + (cargo ? " cargo" : " passenger") + " stop";
            stop = cands[0];
            return null;
        }

        Entity NearestStopOfType(float3 p, GP.TransportType type, bool cargo, float maxDist, out float dist)
        {
            dist = 0;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GR.TransportStop>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            Entity best = Entity.Null; float bd = maxDist;
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var s in arr)
                {
                    if (CheckStopType(s, type, cargo) != null) continue;
                    var d = math.distance(em.GetComponentData<ObjTransform>(s).m_Position.xz, p.xz);
                    if (d < bd) { bd = d; best = s; }
                }
            dist = bd;
            return best;
        }

        static bool IsIntegratedName(string n) => n != null && n.StartsWith("Integrated", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// One stop of a line request: a stop id, a station / outside connection id ({id} or a bare string), or for
        /// road stops (bus, tram) {edge,t,side} / {x,z,side?} (an existing stop within 12 m on the same side is reused,
        /// else a StopJob places one), for station-only types {x,z[,snap=300]} = the nearest existing stop.
        /// </summary>
        public string PlanStop(object o, int i, StopPlan plan, out PlannedStop ps)
        {
            ps = null;
            var r = new Dictionary<string, object> { { "index", i } };
            var dict = o as Dictionary<string, object>;
            if (dict != null && !dict.ContainsKey("id") && !dict.ContainsKey("stop"))
            {
                if (plan.StopPrefab == Entity.Null)
                {
                    var a = new Args(dict);
                    if (!(a.Has("x") && a.Has("z")))
                        return "stop " + i + ": " + plan.Type + (plan.Cargo ? " cargo" : "") + " stops belong to stations (or outside connections): pass a stop id, a station id or {x,z} near one";
                    float snap = a.F("snap", 300f);
                    var s = NearestStopOfType(new float3(a.F("x"), 0, a.F("z")), plan.Type, plan.Cargo, snap, out var dist);
                    if (s == Entity.Null) return "stop " + i + ": no " + plan.Type + (plan.Cargo ? " cargo" : " passenger") + " stop within " + snap + " m of " + a.F("x") + "," + a.F("z");
                    r["stopId"] = Id(s); r["snappedFrom"] = Math.Round(dist, 0);
                    var top = TopBuilding(s);
                    if (top != Entity.Null && top != s) r["station"] = Id(top) + " " + PrefabOf(top);
                    ps = new PlannedStop { Resolve = () => s, Report = r };
                    return null;
                }
                var err = ParseStopSpec(dict, out var edge, out var t, out var right, out var anchor);
                if (err != null) return "stop " + i + ": " + err;
                if (IsLocked(plan.StopPrefab)) return PrefabName(plan.StopPrefab) + " is still locked";
                var probe = new Dictionary<string, object>();
                if (!SnapStop(edge, t, right, plan.StopPrefab, out var cp, probe)) return "stop " + i + ": " + probe["error"];
                // dry-run stand-in: a plain waypoint between the road centre and the stop, so the preview line connects to
                // the lanes on the stop's side of the road
                float3 proxy = math.lerp(anchor, cp.m_Position, 0.3f);
                var ex = ExistingStopNear(edge, cp.m_Position, right, plan.Type, 12f);
                if (ex != Entity.Null)
                {
                    r["stopId"] = Id(ex); r["reused"] = true;
                    ps = new PlannedStop { Resolve = () => ex, Report = r };
                    return null;
                }
                var same = plan.Planned.FirstOrDefault(kv => kv.Value.Edge == edge && kv.Value.Right == right && math.distance(kv.Key.xz, cp.m_Position.xz) < 12f);
                if (same.Value != null)
                {
                    r["sameAsNewStopOf"] = plan.Jobs.IndexOf(same.Value);
                    var sj = same.Value;
                    ps = new PlannedStop { Resolve = () => sj.Placed, Proxy = proxy, Report = r };
                    return null;
                }
                var job = new StopJob { Api = this, Prefab = plan.StopPrefab, Edge = edge, T = t, Right = right, Anchor = anchor, DryRun = plan.Dry };
                plan.Planned.Add(new KeyValuePair<float3, StopJob>(cp.m_Position, job));
                r["new"] = true; r["pos"] = probe["pos"]; r["edge"] = probe["edge"]; r["side"] = probe["side"];
                r["job"] = plan.Jobs.Count;
                plan.Jobs.Add(job);
                ps = new PlannedStop { Resolve = () => job.Placed, Proxy = proxy, Report = r };
                return null;
            }
            object idv = dict != null ? (dict.ContainsKey("id") ? dict["id"] : dict["stop"]) : o;
            var e = Resolve(idv, plan.StopQuery);
            if (e == Entity.Null || !em.Exists(e)) return "stop " + i + ": not found: " + idv;
            var err2 = StopFor(e, plan.Type, plan.Cargo, out var stop);
            if (err2 != null) return "stop " + i + " (" + idv + "): " + err2;
            r["stopId"] = Id(stop);
            if (stop != e) r["station"] = Id(e) + " " + PrefabOf(e);
            var cap = stop;
            ps = new PlannedStop { Resolve = () => cap, Report = r };
            return null;
        }

        /// <summary>{line: id} or {type, number} (number as shown in GET /lines).</summary>
        Entity ResolveLine(Args a, out string err)
        {
            err = null;
            var rq = em.CreateEntityQuery(ComponentType.ReadOnly<GR.Route>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var raw = a.Raw("line") ?? a.Raw("id");
            if (raw != null)
            {
                var e = Resolve(raw, rq);
                if (e == Entity.Null || !IsLive(e) || !em.HasComponent<GR.Route>(e)) { err = "line not found: " + raw; return Entity.Null; }
                return e;
            }
            if (a.Has("number"))
            {
                var type = ParseTransportType(a.Str("type", "bus"));
                bool cargo = a.B("cargo");
                int num = a.I("number");
                using (var arr = rq.ToEntityArray(Allocator.Temp))
                    foreach (var e in arr)
                    {
                        var pf = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
                        if (!em.HasComponent<GP.TransportLineData>(pf) || !em.HasComponent<GR.RouteNumber>(e)) continue;
                        var tld = em.GetComponentData<GP.TransportLineData>(pf);
                        if (tld.m_TransportType == type && tld.m_CargoTransport == cargo && em.GetComponentData<GR.RouteNumber>(e).m_Number == num) return e;
                    }
                err = "no " + type + (cargo ? " cargo" : "") + " line number " + num;
                return Entity.Null;
            }
            err = "line: pass {line: id} or {type, number}";
            return Entity.Null;
        }

        List<object> LineStopIds(Entity route)
        {
            var l = new List<object>();
            if (!em.HasBuffer<GR.RouteWaypoint>(route)) return l;
            var wps = em.GetBuffer<GR.RouteWaypoint>(route, true);
            for (int i = 0; i < wps.Length; i++) l.Add(WaypointLabel(wps[i].m_Waypoint));
            return l;
        }

        // ------------------------------------------------------------------ endpoints

        /// <summary>
        /// POST /line/delete {line: id | type+number, dryRun}: deletes a transport line the way the line panel's
        /// delete button does (Deleted on the route via the EndFrameBarrier; the game removes waypoints and
        /// segments and sends the vehicles back). Stops stay.
        /// </summary>
        object LineDelete(Args a)
        {
            if (Tool.Busy) return new ApiError("agent tool is busy", 409);
            var route = ResolveLine(a, out var err);
            if (err != null) return new ApiError(err, 404);
            var info = LineRef(route);
            info["stops"] = LineStopIds(route);
            var rows = new List<object>();
            if (LineSegments(route, rows, out var f, out var pnd)) { info["failedSegments"] = f; }
            info["dryRun"] = a.B("dryRun");
            if (a.B("dryRun")) { info["wouldDelete"] = true; return info; }
            var ecb = Sys<Game.EndFrameBarrier>().CreateCommandBuffer();
            ecb.AddComponent(route, default(Deleted));
            return new LineDeleteOperation(this, route, info);
        }

        /// <summary>
        /// POST /line/edit {line: id | type+number, remove:[stop], move:[{from: stop, to: stopSpec}],
        ///   add:[{stop: stopSpec, after: stop | before: stop}], dryRun, allowBroken}
        /// stop (anchor) = a stop id on the line, a station id with a stop on the line, or {index: n} (waypoint index as
        /// in GET /lines). stopSpec = anything /line accepts as a stop (stop id, station id, {edge,t,side}, {x,z}).
        /// Operations apply in the order remove, move, add; anchors are resolved against the current line first.
        /// New road stops are placed first. The whole line is previewed and every segment pathfound (pathCheck); a
        /// real edit that leaves more segments without a path than before is not applied unless allowBroken.
        /// </summary>
        object LineEdit(Args a)
        {
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy", 409);
            var route = ResolveLine(a, out var lerr);
            if (lerr != null) return new ApiError(lerr, 404);
            var pf = em.GetComponentData<GP.PrefabRef>(route).m_Prefab;
            if (!em.HasComponent<GP.TransportLineData>(pf)) return new ApiError("not a transport line");
            var tld = em.GetComponentData<GP.TransportLineData>(pf);
            bool dry = a.B("dryRun");
            var plan = new StopPlan
            {
                Type = tld.m_TransportType, Cargo = tld.m_CargoTransport, Dry = dry,
                StopQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GR.TransportStop>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()),
            };
            if (!plan.Cargo)
                plan.StopPrefab = a.Has("stopPrefab") ? FindPrefab(a.Str("stopPrefab"), e => IsStopPrefab(e, plan.Type))
                    : DefaultPrefab(e => IsStopPrefab(e, plan.Type) && !IsIntegratedName(PrefabName(e)));

            var entries = new List<LineEditJob.Entry>();
            var wps = em.GetBuffer<GR.RouteWaypoint>(route, true).AsNativeArray().ToArray();
            for (int i = 0; i < wps.Length; i++)
            {
                var wp = wps[i].m_Waypoint;
                var cs = em.HasComponent<GR.Connected>(wp) ? em.GetComponentData<GR.Connected>(wp).m_Connected : Entity.Null;
                entries.Add(new LineEditJob.Entry { Waypoint = wp, CurrentStop = cs, Index = i, Label = WaypointLabel(wp) });
            }

            // anchor on the current line: stop id, station id or {index}
            Func<object, string, LineEditJob.Entry> anchor = null;
            string anchorErr = null;
            anchor = (spec, what) =>
            {
                if (spec is Dictionary<string, object> sd && sd.ContainsKey("index"))
                {
                    int idx = new Args(sd).I("index", -1);
                    var en = entries.FirstOrDefault(x => x.Index == idx);
                    if (en == null) anchorErr = what + ": no waypoint index " + idx + " (0.." + (wps.Length - 1) + ")";
                    return en;
                }
                if (spec is Dictionary<string, object> sd2 && (sd2.ContainsKey("stop") || sd2.ContainsKey("id"))) spec = sd2.ContainsKey("stop") ? sd2["stop"] : sd2["id"];
                var e = Resolve(spec, plan.StopQuery);
                if (e == Entity.Null) { anchorErr = what + ": not found: " + spec; return null; }
                var hit = entries.FirstOrDefault(x => x.CurrentStop == e);
                if (hit == null && !em.HasComponent<GR.TransportStop>(e))
                {
                    var top = TopBuilding(e);
                    var ss = StopsOf(top != Entity.Null ? top : e);
                    hit = entries.FirstOrDefault(x => ss.Contains(x.CurrentStop));
                }
                if (hit == null) anchorErr = what + ": " + spec + " is not a stop of this line (stops: " + string.Join(", ", entries.Select(x => x.Label)) + ")";
                return hit;
            };

            var ops = new List<object>();
            var removeList = a.List("remove") ?? new List<object>();
            var moveList = a.List("move") ?? new List<object>();
            var addList = a.List("add") ?? new List<object>();
            if (removeList.Count + moveList.Count + addList.Count == 0) return new ApiError("nothing to do: pass remove, move and/or add");

            var removes = new List<LineEditJob.Entry>();
            foreach (var o in removeList)
            {
                var en = anchor(o, "remove");
                if (en == null) return new ApiError(anchorErr);
                removes.Add(en);
            }
            var moves = new List<KeyValuePair<LineEditJob.Entry, object>>();
            foreach (var o in moveList)
            {
                var md = o as Dictionary<string, object>;
                if (md == null || !(md.ContainsKey("from") || md.ContainsKey("stop")) || !md.ContainsKey("to")) return new ApiError("move: entries are {from: stop, to: stopSpec}");
                var en = anchor(md.ContainsKey("from") ? md["from"] : md["stop"], "move.from");
                if (en == null) return new ApiError(anchorErr);
                if (removes.Contains(en)) return new ApiError("move.from " + en.Label + " is also removed");
                moves.Add(new KeyValuePair<LineEditJob.Entry, object>(en, md["to"]));
            }
            var adds = new List<KeyValuePair<object, KeyValuePair<LineEditJob.Entry, bool>>>(); // spec, (anchor, after?)
            foreach (var o in addList)
            {
                var ad = o as Dictionary<string, object>;
                if (ad == null || !ad.ContainsKey("stop")) return new ApiError("add: entries are {stop: stopSpec, after: stop | before: stop}");
                LineEditJob.Entry at = null; bool after = true;
                if (ad.ContainsKey("after")) { at = anchor(ad["after"], "add.after"); if (at == null) return new ApiError(anchorErr); }
                else if (ad.ContainsKey("before")) { at = anchor(ad["before"], "add.before"); after = false; if (at == null) return new ApiError(anchorErr); }
                if (at != null && removes.Contains(at)) return new ApiError("add anchor " + at.Label + " is removed in the same call");
                adds.Add(new KeyValuePair<object, KeyValuePair<LineEditJob.Entry, bool>>(ad["stop"], new KeyValuePair<LineEditJob.Entry, bool>(at, after)));
            }

            // apply: remove, move, add
            foreach (var en in removes) { entries.Remove(en); ops.Add(new Dictionary<string, object> { { "op", "remove" }, { "stop", en.Label }, { "index", en.Index } }); }
            int k = 0;
            foreach (var mv in moves)
            {
                var err = PlanStop(mv.Value, k++, plan, out var ps);
                if (err != null) return new ApiError("move: " + err);
                mv.Key.Stop = ps.Resolve; mv.Key.Proxy = ps.Proxy;
                var label = mv.Key.Label;
                mv.Key.Label = ps.Report.TryGetValue("stopId", out var sid) ? sid.ToString() : "new stop " + k;
                ops.Add(new Dictionary<string, object> { { "op", "move" }, { "from", label }, { "index", mv.Key.Index }, { "to", ps.Report } });
            }
            foreach (var ad in adds)
            {
                var err = PlanStop(ad.Key, k++, plan, out var ps);
                if (err != null) return new ApiError("add: " + err);
                var en = new LineEditJob.Entry { Waypoint = Entity.Null, Stop = ps.Resolve, Proxy = ps.Proxy, Label = ps.Report.TryGetValue("stopId", out var sid) ? sid.ToString() : "new stop " + k };
                var at = ad.Value.Key;
                int pos = at == null ? entries.Count : entries.IndexOf(at) + (ad.Value.Value ? 1 : 0);
                entries.Insert(pos, en);
                var od = new Dictionary<string, object> { { "op", "add" }, { "stop", ps.Report }, { "position", pos } };
                if (at != null) od[ad.Value.Value ? "after" : "before"] = at.Label;
                ops.Add(od);
            }
            if (entries.Count < 2) return new ApiError("the line would have fewer than 2 stops; delete it instead (POST /line/delete)");

            var job = new LineEditJob { Api = this, Route = route, Entries = entries, DryRun = dry };
            var jobs = new List<AgentToolSystem.BuildJob>(plan.Jobs) { job };
            var watch = new LinePathWatch(this, job, pf, route, a.B("allowBroken"));
            if (LineSegments(route, null, out var before, out _)) watch.BaselineFailed = before;
            var stopsBefore = LineStopIds(route);
            foreach (var j in jobs) tool.Enqueue(j);
            var stopJobs = plan.Jobs.OfType<StopJob>().ToList();
            return new ChainOperation(jobs, () =>
            {
                var res = new Dictionary<string, object>
                {
                    { "line", LineRef(route) }, { "dryRun", dry }, { "operations", ops }, { "stopsBefore", stopsBefore },
                    { "edit", job.Result }, { "pathCheck", watch.Report() },
                };
                if (stopJobs.Count > 0) res["newStops"] = stopJobs.Select(j => (object)j.Result).ToList();
                res["applied"] = job.Result.TryGetValue("applied", out var ap) && ap is bool b && b;
                if (watch.Blocked != null) res["blocked"] = watch.Blocked;
                if (dry && stopJobs.Count > 0) res["note"] = "dry run: new stops are previewed separately; the line preview used plain waypoints next to the planned stop positions";
                return res;
            }) { OnStep = watch.Observe };
        }
    }
}
