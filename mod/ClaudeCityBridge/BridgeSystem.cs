using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using Game.SceneFlow;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Runs every frame on the main thread: takes requests queued by the HTTP thread, dispatches them
    /// to the API plugin, and steps multi-frame operations until they finish.
    /// </summary>
    public class BridgeSystem : GameSystemBase
    {
        readonly List<KeyValuePair<Pending, IOperation>> active = new List<KeyValuePair<Pending, IOperation>>();
        IApiPlugin plugin;
        string pluginInfo = "not loaded";
        int pluginGeneration;

        public static string PluginPath => Path.Combine(UnityEngine.Application.persistentDataPath, "ModsData", "ClaudeCityBridge", "ClaudeCityApi.dll");

        protected override void OnCreate()
        {
            base.OnCreate();
            LoadPlugin();
            Log.Info("BridgeSystem created; plugin: " + pluginInfo);
        }

        object LoadPlugin()
        {
            try
            {
                var path = PluginPath;
                if (!File.Exists(path)) { pluginInfo = "missing " + path; return new ApiError(pluginInfo, 500); }
                var asm = Assembly.Load(File.ReadAllBytes(path));
                var type = asm.GetTypes().FirstOrDefault(t => typeof(IApiPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                if (type == null) { pluginInfo = "no IApiPlugin in " + path; return new ApiError(pluginInfo, 500); }
                var p = (IApiPlugin)Activator.CreateInstance(type);
                p.Init(World);
                plugin = p;
                pluginGeneration++;
                pluginInfo = type.FullName + " " + p.Version + " (load #" + pluginGeneration + ", " + File.GetLastWriteTime(path).ToString("s") + ")";
                Log.Info("plugin loaded: " + pluginInfo);
                return new Dictionary<string, object> { { "plugin", pluginInfo } };
            }
            catch (Exception e)
            {
                Log.Error(e, "plugin load failed");
                pluginInfo = "load failed: " + e.Message;
                return new ApiError(pluginInfo + "\n" + e, 500);
            }
        }

        static string Truncate(string s, int n) => s == null || s.Length <= n ? s : s.Substring(0, n) + "...";

        protected override void OnUpdate()
        {
            HttpBridge.LastFrameTick = Environment.TickCount;
            var gm = GameManager.instance;
            HttpBridge.Phase = gm == null ? "none" : gm.gameMode + (gm.isGameLoading ? "/loading" : "");
            HttpBridge.PluginInfo = pluginInfo;

            int budget = 32;
            while (budget-- > 0 && HttpBridge.Queue.TryDequeue(out var p))
            {
                try
                {
                    if (p.Path != "/ping" && p.Path != "/status") Log.Info("> " + p.Method + " " + p.Path + (p.Query.Count > 0 ? "?" + string.Join("&", p.Query.Select(kv => kv.Key + "=" + kv.Value)) : "") + " " + Json.Write(p.Body.RawDict));
                    object r;
                    if (p.Path == "/reload") r = LoadPlugin();
                    else if (plugin == null) r = new ApiError("API plugin not loaded: " + pluginInfo, 503);
                    else r = plugin.Dispatch(p);
                    if (r is IOperation op) active.Add(new KeyValuePair<Pending, IOperation>(p, op));
                    else p.Finish(r, r is ApiError e ? e.Status : 200);
                }
                catch (Exception e)
                {
                    Log.Error(e, "dispatch " + p.Path);
                    var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                    p.Finish(new Dictionary<string, object> { { "error", inner.GetType().Name + ": " + inner.Message }, { "stack", inner.StackTrace } }, 500);
                }
            }

            for (int i = active.Count - 1; i >= 0; i--)
            {
                var kv = active[i];
                try
                {
                    if (kv.Value.Step(out var res))
                    {
                        Log.Info("< " + kv.Key.Path + " " + Truncate(Json.Write(res), 300));
                        active.RemoveAt(i);
                        kv.Key.Finish(res, res is ApiError e ? e.Status : 200);
                    }
                }
                catch (Exception e)
                {
                    active.RemoveAt(i);
                    Log.Error(e, "operation " + kv.Key.Path);
                    kv.Key.Finish(new Dictionary<string, object> { { "error", e.GetType().Name + ": " + e.Message }, { "stack", e.StackTrace } }, 500);
                }
            }
        }
    }


    /// <summary>Error result with HTTP status; serialised as {"error": ...}.</summary>
    public class ApiError : Dictionary<string, object>
    {
        public int Status;
        public ApiError(string message, int status = 400) { this["error"] = message; Status = status; }
    }
}
