using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace ClaudeCityBridge
{
    /// <summary>A request handed from the HTTP thread to the game's main thread.</summary>
    public class Pending
    {
        public string Method;
        public string Path;
        public Args Body;
        public Dictionary<string, string> Query = new Dictionary<string, string>();
        public object Result;
        public int Status = 200;
        public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        public long QueuedAtMs = Environment.TickCount;

        public string Q(string k, string def = null) => Query.TryGetValue(k, out var v) ? v : def;
        public float QF(string k, float def) => Query.TryGetValue(k, out var v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : def;
        public void Finish(object result, int status = 200) { Result = result; Status = status; Done.Set(); }
    }

    /// <summary>Multi-frame operation, stepped once per game frame on the main thread.</summary>
    public interface IOperation
    {
        /// <summary>Return true when finished; set result.</summary>
        bool Step(out object result);
    }

    /// <summary>Minimal localhost HTTP server; all game access is marshalled to the main thread.</summary>
    public static class HttpBridge
    {
        public const int Port = 8787;
        public static readonly ConcurrentQueue<Pending> Queue = new ConcurrentQueue<Pending>();
        public static volatile int LastFrameTick;
        public static volatile string Phase = "starting";
        public static volatile string PluginInfo = "";
        static HttpListener listener;
        static Thread thread;
        static volatile bool running;

        public static void Start()
        {
            if (running) return;
            running = true;
            listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
            listener.Prefixes.Add("http://localhost:" + Port + "/");
            listener.Start();
            thread = new Thread(Loop) { IsBackground = true, Name = "ClaudeCityBridge.Http" };
            thread.Start();
            Log.Info("HTTP bridge listening on port " + Port);
        }

        public static void Stop()
        {
            running = false;
            try { listener?.Stop(); listener?.Close(); } catch { }
        }

        static void Loop()
        {
            while (running)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch (Exception e) { if (running) Log.Warn("listener: " + e.Message); continue; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        static void Handle(HttpListenerContext ctx)
        {
            int status = 200;
            string body;
            try
            {
                var req = ctx.Request;
                var p = new Pending { Method = req.HttpMethod, Path = req.Url.AbsolutePath.TrimEnd('/') };
                if (p.Path == "") p.Path = "/";
                foreach (string k in req.QueryString.Keys) if (k != null) p.Query[k] = req.QueryString[k];
                string text = "";
                if (req.HasEntityBody) using (var r = new StreamReader(req.InputStream, Encoding.UTF8)) text = r.ReadToEnd();
                p.Body = new Args(Json.Parse(text));

                if (p.Path == "/ping")
                {
                    int age = Environment.TickCount - LastFrameTick;
                    body = Json.Write(new Dictionary<string, object> { { "ok", true }, { "phase", Phase }, { "msSinceLastFrame", age }, { "version", Mod.Version }, { "plugin", PluginInfo } });
                }
                else
                {
                    int timeoutMs = p.Query.TryGetValue("timeout", out var t) && int.TryParse(t, out var tt) ? tt : 60000;
                    Queue.Enqueue(p);
                    if (!p.Done.Wait(timeoutMs))
                    {
                        status = 504;
                        body = Json.Write(new Dictionary<string, object> { { "error", "timeout waiting for game main thread" }, { "phase", Phase } });
                    }
                    else
                    {
                        status = p.Status;
                        body = Json.Write(p.Result);
                    }
                }
            }
            catch (Exception e)
            {
                status = 500;
                body = Json.Write(new Dictionary<string, object> { { "error", e.ToString() } });
            }
            try
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception e) { Log.Warn("response: " + e.Message); }
        }
    }
}
