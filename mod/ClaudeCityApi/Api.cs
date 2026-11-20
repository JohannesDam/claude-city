using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Colossal.Mathematics;
using Game;
using Game.Assets;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Notifications;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Game.UI.InGame;
using Game.UI.Menu;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using NetNode = Game.Net.Node;
using NetEdge = Game.Net.Edge;
using ObjTransform = Game.Objects.Transform;
using AreaNode = Game.Areas.Node;

namespace ClaudeCityBridge
{
    /// <summary>All HTTP endpoints (hot-reloadable plugin). Every method runs on the game's main thread.</summary>
    public partial class Api : IApiPlugin
    {
        World world;
        EntityManager em => world.EntityManager;
        T Sys<T>() where T : ComponentSystemBase => world.GetOrCreateSystemManaged<T>();

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public string Version => "0.2.0";
        public void Init(World w) { world = w; }

        public object Dispatch(Pending p)
        {
            var busy = LockGate(p);
            if (busy != null) return busy;
            switch (p.Path)
            {
                case "/lock": return p.Method == "POST" ? LockAcquire(p) : LockStatus();
                case "/unlock": return LockRelease(p);
                case "/status": return Status();
                case "/maps": return Maps();
                case "/newgame": return NewGame(p.Body);
                case "/continue": return ContinueLast();
                case "/saves": return Saves();
                case "/load": return LoadSave(p.Body);
                case "/city": return RequireGame() ?? City();
                case "/speed": return RequireGame() ?? Speed(p.Body.I("speed", 1));
                case "/ui": return Ui(p.Body);
                case "/wait": return RequireGame() ?? new WaitOperation(this, p.Body);
                case "/map": return RequireGame() ?? MapText.Render(this, p);
                case "/areainfo": return RequireGame() ?? AreaInfo();
                case "/terrain": return RequireGame() ?? Terrain(p.Body);
                case "/nets": return RequireGame() ?? NetsPaged(p);
                case "/nodes": return RequireGame() ?? Nodes(p);
                case "/nodeupgrade": return RequireGame() ?? NodeUpgrade(p.Body);
                case "/roundabouts": return RequireGame() ?? Roundabouts(p);
                case "/roundabout": return RequireGame() ?? Safety(Roundabout(p.Body), p.Body);
                case "/prefabs": return RequireGame() ?? Prefabs(p.Q("category", "all"), p.Q("filter"), p.Q("unlockedOnly", "false") == "true");
                case "/road": return RequireGame() ?? (WantsFit(p.Body) ? RoadFit(p) : Safety(Road(p.Body), p.Body));
                case "/roadfit": return RequireGame() ?? RoadFit(p);
                case "/zone": return RequireGame() ?? Zone(p.Body);
                case "/building": return RequireGame() ?? Safety(PlaceBuilding(p.Body), p.Body);
                case "/place": return RequireGame() ?? Place(p);
                case "/buildings": return RequireGame() ?? Buildings(p.QF("x", 0), p.QF("z", 0), p.QF("radius", 1e6f), p.Q("kind", "service"), (int)p.QF("offset", 0), (int)p.QF("limit", 300));
                case "/problems": return RequireGame() ?? Problems(p.Q("type"));
                case "/save": return RequireGame() ?? new SaveOperation(this, p.Body.Str("name"));
                case "/taxes": return RequireGame() ?? Taxes(p.Body);
                case "/budget": return RequireGame() ?? ServiceBudget(p.Body);
                case "/milestones": return RequireGame() ?? Milestones();
                case "/zonestats": return RequireGame() ?? ZoneStats();
                case "/upgrade": return RequireGame() ?? Safety(Upgrade(p.Body), p.Body);
                case "/camera": return RequireGame() ?? Camera(p.Body);
                case "/share": return RequireGame() ?? Share(p.Body);
                case "/bulldoze": return RequireGame() ?? Bulldoze(p.Body);
                case "/groundwater": return RequireGame() ?? Groundwater(p);
                case "/pollution": return RequireGame() ?? Pollution(p);
                case "/happiness": return RequireGame() ?? Happiness();
                case "/tiles": return RequireGame() ?? Tiles();
                case "/buytile": return RequireGame() ?? BuyTile(p.Body);
                case "/devtree": return RequireGame() ?? DevTree(p);
                case "/services": return RequireGame() ?? Services(p);
                case "/coverage": return RequireGame() ?? Coverage(p);
                case "/hotspots": return RequireGame() ?? Hotspots(p);
                case "/icontypes": return RequireGame() ?? IconTypes();
                case "/extensions": return RequireGame() ?? Extensions(p);
                case "/extension": return RequireGame() ?? PlaceExtension(p.Body);
                case "/buildingaction": return RequireGame() ?? BuildingAction(p);
                case "/education": return RequireGame() ?? Education(p);
                case "/stuck": return RequireGame() ?? Stuck(p);
                case "/landvalue": return RequireGame() ?? LandValue(p);
                case "/crime": return RequireGame() ?? Crime(p);
                case "/levels": return RequireGame() ?? Levels(p);
                case "/signatures": return RequireGame() ?? Signatures();
                case "/demand": return RequireGame() ?? Demand(p);
                case "/finance": return RequireGame() ?? Finance(p);
                case "/loan": return RequireGame() ?? LoanRequest(p);
                case "/fees": return RequireGame() ?? Fees(p);
                case "/taxes2": return RequireGame() ?? Taxes2(p);
                case "/jobs": return RequireGame() ?? Jobs(p);
                case "/stats": return RequireGame() ?? Stats(p);
                case "/policies": return RequireGame() ?? PolicyList(p);
                case "/policy": return RequireGame() ?? PolicySet(p);
                case "/modifiers": return RequireGame() ?? Modifiers(p);
                case "/findspot": return RequireGame() ?? FindSpot(p);
                case "/freeland": return RequireGame() ?? FreeLand(p);
                case "/unlockinfo": return RequireGame() ?? UnlockInfo(p.Q("name"));
                case "/roadupgrades": return RequireGame() ?? RoadUpgrades();
                case "/roadupgrade": return RequireGame() ?? Safety(RoadUpgrade(p.Body), p.Body);
                case "/transitprefabs": return RequireGame() ?? TransitPrefabs();
                case "/stops": return RequireGame() ?? Stops(p.QF("x", 0), p.QF("z", 0), p.QF("radius", 1e6f), p.Q("type", "bus"));
                case "/busstop": return RequireGame() ?? BusStop(p.Body);
                case "/busline": return RequireGame() ?? BusLine(p.Body);
                case "/lines": return RequireGame() ?? Lines(p.Q("type", "all"));
                case "/line": return RequireGame() ?? BusLine(p.Body);
                case "/line/edit": return RequireGame() ?? LineEdit(p.Body);
                case "/line/delete": return RequireGame() ?? LineDelete(p.Body);
                case "/freight": return RequireGame() ?? Freight(p);
                case "/vehicles": return RequireGame() ?? Vehicles(p);
                case "/companies": return RequireGame() ?? Companies(p);
                case "/resources": return RequireGame() ?? NaturalResources(p);
                case "/extractors": return RequireGame() ?? Extractors(p);
                case "/areas": return RequireGame() ?? AreaList(p);
                case "/area": return RequireGame() ?? AreaCreate(p);
                case "/area/delete": return RequireGame() ?? AreaDelete(p);
                case "/district/name": return RequireGame() ?? DistrictRename(p);
                case "/requests": return RequireGame() ?? Requests(p);
                case "/requests/reset": return RequireGame() ?? RequestsReset(p);
                case "/wind": return RequireGame() ?? WindEndpoint(p);
                case "/tourism": return RequireGame() ?? TourismEndpoint(p);
                case "/deathcare": return RequireGame() ?? DeathcareMeasure(p);
                case "/districts": return RequireGame() ?? DistrictStatsEndpoint(p);
                default: return new ApiError("unknown endpoint " + p.Path, 404);
            }
        }

