using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using GB = Game.Buildings;
using GC = Game.Companies;
using GCity = Game.City;
using GE = Game.Economy;
using GN = Game.Net;
using GO = Game.Objects;
using GP = Game.Prefabs;
using GPF = Game.Pathfind;
using GR = Game.Routes;
using GV = Game.Vehicles;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Freight and train view (GET /freight, GET /vehicles): train, ship and plane vehicles with their path state,
    /// cargo stations / rail yards / harbours / airports with owned vehicles, storage and pending transfers, the
    /// rail outside connections, and a track test (can a train get from each rail outside connection into a
    /// station and back out) on the game's own net and lane data.
    /// </summary>
    public partial class Api
    {
        // ------------------------------------------------------------------ small helpers

        sealed class IconHit { public string Name; public float3 Pos; }

        /// <summary>Notification icons by owner entity (one query; icons sit on buildings, lanes, waypoints, vehicles).</summary>
        Dictionary<Entity, List<IconHit>> IconsByOwner()
        {
            var map = new Dictionary<Entity, List<IconHit>>();
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Notifications.Icon>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<GP.PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var o = em.GetComponentData<Owner>(e).m_Owner;
                    if (!map.TryGetValue(o, out var l)) map[o] = l = new List<IconHit>();
                    l.Add(new IconHit { Name = PrefabName(em.GetComponentData<GP.PrefabRef>(e).m_Prefab), Pos = em.GetComponentData<Game.Notifications.Icon>(e).m_Location });
                }
            return map;
        }

        static List<object> IconList(Dictionary<Entity, List<IconHit>> icons, Entity e)
            => icons != null && icons.TryGetValue(e, out var l) ? l.Select(x => (object)x.Name).Distinct().ToList() : null;

        /// <summary>The building (or outside connection object) an entity belongs to: walks Owner/Attached up (stop -> station, upgrade -> main building).</summary>
        Entity TopBuilding(Entity e)
        {
            Entity found = Entity.Null;
            for (int i = 0; i < 8 && e != Entity.Null && em.Exists(e); i++)
            {
                if (em.HasComponent<GB.Building>(e) || em.HasComponent<GO.OutsideConnection>(e)) found = e;
                if (em.HasComponent<Owner>(e)) { e = em.GetComponentData<Owner>(e).m_Owner; continue; }
                break;
            }
            return found;
        }

        string PrefabOf(Entity e) => e != Entity.Null && em.Exists(e) && em.HasComponent<GP.PrefabRef>(e) ? PrefabName(em.GetComponentData<GP.PrefabRef>(e).m_Prefab) : null;

        /// <summary>Compact reference to a target/owner entity: id, prefab, the stop and building behind a waypoint, position.</summary>
        Dictionary<string, object> EntityRef(Entity e)
        {
            if (e == Entity.Null || !em.Exists(e)) return null;
            var d = new Dictionary<string, object> { { "id", Id(e) } };
            var pf = PrefabOf(e);
            if (pf != null) d["prefab"] = pf;
            var x = e;
            if (em.HasComponent<GR.Waypoint>(e) && em.HasComponent<Owner>(e)) d["line"] = Id(em.GetComponentData<Owner>(e).m_Owner);
            if (em.HasComponent<GR.Connected>(e)) { x = em.GetComponentData<GR.Connected>(e).m_Connected; d["stop"] = Id(x); }
            var b = TopBuilding(x);
            if (b != Entity.Null && b != e) { d["building"] = Id(b); d["buildingPrefab"] = PrefabOf(b); }
            if (TryPos(x, out var p)) d["pos"] = F2(p);
            return d;
        }

        static void Count(Dictionary<string, int> d, string k, int n = 1) { if (k != null) d[k] = d.TryGetValue(k, out var c) ? c + n : n; }

        static Dictionary<string, object> Ordered(Dictionary<string, int> d) => d.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => (object)kv.Value);

        static string ResName(GE.Resource r) => r.ToString();

        /// <summary>Lead vehicle of a (multi-car) vehicle: trains are a controller car plus LayoutElement cars.</summary>
        Entity LeadVehicle(Entity v)
        {
            if (em.HasComponent<GV.Controller>(v))
            {
                var c = em.GetComponentData<GV.Controller>(v).m_Controller;
                if (c != Entity.Null && em.Exists(c)) return c;
            }
            return v;
        }

        string VehicleKind(Entity v)
        {
            var pf = em.HasComponent<GP.PrefabRef>(v) ? em.GetComponentData<GP.PrefabRef>(v).m_Prefab : Entity.Null;
            if (em.HasComponent<GV.Train>(v))
            {
                var tt = pf != Entity.Null && em.HasComponent<GP.TrainData>(pf) ? em.GetComponentData<GP.TrainData>(pf).m_TrackType : GN.TrackTypes.Train;
                return tt == GN.TrackTypes.Tram ? "tram" : tt == GN.TrackTypes.Subway ? "subway" : "train";
            }
            if (em.HasComponent<GV.Watercraft>(v)) return "ship";
            if (em.HasComponent<GV.Airplane>(v)) return "airplane";
            if (em.HasComponent<GV.Helicopter>(v)) return "helicopter";
            if (em.HasComponent<GV.Car>(v)) return em.HasComponent<GV.PublicTransport>(v) ? "bus" : em.HasComponent<GV.DeliveryTruck>(v) ? "truck" : "car";
            return "other";
        }

        string VehicleRole(Entity v)
        {
            if (em.HasComponent<GV.CargoTransport>(v)) return "cargo";
            if (em.HasComponent<GV.PublicTransport>(v)) return "passenger";
            if (em.HasComponent<GV.DeliveryTruck>(v)) return "delivery";
            if (em.HasComponent<GV.PersonalCar>(v)) return "personal";
            return "service";
        }

        /// <summary>Path state of a vehicle or route segment: pending | failed | stuck | ok (+ flags, remaining elements).</summary>
        string PathState(Entity e, Dictionary<string, object> d)
        {
            GPF.PathFlags st = 0;
            if (em.HasComponent<GPF.PathOwner>(e))
            {
                var po = em.GetComponentData<GPF.PathOwner>(e);
                st = po.m_State;
                if (em.HasBuffer<GPF.PathElement>(e))
                {
                    int len = em.GetBuffer<GPF.PathElement>(e, true).Length;
                    d["pathRemaining"] = math.max(0, len - po.m_ElementIndex);
                }
            }
            if (st != 0) d["pathFlags"] = FlagNames(st);
            if (em.HasComponent<GPF.PathInformation>(e))
            {
                var pi = em.GetComponentData<GPF.PathInformation>(e);
                if (pi.m_Distance >= 0 && (pi.m_Distance > 0 || pi.m_Duration > 0))
                { d["pathLength"] = Math.Round(pi.m_Distance, 0); d["pathDuration"] = Math.Round(pi.m_Duration, 0); }
                if (pi.m_Distance < 0) return "failed";
            }
            if ((st & GPF.PathFlags.Stuck) != 0) return "stuck";
            if ((st & GPF.PathFlags.Failed) != 0) return "failed";
            if ((st & (GPF.PathFlags.Pending | GPF.PathFlags.Scheduled)) != 0) return "pending";
            return "ok";
        }

        /// <summary>One vehicle (lead car) with position, kind, state, owner, line, target, path state, lane and load.</summary>
        Dictionary<string, object> DescribeVehicle(Entity v, Dictionary<Entity, List<IconHit>> icons, List<string> problems)
        {
            var d = new Dictionary<string, object> { { "id", Id(v) }, { "prefab", PrefabOf(v) }, { "kind", VehicleKind(v) }, { "role", VehicleRole(v) } };
            if (em.HasComponent<ObjTransform>(v)) d["pos"] = F2(em.GetComponentData<ObjTransform>(v).m_Position);
            bool parked = em.HasComponent<GV.ParkedTrain>(v) || em.HasComponent<GV.ParkedCar>(v);
            if (parked) d["parked"] = true;
            float speed = em.HasComponent<GO.Moving>(v) ? math.length(em.GetComponentData<GO.Moving>(v).m_Velocity) : 0f;
            d["speed"] = Math.Round(speed, 1);

            // cars, load and capacity
            var carsList = new List<Entity>();
            if (em.HasBuffer<GV.LayoutElement>(v))
            {
                var lay = em.GetBuffer<GV.LayoutElement>(v, true);
                for (int i = 0; i < lay.Length; i++) if (lay[i].m_Vehicle != Entity.Null && em.Exists(lay[i].m_Vehicle)) carsList.Add(lay[i].m_Vehicle);
            }
            if (carsList.Count == 0) carsList.Add(v);
            long cargo = 0; int cap = 0, pax = 0, paxCap = 0;
            var load = new Dictionary<string, int>();
            foreach (var c in carsList)
            {
                if (em.HasBuffer<GE.Resources>(c))
                {
                    var rb = em.GetBuffer<GE.Resources>(c, true);
                    for (int i = 0; i < rb.Length; i++) if (rb[i].m_Amount != 0) { cargo += rb[i].m_Amount; Count(load, ResName(rb[i].m_Resource), rb[i].m_Amount); }
                }
                var cpf = em.GetComponentData<GP.PrefabRef>(c).m_Prefab;
                if (em.HasComponent<GP.CargoTransportVehicleData>(cpf)) cap += em.GetComponentData<GP.CargoTransportVehicleData>(cpf).m_CargoCapacity;
                if (em.HasComponent<GP.PublicTransportVehicleData>(cpf)) paxCap += em.GetComponentData<GP.PublicTransportVehicleData>(cpf).m_PassengerCapacity;
                if (em.HasBuffer<GV.Passenger>(c)) pax += em.GetBuffer<GV.Passenger>(c, true).Length;
            }
            if (carsList.Count > 1) d["cars"] = carsList.Count;
            if (cap > 0 || cargo > 0) { d["cargo"] = cargo; d["cargoCapacity"] = cap; if (load.Count > 0) d["load"] = Ordered(load); }
            if (paxCap > 0) { d["passengers"] = pax; d["passengerCapacity"] = paxCap; }
            if (em.HasComponent<GV.DeliveryTruck>(v))
            {
                var dt = em.GetComponentData<GV.DeliveryTruck>(v);
                d["state"] = FlagNames(dt.m_State); d["resource"] = ResName(dt.m_Resource); d["amount"] = dt.m_Amount;
            }

            // state
            bool abandon = false, dummy = false;
            if (em.HasComponent<GV.CargoTransport>(v))
            {
                var ct = em.GetComponentData<GV.CargoTransport>(v);
                d["state"] = FlagNames(ct.m_State);
                abandon = (ct.m_State & GV.CargoTransportFlags.AbandonRoute) != 0;
                dummy = (ct.m_State & GV.CargoTransportFlags.DummyTraffic) != 0;
                if (ct.m_RequestCount > 0) d["requests"] = ct.m_RequestCount;
            }
            else if (em.HasComponent<GV.PublicTransport>(v))
            {
                var pt = em.GetComponentData<GV.PublicTransport>(v);
                d["state"] = FlagNames(pt.m_State);
                abandon = (pt.m_State & GV.PublicTransportFlags.AbandonRoute) != 0;
                dummy = (pt.m_State & GV.PublicTransportFlags.DummyTraffic) != 0;
            }
            if (dummy) d["dummyTraffic"] = true; // visual through-traffic spawned at outside connections, carries no city cargo

            // owner, line, target
            if (em.HasComponent<Owner>(v)) d["owner"] = EntityRef(em.GetComponentData<Owner>(v).m_Owner);
            if (em.HasComponent<GR.CurrentRoute>(v))
            {
                var r = em.GetComponentData<GR.CurrentRoute>(v).m_Route;
                if (r != Entity.Null && em.Exists(r))
                {
                    var ld = new Dictionary<string, object> { { "id", Id(r) }, { "prefab", PrefabOf(r) } };
                    if (em.HasComponent<GR.RouteNumber>(r)) ld["number"] = em.GetComponentData<GR.RouteNumber>(r).m_Number;
                    d["line"] = ld;
                }
            }
            Entity target = em.HasComponent<Target>(v) ? em.GetComponentData<Target>(v).m_Target : Entity.Null;
            if (target != Entity.Null) d["target"] = EntityRef(target);

            // path and lane
            var path = new Dictionary<string, object>();
            var ps = PathState(v, path);
            path["state"] = ps;
            d["path"] = path;
            Entity lane = Entity.Null;
            if (em.HasComponent<GV.TrainCurrentLane>(v))
            {
                var tl = em.GetComponentData<GV.TrainCurrentLane>(v);
                lane = tl.m_Front.m_Lane;
                if (tl.m_Front.m_LaneFlags != 0) d["laneFlags"] = FlagNames(tl.m_Front.m_LaneFlags);
            }
            else if (em.HasComponent<GV.WatercraftCurrentLane>(v)) lane = em.GetComponentData<GV.WatercraftCurrentLane>(v).m_Lane;
            else if (em.HasComponent<GV.AircraftCurrentLane>(v)) lane = em.GetComponentData<GV.AircraftCurrentLane>(v).m_Lane;
            else if (em.HasComponent<GV.CarCurrentLane>(v)) lane = em.GetComponentData<GV.CarCurrentLane>(v).m_Lane;
            if (lane != Entity.Null && em.Exists(lane))
            {
                var on = em.HasComponent<Owner>(lane) ? em.GetComponentData<Owner>(lane).m_Owner : Entity.Null;
                d["on"] = on != Entity.Null ? (object)new Dictionary<string, object> { { "id", Id(on) }, { "prefab", PrefabOf(on) }, { "lane", PrefabOf(lane) } } : PrefabOf(lane);
                var top = TopBuilding(on);
                if (top != Entity.Null) ((Dictionary<string, object>)d["on"])["insideBuilding"] = Id(top) + " " + PrefabOf(top);
            }
            if (em.HasComponent<GV.Blocker>(v))
            {
                var bl = em.GetComponentData<GV.Blocker>(v);
                if (bl.m_Blocker != Entity.Null && bl.m_Type != GV.BlockerType.None)
                    d["blockedBy"] = new Dictionary<string, object> { { "id", Id(bl.m_Blocker) }, { "prefab", PrefabOf(bl.m_Blocker) }, { "type", bl.m_Type.ToString() } };
            }
            var ic = IconList(icons, v);
            if (ic != null) d["icons"] = ic;

            // problems
            if (problems != null)
            {
                if (ps == "failed") problems.Add("pathfind failed");
                if (ps == "stuck") problems.Add("stuck");
                if (abandon) problems.Add("abandoning its route");
                if (!parked && target == Entity.Null && ps != "pending") problems.Add("no target");
                if (ic != null) problems.AddRange(ic.Select(x => "icon: " + x));
                if (problems.Count > 0) d["problems"] = problems.ToList();
            }
            return d;
        }

        static bool IsLead(EntityManager m, Entity e)
        {
            if (!m.HasComponent<GV.Controller>(e)) return true;
            var c = m.GetComponentData<GV.Controller>(e).m_Controller;
            return c == Entity.Null || c == e;
        }

        /// <summary>
        /// GET /vehicles?kind=train|tram|subway|ship|airplane|bus|truck|cargo|passenger|all&amp;x&amp;z&amp;radius&amp;limit=100&amp;problems=1
        /// Lead vehicles (one row per train) with position, state, owner, line, target, path state (pending/failed/stuck),
        /// the net edge they are on and what blocks them. problems=1 lists only vehicles with a problem.
        /// </summary>
        object Vehicles(Pending p)
        {
            var kind = (p.Q("kind", "train") ?? "train").ToLowerInvariant();
            float x = p.QF("x", 0), z = p.QF("z", 0), radius = p.QF("radius", 1e6f);
            int limit = (int)math.clamp(p.QF("limit", 100), 1, 2000);
            bool problemsOnly = p.Q("problems", "0") == "1" || p.Q("problems", "") == "true";
            ComponentType primary;
            switch (kind)
            {
                case "train": case "tram": case "subway": primary = ComponentType.ReadOnly<GV.Train>(); break;
                case "ship": primary = ComponentType.ReadOnly<GV.Watercraft>(); break;
                case "airplane": primary = ComponentType.ReadOnly<GV.Airplane>(); break;
                case "cargo": primary = ComponentType.ReadOnly<GV.CargoTransport>(); break;
                case "passenger": case "bus": primary = ComponentType.ReadOnly<GV.PublicTransport>(); break;
                case "truck": primary = ComponentType.ReadOnly<GV.DeliveryTruck>(); break;
                case "all": primary = ComponentType.ReadOnly<GV.Vehicle>(); break;
                default: return new ApiError("kind must be train|tram|subway|ship|airplane|bus|truck|cargo|passenger|all");
            }
            var icons = IconsByOwner();
            var q = em.CreateEntityQuery(primary, ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var rows = new List<KeyValuePair<float, Dictionary<string, object>>>();
            var byKind = new Dictionary<string, int>(); var byRole = new Dictionary<string, int>(); var byPath = new Dictionary<string, int>(); var byProblem = new Dictionary<string, int>();
            int total = 0, withProblems = 0, moving = 0, parkedN = 0;
            var center = new float2(x, z);
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (!IsLead(em, e)) continue;
                    if (kind == "all" && em.HasComponent<GV.ParkedCar>(e)) continue; // tens of thousands of parked cars
                    var vk = VehicleKind(e);
                    if ((kind == "train" || kind == "tram" || kind == "subway" || kind == "bus") && vk != kind) continue;
                    float dist = 0;
                    if (em.HasComponent<ObjTransform>(e)) dist = math.distance(em.GetComponentData<ObjTransform>(e).m_Position.xz, center);
                    if (dist > radius) continue;
                    total++;
                    var probs = new List<string>();
                    var d = DescribeVehicle(e, icons, probs);
                    Count(byKind, vk); Count(byRole, (string)d["role"]);
                    Count(byPath, (string)((Dictionary<string, object>)d["path"])["state"]);
                    if (d.ContainsKey("parked")) parkedN++; else if ((double)d["speed"] > 0.5) moving++;
                    if (probs.Count > 0) { withProblems++; foreach (var pr in probs) Count(byProblem, pr); }
                    if (problemsOnly && probs.Count == 0) continue;
                    if (radius < 1e5f) d["distance"] = Math.Round(dist, 0);
                    rows.Add(new KeyValuePair<float, Dictionary<string, object>>(radius < 1e5f ? dist : (probs.Count > 0 ? 0 : 1), d));
                }
            rows.Sort((a, b) => a.Key.CompareTo(b.Key));
            return new Dictionary<string, object>
            {
                { "kind", kind }, { "count", total }, { "moving", moving }, { "parked", parkedN }, { "withProblems", withProblems },
                { "byKind", Ordered(byKind) }, { "byRole", Ordered(byRole) }, { "byPathState", Ordered(byPath) }, { "byProblem", Ordered(byProblem) },
                { "listed", Math.Min(rows.Count, limit) },
                { "vehicles", rows.Take(limit).Select(r => (object)r.Value).ToList() },
                { "note", "one row per vehicle (trains: the lead car, cars/load summed over the train). path.state: ok | pending (pathfinding queued) | failed (no path: the game shows Pathfind Failed) | stuck. dummyTraffic = visual through-traffic from outside connections (carries no city cargo)." },
            };
        }

        // ------------------------------------------------------------------ track graph

        /// <summary>
        /// Train track network: undirected components of net nodes over TrainTrack edges (is there any track connection),
        /// and the directed lane graph of train track lanes (can a train drive it in the allowed directions). Lanes follow
        /// the pathfinder's rules (PathUtils.GetTrackDriveSpecification): one-way from start to end node unless Twoway;
        /// a train cannot turn back on the lane it came on (no reversing except at stops).
        /// </summary>
        sealed class TrackGraph
        {
            public readonly Dictionary<Entity, Entity> Parent = new Dictionary<Entity, Entity>();
            public readonly Dictionary<Entity, int> Degree = new Dictionary<Entity, int>();
            public readonly List<Entity> Lanes = new List<Entity>();
            public readonly List<GPF.PathNode> Start = new List<GPF.PathNode>(), End = new List<GPF.PathNode>();
            public readonly List<bool> Twoway = new List<bool>();
            public readonly Dictionary<Entity, int> LaneIndex = new Dictionary<Entity, int>();
            public readonly Dictionary<GPF.PathNode, List<int>> ByStart = new Dictionary<GPF.PathNode, List<int>>(), ByEnd = new Dictionary<GPF.PathNode, List<int>>();
            public readonly Dictionary<Entity, List<int>> LanesByOwner = new Dictionary<Entity, List<int>>();
            public int EdgeCount;

            public Entity Find(Entity n)
            {
                if (!Parent.TryGetValue(n, out var p)) { Parent[n] = n; return n; }
                while (p != n) { var gp = Parent.TryGetValue(p, out var x) ? x : p; Parent[n] = gp; n = p; p = gp; }
                return n;
            }
            public void Union(Entity a, Entity b) { var ra = Find(a); var rb = Find(b); if (ra != rb) Parent[ra] = rb; }

            static void Add(Dictionary<GPF.PathNode, List<int>> d, GPF.PathNode k, int i) { if (!d.TryGetValue(k, out var l)) d[k] = l = new List<int>(); l.Add(i); }
            public void AddLane(Entity lane, Entity owner, GPF.PathNode s, GPF.PathNode e, bool twoway)
            {
                int i = Lanes.Count;
                Lanes.Add(lane); Start.Add(s); End.Add(e); Twoway.Add(twoway); LaneIndex[lane] = i;
                Add(ByStart, s, i); Add(ByEnd, e, i);
                if (!LanesByOwner.TryGetValue(owner, out var l)) LanesByOwner[owner] = l = new List<int>();
                l.Add(i);
            }

            /// <summary>Directed reachability: from any of the start lanes (both directions on twoway lanes) to any target lane.</summary>
            public bool Reach(HashSet<int> from, HashSet<int> to, out int visitedStates)
            {
                visitedStates = 0;
                if (from.Count == 0 || to.Count == 0) return false;
                var seen = new HashSet<long>();
                var queue = new Queue<long>();
                foreach (var i in from)
                {
                    if (to.Contains(i)) return true;
                    long f = (long)i * 2; if (seen.Add(f)) queue.Enqueue(f);
                    if (Twoway[i]) { long r = (long)i * 2 + 1; if (seen.Add(r)) queue.Enqueue(r); }
                }
                while (queue.Count > 0 && seen.Count < 400000)
                {
                    long st = queue.Dequeue();
                    int i = (int)(st >> 1); bool rev = (st & 1) != 0;
                    var exit = rev ? Start[i] : End[i];
                    if (ByStart.TryGetValue(exit, out var fw))
                        foreach (var j in fw)
                        {
                            if (j == i) continue;
                            if (to.Contains(j)) { visitedStates = seen.Count; return true; }
                            long s2 = (long)j * 2; if (seen.Add(s2)) queue.Enqueue(s2);
                        }
                    if (ByEnd.TryGetValue(exit, out var bw))
                        foreach (var j in bw)
                        {
                            if (j == i || !Twoway[j]) continue;
                            if (to.Contains(j)) { visitedStates = seen.Count; return true; }
                            long s2 = (long)j * 2 + 1; if (seen.Add(s2)) queue.Enqueue(s2);
                        }
                }
                visitedStates = seen.Count;
                return false;
            }
        }

        TrackGraph BuildTrackGraph(GN.TrackTypes type = GN.TrackTypes.Train)
        {
            var g = new TrackGraph();
            var eq = em.CreateEntityQuery(ComponentType.ReadOnly<GN.Edge>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = eq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    bool match = type == GN.TrackTypes.Train ? em.HasComponent<GN.TrainTrack>(e) : type == GN.TrackTypes.Tram ? em.HasComponent<GN.TramTrack>(e) : em.HasComponent<GN.SubwayTrack>(e);
                    if (!match) continue;
                    var ed = em.GetComponentData<GN.Edge>(e);
                    g.EdgeCount++;
                    g.Union(ed.m_Start, ed.m_End);
                    g.Degree[ed.m_Start] = (g.Degree.TryGetValue(ed.m_Start, out var a) ? a : 0) + 1;
                    g.Degree[ed.m_End] = (g.Degree.TryGetValue(ed.m_End, out var b) ? b : 0) + 1;
                    if (em.HasBuffer<GN.ConnectedNode>(e))
                    {
                        var cn = em.GetBuffer<GN.ConnectedNode>(e, true);
                        for (int i = 0; i < cn.Length; i++) g.Union(ed.m_Start, cn[i].m_Node);
                    }
                }
            var lq = em.CreateEntityQuery(ComponentType.ReadOnly<GN.Lane>(), ComponentType.ReadOnly<GN.TrackLane>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.ReadOnly<Owner>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = lq.ToEntityArray(Allocator.Temp))
                foreach (var l in arr)
                {
                    var pf = em.GetComponentData<GP.PrefabRef>(l).m_Prefab;
                    if (!em.HasComponent<GP.TrackLaneData>(pf) || (em.GetComponentData<GP.TrackLaneData>(pf).m_TrackTypes & type) == 0) continue;
                    var ln = em.GetComponentData<GN.Lane>(l);
                    var tl = em.GetComponentData<GN.TrackLane>(l);
                    g.AddLane(l, em.GetComponentData<Owner>(l).m_Owner, ln.m_StartNode, ln.m_EndNode, (tl.m_Flags & GN.TrackLaneFlags.Twoway) != 0);
                }
            return g;
        }

        /// <summary>Net nodes and edges of a building, its upgrades and their sub-objects (station tracks).</summary>
        void CollectSubNets(Entity b, HashSet<Entity> nets, int depth = 0)
        {
            if (depth > 3 || b == Entity.Null || !em.Exists(b)) return;
            if (em.HasBuffer<GN.SubNet>(b))
            {
                var sn = em.GetBuffer<GN.SubNet>(b, true);
                for (int i = 0; i < sn.Length; i++) if (em.Exists(sn[i].m_SubNet) && !em.HasComponent<Deleted>(sn[i].m_SubNet)) nets.Add(sn[i].m_SubNet);
            }
            if (em.HasBuffer<GB.InstalledUpgrade>(b))
            {
                var iu = em.GetBuffer<GB.InstalledUpgrade>(b, true);
                for (int i = 0; i < iu.Length; i++) CollectSubNets(iu[i].m_Upgrade, nets, depth + 1);
            }
        }

        /// <summary>Transport stops of a building (sub-objects, also of upgrades), or the outside connection object itself.</summary>
        List<Entity> StopsOf(Entity b, int depth = 0)
        {
            var l = new List<Entity>();
            if (b == Entity.Null || !em.Exists(b) || depth > 3) return l;
            if (depth == 0 && em.HasComponent<GR.TransportStop>(b)) l.Add(b);
            if (em.HasBuffer<GO.SubObject>(b))
            {
                var so = em.GetBuffer<GO.SubObject>(b, true);
                for (int i = 0; i < so.Length; i++)
                {
                    var s = so[i].m_SubObject;
                    if (!em.Exists(s) || em.HasComponent<Deleted>(s)) continue;
                    if (em.HasComponent<GR.TransportStop>(s)) l.Add(s);
                    else if (em.HasBuffer<GO.SubObject>(s)) l.AddRange(StopsOf(s, depth + 1));
                }
            }
            if (em.HasBuffer<GB.InstalledUpgrade>(b))
            {
                var iu = em.GetBuffer<GB.InstalledUpgrade>(b, true);
                for (int i = 0; i < iu.Length; i++) l.AddRange(StopsOf(iu[i].m_Upgrade, depth + 1));
            }
            return l.Distinct().ToList();
        }

        /// <summary>Lines (routes) connected to a stop through its ConnectedRoute buffer.</summary>
        List<Entity> RoutesAtStop(Entity stop)
        {
            var l = new List<Entity>();
            if (!em.HasBuffer<GR.ConnectedRoute>(stop)) return l;
            var cr = em.GetBuffer<GR.ConnectedRoute>(stop, true);
            for (int i = 0; i < cr.Length; i++)
            {
                var wp = cr[i].m_Waypoint;
                if (em.Exists(wp) && em.HasComponent<Owner>(wp))
                {
                    var r = em.GetComponentData<Owner>(wp).m_Owner;
                    if (IsLive(r) && !l.Contains(r)) l.Add(r);
                }
            }
            return l;
        }

        Dictionary<string, object> LineRef(Entity r)
        {
            var pf = em.GetComponentData<GP.PrefabRef>(r).m_Prefab;
            var d = new Dictionary<string, object> { { "id", Id(r) }, { "prefab", PrefabName(pf) } };
            if (em.HasComponent<GR.RouteNumber>(r)) d["number"] = em.GetComponentData<GR.RouteNumber>(r).m_Number;
            if (em.HasComponent<GP.TransportLineData>(pf)) d["cargo"] = em.GetComponentData<GP.TransportLineData>(pf).m_CargoTransport;
            if (em.HasBuffer<GR.RouteVehicle>(r)) d["vehicles"] = em.GetBuffer<GR.RouteVehicle>(r, true).Length;
            return d;
        }

        /// <summary>Net node (and its train lanes) of a rail outside connection object: walks Owner, else the nearest net OutsideConnection node.</summary>
        Entity OutsideConnectionNode(Entity oc, TrackGraph g)
        {
            var e = oc;
            for (int i = 0; i < 4 && e != Entity.Null && em.Exists(e); i++)
            {
                if (em.HasComponent<GN.Node>(e)) return e;
                if (em.HasComponent<GN.Edge>(e))
                {
                    var ed = em.GetComponentData<GN.Edge>(e);
                    return em.HasComponent<GN.OutsideConnection>(ed.m_End) ? ed.m_End : ed.m_Start;
                }
                if (!em.HasComponent<Owner>(e)) break;
                e = em.GetComponentData<Owner>(e).m_Owner;
            }
            if (!em.HasComponent<ObjTransform>(oc)) return Entity.Null;
            var pos = em.GetComponentData<ObjTransform>(oc).m_Position;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GN.Node>(), ComponentType.ReadOnly<GN.OutsideConnection>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            Entity best = Entity.Null; float bd = 150f;
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var n in arr)
                {
                    if (!g.Degree.ContainsKey(n)) continue;
                    var d = math.distance(em.GetComponentData<GN.Node>(n).m_Position.xz, pos.xz);
                    if (d < bd) { bd = d; best = n; }
                }
            return best;
        }

        HashSet<int> LanesOfNets(TrackGraph g, IEnumerable<Entity> nets)
        {
            var s = new HashSet<int>();
            foreach (var n in nets) if (g.LanesByOwner.TryGetValue(n, out var l)) foreach (var i in l) s.Add(i);
            return s;
        }

        HashSet<int> LanesAtNode(TrackGraph g, Entity node)
        {
            var nets = new List<Entity> { node };
            if (em.HasBuffer<GN.ConnectedEdge>(node))
            {
                var ce = em.GetBuffer<GN.ConnectedEdge>(node, true);
                for (int i = 0; i < ce.Length; i++) nets.Add(ce[i].m_Edge);
            }
            return LanesOfNets(g, nets);
        }

        /// <summary>Train-track test between a station (its track lanes) and a rail outside connection.</summary>
        Dictionary<string, object> TrackTest(TrackGraph g, HashSet<Entity> stationNets, HashSet<int> stationLanes, Entity oc, Entity ocNode)
        {
            var d = new Dictionary<string, object> { { "outsideConnection", Id(oc) }, { "outsideConnectionPrefab", PrefabOf(oc) } };
            if (ocNode == Entity.Null) { d["verdict"] = "could not find the track node of this outside connection"; return d; }
            var ocRoot = g.Find(ocNode);
            bool same = stationNets.Any(n => em.HasComponent<GN.Node>(n) && g.Degree.ContainsKey(n) && g.Find(n) == ocRoot)
                        || stationNets.Any(n => em.HasComponent<GN.Edge>(n) && g.Degree.ContainsKey(em.GetComponentData<GN.Edge>(n).m_Start) && g.Find(em.GetComponentData<GN.Edge>(n).m_Start) == ocRoot);
            d["trackConnected"] = same;
            var ocLanes = LanesAtNode(g, ocNode);
            bool inbound = same && g.Reach(ocLanes, stationLanes, out var v1);
            bool outbound = same && g.Reach(stationLanes, ocLanes, out var v2);
            d["inbound"] = inbound;   // outside connection -> into the station
            d["outbound"] = outbound; // station -> out to the outside connection
            if (!same) d["verdict"] = "no track connection: the station's tracks and this outside connection are separate track networks";
            else if (stationLanes.Count == 0) d["verdict"] = "the station has no train track lanes";
            else if (inbound && outbound) d["verdict"] = "ok: the track layout lets a train drive in from this connection and back out (the game's pathfinder decides the rest: test a cargo route with POST /line dryRun)";
            else if (!inbound && !outbound) d["verdict"] = "tracks touch but no train can drive between them in the allowed directions (one-way track pointing the wrong way, or the only way needs reversing outside a stop)";
            else if (!inbound) d["verdict"] = "a train can leave the station towards this connection, but none can arrive from it (one-way track against the inbound direction, or arriving needs a reversal)";
            else d["verdict"] = "a train can arrive from this connection but not leave towards it (a one-ended terminal needs the train to reverse at the stop, or a one-way track blocks the way out)";
            return d;
        }

        // ------------------------------------------------------------------ /freight

        /// <summary>City cargo statistics (Statistics panel series: 32 samples per in-game day), last n samples.</summary>
        Dictionary<string, object> CargoStats(int n)
        {
            var css = Sys<CityStatisticsSystem>();
            css.CompleteWriters();
            var lookup = css.GetLookup();
            var res = new Dictionary<string, object>();
            foreach (var t in new[] { GCity.StatisticType.CargoCountTruck, GCity.StatisticType.CargoCountTrain, GCity.StatisticType.CargoCountShip, GCity.StatisticType.CargoCountAirplane })
            {
                if (!lookup.TryGetValue(new CityStatisticsSystem.StatisticsKey(t, 0), out var ent)) { res[t.ToString()] = null; continue; }
                var s = StatSeries(ent, n);
                res[t.ToString()] = n <= 1 ? (object)(s.Count > 0 ? s[s.Count - 1] : 0) : new Dictionary<string, object> { { "current", s.Count > 0 ? s[s.Count - 1] : 0 }, { "history", s } };
            }
            return res;
        }

        Dictionary<string, object> ResourceMap(DynamicBuffer<GE.Resources> rb)
        {
            var m = new Dictionary<string, int>();
            for (int i = 0; i < rb.Length; i++) if (rb[i].m_Amount != 0) Count(m, ResName(rb[i].m_Resource), rb[i].m_Amount);
            return Ordered(m);
        }

        /// <summary>StorageTransferRequest buffer: pending transfers by resource and direction, plus the first few targets.</summary>
        Dictionary<string, object> TransferRequests(Entity e)
        {
            if (!em.HasBuffer<GC.StorageTransferRequest>(e)) return null;
            var buf = em.GetBuffer<GC.StorageTransferRequest>(e, true);
            if (buf.Length == 0) return new Dictionary<string, object> { { "count", 0 } };
            var inn = new Dictionary<string, int>(); var outt = new Dictionary<string, int>(); var via = new Dictionary<string, int>();
            var list = new List<object>();
            for (int i = 0; i < buf.Length; i++)
            {
                var r = buf[i];
                bool incoming = (r.m_Flags & GC.StorageTransferFlags.Incoming) != 0;
                Count(incoming ? inn : outt, ResName(r.m_Resource), r.m_Amount);
                Count(via, FlagNames(r.m_Flags & ~GC.StorageTransferFlags.Incoming));
                if (list.Count < 8)
                    list.Add(new Dictionary<string, object> { { "resource", ResName(r.m_Resource) }, { "amount", r.m_Amount }, { "flags", FlagNames(r.m_Flags) }, { "target", Id(r.m_Target) }, { "targetPrefab", PrefabOf(TopBuilding(r.m_Target) != Entity.Null ? TopBuilding(r.m_Target) : r.m_Target) } });
            }
            return new Dictionary<string, object> { { "count", buf.Length }, { "incoming", Ordered(inn) }, { "outgoing", Ordered(outt) }, { "via", Ordered(via) }, { "first", list } };
        }

        Dictionary<string, object> OwnedVehicles(Entity e, Dictionary<Entity, List<IconHit>> icons, int maxList)
        {
            if (!em.HasBuffer<GV.OwnedVehicle>(e)) return null;
            var ov = em.GetBuffer<GV.OwnedVehicle>(e, true);
            var byKind = new Dictionary<string, int>(); var byState = new Dictionary<string, int>();
            var list = new List<object>();
            for (int i = 0; i < ov.Length; i++)
            {
                var v = ov[i].m_Vehicle;
                if (!em.Exists(v) || em.HasComponent<Deleted>(v)) continue;
                var k = VehicleKind(v) + "/" + VehicleRole(v);
                Count(byKind, k);
                var probs = new List<string>();
                var d = DescribeVehicle(v, icons, probs);
                Count(byState, d.ContainsKey("parked") ? "parked" : (string)((Dictionary<string, object>)d["path"])["state"] == "ok" ? (d.TryGetValue("state", out var s) && s is string ss && ss.Length > 0 ? ss : "driving") : "path " + ((Dictionary<string, object>)d["path"])["state"]);
                if (list.Count < maxList && (VehicleKind(v) != "truck" || probs.Count > 0)) list.Add(d);
            }
            return new Dictionary<string, object> { { "count", ov.Length }, { "byKind", Ordered(byKind) }, { "byState", Ordered(byState) }, { "vehicles", list } };
        }

        /// <summary>
        /// GET /freight[?trains=60&amp;n=1&amp;station=id]: why cargo does (not) move by train/ship/plane. summary (city cargo
        /// statistics per mode, cargo lines, trains), stations (cargo terminals, rail yards, harbours, airports, train
        /// stations: stops, lines, owned and inbound vehicles, stored cargo, pending transfers, track ends, track test
        /// to every rail outside connection), outsideConnections (rail/ship/air: owned vehicles, stored cargo,
        /// transfers, lines), trains (every train on the map), cargoVehicles (cargo ships/planes). station=id limits
        /// the stations list to one building.
        /// </summary>
        object Freight(Pending p)
        {
            int trainLimit = (int)math.clamp(p.QF("trains", 60), 0, 1000);
            int n = (int)math.clamp(p.QF("n", 1), 1, 256);
            Entity only = Entity.Null;
            if (p.Q("station") != null)
            {
                var bq = em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>());
                only = Resolve(p.Q("station"), bq);
                if (only == Entity.Null) return new ApiError("station not found: " + p.Q("station"), 404);
                var top = TopBuilding(only);
                if (top != Entity.Null) only = top;
            }
            var icons = IconsByOwner();
            var g = BuildTrackGraph();
            var hints = new List<object>();

            // ---- vehicles: inbound counts per building, trains, cargo ships/planes
            var inbound = new Dictionary<Entity, Dictionary<string, int>>();
            var trains = new List<object>();
            var cargoVehicles = new List<object>();
            int trainCount = 0, cargoTrains = 0, paxTrains = 0, trainsPathFailed = 0, trainsMoving = 0, dummyTrains = 0;
            var tq = em.CreateEntityQuery(ComponentType.ReadOnly<GV.Vehicle>(), ComponentType.ReadOnly<Target>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = tq.ToEntityArray(Allocator.Temp))
                foreach (var v in arr)
                {
                    if (!IsLead(em, v)) continue;
                    var t = em.GetComponentData<Target>(v).m_Target;
                    var kind = VehicleKind(v);
                    if (t != Entity.Null && em.Exists(t))
                    {
                        var tb = TopBuilding(em.HasComponent<GR.Connected>(t) ? em.GetComponentData<GR.Connected>(t).m_Connected : t);
                        if (tb != Entity.Null)
                        {
                            if (!inbound.TryGetValue(tb, out var c)) inbound[tb] = c = new Dictionary<string, int>();
                            Count(c, kind + "/" + VehicleRole(v));
                        }
                    }
                }
            var allTrains = em.CreateEntityQuery(ComponentType.ReadOnly<GV.Train>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = allTrains.ToEntityArray(Allocator.Temp))
                foreach (var v in arr)
                {
                    if (!IsLead(em, v) || VehicleKind(v) != "train") continue;
                    trainCount++;
                    var probs = new List<string>();
                    var d = DescribeVehicle(v, icons, probs);
                    if ((string)d["role"] == "cargo") cargoTrains++; else if ((string)d["role"] == "passenger") paxTrains++;
                    if (d.ContainsKey("dummyTraffic")) dummyTrains++;
                    if ((string)((Dictionary<string, object>)d["path"])["state"] == "failed") trainsPathFailed++;
                    if ((double)d["speed"] > 0.5) trainsMoving++;
                    if (trains.Count < trainLimit) trains.Add(d);
                }
            var cq = em.CreateEntityQuery(ComponentType.ReadOnly<GV.CargoTransport>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<GV.Train>());
            int cargoShips = 0, cargoPlanes = 0;
            using (var arr = cq.ToEntityArray(Allocator.Temp))
                foreach (var v in arr)
                {
                    if (!IsLead(em, v)) continue;
                    var k = VehicleKind(v);
                    if (k == "ship") cargoShips++; else if (k == "airplane") cargoPlanes++;
                    if (cargoVehicles.Count < 40) cargoVehicles.Add(DescribeVehicle(v, icons, new List<string>()));
                }

            // ---- lines: cargo routes
            var cargoLines = new List<object>();
            foreach (var r in AllRoutes())
            {
                var pf = em.GetComponentData<GP.PrefabRef>(r).m_Prefab;
                if (!em.HasComponent<GP.TransportLineData>(pf) || !em.GetComponentData<GP.TransportLineData>(pf).m_CargoTransport) continue;
                var ld = LineRef(r);
                var stops = new List<object>();
                if (em.HasBuffer<GR.RouteWaypoint>(r))
                {
                    var wps = em.GetBuffer<GR.RouteWaypoint>(r, true);
                    for (int i = 0; i < wps.Length; i++)
                        if (em.HasComponent<GR.Connected>(wps[i].m_Waypoint))
                        {
                            var s = em.GetComponentData<GR.Connected>(wps[i].m_Waypoint).m_Connected;
                            var b = TopBuilding(s);
                            stops.Add(Id(s) + (b != Entity.Null ? " (" + PrefabOf(b) + " " + Id(b) + ")" : ""));
                        }
                }
                ld["stops"] = stops;
                ld["broken"] = LineSegments(r, null, out var failed, out var pending) && failed > 0;
                cargoLines.Add(ld);
            }

            // ---- outside connections (rail, ship, air)
            var ocs = new List<object>();
            var trainOcs = new List<KeyValuePair<Entity, Entity>>(); // (oc object, track node)
            var oq = em.CreateEntityQuery(ComponentType.ReadOnly<GO.OutsideConnection>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = oq.ToEntityArray(Allocator.Temp))
                foreach (var oc in arr)
                {
                    var pf = em.GetComponentData<GP.PrefabRef>(oc).m_Prefab;
                    var type = em.HasComponent<GP.TransportStopData>(pf) ? em.GetComponentData<GP.TransportStopData>(pf).m_TransportType : GP.TransportType.None;
                    if (type != GP.TransportType.Train && type != GP.TransportType.Ship && type != GP.TransportType.Airplane) continue;
                    var d = new Dictionary<string, object> { { "id", Id(oc) }, { "prefab", PrefabName(pf) }, { "type", type.ToString() }, { "pos", F2(em.GetComponentData<ObjTransform>(oc).m_Position) } };
                    if (em.HasComponent<GR.TransportStop>(oc)) d["active"] = (em.GetComponentData<GR.TransportStop>(oc).m_Flags & GR.StopFlags.Active) != 0;
                    var lines = RoutesAtStop(oc);
                    d["lines"] = lines.Select(r => (object)LineRef(r)).ToList();
                    var ov = OwnedVehicles(oc, icons, 6);
                    if (ov != null) d["ownedVehicles"] = ov;
                    if (inbound.TryGetValue(oc, out var ib)) d["inboundVehicles"] = Ordered(ib);
                    if (em.HasBuffer<GE.Resources>(oc)) d["stored"] = ResourceMap(em.GetBuffer<GE.Resources>(oc, true));
                    var tr = TransferRequests(oc);
                    if (tr != null) d["transferRequests"] = tr;
                    if (type == GP.TransportType.Train)
                    {
                        var node = OutsideConnectionNode(oc, g);
                        d["trackNode"] = node == Entity.Null ? null : Id(node);
                        if (node != Entity.Null)
                        {
                            d["trackNodeDegree"] = g.Degree.TryGetValue(node, out var deg) ? deg : 0;
                            d["trackLanes"] = LanesAtNode(g, node).Count;
                        }
                        trainOcs.Add(new KeyValuePair<Entity, Entity>(oc, node));
                    }
                    ocs.Add(d);
                }

            // ---- stations
            var stations = new List<object>();
            var bq2 = em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            int cargoStationsNoRoute = 0;
            using (var arr = bq2.ToEntityArray(Allocator.Temp))
                foreach (var b in arr)
                {
                    if (only != Entity.Null && b != only) continue;
                    if (em.HasComponent<Owner>(b) && em.HasComponent<GB.Building>(em.GetComponentData<Owner>(b).m_Owner)) continue; // upgrade: reported with its main building
                    var pf = em.GetComponentData<GP.PrefabRef>(b).m_Prefab;
                    bool cargoStation = em.HasComponent<GB.CargoTransportStation>(b);
                    var depotType = em.HasComponent<GB.TransportDepot>(b) && em.HasComponent<GP.TransportDepotData>(pf) ? em.GetComponentData<GP.TransportDepotData>(pf).m_TransportType : GP.TransportType.None;
                    bool depot = depotType == GP.TransportType.Train || depotType == GP.TransportType.Ship || depotType == GP.TransportType.Airplane;
                    List<Entity> stops = null;
                    bool trainStops = false, shipOrAir = false;
                    if (cargoStation || depot || em.HasComponent<GB.TransportStation>(b))
                    {
                        stops = StopsOf(b);
                        foreach (var s in stops)
                        {
                            var spf = em.GetComponentData<GP.PrefabRef>(s).m_Prefab;
                            if (!em.HasComponent<GP.TransportStopData>(spf)) continue;
                            var tt = em.GetComponentData<GP.TransportStopData>(spf).m_TransportType;
                            if (tt == GP.TransportType.Train) trainStops = true;
                            if (tt == GP.TransportType.Ship || tt == GP.TransportType.Airplane) shipOrAir = true;
                        }
                    }
                    if (!cargoStation && !depot && !trainStops && !shipOrAir) continue;
                    var roles = new List<object>();
                    if (cargoStation) roles.Add("cargo station");
                    if (depot) roles.Add(depotType + " depot");
                    if (!cargoStation && (trainStops || shipOrAir)) roles.Add("passenger station");
                    var d = new Dictionary<string, object> { { "id", Id(b) }, { "prefab", PrefabName(pf) }, { "roles", roles }, { "pos", F2(em.GetComponentData<ObjTransform>(b).m_Position) } };
                    d["roadConnected"] = em.GetComponentData<GB.Building>(b).m_RoadEdge != Entity.Null;
                    if (em.HasBuffer<GB.Efficiency>(b)) d["efficiency"] = Math.Round(GB.BuildingUtils.GetEfficiency(em.GetBuffer<GB.Efficiency>(b, true)), 2);
                    var bic = IconList(icons, b);
                    if (bic != null) d["icons"] = bic;
                    if (em.HasBuffer<GB.InstalledUpgrade>(b))
                    {
                        var iu = em.GetBuffer<GB.InstalledUpgrade>(b, true);
                        if (iu.Length > 0) d["upgrades"] = Enumerable.Range(0, iu.Length).Select(i => (object)(PrefabOf(iu[i].m_Upgrade) + " " + Id(iu[i].m_Upgrade))).ToList();
                    }

                    // stops and lines
                    var lineSet = new List<Entity>();
                    var stopRows = new List<object>();
                    foreach (var s in stops ?? new List<Entity>())
                    {
                        var spf = em.GetComponentData<GP.PrefabRef>(s).m_Prefab;
                        var sd = new Dictionary<string, object> { { "id", Id(s) }, { "prefab", PrefabName(spf) } };
                        if (em.HasComponent<GP.TransportStopData>(spf))
                        {
                            var tsd = em.GetComponentData<GP.TransportStopData>(spf);
                            sd["type"] = tsd.m_TransportType.ToString(); sd["cargo"] = tsd.m_CargoTransport; sd["passenger"] = tsd.m_PassengerTransport;
                        }
                        sd["active"] = (em.GetComponentData<GR.TransportStop>(s).m_Flags & GR.StopFlags.Active) != 0;
                        var rs = RoutesAtStop(s);
                        sd["lines"] = rs.Select(Id).Cast<object>().ToList();
                        foreach (var r in rs) if (!lineSet.Contains(r)) lineSet.Add(r);
                        stopRows.Add(sd);
                    }
                    d["stops"] = stopRows;
                    d["lines"] = lineSet.Select(r => (object)LineRef(r)).ToList();
                    int cargoRouteCount = lineSet.Count(r => em.HasComponent<GP.TransportLineData>(em.GetComponentData<GP.PrefabRef>(r).m_Prefab) && em.GetComponentData<GP.TransportLineData>(em.GetComponentData<GP.PrefabRef>(r).m_Prefab).m_CargoTransport);
                    d["cargoRoutes"] = cargoRouteCount;

                    // vehicles
                    var ov = OwnedVehicles(b, icons, 10);
                    if (ov != null) d["ownedVehicles"] = ov;
                    if (inbound.TryGetValue(b, out var ib)) d["inboundVehicles"] = Ordered(ib);
                    if (em.HasComponent<GC.TransportCompanyData>(pf)) d["maxTransports"] = em.GetComponentData<GC.TransportCompanyData>(pf).m_MaxTransports;
                    if (depot)
                    {
                        var td = em.GetComponentData<GB.TransportDepot>(b);
                        var tdd = em.GetComponentData<GP.TransportDepotData>(pf);
                        d["depot"] = new Dictionary<string, object> { { "type", tdd.m_TransportType.ToString() }, { "capacity", tdd.m_VehicleCapacity }, { "availableVehicles", (int)td.m_AvailableVehicles }, { "flags", FlagNames(td.m_Flags) } };
                    }

                    // storage
                    if (em.HasBuffer<GE.Resources>(b)) d["stored"] = ResourceMap(em.GetBuffer<GE.Resources>(b, true));
                    if (em.HasComponent<GP.StorageCompanyData>(pf)) d["tradedResources"] = FlagNames(em.GetComponentData<GP.StorageCompanyData>(pf).m_StoredResources);
                    if (em.HasComponent<GC.StorageLimitData>(pf)) d["storageLimit"] = em.GetComponentData<GC.StorageLimitData>(pf).m_Limit;
                    var tr = TransferRequests(b);
                    if (tr != null) d["transferRequests"] = tr;
                    if (em.HasComponent<GC.StorageCompany>(b))
                    {
                        var lp = em.GetComponentData<GC.StorageCompany>(b).m_LastTradePartner;
                        if (lp != Entity.Null) d["lastTradePartner"] = EntityRef(TopBuilding(lp) != Entity.Null ? TopBuilding(lp) : lp);
                    }

                    // tracks
                    var nets = new HashSet<Entity>();
                    CollectSubNets(b, nets);
                    var trackEdges = nets.Where(x => em.HasComponent<GN.Edge>(x) && em.HasComponent<GN.TrainTrack>(x)).ToList();
                    if (trackEdges.Count > 0 || trainStops)
                    {
                        var tnodes = new HashSet<Entity>();
                        foreach (var e in trackEdges) { var ed = em.GetComponentData<GN.Edge>(e); tnodes.Add(ed.m_Start); tnodes.Add(ed.m_End); }
                        foreach (var x in nets) if (em.HasComponent<GN.Node>(x) && g.Degree.ContainsKey(x)) tnodes.Add(x);
                        var allNets = new HashSet<Entity>(nets); foreach (var x in tnodes) allNets.Add(x);
                        var ends = new List<object>();
                        foreach (var nd in tnodes)
                        {
                            if (!g.Degree.TryGetValue(nd, out var deg) || deg != 1 || !em.HasComponent<GN.Node>(nd)) continue;
                            bool ownedByStation = em.HasComponent<Owner>(nd) && TopBuilding(nd) == b;
                            ends.Add(new Dictionary<string, object> { { "node", Id(nd) }, { "pos", F2(em.GetComponentData<GN.Node>(nd).m_Position) }, { "partOfStation", ownedByStation } });
                        }
                        // "Track Not Connected" icons on the station's track lanes (the game's own verdict on open track ends)
                        var notConnected = new List<object>();
                        foreach (var e in trackEdges)
                            if (em.HasBuffer<GN.SubLane>(e))
                            {
                                var sl = em.GetBuffer<GN.SubLane>(e, true);
                                for (int i = 0; i < sl.Length; i++)
                                    if (icons.TryGetValue(sl[i].m_SubLane, out var hits))
                                        foreach (var h in hits) if (h.Name.IndexOf("Not Connected", StringComparison.OrdinalIgnoreCase) >= 0) notConnected.Add(new Dictionary<string, object> { { "icon", h.Name }, { "pos", F2(h.Pos) }, { "edge", Id(e) } });
                            }
                        var stationLanes = LanesOfNets(g, allNets);
                        var tracks = new Dictionary<string, object>
                        {
                            { "trackEdges", trackEdges.Count }, { "trackLanes", stationLanes.Count },
                            { "trackEnds", ends }, { "notConnectedIcons", notConnected },
                            { "note", "trackEnds = track nodes with one train track: a buffer stop inside the station (partOfStation, normal for a terminus) or an open end where an outside track should join" },
                        };
                        var tests = new List<object>();
                        foreach (var kv in trainOcs) tests.Add(TrackTest(g, allNets, stationLanes, kv.Key, kv.Value));
                        tracks["toOutsideConnections"] = tests;
                        d["tracks"] = tracks;
                    }

                    // hints
                    var sh = new List<object>();
                    if (cargoStation && trainStops && cargoRouteCount == 0)
                    {
                        cargoStationsNoRoute++;
                        var cargoStop = (stops ?? new List<Entity>()).FirstOrDefault(s => { var spf = em.GetComponentData<GP.PrefabRef>(s).m_Prefab; return em.HasComponent<GP.TransportStopData>(spf) && em.GetComponentData<GP.TransportStopData>(spf).m_CargoTransport && em.GetComponentData<GP.TransportStopData>(spf).m_TransportType == GP.TransportType.Train; });
                        var ocStop = trainOcs.Count > 0 ? Id(trainOcs[0].Key) : "<rail outside connection id>";
                        sh.Add("no Cargo Train Route uses this terminal. Cargo trains are dispatched for cargo routes (TransportDepotAISystem, from a rail yard), and the station's storage job checks its connected cargo routes; without one it trades by truck only. Test one: POST /line {type:\"train\", cargo:true, stops:[\"" + (cargoStop != Entity.Null ? Id(cargoStop) : Id(b)) + "\",\"" + ocStop + "\"], dryRun:true} (pathCheck shows whether trains can run it)");
                    }
                    if (cargoStation && !d.ContainsKey("transferRequests")) { }
                    if (depot && depotType == GP.TransportType.Train && lineSet.Count == 0 && ov != null && (int)ov["count"] == 0) sh.Add("rail yard without vehicles out: it only dispatches trains for train lines/cargo routes that its tracks reach");
                    if (!(bool)d["roadConnected"]) sh.Add("no road connection");
                    if (sh.Count > 0) d["hints"] = sh;
                    stations.Add(d);
                }

            // ---- summary
            var summary = new Dictionary<string, object>
            {
                { "cargoStatistics", CargoStats(n) },
                { "trains", new Dictionary<string, object> { { "total", trainCount }, { "cargo", cargoTrains }, { "passenger", paxTrains }, { "dummyTraffic", dummyTrains }, { "moving", trainsMoving }, { "pathFailed", trainsPathFailed } } },
                { "cargoShips", cargoShips }, { "cargoAirplanes", cargoPlanes },
                { "cargoLines", cargoLines.Count },
                { "trackNetwork", new Dictionary<string, object> { { "trainTrackEdges", g.EdgeCount }, { "trainTrackLanes", g.Lanes.Count } } },
            };
            if (cargoLines.Count == 0) hints.Add("the city has no cargo routes (Cargo Train/Ship/Airplane Route): cargo trains, ships and planes run on cargo routes between cargo stations and/or outside connections; without them all freight goes by truck");
            if (cargoStationsNoRoute > 0) hints.Add(cargoStationsNoRoute + " cargo train terminal(s) without a cargo route (see stations[].hints)");
            summary["hints"] = hints;
            return new Dictionary<string, object>
            {
                { "summary", summary },
                { "cargoLines", cargoLines },
                { "stations", stations },
                { "outsideConnections", ocs },
                { "trains", trains },
                { "cargoVehicles", cargoVehicles },
                { "note", "per-building counts of trains/ships per day are not kept by the game; inboundVehicles = vehicles heading there now, ownedVehicles = vehicles it dispatched. cargoStatistics are the Statistics panel series (n samples, 32 per in-game day). tracks.toOutsideConnections: trackConnected = same track network (undirected), inbound/outbound = a train can drive it on the lane graph in the allowed directions (one-way lanes, no reversing except at stops)." },
            };
        }
    }
}
