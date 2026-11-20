using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using NetEdge = Game.Net.Edge;
using ObjTransform = Game.Objects.Transform;
using Attached = Game.Objects.Attached;
using GeometryFlags = Game.Objects.GeometryFlags;
using RouteStop = Game.Routes.TransportStop;
using RouteBusStop = Game.Routes.BusStop;
using Route = Game.Routes.Route;
using RouteFlags = Game.Routes.RouteFlags;
using TransportLine = Game.Routes.TransportLine;
using RouteNumber = Game.Routes.RouteNumber;
using RouteColor = Game.Routes.Color;
using RoutePosition = Game.Routes.Position;
using RouteWaypoint = Game.Routes.RouteWaypoint;
using RouteSegment = Game.Routes.RouteSegment;
using RouteVehicle = Game.Routes.RouteVehicle;
using RouteInfo = Game.Routes.RouteInfo;
using WaypointConnected = Game.Routes.Connected;
using ConnectedRoute = Game.Routes.ConnectedRoute;
using WaitingPassengers = Game.Routes.WaitingPassengers;
using WaypointDefinition = Game.Routes.WaypointDefinition;
using VehiclePassenger = Game.Vehicles.Passenger;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Places one transport stop (bus stop) on a road edge exactly like ObjectToolSystem does for a
    /// RoadEdge net object: the control point is snapped onto a "Buildable" NetCompositionArea of the
    /// edge (sidewalk), m_OriginalEntity = the edge, and ObjectToolBaseSystem.CreateDefinitions does
    /// the rest (Attach flag + an update definition for the edge so its composition gets the stop).
    /// </summary>
    public class StopJob : AgentToolSystem.BuildJob
    {
        public Api Api;
        public Entity Prefab;
        public Entity Edge;
        public float T;
        public bool Right;
        public float3 Anchor;            // world point at t, to re-find the edge if an earlier job replaced it
        public Entity Placed = Entity.Null;
        public float3 Position;
        HashSet<Entity> before;
        NativeList<ControlPoint> points;
        NativeReference<ObjectToolBaseSystem.AttachmentData> attachment;

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            var edge = Edge;
            float t = T;
            if (!Api.IsLive(edge) || !tool.EntityManager.HasComponent<NetEdge>(edge))
            {
                edge = Api.FindEdgeNear(Anchor, 6f, out t);
                Result["edgeReResolved"] = edge == Entity.Null ? null : Api.Id(edge);
                if (edge == Entity.Null) { Result["error"] = "edge is gone and no road found at the anchor point"; return deps; }
            }
            var info = new Dictionary<string, object>();
            if (!Api.SnapStop(edge, t, Right, Prefab, out var cp, info))
            {
                foreach (var kv in info) Result[kv.Key] = kv.Value;
                return deps;
            }
            foreach (var kv in info) Result[kv.Key] = kv.Value;
            Position = cp.m_Position;
            before = Api.AllStops();
            points = new NativeList<ControlPoint>(1, Allocator.Persistent);
            attachment = new NativeReference<ObjectToolBaseSystem.AttachmentData>(Allocator.Persistent);
            points.Add(cp);
            var h = tool.CreateObjectDefinitions(Prefab, points, attachment, deps);
            h.Complete();
            return h;
        }

        public override void AfterApply(AgentToolSystem tool)
        {
            Placed = Api.FindNewStop(Position, before);
            Result["stopId"] = Placed == Entity.Null ? null : Api.Id(Placed);
            if (Placed != Entity.Null)
            {
                Result["stopPrefab"] = Api.PrefabName(tool.EntityManager.GetComponentData<PrefabRef>(Placed).m_Prefab);
                if (tool.EntityManager.HasComponent<Attached>(Placed)) Result["attachedTo"] = Api.Id(tool.EntityManager.GetComponentData<Attached>(Placed).m_Parent);
            }
        }

        public override void Dispose()
        {
            if (points.IsCreated) points.Dispose();
            if (attachment.IsCreated) attachment.Dispose();
        }
    }

    /// <summary>
    /// A new transport line, written like RouteToolSystem.CreateDefinitionsJob for a freshly drawn,
    /// closed route: CreationDefinition { m_Prefab = route prefab } + ColorDefinition + a
    /// WaypointDefinition buffer with one entry per stop (m_Position = stop position,
    /// m_Connection = stop entity) and the first stop repeated at the end with the identical
    /// position (GenerateRoutesSystem then drops it and sets RouteFlags.Complete).
    /// </summary>
    public class LineJob : AgentToolSystem.BuildJob
    {
        public Api Api;
        public Entity RoutePrefab;
        public UnityEngine.Color32 Color;
        public List<Func<Entity>> Stops = new List<Func<Entity>>();
        /// <summary>Dry runs: per stop, a plain waypoint position used when the stop does not exist yet (new stops are only previewed).</summary>
        public List<float3?> Proxies;
        public Entity Placed = Entity.Null;
        HashSet<Entity> before;

        public override JobHandle Create(AgentToolSystem tool, EntityCommandBuffer ecb, JobHandle deps)
        {
            var em = tool.EntityManager;
            var stops = new List<KeyValuePair<Entity, float3>>(); // stop (Entity.Null = plain proxy waypoint), position
            var missing = new List<object>();
            int proxies = 0;
            for (int i = 0; i < Stops.Count; i++)
            {
                var s = Stops[i]();
                if (s != Entity.Null && Api.IsLive(s) && em.HasComponent<ObjTransform>(s))
                {
                    if (stops.Count > 0 && stops[stops.Count - 1].Key == s) continue;
                    stops.Add(new KeyValuePair<Entity, float3>(s, em.GetComponentData<ObjTransform>(s).m_Position));
                }
                else if (DryRun && Proxies != null && i < Proxies.Count && Proxies[i].HasValue)
                {
                    stops.Add(new KeyValuePair<Entity, float3>(Entity.Null, Proxies[i].Value));
                    proxies++;
                }
                else missing.Add(i);
            }
            if (stops.Count > 1 && stops[0].Key != Entity.Null && stops[0].Key == stops[stops.Count - 1].Key) stops.RemoveAt(stops.Count - 1);
            if (missing.Count > 0) { Result["error"] = "stops missing (not created?)"; Result["missingStopIndexes"] = missing; return deps; }
            if (stops.Count < 2) { Result["error"] = "a line needs at least 2 different stops"; return deps; }
            before = Api.AllRoutes();
            var e = ecb.CreateEntity();
            ecb.AddComponent(e, new CreationDefinition { m_Prefab = RoutePrefab });
            ecb.AddComponent(e, new ColorDefinition { m_Color = Color });
            ecb.AddComponent(e, default(Updated));
            var buf = ecb.AddBuffer<WaypointDefinition>(e);
            foreach (var s in stops)
                buf.Add(new WaypointDefinition(s.Value) { m_Connection = s.Key });
            buf.Add(new WaypointDefinition(stops[0].Value) { m_Connection = stops[0].Key });
            Result["stopsInLine"] = stops.Select(x => x.Key == Entity.Null ? (object)("planned stop " + Math.Round(x.Value.x) + "," + Math.Round(x.Value.z)) : Api.Id(x.Key)).ToList();
            if (proxies > 0) Result["proxyWaypoints"] = proxies;
            return deps;
        }

        public override void AfterApply(AgentToolSystem tool)
        {
            var em = tool.EntityManager;
            foreach (var r in Api.AllRoutes())
            {
                if (before != null && before.Contains(r)) continue;
                if (em.GetComponentData<PrefabRef>(r).m_Prefab != RoutePrefab) continue;
                Placed = r;
                break;
            }
            Result["lineId"] = Placed == Entity.Null ? null : Api.Id(Placed);
            if (Placed != Entity.Null && em.HasComponent<RouteNumber>(Placed)) Result["number"] = em.GetComponentData<RouteNumber>(Placed).m_Number;
        }
    }

    /// <summary>Waits for several queued build jobs, then builds one combined result.</summary>
    public class ChainOperation : IOperation
    {
        readonly List<AgentToolSystem.BuildJob> jobs;
        readonly Func<object> finish;
        readonly int started = Environment.TickCount;
        readonly int timeoutMs;
        /// <summary>Called every frame while the jobs run (e.g. LinePathWatch.Observe).</summary>
        public Action OnStep;
        public ChainOperation(List<AgentToolSystem.BuildJob> jobs, Func<object> finish)
        {
            this.jobs = jobs; this.finish = finish;
            timeoutMs = 30000 + 15000 * jobs.Count;
        }
        public bool Step(out object result)
        {
            result = null;
            OnStep?.Invoke();
            if (jobs.All(j => j.Done)) { result = finish(); return true; }
            if (Environment.TickCount - started > timeoutMs) { result = new ApiError("build chain timed out (is the game running and in-game?)", 504); return true; }
            return false;
        }
    }

    public partial class Api
    {
        static readonly string[] LinePalette = { "#E53935", "#1E88E5", "#43A047", "#FB8C00", "#8E24AA", "#00ACC1", "#FDD835", "#6D4C41", "#D81B60", "#3949AB", "#7CB342", "#F4511E" };

        static bool TryParseColor(string s, out UnityEngine.Color32 c)
        {
            c = default;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.TrimStart('#');
            if (s.Length != 6 || !uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v)) return false;
            c = new UnityEngine.Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
            return true;
        }

        static string ColorHex(UnityEngine.Color32 c) => "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");

        // ------------------------------------------------------------------ lookups

        public HashSet<Entity> AllStops()
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<RouteStop>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = q.ToEntityArray(Allocator.Temp)) return new HashSet<Entity>(arr.ToArray());
        }

        public HashSet<Entity> AllRoutes()
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = q.ToEntityArray(Allocator.Temp)) return new HashSet<Entity>(arr.ToArray());
        }

        public Entity FindNewStop(float3 pos, HashSet<Entity> before)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<RouteStop>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            Entity best = Entity.Null; float bd = 25f;
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (before != null && before.Contains(e)) continue;
                    var d = math.distance(em.GetComponentData<ObjTransform>(e).m_Position.xz, pos.xz);
                    if (d < bd) { bd = d; best = e; }
                }
            return best;
        }

        public Entity FindEdgeNear(float3 p, float maxDist, out float t)
        {
            t = 0.5f;
            Entity best = Entity.Null; float bd = float.MaxValue;
            using (var edges = EdgeQuery().ToEntityArray(Allocator.Temp))
                foreach (var e in edges)
                {
                    if (!em.HasComponent<Road>(e)) continue;
                    var cv = em.GetComponentData<Curve>(e).m_Bezier;
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    float hw = em.HasComponent<NetGeometryData>(prefab) ? em.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth * 0.5f : 4f;
                    var d = MathUtils.Distance(cv.xz, p.xz, out float tt);
                    if (d > maxDist + hw || d >= bd) continue;
                    bd = d; best = e; t = tt;
                }
            return best;
        }

        static Game.Prefabs.TransportType ParseTransportType(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "all") return Game.Prefabs.TransportType.None;
            foreach (Game.Prefabs.TransportType v in Enum.GetValues(typeof(Game.Prefabs.TransportType)))
                if (string.Equals(v.ToString(), s, StringComparison.OrdinalIgnoreCase)) return v;
            return Game.Prefabs.TransportType.None;
        }

        bool IsStopPrefab(Entity e, Game.Prefabs.TransportType type)
            => em.HasComponent<TransportStopData>(e) && em.HasComponent<PlaceableObjectData>(e) && !em.HasComponent<BuildingData>(e)
               && em.GetComponentData<TransportStopData>(e).m_TransportType == type && em.GetComponentData<TransportStopData>(e).m_PassengerTransport;

        bool IsLinePrefab(Entity e, Game.Prefabs.TransportType type, bool cargo = false)
            => em.HasComponent<TransportLineData>(e) && em.HasComponent<RouteData>(e)
               && em.GetComponentData<TransportLineData>(e).m_TransportType == type
               && (cargo ? em.GetComponentData<TransportLineData>(e).m_CargoTransport : em.GetComponentData<TransportLineData>(e).m_PassengerTransport);

        /// <summary>Pick the placeable (menu) prefab; prefer unlocked ones.</summary>
        Entity DefaultPrefab(Func<Entity, bool> filter)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>());
            Entity best = Entity.Null; int bestScore = -1;
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (!filter(e)) continue;
                    int score = (em.HasComponent<UIObjectData>(e) ? 2 : 0) + (IsLocked(e) ? 0 : 4);
                    if (score > bestScore) { bestScore = score; best = e; }
                }
            return best;
        }

        // ------------------------------------------------------------------ stop snapping

        /// <summary>
        /// Control point for a stop on the given side of an edge, as ObjectToolSystem.SnapJob.SnapSegmentAreas
        /// computes it for RoadEdge objects (Snap.NetArea): nearest Buildable composition area of the edge's
        /// start/end segment, moved towards the area's snap line, rotation from the area direction.
        /// </summary>
        public bool SnapStop(Entity edge, float t, bool right, Entity prefab, out ControlPoint cp, Dictionary<string, object> info)
        {
            cp = default;
            if (!em.HasComponent<Curve>(edge) || !em.HasComponent<Composition>(edge) || !em.HasComponent<EdgeGeometry>(edge))
            { info["error"] = "edge has no road geometry"; return false; }
            var curve = em.GetComponentData<Curve>(edge).m_Bezier;
            t = math.clamp(t, 0.05f, 0.95f);
            var p = MathUtils.Position(curve, t);
            var tan = math.normalizesafe(MathUtils.Tangent(curve, t).xz);
            var n = right ? MathUtils.Right(tan) : MathUtils.Left(tan);
            var comp = em.GetComponentData<Composition>(edge);
            var cd = em.GetComponentData<NetCompositionData>(comp.m_Edge);
            var hit = p;
            hit.xz = p.xz + n * math.max(0.5f, cd.m_Width * 0.5f - 1f);

            float radius = 0f;
            bool building = em.HasComponent<BuildingData>(prefab);
            if (em.HasComponent<ObjectGeometryData>(prefab))
            {
                var og = em.GetComponentData<ObjectGeometryData>(prefab);
                bool standing = (og.m_Flags & GeometryFlags.Standing) != 0;
                radius = building ? 1f : standing ? og.m_LegSize.z * 0.5f + og.m_LegOffset.y : og.m_Size.z * 0.5f;
                if (standing && !building && og.m_LegSize.y <= cd.m_HeightRange.max) radius = math.max(radius, og.m_Size.z * 0.5f);
            }

            bool found = false; float bestD = float.MaxValue; float3 bestPos = default; float2 bestDir = default;
            var placement = em.HasComponent<PlaceableObjectData>(prefab) ? em.GetComponentData<PlaceableObjectData>(prefab).m_Flags : 0;
            info["placement"] = FlagNames(placement);
            if ((placement & Game.Objects.PlacementFlags.RoadEdge) == 0 && (placement & Game.Objects.PlacementFlags.RoadSide) != 0)
            {
                // RoadSide objects (NetSide snap) stand next to the road like buildings, front facing the road
                float depth = em.HasComponent<ObjectGeometryData>(prefab) ? em.GetComponentData<ObjectGeometryData>(prefab).m_Size.z : 2f;
                bestPos = p; bestPos.xz = p.xz + n * (cd.m_Width * 0.5f + depth * 0.5f);
                bestDir = -n;
                found = true;
                info["snap"] = "roadside";
            }
            else if (em.HasBuffer<NetCompositionArea>(comp.m_Edge))
            {
                var areas = em.GetBuffer<NetCompositionArea>(comp.m_Edge, true);
                var geo = em.GetComponentData<EdgeGeometry>(edge);
                var segs = new[] { geo.m_Start, geo.m_End };
                for (int pass = 0; pass < 2 && !found; pass++) // pass 0: sidewalk areas on the requested side only
                    foreach (var seg in segs)
                    {
                        var center = MathUtils.Lerp(seg.m_Left, seg.m_Right, 0.5f);
                        for (int i = 0; i < areas.Length; i++)
                        {
                            var ar = areas[i];
                            if ((ar.m_Flags & NetAreaFlags.Buildable) == 0) continue;
                            if (radius >= ar.m_Width * 0.51f) continue;
                            if (pass == 0 && ((ar.m_Flags & NetAreaFlags.Median) != 0 || (right ? ar.m_Position.x <= 0f : ar.m_Position.x >= 0f))) continue;
                            var ac = MathUtils.Lerp(seg.m_Left, seg.m_Right, ar.m_Position.x / cd.m_Width + 0.5f);
                            MathUtils.Distance(ac.xz, hit.xz, out float tt);
                            var pos = MathUtils.Position(ac, tt);
                            var dir = math.normalizesafe(MathUtils.Tangent(ac, tt).xz);
                            if ((ar.m_Flags & NetAreaFlags.Median) != 0)
                            {
                                dir = MathUtils.Left(dir);
                                if (((cd.m_Flags.m_Left ^ cd.m_Flags.m_Right) & CompositionFlags.Side.Raised) != 0)
                                { if ((cd.m_Flags.m_Left & CompositionFlags.Side.Raised) != 0) dir = -dir; }
                                else if (math.dot(MathUtils.Position(center, tt).xz - hit.xz, dir) < 0f) dir = -dir;
                            }
                            else if ((ar.m_Flags & NetAreaFlags.Invert) != 0) dir = MathUtils.Right(dir);
                            else dir = MathUtils.Left(dir);
                            var snap = MathUtils.Position(MathUtils.Lerp(seg.m_Left, seg.m_Right, ar.m_SnapPosition.x / cd.m_Width + 0.5f), tt);
                            float o1 = math.max(0f, math.min(ar.m_Width * 0.5f, math.abs(ar.m_SnapPosition.x - ar.m_Position.x) + ar.m_SnapWidth * 0.5f) - radius);
                            float o2 = math.max(0f, ar.m_SnapWidth * 0.5f - radius);
                            pos.xz = pos.xz + MathUtils.ClampLength(snap.xz - pos.xz, o1);
                            pos.xz = pos.xz + MathUtils.ClampLength(hit.xz - pos.xz, o2);
                            pos.y += ar.m_Position.y;
                            float d = math.distance(pos.xz, hit.xz);
                            if (d < bestD) { bestD = d; bestPos = pos; bestDir = dir; found = true; }
                        }
                    }
            }
            if (!found)
            {
                // no buildable area (e.g. road without sidewalk): put it at the road side
                bestPos = p; bestPos.xz = p.xz + n * (cd.m_Width * 0.5f - 1f);
                bestDir = n;
                info["snap"] = "fallback (no sidewalk area found)";
            }
            else if (!info.ContainsKey("snap")) info["snap"] = "sidewalk";
            MathUtils.Distance(curve.xz, bestPos.xz, out float edgeT);
            cp = new ControlPoint
            {
                m_Position = bestPos,
                m_HitPosition = hit,
                m_Direction = bestDir,
                m_HitDirection = new float3(0, 1, 0),
                m_Rotation = math.all(bestDir == 0) ? quaternion.identity : quaternion.LookRotation(new float3(bestDir.x, 0, bestDir.y), math.up()),
                m_OriginalEntity = edge,
                m_ElementIndex = new int2(-1, -1),
                m_CurvePosition = edgeT,
            };
            info["edge"] = Id(edge);
            info["t"] = Math.Round(edgeT, 3);
            info["side"] = right ? "right" : "left";
            info["pos"] = F2(bestPos);
            info["facing"] = new List<object> { Math.Round(bestDir.x, 2), Math.Round(bestDir.y, 2) };
            return true;
        }

        /// <summary>Existing stop of this transport type on the same side of the same edge within `dist` metres.</summary>
        Entity ExistingStopNear(Entity edge, float3 pos, bool right, Game.Prefabs.TransportType type, float dist)
        {
            var cv = em.GetComponentData<Curve>(edge).m_Bezier;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<RouteStop>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            Entity best = Entity.Null; float bd = dist;
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var pf = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!em.HasComponent<TransportStopData>(pf) || em.GetComponentData<TransportStopData>(pf).m_TransportType != type) continue;
                    if (em.HasComponent<Attached>(e) && em.GetComponentData<Attached>(e).m_Parent != edge) continue;
                    var sp = em.GetComponentData<ObjTransform>(e).m_Position;
                    MathUtils.Distance(cv.xz, sp.xz, out float st);
                    bool stopRight = math.dot(sp.xz - MathUtils.Position(cv, st).xz, MathUtils.Right(math.normalizesafe(MathUtils.Tangent(cv, st).xz))) >= 0f;
                    if (stopRight != right) continue;
                    var d = math.distance(sp.xz, pos.xz);
                    if (d < bd) { bd = d; best = e; }
                }
            return best;
        }

        /// <summary>Parse {edge,t,side} or {x,z[,side]} into (edge, t, right, anchor).</summary>
        string ParseStopSpec(Dictionary<string, object> d, out Entity edge, out float t, out bool right, out float3 anchor)
        {
            var a = new Args(d);
            edge = Entity.Null; t = 0.5f; right = true; anchor = default;
            if (a.Has("edge"))
            {
                edge = Resolve(a.Raw("edge"), EdgeQuery());
                if (edge == Entity.Null || !em.HasComponent<NetEdge>(edge)) return "edge not found: " + a.Raw("edge");
                if (!em.HasComponent<Road>(edge)) return "edge " + a.Raw("edge") + " is not a road";
                t = math.clamp(a.F("t", 0.5f), 0f, 1f);
                right = a.Str("side", "right").ToLowerInvariant() != "left";
                anchor = MathUtils.Position(em.GetComponentData<Curve>(edge).m_Bezier, t);
                return null;
            }
            if (a.Has("x") && a.Has("z"))
            {
                var p = new float3(a.F("x"), 0, a.F("z"));
                edge = FindEdgeNear(p, a.F("snap", 20f), out t);
                if (edge == Entity.Null) return "no road within snap distance of " + a.F("x") + "," + a.F("z");
                var cv = em.GetComponentData<Curve>(edge).m_Bezier;
                anchor = MathUtils.Position(cv, t);
                if (a.Has("side")) right = a.Str("side").ToLowerInvariant() != "left";
                else right = math.dot(p.xz - anchor.xz, MathUtils.Right(math.normalizesafe(MathUtils.Tangent(cv, t).xz))) >= 0f;
                return null;
            }
            return "stop needs {edge,t,side} or {x,z}";
        }

        // ------------------------------------------------------------------ endpoints

        /// <summary>POST /busstop {edge, t, side:"left|right"} or {x, z[, side]}, prefab?, dryRun</summary>
        object BusStop(Args a)
        {
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy", 409);
            var type = ParseTransportType(a.Str("type", "bus"));
            if (type == Game.Prefabs.TransportType.None) return new ApiError("unknown transport type " + a.Str("type"));
            var prefab = a.Has("prefab") ? FindPrefab(a.Str("prefab"), e => IsStopPrefab(e, type)) : DefaultPrefab(e => IsStopPrefab(e, type));
            if (prefab == Entity.Null) return new ApiError("no " + type + " stop prefab found (GET /transitprefabs)");
            if (IsLocked(prefab)) return new ApiError(PrefabName(prefab) + " is still locked");
            var err = ParseStopSpec(a.RawDict, out var edge, out var t, out var right, out var anchor);
            if (err != null) return new ApiError(err);
            var info = new Dictionary<string, object> { { "prefab", PrefabName(prefab) } };
            var probe = new Dictionary<string, object>();
            if (SnapStop(edge, t, right, prefab, out var cp, probe) && !a.B("allowDuplicate"))
            {
                var ex = ExistingStopNear(edge, cp.m_Position, right, type, a.F("reuseDistance", 12f));
                if (ex != Entity.Null)
                {
                    info["stopId"] = Id(ex); info["reused"] = true; info["applied"] = false;
                    info["note"] = "a stop already exists here; pass allowDuplicate:true to place another one";
                    return info;
                }
            }
            var job = new StopJob { Api = this, Prefab = prefab, Edge = edge, T = t, Right = right, Anchor = anchor, DryRun = a.B("dryRun") };
            tool.Enqueue(job);
            return new BuildOperation(job, info);
        }

        /// <summary>
        /// POST /busline (also POST /line) {type: bus|tram|train|subway|ship|airplane (default bus), cargo: false,
        ///   stops:[stopId | stationId | {edge,t,side} | {x,z,side?}, ...], color:"#RRGGBB", prefab?, stopPrefab?, dryRun, allowBroken}
        /// Road stops (bus, tram): missing stops are placed first (existing stops within 12 m on the same side are
        /// reused). Train, subway, ship, airplane and all cargo stops belong to stations / outside connections: pass
        /// their stop id, the station (or outside connection) id, or {x,z} near one (nearest fitting stop within
        /// snap=300 m). cargo:true makes a cargo route (Cargo Train/Ship/Airplane Route) through cargo stops. The line
        /// is a closed loop through the stops in order. Its preview is pathfound segment by segment (pathCheck); a
        /// real build with a segment that has no vehicle path is not applied unless allowBroken:true. A dry run with
        /// new stops previews the line through plain waypoints next to the planned stop positions.
        /// </summary>
        object BusLine(Args a)
        {
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy", 409);
            var type = ParseTransportType(a.Str("type", "bus"));
            if (type == Game.Prefabs.TransportType.None) return new ApiError("unknown transport type " + a.Str("type"));
            bool cargo = a.B("cargo");
            var linePrefab = a.Has("prefab") ? FindPrefab(a.Str("prefab"), e => IsLinePrefab(e, type, cargo)) : DefaultPrefab(e => IsLinePrefab(e, type, cargo));
            if (linePrefab == Entity.Null) return new ApiError("no " + type + (cargo ? " cargo route" : " line") + " prefab found (GET /transitprefabs)");
            if (IsLocked(linePrefab)) return new ApiError(PrefabName(linePrefab) + " is still locked");
            var list = a.List("stops");
            if (list == null || list.Count < 2) return new ApiError("stops: at least 2 entries (stop ids, station ids, {edge,t,side} or {x,z})");
            bool dry = a.B("dryRun");

            var plan = new StopPlan
            {
                Type = type, Cargo = cargo, Dry = dry,
                StopQuery = em.CreateEntityQuery(ComponentType.ReadOnly<RouteStop>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()),
            };
            if (!cargo)
                plan.StopPrefab = a.Has("stopPrefab") ? FindPrefab(a.Str("stopPrefab"), e => IsStopPrefab(e, type))
                    : DefaultPrefab(e => IsStopPrefab(e, type) && !IsIntegratedName(PrefabName(e)));
            var stopReport = new List<object>();
            var resolvers = new List<Func<Entity>>();
            var proxies = new List<float3?>();
            for (int i = 0; i < list.Count; i++)
            {
                var err = PlanStop(list[i], i, plan, out var ps);
                if (err != null) return new ApiError(err);
                stopReport.Add(ps.Report);
                resolvers.Add(ps.Resolve);
                proxies.Add(ps.Proxy);
            }

            UnityEngine.Color32 color;
            if (!TryParseColor(a.Str("color"), out color))
                TryParseColor(LinePalette[AllRoutes().Count % LinePalette.Length], out color);

            var jobs = new List<AgentToolSystem.BuildJob>(plan.Jobs);
            var lineJob = new LineJob { Api = this, RoutePrefab = linePrefab, Color = color, Stops = resolvers, Proxies = proxies, DryRun = dry };
            jobs.Add(lineJob);
            var watch = new LinePathWatch(this, lineJob, linePrefab, Entity.Null, a.B("allowBroken"));
            var depots = BusDepots(type);
            foreach (var j in jobs) tool.Enqueue(j);

            var stopJobs = jobs.OfType<StopJob>().ToList();
            return new ChainOperation(jobs, () =>
            {
                var res = new Dictionary<string, object>
                {
                    { "linePrefab", PrefabName(linePrefab) }, { "cargo", cargo }, { "color", ColorHex(color) }, { "dryRun", dry },
                    { "stops", stopReport },
                    { "newStops", stopJobs.Select(j => (object)j.Result).ToList() },
                    { "line", lineJob.Result },
                    { "pathCheck", watch.Report() },
                };
                if (lineJob.Placed != Entity.Null) res["lineId"] = Id(lineJob.Placed);
                if (watch.Blocked != null) res["blocked"] = watch.Blocked;
                if (dry && stopJobs.Count > 0) res["note"] = "dry run: new stops are previewed separately; the line preview used plain waypoints next to the planned stop positions";
                if (depots.Count == 0) res["warning"] = "no " + type + " depot in the city: the line gets no vehicles until one is built (bus: BusDepot01, tram: TramDepot01, train: RailYard01/02, subway: SubwayYard01)";
                return res;
            }) { OnStep = watch.Observe };
        }

        List<object> BusDepots(Game.Prefabs.TransportType type)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var res = new List<object>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var pf = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!em.HasComponent<TransportDepotData>(pf) || em.GetComponentData<TransportDepotData>(pf).m_TransportType != type) continue;
                    res.Add(new Dictionary<string, object>
                    {
                        { "id", Id(e) }, { "prefab", PrefabName(pf) }, { "pos", F2(em.GetComponentData<ObjTransform>(e).m_Position) },
                        { "capacity", em.GetComponentData<TransportDepotData>(pf).m_VehicleCapacity },
                        { "roadConnected", em.GetComponentData<Building>(e).m_RoadEdge != Entity.Null },
                    });
                }
            return res;
        }

        /// <summary>GET /stops?x&z&radius&type=bus|all: transport stops (also those inside stations).</summary>
        object Stops(float x, float z, float radius, string typeName)
        {
            var type = ParseTransportType(typeName);
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<RouteStop>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var res = new List<object>();
            int total = 0;
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var pf = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    var tsd = em.HasComponent<TransportStopData>(pf) ? em.GetComponentData<TransportStopData>(pf) : default;
                    if (type != Game.Prefabs.TransportType.None && (!em.HasComponent<TransportStopData>(pf) || tsd.m_TransportType != type)) continue;
                    var pos = em.GetComponentData<ObjTransform>(e).m_Position;
                    var dist = math.distance(pos.xz, new float2(x, z));
                    if (dist > radius) continue;
                    total++;
                    if (res.Count >= 300) continue;
                    var d = new Dictionary<string, object>
                    {
                        { "id", Id(e) }, { "prefab", PrefabName(pf) }, { "type", em.HasComponent<TransportStopData>(pf) ? tsd.m_TransportType.ToString() : "?" },
                        { "pos", F2(pos) }, { "distance", Math.Round(dist, 1) },
                        { "active", (em.GetComponentData<RouteStop>(e).m_Flags & Game.Routes.StopFlags.Active) != 0 },
                    };
                    if (em.HasComponent<Attached>(e)) d["edge"] = Id(em.GetComponentData<Attached>(e).m_Parent);
                    if (em.HasComponent<Owner>(e)) d["owner"] = Id(em.GetComponentData<Owner>(e).m_Owner);
                    if (em.HasBuffer<ConnectedRoute>(e))
                    {
                        var cr = em.GetBuffer<ConnectedRoute>(e, true);
                        var lines = new List<object>(); int waiting = 0;
                        for (int i = 0; i < cr.Length; i++)
                        {
                            var wp = cr[i].m_Waypoint;
                            if (em.HasComponent<Owner>(wp)) lines.Add(Id(em.GetComponentData<Owner>(wp).m_Owner));
                            if (em.HasComponent<WaitingPassengers>(wp)) waiting += em.GetComponentData<WaitingPassengers>(wp).m_Count;
                        }
                        d["lines"] = lines; d["waiting"] = waiting;
                    }
                    res.Add(d);
                }
            res.Sort((p1, p2) => ((double)((Dictionary<string, object>)p1)["distance"]).CompareTo((double)((Dictionary<string, object>)p2)["distance"]));
            return new Dictionary<string, object> { { "count", total }, { "stops", res } };
        }

        /// <summary>
        /// GET /lines?type=bus|all: transport lines with stops, vehicles, passengers and depots, the path state of every
        /// segment (stop i -> stop i+1: ok | pending | failed) and broken = a segment has no vehicle path (the game
        /// shows Pathfind Failed on its waypoints; complete only says the loop is closed).
        /// </summary>
        object Lines(string typeName)
        {
            var type = ParseTransportType(typeName);
            var icons = IconsByOwner();
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<TransportLine>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var res = new List<object>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var pf = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    var tld = em.HasComponent<TransportLineData>(pf) ? em.GetComponentData<TransportLineData>(pf) : default;
                    if (type != Game.Prefabs.TransportType.None && tld.m_TransportType != type) continue;
                    var tl = em.GetComponentData<TransportLine>(e);
                    var route = em.GetComponentData<Route>(e);
                    var d = new Dictionary<string, object>
                    {
                        { "id", Id(e) }, { "prefab", PrefabName(pf) }, { "type", tld.m_TransportType.ToString() }, { "cargo", tld.m_CargoTransport },
                        { "number", em.HasComponent<RouteNumber>(e) ? em.GetComponentData<RouteNumber>(e).m_Number : 0 },
                        { "color", em.HasComponent<RouteColor>(e) ? ColorHex(em.GetComponentData<RouteColor>(e).m_Color) : null },
                        { "complete", (route.m_Flags & RouteFlags.Complete) != 0 },
                        { "vehicleInterval", tl.m_VehicleInterval }, { "unbunching", tl.m_UnbunchingFactor },
                        { "ticketPrice", (int)tl.m_TicketPrice }, { "flags", FlagNames(tl.m_Flags) },
                    };
                    var stops = new List<object>(); int waiting = 0;
                    if (em.HasBuffer<RouteWaypoint>(e))
                    {
                        var wps = em.GetBuffer<RouteWaypoint>(e, true);
                        d["waypoints"] = wps.Length;
                        for (int i = 0; i < wps.Length; i++)
                        {
                            var wp = wps[i].m_Waypoint;
                            if (!em.HasComponent<WaypointConnected>(wp)) continue;
                            var stop = em.GetComponentData<WaypointConnected>(wp).m_Connected;
                            var s = new Dictionary<string, object> { { "stopId", Id(stop) }, { "index", i } };
                            if (em.HasComponent<PrefabRef>(stop)) s["prefab"] = PrefabName(em.GetComponentData<PrefabRef>(stop).m_Prefab);
                            var wic = IconList(icons, wp);
                            if (wic != null) s["icons"] = wic;
                            if (em.HasComponent<RoutePosition>(wp)) s["pos"] = F2(em.GetComponentData<RoutePosition>(wp).m_Position);
                            if (em.HasComponent<WaitingPassengers>(wp))
                            {
                                var w = em.GetComponentData<WaitingPassengers>(wp);
                                s["waiting"] = w.m_Count; s["avgWait"] = (int)w.m_AverageWaitingTime;
                                waiting += w.m_Count;
                            }
                            stops.Add(s);
                        }
                    }
                    d["stops"] = stops;
                    d["waitingPassengers"] = waiting;
                    float length = 0, duration = 0;
                    if (em.HasBuffer<RouteSegment>(e))
                    {
                        var segs = em.GetBuffer<RouteSegment>(e, true);
                        for (int i = 0; i < segs.Length; i++)
                            if (em.HasComponent<RouteInfo>(segs[i].m_Segment))
                            {
                                var ri = em.GetComponentData<RouteInfo>(segs[i].m_Segment);
                                length += ri.m_Distance; duration += ri.m_Duration;
                            }
                    }
                    d["length"] = Math.Round(length, 0);
                    d["duration"] = Math.Round(duration, 0);
                    var segRows = new List<object>();
                    if (LineSegments(e, segRows, out var failedSegs, out var pendingSegs))
                    {
                        d["broken"] = failedSegs > 0;
                        d["failedSegments"] = failedSegs;
                        if (pendingSegs > 0) d["pendingSegments"] = pendingSegs;
                        // compact: every segment's status and length; from/to for the ones that are not ok
                        d["segments"] = segRows.Select(o =>
                        {
                            var sr = (Dictionary<string, object>)o;
                            var c = new Dictionary<string, object> { { "i", sr["i"] }, { "status", sr["status"] } };
                            if (sr.TryGetValue("length", out var sl)) c["length"] = sl;
                            if ((string)sr["status"] != "ok") { c["from"] = sr.TryGetValue("from", out var f) ? f : null; c["to"] = sr.TryGetValue("to", out var t2) ? t2 : null; if (sr.TryGetValue("pathFlags", out var pfl)) c["pathFlags"] = pfl; }
                            return (object)c;
                        }).ToList();
                    }
                    int vehicles = 0, passengers = 0;
                    if (em.HasBuffer<RouteVehicle>(e))
                    {
                        var rv = em.GetBuffer<RouteVehicle>(e, true);
                        vehicles = rv.Length;
                        for (int i = 0; i < rv.Length; i++)
                            if (em.HasBuffer<VehiclePassenger>(rv[i].m_Vehicle)) passengers += em.GetBuffer<VehiclePassenger>(rv[i].m_Vehicle, true).Length;
                    }
                    d["vehicles"] = vehicles;
                    d["passengersOnBoard"] = passengers;
                    res.Add(d);
                }
            var r = new Dictionary<string, object> { { "count", res.Count }, { "lines", res } };
            r["busDepots"] = BusDepots(Game.Prefabs.TransportType.Bus);
            return r;
        }

        /// <summary>GET /transitprefabs: line, stop, depot and station prefabs with lock state.</summary>
        object TransitPrefabs()
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>());
            var lines = new List<object>(); var stops = new List<object>(); var depots = new List<object>(); var stations = new List<object>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (em.HasComponent<TransportLineData>(e))
                    {
                        var t = em.GetComponentData<TransportLineData>(e);
                        lines.Add(new Dictionary<string, object> { { "name", PrefabName(e) }, { "type", t.m_TransportType.ToString() }, { "locked", IsLocked(e) }, { "passenger", t.m_PassengerTransport }, { "defaultVehicleInterval", t.m_DefaultVehicleInterval } });
                    }
                    else if (em.HasComponent<TransportStopData>(e) && em.HasComponent<PlaceableObjectData>(e) && !em.HasComponent<BuildingData>(e) && em.HasComponent<UIObjectData>(e))
                    {
                        var t = em.GetComponentData<TransportStopData>(e);
                        stops.Add(new Dictionary<string, object> { { "name", PrefabName(e) }, { "type", t.m_TransportType.ToString() }, { "locked", IsLocked(e) }, { "passenger", t.m_PassengerTransport }, { "placement", FlagNames(em.GetComponentData<PlaceableObjectData>(e).m_Flags) }, { "cost", em.GetComponentData<PlaceableObjectData>(e).m_ConstructionCost } });
                    }
                    else if (em.HasComponent<TransportDepotData>(e) && em.HasComponent<BuildingData>(e) && em.HasComponent<UIObjectData>(e))
                    {
                        var t = em.GetComponentData<TransportDepotData>(e);
                        depots.Add(new Dictionary<string, object> { { "name", PrefabName(e) }, { "type", t.m_TransportType.ToString() }, { "locked", IsLocked(e) }, { "vehicles", t.m_VehicleCapacity } });
                    }
                    else if (em.HasComponent<TransportStationData>(e) && em.HasComponent<BuildingData>(e) && em.HasComponent<UIObjectData>(e))
                        stations.Add(new Dictionary<string, object> { { "name", PrefabName(e) }, { "locked", IsLocked(e) } });
                }
            return new Dictionary<string, object> { { "lines", lines }, { "stops", stops }, { "depots", depots }, { "stations", stations } };
        }
    }
}