        object RequireGame()
        {
            var gm = GameManager.instance;
            if (gm == null || gm.gameMode != GameMode.Game) return new ApiError("not in a game (mode=" + gm?.gameMode + ")", 409);
            if (gm.isGameLoading) return new ApiError("game is loading", 409);
            return null;
        }

        // ------------------------------------------------------------------ helpers

        public string PrefabName(Entity prefab) => Util.PrefabName(world, prefab);

        public static string Id(Entity e) => Util.Id(e);

        public World World => world;

        public static object F3(float3 v) => new List<object> { Math.Round(v.x, 1), Math.Round(v.y, 1), Math.Round(v.z, 1) };
        public static object F2(float3 v) => new List<object> { Math.Round(v.x, 1), Math.Round(v.z, 1) };

        static object GetField(object o, string name) => o?.GetType().GetField(name, Any)?.GetValue(o);
        static object Invoke(object o, string name, params object[] args)
        {
            var m = o.GetType().GetMethods(Any).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length);
            if (m == null) throw new MissingMethodException(o.GetType().Name, name);
            return m.Invoke(o, args);
        }

        bool IsLocked(Entity prefab) => em.HasComponent<Locked>(prefab) && em.IsComponentEnabled<Locked>(prefab);

        public bool IsLive(Entity e) => em.Exists(e) && !em.HasComponent<Deleted>(e) && !em.HasComponent<Temp>(e);

