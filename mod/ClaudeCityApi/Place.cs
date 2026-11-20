using System;
using System.Collections.Generic;
using System.Linq;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace ClaudeCityBridge
{
    /// <summary>
    /// POST /place: validated placement in one call. /findspot candidates (obstacle model + city limits + water,
    /// FindSpot.cs / FindSpotWater.cs) are previewed one after another through the game's own validation (the
    /// same seeded preview POST /building runs) until one passes: 0 errors, demolition within the limits and
    /// (requireRoad) a road connection in the preview. Unless dryRun, that passing preview is the build.
    /// </summary>
    public partial class Api
    {
        static int HttpTimeoutMs(Pending p) =>
            p.Query.TryGetValue("timeout", out var t) && int.TryParse(t, out var ms) && ms > 0 ? ms : 60000;

        /// <summary>A number from an in-process result dictionary (double, float, int or long).</summary>
        static double NumOf(Dictionary<string, object> d, string k, double def = 0)
        {
            if (!d.TryGetValue(k, out var v) || v == null) return def;
            switch (v)
            {
                case double x: return x;
                case float f: return f;
                case int i: return i;
                case long l: return l;
                default: return def;
            }
        }

        /// <summary>
        /// POST /place {prefab, near:[x,z] | x,z, radius:300, within:[x0,z0,x1,z1], n:12, maxHouseholds:0, maxCompanies:0,
        /// maxBuildings (default no limit), requireRoad:true, dryRun, all, seed, and /findspot options: side, roadFilter,
        /// setbacks|gap, maxSteepPct, steep, deadEnds, limitMargin:4, growables ("free" by default when a limit is &gt; 0)}
        /// </summary>
        object Place(Pending p)
        {
            var a = p.Body;
            var tool = Tool;
            if (tool.Busy) return new ApiError("agent tool is busy with another build", 409);
            // same lookup as PlaceBuilding
            var prefab = FindPrefab(a.Str("prefab", ""), e => em.HasComponent<PlaceableObjectData>(e) && em.HasComponent<BuildingData>(e));
            if (prefab == Entity.Null) prefab = FindPrefab(a.Str("prefab", ""), e => em.HasComponent<PlaceableObjectData>(e));
            if (prefab == Entity.Null) return new ApiError("object prefab not found: " + a.Str("prefab"));
            if (IsLocked(prefab)) return new ApiError(PrefabName(prefab) + " is still locked");
            var name = PrefabName(prefab);

            float2 c;
            try
            {
                if (a.Has("near")) c = P(a.Raw("near")).xz;
                else if (a.Has("x") && a.Has("z")) c = new float2(a.F("x"), a.F("z"));
                else return new ApiError("give near:[x,z] or x,z");
            }
            catch (Exception) { return new ApiError("near must be [x, z]"); }
            float radius = math.clamp(a.F("radius", 300f), 16f, 1000f);
            int n = math.clamp(a.I("n", 12), 1, 30);
            var limits = new ProbeGuard.Limits
            {
                MaxHouseholds = math.max(0, a.I("maxHouseholds", 0)),
                MaxCompanies = math.max(0, a.I("maxCompanies", 0)),
                MaxBuildings = a.Has("maxBuildings") ? math.max(0, a.I("maxBuildings")) : -1,
            };
            bool all = a.B("all") || a.Str("dryRun") == "all";
            bool dry = a.B("dryRun") || a.Str("dryRun") == "all";
            bool requireRoad = a.B("requireRoad", true);
            var growables = a.Str("growables") ?? (limits.MaxHouseholds > 0 || limits.MaxCompanies > 0 ? "free" : "obstacle");

            // candidates: /findspot (twice as many as we preview, so growables=free can put clean spots first)
            var q = new Pending { Method = "GET", Path = "/findspot", Body = new Args(null) };
            q.Query["prefab"] = name;
            q.Query["x"] = Inv(c.x);
            q.Query["z"] = Inv(c.y);
            q.Query["radius"] = Inv(radius);
            q.Query["n"] = Math.Min(50, n * 2).ToString();
            q.Query["growables"] = growables;
            if (a.Has("within"))
            {
                var box = ParseBox(a.Raw("within"));
                if (box == null) return new ApiError("within must be [x0, z0, x1, z1]");
                q.Query["within"] = Inv(box.Value.min.x) + "," + Inv(box.Value.min.y) + "," + Inv(box.Value.max.x) + "," + Inv(box.Value.max.y);
            }
            if (a.Has("side")) q.Query["side"] = a.Str("side") == "auto" ? "both" : a.Str("side");
            if (a.Has("roadFilter")) q.Query["filter"] = a.Str("roadFilter");
            foreach (var k in new[] { "maxSteepPct", "steep", "limitMargin", "gap" })
                if (a.Has(k)) q.Query[k] = Inv(a.F(k));
            if (a.Has("setbacks")) q.Query["setbacks"] = a.Raw("setbacks") is List<object> sl ? string.Join(",", sl.Select(o => o is double d ? Inv((float)d) : o.ToString())) : a.Str("setbacks");
            if (a.B("deadEnds")) q.Query["deadEnds"] = "1";
            var fsRes = FindSpot(q);
            if (fsRes is ApiError fe) return fe;
            var fs = (Dictionary<string, object>)fsRes;
            var fsInfo = new Dictionary<string, object>();
            foreach (var k in new[] { "searched", "rejected", "waterRule", "waterParts", "footprint", "limitMargin", "within", "growables", "standing", "hint" })
                if (fs.TryGetValue(k, out var v)) fsInfo[k] = v;
            var cands = (fs.TryGetValue("candidates", out var cl) ? cl as List<object> : null)?.OfType<Dictionary<string, object>>().ToList() ?? new List<Dictionary<string, object>>();
            if (cands.Count == 0)
            {
                var err = new ApiError("no candidate spot for " + name + " within " + radius + " m of [" + c.x + ", " + c.y + "]" + (a.Has("within") ? " inside 'within'" : ""), 404);
                err["findspot"] = fsInfo;
                return err;
            }
            // clean spots (nothing to demolish by the lot model) first, then findspot's ranking
            cands = cands.Select((d, i) => new { d, i, w = NumOf(d, "wouldDemolish") })
                .OrderBy(x => x.w > 0 ? 1 : 0).ThenBy(x => x.i).Select(x => x.d).Take(n).ToList();

            var probes = new List<ProbeOperation.Probe>();
            int order = 0;
            foreach (var cd in cands)
            {
                int ord = order++;
                float x = (float)NumOf(cd, "x"), z = (float)NumOf(cd, "z"), angle = (float)NumOf(cd, "angle");
                var pos = new float3(x, 0, z);
                pos.y = TerrainHeight(pos);
                var rot = quaternion.RotateY(math.radians(angle)); // exactly what POST /building x/z/angle does
                uint seed = a.Has("seed") ? (uint)(long)a.Num("seed") : SeededObjectJob.SeedFor(name, pos, rot);
                if (seed == 0) seed = 1;
                var info = new Dictionary<string, object>
                {
                    { "rank", cd.TryGetValue("rank", out var rk) ? rk : null }, { "score", cd.TryGetValue("score", out var sc) ? sc : null },
                    { "mode", cd.TryGetValue("mode", out var md) ? md : null }, { "distance", cd.TryGetValue("distance", out var di) ? di : null },
                    { "x", Math.Round(x, 2) }, { "z", Math.Round(z, 2) }, { "angle", Math.Round(angle, 2) },
                    { "corners", LotCorners(prefab, pos, rot) },
                    { "building", new Dictionary<string, object> { { "prefab", name }, { "x", Math.Round(x, 2) }, { "z", Math.Round(z, 2) }, { "angle", Math.Round(angle, 2) }, { "seed", (double)seed } } },
                };
                foreach (var k in new[] { "edge", "road", "t", "side", "gap", "setback", "slack", "roomy", "heightRange" })
                    if (cd.TryGetValue(k, out var v)) info[k] = v;
                if (cd.TryGetValue("wouldDemolish", out var wd)) info["lotOverlapsZoned"] = wd;
                var capPos = pos; var capRot = rot; var capSeed = seed;
                probes.Add(new ProbeOperation.Probe
                {
                    Info = info,
                    Make = dryJob => new SeededObjectJob
                    {
                        Prefab = prefab, Position = capPos, Rotation = capRot, DryRun = dryJob, Seed = capSeed,
                        After = t => FindPlaced(prefab, capPos),
                    },
                    Guard = job => new ProbeGuard(this, job, limits, prefab, capPos, requireRoad),
                    // among passing spots: least demolition, then /findspot's order
                    RankOf = r => DemolitionRank(r) + ord * 0.01,
                });
            }

            var extra = new Dictionary<string, object>
            {
                { "prefab", name }, { "near", XZ(c) }, { "radius", radius }, { "limits", limits.ToJson() }, { "requireRoad", requireRoad },
                { "findspot", fsInfo },
                { "usage", "candidates[] are ranked (passing first); each has the /findspot spot, the preview's errors/problems, demolition {buildings, households, companies}, roadConnected and 'building' = {prefab, x, z, angle, seed}: POST /building with it reproduces exactly that preview. Not dryRun: the first passing candidate was built (placedId). all:true previews every candidate and builds the best (least demolition)." },
            };
            return new ProbeOperation(tool, probes, dry, all, HttpTimeoutMs(p), extra, (res, chosen) =>
            {
                var r = chosen?.Result;
                res["placed"] = r != null && r.TryGetValue("applied", out var ap) && ap is bool b && b;
                res["placedId"] = r != null && r.TryGetValue("placedId", out var pid) ? pid : null;
                if (chosen != null)
                {
                    res["building"] = chosen.Info["building"];
                    if (r != null && r.TryGetValue("roadConnected", out var rc)) res["roadConnected"] = rc;
                }
                else res["hint"] = "no previewed spot passed: see candidates[].reason; raise n/radius, allow some demolition (maxHouseholds/maxCompanies), or GET /freeland / buy a tile (GET /tiles)";
            });
        }

        /// <summary>Sort key of a passing candidate's demolition: companies weigh most, then households, then buildings.</summary>
        static double DemolitionRank(Dictionary<string, object> r)
        {
            if (!(r.TryGetValue("demolition", out var d) && d is Dictionary<string, object> dd)) return 0;
            int I(string k) => dd.TryGetValue(k, out var v) && v is int i ? i : 0;
            return I("companies") * 1000.0 + I("households") * 20.0 + I("buildings") * 5.0;
        }
    }
}
