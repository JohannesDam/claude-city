using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Roadside placement for POST /building {prefab, near:[x,z], side:"auto"|"left"|"right", radius:150,
    /// pick:"nearest"|"best", roadFilter, maxSteepPct, gap, dryRun}: the spot is chosen with the /findspot
    /// obstacle model at request time (so no precomputed edge/t), then placed exactly like edge/t/side/gap.
    /// </summary>
    public partial class Api
    {
        static string Inv(float v) => v.ToString(CultureInfo.InvariantCulture);

        /// <summary>Fills edge/t/side/gap in the request from the best roadside spot near "near"; null on success.</summary>
        internal ApiError ResolveNearRoad(Args a, Entity prefab, Dictionary<string, object> info)
        {
            float3 near;
            try { near = P(a.Raw("near")); } catch { return new ApiError("near must be [x, z]"); }
            var side = (a.Str("side", "auto") ?? "auto").ToLowerInvariant();
            if (side != "auto" && side != "both" && side != "left" && side != "right") return new ApiError("side must be auto|left|right");
            var pick = (a.Str("pick", "nearest") ?? "nearest").ToLowerInvariant();
            float radius = math.clamp(a.F("radius", 150f), 16f, 1000f);
            var q = new Pending { Method = "GET", Path = "/findspot", Body = new Args(null) };
            q.Query["prefab"] = PrefabName(prefab);
            q.Query["x"] = Inv(near.x);
            q.Query["z"] = Inv(near.z);
            q.Query["radius"] = Inv(radius);
            q.Query["n"] = "25";
            q.Query["side"] = side == "auto" ? "both" : side;
            if (a.Has("roadFilter")) q.Query["filter"] = a.Str("roadFilter");
            if (a.Has("maxSteepPct")) q.Query["maxSteepPct"] = Inv(a.F("maxSteepPct"));
            if (a.Has("gap")) q.Query["gap"] = Inv(a.F("gap"));
            var res = FindSpot(q);
            if (res is ApiError fe) return fe;
            var d = res as Dictionary<string, object>;
            var cands = (d != null && d.TryGetValue("candidates", out var cl) ? cl as List<object> : null) ?? new List<object>();
            var edgeCands = cands.OfType<Dictionary<string, object>>()
                .Where(c => c.TryGetValue("building", out var b) && b is Dictionary<string, object> bd && bd.ContainsKey("edge") && bd.ContainsKey("t"))
                .ToList();
            if (edgeCands.Count == 0)
            {
                var err = new ApiError("no roadside spot for " + PrefabName(prefab) + " within " + radius + " m of [" + near.x + ", " + near.z + "]");
                if (d != null && d.TryGetValue("rejected", out var rej)) err["rejected"] = rej;
                if (d != null && d.TryGetValue("hint", out var hint)) err["hint"] = hint;
                return err;
            }
            double Dist(Dictionary<string, object> c) => c.TryGetValue("distance", out var v) && v is double x ? x : double.MaxValue;
            var chosen = pick == "best" ? edgeCands[0] : edgeCands.OrderBy(Dist).First();
            var bld = (Dictionary<string, object>)chosen["building"];
            a.RawDict["edge"] = bld["edge"];
            a.RawDict["t"] = bld["t"];
            if (bld.TryGetValue("side", out var s)) a.RawDict["side"] = s;
            if (bld.TryGetValue("gap", out var g)) a.RawDict["gap"] = g;
            var ch = new Dictionary<string, object> { { "edge", bld["edge"] }, { "t", bld["t"] }, { "side", a.RawDict["side"] } };
            if (g != null) ch["gap"] = g;
            foreach (var k in new[] { "road", "distance", "score", "slack", "roomy", "x", "z", "angle", "front" })
                if (chosen.TryGetValue(k, out var v)) ch[k] = v;
            info["near"] = new Dictionary<string, object>
            {
                { "requested", F2(near) }, { "pick", pick }, { "candidates", edgeCands.Count }, { "chosen", ch },
            };
            return null;
        }

        /// <summary>After a placement: the road edge the building connected to and the (possibly re-created) road edges around it.</summary>
        internal void AddRoadsideInfo(Entity best, Dictionary<string, object> r)
        {
            if (best == Entity.Null || !em.Exists(best) || !em.HasComponent<ObjTransform>(best)) return;
            if (em.HasComponent<Building>(best))
            {
                var b = em.GetComponentData<Building>(best);
                r["roadEdge"] = b.m_RoadEdge != Entity.Null ? Id(b.m_RoadEdge) : null;
                if (b.m_RoadEdge != Entity.Null) r["roadEdgeT"] = Math.Round(b.m_CurvePosition, 4);
            }
            var pos = em.GetComponentData<ObjTransform>(best).m_Position.xz;
            var pf = em.GetComponentData<PrefabRef>(best).m_Prefab;
            float reach = 24f;
            if (em.HasComponent<BuildingData>(pf)) reach += 0.5f * math.length((float2)em.GetComponentData<BuildingData>(pf).m_LotSize * 8f);
            var near = new List<KeyValuePair<float, Entity>>();
            using (var arr = EdgeQuery().ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    if (!em.HasComponent<Road>(e)) continue;
                    float dist = MathUtils.Distance(em.GetComponentData<Curve>(e).m_Bezier.xz, pos, out float _t);
                    if (dist <= reach) near.Add(new KeyValuePair<float, Entity>(dist, e));
                }
            r["edgesNearby"] = near.OrderBy(kv => kv.Key).Take(12).Select(kv =>
            {
                var d = EdgeBrief(kv.Value);
                d["distance"] = Math.Round(kv.Key, 1);
                return (object)d;
            }).ToList();
        }
    }
}