        /// <summary>Resolve "index:version" or a bare index (searching the given query).</summary>
        public Entity Resolve(object id, EntityQuery search)
        {
            if (id == null) return Entity.Null;
            var s = id is double d ? ((long)d).ToString() : id.ToString();
            var parts = s.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var i) && int.TryParse(parts[1], out var v))
            {
                var e = new Entity { Index = i, Version = v };
                return em.Exists(e) ? e : Entity.Null;
            }
            if (int.TryParse(s, out var idx))
                using (var arr = search.ToEntityArray(Allocator.Temp))
                    foreach (var e in arr) if (e.Index == idx) return e;
            return Entity.Null;
        }

        public Entity FindPrefab(string name, Func<Entity, bool> filter = null)
        {
            if (string.IsNullOrEmpty(name)) return Entity.Null;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>());
            Entity contains = Entity.Null;
            using (var arr = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in arr)
                {
                    if (filter != null && !filter(e)) continue;
                    var n = PrefabName(e);
                    if (n == null) continue;
                    if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return e;
                    if (contains == Entity.Null && n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) contains = e;
                }
            }
            return contains;
        }

        public float TerrainHeight(float3 p)
        {
            var hd = Sys<TerrainSystem>().GetHeightData(false);
            return TerrainUtils.SampleHeight(ref hd, p);
        }

        // ------------------------------------------------------------------ status / menu

        object Status()
        {
            var gm = GameManager.instance;
            var r = new Dictionary<string, object>
            {
                { "mode", gm?.gameMode.ToString() },
                { "loading", gm?.isGameLoading ?? false },
                { "bridge", Mod.Version },
            };
            if (gm != null && gm.gameMode == GameMode.Game && !gm.isGameLoading)
            {
                r["cityName"] = Sys<CityConfigurationSystem>().cityName;
                r["date"] = DateString();
                var sim = Sys<SimulationSystem>();
                r["speed"] = sim.selectedSpeed;
                var city = Sys<CitySystem>().City;
                if (city != Entity.Null && em.Exists(city))
                {
                    if (em.HasComponent<Population>(city)) r["population"] = em.GetComponentData<Population>(city).m_Population;
                    if (em.HasComponent<PlayerMoney>(city)) r["money"] = em.GetComponentData<PlayerMoney>(city).money;
                }
                r["toolBusy"] = Sys<AgentToolSystem>().Busy;
            }
            return r;
        }

        string DateString()
        {
            try
            {
                var dt = Sys<TimeSystem>().GetCurrentDateTime();
                return dt.ToString("yyyy-MM-dd HH:mm");
            }
            catch (Exception e) { return "?" + e.Message; }
        }

        IList MapList()
        {
            var menu = world.GetExistingSystemManaged<MenuUISystem>();
            var binding = GetField(menu, "m_MapsBinding");
            return binding?.GetType().GetProperty("value")?.GetValue(binding) as IList;
        }

        object Maps()
        {
            var list = MapList();
            if (list == null) return new ApiError("map list not available (open the main menu?)", 409);
            var res = new List<object>();
            foreach (MapInfo m in list)
            {
                var c = m.connections;
                res.Add(new Dictionary<string, object>
                {
                    { "id", m.id }, { "name", m.displayName }, { "theme", m.theme },
                    { "buildableLand", m.buildableLand }, { "area", m.area },
                    { "surfaceWater", m.surfaceWaterAvailability }, { "groundWater", m.groundWaterAvailability },
                    { "playable", m.isPlayable }, { "locked", m.locked },
                    { "connections", new Dictionary<string, object> { { "road", c.road }, { "train", c.train }, { "air", c.air }, { "ship", c.ship }, { "electricity", c.electricity }, { "water", c.water } } },
                });
            }
            return new Dictionary<string, object> { { "maps", res } };
        }

        object NewGame(Args a)
        {
            var gm = GameManager.instance;
            if (gm.gameMode == GameMode.Game && !a.B("force")) return new ApiError("already in a game; pass force=true to abandon it", 409);
            var list = MapList();
            if (list == null) return new ApiError("map list not available", 409);
            var want = a.Str("map", "");
            MapInfo map = null;
            foreach (MapInfo m in list) if (m.id == want || string.Equals(m.displayName, want, StringComparison.OrdinalIgnoreCase)) { map = m; break; }
            if (map == null) foreach (MapInfo m in list) if (m.displayName != null && m.displayName.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) { map = m; break; }
            if (map == null) return new ApiError("map not found: " + want);

            var menu = world.GetExistingSystemManaged<MenuUISystem>();
            var options = new Dictionary<string, bool>();
            try
            {
                var defs = GetField(menu, "m_DefaultGameOptions");
                if (defs != null && Invoke(defs, "MergeOptions", new Dictionary<string, bool>()) is Dictionary<string, bool> merged) options = merged;
            }
            catch (Exception e) { Log.Warn("default options: " + e.Message); }
            options["unlockAll"] = false;
            options["unlimitedMoney"] = false;
            options["unlockMapTiles"] = false;
            if (a.Has("leftHandTraffic")) options["leftHandTraffic"] = a.B("leftHandTraffic");
            if (a.Has("naturalDisasters")) options["naturalDisasters"] = a.B("naturalDisasters");

            var args = new MenuUISystem.NewGameArgs
            {
                mapId = map.id,
                cityName = a.Str("cityName", "Claude City"),
                theme = a.Str("theme", map.theme),
                options = options,
                gameMode = a.Str("gameMode", null),
            };
            Invoke(menu, "SafeNewGame", args, true);
            return new Dictionary<string, object> { { "started", true }, { "map", map.displayName }, { "mapId", map.id }, { "cityName", args.cityName }, { "theme", args.theme }, { "options", options } };
        }

        /// <summary>
        /// UI state and fallbacks for things that block play: error dialogs, the pause menu,
        /// confirmation dialogs. action: status | resume | dismiss | confirm (choice) | pausemenu
        /// </summary>
        object Ui(Args a)
        {
            var gm = GameManager.instance;
            var action = a.Str("action", "status");
            var ab = gm?.userInterface?.appBindings;
            var screens = world.GetExistingSystemManaged<GameScreenUISystem>();
            var done = new List<object>();
            if (action == "dismiss" || action == "resume")
            {
                ab?.DismissAllErrors();
                done.Add("errors dismissed");
            }
            if (action == "resume" && gm.gameMode == GameMode.Game)
            {
                EnsurePlayable();
                done.Add("ensurePlayable (errors, milestone popup, menus)");
            }
            if (action == "pausemenu" && screens != null) { screens.SetScreen(GameScreenUISystem.GameScreen.PauseMenu); done.Add("screen=PauseMenu"); }
            if (action == "notutorials")
            {
                // same as answering "disable tutorials" in the welcome dialog; the active intro
                // tutorial otherwise keeps the toolbar (and all unlocks) locked
                var ts = world.GetExistingSystemManaged<Game.Tutorials.TutorialSystem>();
                if (ts != null)
                {
                    ts.mode = 0;
                    ts.tutorialEnabled = false;
                    try { Game.Settings.SharedSettings.instance.gameplay.ApplyAndSave(); } catch (Exception e) { done.Add("settings save: " + e.Message); }
                    done.Add("tutorials disabled");
                }
            }
            if (action == "confirm" && ab != null)
            {
                var cb = GetField(ab, "m_ConfirmationDialogCallback") as Action<int>;
                var dcb = GetField(ab, "m_DismissibleConfirmationDialogCallback") as Action<int, bool>;
                int choice = a.I("choice", 0);
                if (cb != null) { cb(choice); done.Add("confirmation callback(" + choice + ")"); }
                else if (dcb != null) { dcb(choice, false); done.Add("dismissible confirmation callback(" + choice + ")"); }
                else done.Add("no confirmation dialog");
            }
            return UiStatus(done);
        }

        Dictionary<string, object> UiStatus(List<object> done = null)
        {
            var gm = GameManager.instance;
            var menu = world.GetExistingSystemManaged<MenuUISystem>();
            var screens = world.GetExistingSystemManaged<GameScreenUISystem>();
            var time = world.GetExistingSystemManaged<TimeUISystem>();
            var ab = gm?.userInterface?.appBindings;
            object err = null;
            try
            {
                var mgr = GetField(ab, "m_ErrorDialogManager");
                var cur = mgr?.GetType().GetProperty("currentError", Any)?.GetValue(mgr);
                if (cur != null) err = cur.ToString();
            }
            catch { }
            bool barrier = false;
            try { barrier = (bool)time.GetType().GetProperty("pausedBarrierActive", Any).GetValue(time); } catch { }
            var r = new Dictionary<string, object>
            {
                { "mode", gm?.gameMode.ToString() },
                { "menuScreen", menu?.activeScreen.ToString() },
                { "gameScreen", screens?.activeScreen.ToString() },
                { "errorDialog", err },
                { "confirmationPending", GetField(ab, "m_ConfirmationDialogCallback") != null || GetField(ab, "m_DismissibleConfirmationDialogCallback") != null },
                { "pausedBarrier", barrier },
            };
            if (done != null) r["done"] = done;
            return r;
        }

        /// <summary>Close error dialogs and the pause menu so the simulation can run.</summary>
        public void EnsurePlayable()
        {
            var gm = GameManager.instance;
            try { gm?.userInterface?.appBindings?.DismissAllErrors(); } catch (Exception e) { Log.Warn("dismiss: " + e.Message); }
            // "milestone reached" popup pauses the simulation until closed
            try
            {
                var ms = world.GetExistingSystemManaged<MilestoneUISystem>();
                if (ms != null) Invoke(ms, "ClearUnlockedMilestone");
            }
            catch (Exception e) { Log.Warn("milestone popup: " + e.Message); }
            // "new signature building available" popup also pauses
            try { world.GetExistingSystemManaged<SignatureBuildingUISystem>()?.ClearUnlockedSignature(); }
            catch (Exception e) { Log.Warn("signature popup: " + e.Message); }
            var screens = world.GetExistingSystemManaged<GameScreenUISystem>();
            if (screens != null && screens.activeScreen != GameScreenUISystem.GameScreen.Main && screens.activeScreen != GameScreenUISystem.GameScreen.FreeCamera)
                screens.SetScreen(GameScreenUISystem.GameScreen.Main);
        }

        // ------------------------------------------------------------------ dashboard

        public Dictionary<string, object> City()
        {
            var r = new Dictionary<string, object>();
            r["cityName"] = Sys<CityConfigurationSystem>().cityName;
            r["date"] = DateString();
            var sim = Sys<SimulationSystem>();
            r["speed"] = sim.selectedSpeed;
            r["frame"] = sim.frameIndex;
            var city = Sys<CitySystem>().City;
            if (em.HasComponent<Population>(city))
            {
                var pop = em.GetComponentData<Population>(city);
                r["population"] = pop.m_Population;
                r["populationWithMoveIn"] = pop.m_PopulationWithMoveIn;
                r["happiness"] = pop.m_AverageHappiness;
                r["health"] = pop.m_AverageHealth;
            }
            if (em.HasComponent<PlayerMoney>(city)) r["money"] = em.GetComponentData<PlayerMoney>(city).money;
            try
            {
                var b = Sys<CityServiceBudgetSystem>();
                r["income"] = b.GetTotalIncome();
                r["expenses"] = b.GetTotalExpenses();
                r["balance"] = b.GetBalance();
                r["taxIncome"] = b.GetTotalTaxIncome();
            }
            catch (Exception e) { r["budgetError"] = e.Message; }

            var res = Sys<ResidentialDemandSystem>();
            var com = Sys<CommercialDemandSystem>();
            var ind = Sys<IndustrialDemandSystem>();
            var bd = res.buildingDemand;
            r["demand"] = new Dictionary<string, object>
            {
                { "residentialLow", bd.x }, { "residentialMedium", bd.y }, { "residentialHigh", bd.z },
                { "household", res.householdDemand },
                { "commercial", com.buildingDemand }, { "commercialCompanies", com.companyDemand },
                { "industrial", ind.industrialBuildingDemand }, { "office", ind.officeBuildingDemand }, { "storage", ind.storageBuildingDemand },
            };
            var el = Sys<ElectricityStatisticsSystem>();
            r["electricity"] = new Dictionary<string, object> { { "production", el.production }, { "consumption", el.consumption }, { "fulfilled", el.fulfilledConsumption } };
            var wa = Sys<WaterStatisticsSystem>();
            r["water"] = new Dictionary<string, object> { { "capacity", wa.freshCapacity }, { "consumption", wa.freshConsumption }, { "fulfilled", wa.fulfilledFreshConsumption } };
            r["sewage"] = new Dictionary<string, object> { { "capacity", wa.sewageCapacity }, { "consumption", wa.sewageConsumption }, { "fulfilled", wa.fulfilledSewageConsumption } };
            var ms = Sys<MilestoneSystem>();
            r["milestone"] = new Dictionary<string, object>
            {
                { "achieved", em.HasComponent<MilestoneLevel>(city) ? em.GetComponentData<MilestoneLevel>(city).m_AchievedMilestone : -1 },
                { "xp", ms.currentXP }, { "nextRequiredXP", ms.nextRequiredXP }, { "progress", ms.progress }, { "next", ms.nextMilestone },
            };
            try
            {
                var tax = Sys<TaxSystem>();
                r["taxRate"] = tax.TaxRate;
            }
            catch { }
            return r;
        }

        object Speed(int speed)
        {
            var ui = world.GetExistingSystemManaged<TimeUISystem>();
            var sim = Sys<SimulationSystem>();
            if (speed > 0) EnsurePlayable();
            try
            {
                if (speed <= 0) Invoke(ui, "SetSimulationPaused", true);
                else
                {
                    Invoke(ui, "SetSimulationPaused", false);
                    Invoke(ui, "SetSimulationSpeed", math.clamp(speed - 1, 0, 2));
                }
            }
            catch (Exception e)
            {
                Log.Warn("speed via UI failed: " + e.Message);
            }
            float want = speed <= 0 ? 0f : math.pow(2f, math.clamp(speed - 1, 0, 2));
            if (math.abs(sim.selectedSpeed - want) > 0.01f) sim.selectedSpeed = want; // UI ignored it (pause barrier)
            var r = UiStatus();
            r["selectedSpeed"] = sim.selectedSpeed;
            return r;
        }

        public void SetSpeed(int speed) => Speed(speed);

        // ------------------------------------------------------------------ geography

        object Terrain(Args a)
        {
            var pts = a.List("points") ?? new List<object>();
            var hd = Sys<TerrainSystem>().GetHeightData(false);
            var ws = Sys<WaterSystem>();
            JobHandle deps = default;
            var wd = ws.GetSurfaceData(out deps);
            deps.Complete();
            var res = new List<object>();
            foreach (var o in pts)
            {
                var l = o as List<object>;
                if (l == null || l.Count < 2) continue;
                var p = new float3((float)(double)l[0], 0, (float)(double)l[1]);
                var h = TerrainUtils.SampleHeight(ref hd, p);
                var depth = WaterUtils.SampleDepth(ref wd, p);
                res.Add(new Dictionary<string, object> { { "x", p.x }, { "z", p.z }, { "height", h }, { "waterDepth", depth } });
            }
            return new Dictionary<string, object> { { "points", res } };
        }

        object AreaInfo()
        {
            var ts = Sys<TerrainSystem>();
            var r = new Dictionary<string, object>();
            r["playableArea"] = new List<object> { ts.playableArea.x, ts.playableArea.y };
            r["playableOffset"] = new List<object> { ts.playableOffset.x, ts.playableOffset.y };
            r["worldSize"] = new List<object> { ts.worldSize.x, ts.worldSize.y };

            // owned map tiles
            var tq = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Areas.MapTile>(), ComponentType.ReadOnly<AreaNode>(), ComponentType.Exclude<Deleted>());
            int owned = 0, total = 0;
            var ob = new Bounds2(new float2(float.MaxValue), new float2(float.MinValue));
            using (var tiles = tq.ToEntityArray(Allocator.Temp))
            {
                foreach (var t in tiles)
                {
                    total++;
                    if (em.HasComponent<Native>(t)) continue;
                    owned++;
                    var nodes = em.GetBuffer<AreaNode>(t, true);
                    for (int i = 0; i < nodes.Length; i++) { ob.min = math.min(ob.min, nodes[i].m_Position.xz); ob.max = math.max(ob.max, nodes[i].m_Position.xz); }
                }
            }
            r["mapTiles"] = new Dictionary<string, object> { { "owned", owned }, { "total", total }, { "ownedBounds", owned > 0 ? new List<object> { ob.min.x, ob.min.y, ob.max.x, ob.max.y } : null } };

            // outside connections
            var oq = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Objects.OutsideConnection>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var oc = new List<object>();
            using (var arr = oq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                    oc.Add(new Dictionary<string, object> { { "id", Id(e) }, { "prefab", PrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab) }, { "pos", F2(em.GetComponentData<ObjTransform>(e).m_Position) } });
            r["outsideConnections"] = oc;

            // existing nets summary
            var nq = em.CreateEntityQuery(ComponentType.ReadOnly<NetEdge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Owner>());
            var counts = new Dictionary<string, object>();
            var nb = new Bounds2(new float2(float.MaxValue), new float2(float.MinValue));
            using (var arr = nq.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in arr)
                {
                    var n = PrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab);
                    counts[n] = (counts.TryGetValue(n, out var c) ? (int)c : 0) + 1;
                    var cv = em.GetComponentData<Curve>(e).m_Bezier;
                    nb.min = math.min(nb.min, math.min(cv.a.xz, cv.d.xz)); nb.max = math.max(nb.max, math.max(cv.a.xz, cv.d.xz));
                }
            }
            r["netSegmentsByPrefab"] = counts;
            r["netBounds"] = counts.Count > 0 ? new List<object> { nb.min.x, nb.min.y, nb.max.x, nb.max.y } : null;
            return r;
        }

        public List<Dictionary<string, object>> NetList(float x, float z, float radius, string filter)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<NetEdge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Owner>());
            var res = new List<Dictionary<string, object>>();
            var c = new float2(x, z);
            using (var arr = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in arr)
                {
                    var cv = em.GetComponentData<Curve>(e);
                    float t = 0;
                    var dist = MathUtils.Distance(cv.m_Bezier.xz, c, out t);
                    if (dist > radius) continue;
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    var name = PrefabName(prefab);
                    if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var edge = em.GetComponentData<NetEdge>(e);
                    float width = em.HasComponent<NetGeometryData>(prefab) ? em.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth : 0;
                    res.Add(new Dictionary<string, object>
                    {
                        { "id", Id(e) }, { "prefab", name }, { "length", Math.Round(cv.m_Length, 1) }, { "width", width },
                        { "start", F2(cv.m_Bezier.a) }, { "end", F2(cv.m_Bezier.d) },
                        { "startNode", Id(edge.m_Start) }, { "endNode", Id(edge.m_End) },
                        { "startDegree", Degree(edge.m_Start) }, { "endDegree", Degree(edge.m_End) },
                        { "road", em.HasComponent<Road>(e) }, { "distance", Math.Round(dist, 1) },
                    });
                }
            }
            res.Sort((a, b) => ((double)a["distance"]).CompareTo((double)b["distance"]));
            return res;
        }

        int Degree(Entity node) => em.HasBuffer<ConnectedEdge>(node) ? em.GetBuffer<ConnectedEdge>(node, true).Length : 0;

        object Nets(float x, float z, float radius, string filter)
        {
            var l = NetList(x, z, radius, filter);
            var cut = l.Count > 200 ? l.GetRange(0, 200) : l;
            return new Dictionary<string, object> { { "count", l.Count }, { "segments", cut } };
        }

        // ------------------------------------------------------------------ prefabs

        public string Category(Entity e)
        {
            if (em.HasComponent<SignatureBuildingData>(e)) return "signature";
            if (em.HasComponent<ZoneData>(e)) return "zones";
            if (em.HasComponent<RoadData>(e) && em.HasComponent<PlaceableNetData>(e)) return "roads";
            if (em.HasComponent<NetData>(e) && em.HasComponent<PlaceableNetData>(e)) return "nets";
            if (em.HasComponent<BuildingData>(e) && em.HasComponent<ServiceObjectData>(e)) return "services";
            if (em.HasComponent<BuildingData>(e) && em.HasComponent<PlaceableObjectData>(e) && !em.HasComponent<SpawnableBuildingData>(e)) return "buildings";
            return null;
        }

        object Prefabs(string category, string filter, bool unlockedOnly)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<UIObjectData>());
            var res = new List<object>();
            bool sigReset = true;
            using (var arr = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in arr)
                {
                    var cat = Category(e);
                    if (cat == null) continue;
                    if (category != "all" && category != cat) continue;
                    var name = PrefabName(e);
                    if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    bool locked = IsLocked(e);
                    if (unlockedOnly && locked) continue;
                    var d = new Dictionary<string, object> { { "name", name }, { "category", cat }, { "locked", locked } };
                    var ui = em.GetComponentData<UIObjectData>(e);
                    if (ui.m_Group != Entity.Null) d["group"] = PrefabName(ui.m_Group);
                    if (em.HasComponent<PlaceableObjectData>(e)) d["cost"] = em.GetComponentData<PlaceableObjectData>(e).m_ConstructionCost;
                    if (em.HasComponent<PlaceableNetData>(e)) { var pn = em.GetComponentData<PlaceableNetData>(e); d["costPer8m"] = pn.m_DefaultConstructionCost; d["upkeep"] = pn.m_DefaultUpkeepCost; }
                    if (em.HasComponent<NetGeometryData>(e)) d["width"] = em.GetComponentData<NetGeometryData>(e).m_DefaultWidth;
                    if (em.HasComponent<BuildingData>(e)) { var bd = em.GetComponentData<BuildingData>(e); d["lot"] = new List<object> { bd.m_LotSize.x, bd.m_LotSize.y }; }
                    if (em.HasComponent<ServiceObjectData>(e)) d["service"] = PrefabName(em.GetComponentData<ServiceObjectData>(e).m_Service);
                    if (em.HasComponent<ZoneData>(e)) d["areaType"] = em.GetComponentData<ZoneData>(e).m_AreaType.ToString();
                    if (cat == "signature") { AddSignaturePlaced(d, e, sigReset); sigReset = false; }
                    if (cat == "services") AddPrefabUpkeep(d, e);
                    res.Add(d);
                }
            }
            return new Dictionary<string, object> { { "count", res.Count }, { "prefabs", res } };
        }

        // ------------------------------------------------------------------ problems / buildings

        object Problems(string only = null)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Icon>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var groups = new Dictionary<string, List<object>>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in arr)
                {
                    if (em.HasComponent<Owner>(e) && em.HasComponent<Temp>(em.GetComponentData<Owner>(e).m_Owner)) continue;
                    var name = PrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab);
                    if (!groups.TryGetValue(name, out var l)) groups[name] = l = new List<object>();
                    l.Add(e);
                }
            }
            var res = new List<object>();
            foreach (var kv in groups.OrderByDescending(k => k.Value.Count))
            {
                if (only != null && kv.Key.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var samples = new List<object>();
                var byPrefab = new Dictionary<string, int>();
                if (only != null)
                    foreach (Entity e in kv.Value)
                    {
                        if (!em.HasComponent<Owner>(e)) continue;
                        var o = em.GetComponentData<Owner>(e).m_Owner;
                        if (!em.HasComponent<PrefabRef>(o)) continue;
                        var pn = PrefabName(em.GetComponentData<PrefabRef>(o).m_Prefab);
                        byPrefab[pn] = byPrefab.TryGetValue(pn, out var c) ? c + 1 : 1;
                    }
                foreach (Entity e in kv.Value.Take(only != null ? 400 : 5))
                {
                    var ic = em.GetComponentData<Icon>(e);
                    var s = new Dictionary<string, object> { { "pos", F2(ic.m_Location) } };
                    if (em.HasComponent<Owner>(e))
                    {
                        var o = em.GetComponentData<Owner>(e).m_Owner;
                        s["owner"] = Id(o);
                        if (em.HasComponent<PrefabRef>(o)) s["ownerPrefab"] = PrefabName(em.GetComponentData<PrefabRef>(o).m_Prefab);
                    }
                    samples.Add(s);
                }
                var g = new Dictionary<string, object> { { "type", kv.Key }, { "count", kv.Value.Count }, { "examples", samples } };
                if (only != null) g["byPrefab"] = byPrefab.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => (object)x.Value);
                res.Add(g);
            }
            return new Dictionary<string, object> { { "problems", res } };
        }

        object Buildings(float x, float z, float radius, string kind, int offset = 0, int limit = 300)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var res = new List<object>();
            var matched = new List<KeyValuePair<float, Entity>>(); // (distance, building): sorted for stable paging
            var zoned = new Dictionary<string, int>();
            int total = 0, noRoad = 0, abandoned = 0;
            using (var arr = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in arr)
                {
                    var tr = em.GetComponentData<ObjTransform>(e);
                    if (math.distance(tr.m_Position.xz, new float2(x, z)) > radius) continue;
                    total++;
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    var b = em.GetComponentData<Building>(e);
                    if (b.m_RoadEdge == Entity.Null) noRoad++;
                    if (em.HasComponent<Abandoned>(e)) abandoned++;
                    bool service = em.HasComponent<ServiceObjectData>(prefab);
                    if (em.HasComponent<SpawnableBuildingData>(prefab))
                    {
                        var zn = PrefabName(em.GetComponentData<SpawnableBuildingData>(prefab).m_ZonePrefab) ?? "?";
                        zoned[zn] = zoned.TryGetValue(zn, out var c) ? c + 1 : 1;
                    }
                    bool destroyed = em.HasComponent<Game.Common.Destroyed>(e);
                    if (kind == "all" || (kind == "service" && service) || (kind == "destroyed" && destroyed) || (kind == "abandoned" && em.HasComponent<Abandoned>(e)))
                        matched.Add(new KeyValuePair<float, Entity>(math.distance(tr.m_Position.xz, new float2(x, z)), e));
                }
            }
            matched.Sort((m1, m2) => m1.Key != m2.Key ? m1.Key.CompareTo(m2.Key) : m1.Value.Index.CompareTo(m2.Value.Index));
            offset = math.max(0, offset); limit = math.clamp(limit, 1, 5000);
            for (int i = offset; i < matched.Count && res.Count < limit; i++)
            {
                var e = matched[i].Value;
                var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                bool service = em.HasComponent<ServiceObjectData>(prefab);
                res.Add(new Dictionary<string, object>
                {
                    { "id", Id(e) }, { "prefab", PrefabName(prefab) }, { "pos", F2(em.GetComponentData<ObjTransform>(e).m_Position) },
                    { "roadConnected", em.GetComponentData<Building>(e).m_RoadEdge != Entity.Null }, { "service", service ? PrefabName(em.GetComponentData<ServiceObjectData>(prefab).m_Service) : null },
                });
            }
            bool truncated = offset + res.Count < matched.Count;
            var r = new Dictionary<string, object> { { "total", total }, { "withoutRoad", noRoad }, { "abandoned", abandoned }, { "zonedByZone", zoned },
                { "matching", matched.Count }, { "offset", offset }, { "limit", limit }, { "returned", res.Count }, { "truncated", truncated }, { "buildings", res } };
            if (truncated) r["nextOffset"] = offset + res.Count;
            return r;
        }

        // ------------------------------------------------------------------ economy

        object Taxes(Args a)
        {
            var tax = Sys<TaxSystem>();
            if (a.Has("overall")) tax.TaxRate = a.I("overall");
            if (a.Has("residential")) tax.SetTaxRate(TaxAreaType.Residential, a.I("residential"));
            if (a.Has("commercial")) tax.SetTaxRate(TaxAreaType.Commercial, a.I("commercial"));
            if (a.Has("industrial")) tax.SetTaxRate(TaxAreaType.Industrial, a.I("industrial"));
            if (a.Has("office")) tax.SetTaxRate(TaxAreaType.Office, a.I("office"));
            return new Dictionary<string, object>
            {
                { "overall", tax.TaxRate },
                { "residential", tax.GetTaxRate(TaxAreaType.Residential) },
                { "commercial", tax.GetTaxRate(TaxAreaType.Commercial) },
                { "industrial", tax.GetTaxRate(TaxAreaType.Industrial) },
                { "office", tax.GetTaxRate(TaxAreaType.Office) },
            };
        }

        object ServiceBudget(Args a)
        {
            var b = Sys<CityServiceBudgetSystem>();
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<ServiceData>(), ComponentType.ReadOnly<PrefabData>());
            var res = new List<object>();
            var want = a.Str("service");
            using (var arr = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in arr)
                {
                    var name = PrefabName(e);
                    if (want != null && a.Has("percent") && string.Equals(name, want, StringComparison.OrdinalIgnoreCase))
                        b.SetServiceBudget(e, math.clamp(a.I("percent"), 50, 150));
                    int upkeep = 0;
                    try { b.GetEstimatedServiceBudget(e, out upkeep); } catch { }
                    res.Add(new Dictionary<string, object> { { "service", name }, { "budget", b.GetServiceBudget(e) }, { "upkeep", upkeep } });
                }
            }
            return new Dictionary<string, object> { { "services", res } };
        }

        object Milestones()
        {
            var ms = Sys<MilestoneSystem>();
            var city = Sys<CitySystem>().City;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<MilestoneData>(), ComponentType.ReadOnly<PrefabData>());
            var list = new List<object>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in arr)
                {
                    var md = em.GetComponentData<MilestoneData>(e);
                    list.Add(new Dictionary<string, object> { { "name", PrefabName(e) }, { "index", md.m_Index }, { "xpRequired", md.m_XpRequried }, { "reward", md.m_Reward }, { "mapTiles", md.m_MapTiles }, { "locked", IsLocked(e) } });
                }
            }
            list.Sort((x, y) => ((int)((Dictionary<string, object>)x)["index"]).CompareTo((int)((Dictionary<string, object>)y)["index"]));
            return new Dictionary<string, object>
            {
                { "achieved", em.HasComponent<MilestoneLevel>(city) ? em.GetComponentData<MilestoneLevel>(city).m_AchievedMilestone : -1 },
                { "xp", ms.currentXP }, { "nextRequiredXP", ms.nextRequiredXP }, { "progress", ms.progress },
                { "milestones", list },
            };
        }

        /// <summary>Per zone type: zoned cells, cells with a building on them, and free cells.</summary>
        object ZoneStats()
        {
            var zoneSys = Sys<Game.Prefabs.ZoneSystem>();
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Block>(), ComponentType.ReadOnly<Cell>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var total = new Dictionary<ushort, int>();
            var occupied = new Dictionary<ushort, int>();
            using (var blocks = q.ToEntityArray(Allocator.Temp))
                foreach (var b in blocks)
                {
                    var cells = em.GetBuffer<Cell>(b, true);
                    for (int i = 0; i < cells.Length; i++)
                    {
                        var c = cells[i];
                        if (c.m_Zone.m_Index == 0 || (c.m_State & CellFlags.Visible) == 0) continue;
                        var k = c.m_Zone.m_Index;
                        total[k] = total.TryGetValue(k, out var t) ? t + 1 : 1;
                        if ((c.m_State & CellFlags.Occupied) != 0) occupied[k] = occupied.TryGetValue(k, out var o) ? o + 1 : 1;
                    }
                }
            var res = new List<object>();
            foreach (var kv in total.OrderByDescending(k => k.Value))
            {
                occupied.TryGetValue(kv.Key, out var occ);
                res.Add(new Dictionary<string, object>
                {
                    { "zone", PrefabName(zoneSys.GetPrefab(new ZoneType { m_Index = kv.Key })) },
                    { "cells", kv.Value }, { "occupied", occ }, { "free", kv.Value - occ },
                    { "freePct", Math.Round(100.0 * (kv.Value - occ) / kv.Value, 1) },
                });
            }
            return new Dictionary<string, object> { { "zones", res } };
        }

        /// <summary>Main menu "Continue": load the most recent save.</summary>
        object ContinueLast()
        {
            var gm = GameManager.instance;
            if (gm.gameMode == GameMode.Game) return new ApiError("already in a game", 409);
            Invoke(Menu, "SafeContinueGame");
            return new Dictionary<string, object> { { "continuing", true } };
        }

        List<SaveInfo> SaveList()
        {
            var b = GetField(Menu, "m_SavesBinding");
            var l = b?.GetType().GetProperty("value")?.GetValue(b) as List<SaveInfo>;
            return l ?? new List<SaveInfo>();
        }

        object Saves()
        {
            return new Dictionary<string, object>
            {
                { "saves", SaveList().OrderByDescending(x => x.lastModified).Take(20).Select(x => (object)new Dictionary<string, object>
                    { { "id", x.id }, { "name", x.displayName }, { "city", x.cityName }, { "population", x.population }, { "modified", x.lastModified.ToString("s") }, { "autoSave", x.autoSave } }).ToList() },
            };
        }

        /// <summary>Load a save by id, or the newest save whose name/city matches (default: newest overall).</summary>
        object LoadSave(Args a)
        {
            var gm = GameManager.instance;
            if (gm.gameMode == GameMode.Game && !a.B("force")) return new ApiError("already in a game; pass force=true", 409);
            var list = SaveList().OrderByDescending(x => x.lastModified).ToList();
            var want = a.Str("id") ?? a.Str("name");
            var save = want == null ? list.FirstOrDefault()
                : list.FirstOrDefault(x => x.id == want) ?? list.FirstOrDefault(x => string.Equals(x.displayName, want, StringComparison.OrdinalIgnoreCase) || string.Equals(x.cityName, want, StringComparison.OrdinalIgnoreCase));
            if (save == null) return new ApiError("no matching save (" + list.Count + " saves known)");
            var args = new MenuUISystem.LoadGameArgs { saveId = save.id, cityName = save.cityName, options = save.options ?? new Dictionary<string, bool>(), gameMode = save.gameMode };
            Invoke(Menu, "SafeLoadGame", args, true);
            return new Dictionary<string, object> { { "loading", save.displayName }, { "city", save.cityName }, { "modified", save.lastModified.ToString("s") }, { "population", save.population } };
        }

        public string CityName => Sys<CityConfigurationSystem>().cityName;
        public MenuUISystem Menu => world.GetExistingSystemManaged<MenuUISystem>();
    }

    /// <summary>
    /// Saves like the Save Game panel does, but supplies its own preview texture (the panel's
    /// preview only exists while the panel is open, which is what made SafeSaveGame throw).
    /// </summary>
    public class SaveOperation : IOperation
    {
        readonly Api api;
        readonly string name;
        System.Threading.Tasks.Task<bool> task;
        UnityEngine.RenderTexture rt;
        int frames;

        public SaveOperation(Api api, string name) { this.api = api; this.name = string.IsNullOrEmpty(name) ? api.CityName : name; }

        public bool Step(out object result)
        {
            result = null;
            if (task == null)
            {
                if (rt == null)
                {
                    // render the 3D view (no UI) into the preview like the Save panel does
                    rt = Game.UI.ScreenCaptureHelper.CreateRenderTarget("ClaudeCitySavePreview", 680, 383, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB);
                    try
                    {
                        var settings = Api.Field(api.Menu, "m_PreviewSettings") as MenuHelpers.SaveGamePreviewSettings ?? new MenuHelpers.SaveGamePreviewSettings();
                        var cam = UnityEngine.Camera.main;
                        if (cam != null) Game.UI.ScreenCaptureHelper.CaptureScreenshot(cam, rt, settings);
                    }
                    catch (Exception e) { Log.Warn("save preview capture: " + e.Message); }
                    return false; // give the capture a frame
                }
                var target = MenuHelpers.GetSanitizedCloudTarget(Game.Settings.SharedSettings.instance.userState.lastCloudTarget);
                var info = api.Menu.GetSaveInfo(false);
                task = GameManager.instance.Save(name, info, target.Item2, rt);
                return false;
            }
            frames++;
            if (!task.IsCompleted && frames < 60 * 60) return false;
            bool ok = task.IsCompleted && !task.IsFaulted && task.Result;
            var r = new Dictionary<string, object> { { "saved", ok }, { "name", name } };
            if (task.IsFaulted) r["error"] = task.Exception?.GetBaseException().Message;
            if (!task.IsCompleted) r["error"] = "save did not finish in time";
            try { rt.Release(); UnityEngine.Object.Destroy(rt); } catch { }
            api.EnsurePlayable();
            result = r;
            return true;
        }
    }

    /// <summary>Runs the simulation for N in-game days (or until a wall-clock cap) and returns the dashboard.</summary>
    public class WaitOperation : IOperation
    {
        readonly Api api;
        readonly float days;
        readonly int speed;
        readonly int maxMs;
        uint startFrame;
        int startTick;
        int lastKick;
        int kicks;
        bool started;

        public WaitOperation(Api api, Args a)
        {
            this.api = api;
            days = a.F("days", 1f);
            speed = a.I("speed", 3);
            maxMs = (int)(a.F("maxSeconds", 50f) * 1000);
        }

        public bool Step(out object result)
        {
            result = null;
            var sim = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<SimulationSystem>();
            if (!started)
            {
                started = true;
                startFrame = sim.frameIndex;
                startTick = Environment.TickCount;
                api.SetSpeed(speed);
                return false;
            }
            uint frames = sim.frameIndex - startFrame;
            float elapsedDays = frames / (float)TimeSystem.kTicksPerDay;
            int ms = Environment.TickCount - startTick;
            // something (menu, error dialog) paused the game: resume it, at most every 2 s
            if (sim.selectedSpeed <= 0f && ms - lastKick > 2000) { lastKick = ms; api.SetSpeed(speed); kicks++; }
            if (elapsedDays >= days || ms >= maxMs)
            {
                var r = api.City();
                r["waitedDays"] = Math.Round(elapsedDays, 3);
                r["waitedSeconds"] = ms / 1000.0;
                r["simFrames"] = frames;
                if (kicks > 0) r["resumedAfterPause"] = kicks;
                result = r;
                return true;
            }
            return false;
        }
    }
}
