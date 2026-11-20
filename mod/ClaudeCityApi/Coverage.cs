using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Colossal.Mathematics;
using Game.Common;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using GB = Game.Buildings;
using GCit = Game.Citizens;
using GN = Game.Net;
using GP = Game.Prefabs;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        static bool TryParseCoverage(string s, out GN.CoverageService svc)
        {
            svc = GN.CoverageService.Park;
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.ToLowerInvariant())
            {
                case "entertainment": case "parks": svc = GN.CoverageService.Park; return true;
                case "health": case "hospital": svc = GN.CoverageService.Healthcare; return true;
                case "fire": svc = GN.CoverageService.FireRescue; return true;
                case "post": case "mail": svc = GN.CoverageService.PostService; return true;
                case "shelter": svc = GN.CoverageService.EmergencyShelter; return true;
                case "school": svc = GN.CoverageService.Education; return true;
            }
            return Enum.TryParse(s, true, out svc) && svc != GN.CoverageService.Count;
        }

        /// <summary>
        /// GET /coverage?service=Park|Healthcare|Police|FireRescue|Education|PostService|Welfare|EmergencyShelter&amp;cell=200
        /// [&amp;layer=roads|homes][&amp;x&amp;z&amp;size][&amp;min=10][&amp;limit=15]: the game's per-road-edge ServiceCoverage
        /// aggregated per cell (length-weighted), plus residential coverage read exactly like
        /// CitizenHappinessSystem (building road edge + curve position) with the worst-covered housing cells.
        /// </summary>
        object Coverage(Pending p)
        {
            if (!TryParseCoverage(p.Q("service", "Park"), out var svc))
                return new ApiError("service must be one of Park|Healthcare|Police|FireRescue|Education|PostService|Welfare|EmergencyShelter");
            int si = (int)svc;
            float cell = math.clamp(p.QF("cell", 200f), 32f, 2000f);
            string layer = (p.Q("layer", "roads") ?? "roads").ToLowerInvariant();
            int minHh = (int)math.max(1f, p.QF("min", 10f));
            int limit = (int)math.clamp(p.QF("limit", 15f), 1f, 100f);

            // --- road edges: coverage per edge (average of start/end value), at the edge midpoint
            var edgePos = new List<float2>();
            var edgeLen = new List<float>();
            var edgeCov = new List<float>();
            var eq = em.CreateEntityQuery(ComponentType.ReadOnly<GN.ServiceCoverage>(), ComponentType.ReadOnly<GN.Curve>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = eq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var buf = em.GetBuffer<GN.ServiceCoverage>(e, true);
                    if (buf.Length <= si) continue;
                    var c = buf[si].m_Coverage;
                    var cv = em.GetComponentData<GN.Curve>(e);
                    edgePos.Add(MathUtils.Position(cv.m_Bezier, 0.5f).xz);
                    edgeLen.Add(math.max(1f, cv.m_Length));
                    edgeCov.Add((c.x + c.y) * 0.5f);
                }

            // --- residential buildings: exactly what happiness reads
            var homePos = new List<float2>();
            var homeHh = new List<int>();
            var homeCov = new List<float>();
            var homeId = new List<Entity>();
            int noRoad = 0;
            var rq = em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<GB.ResidentialProperty>(), ComponentType.ReadOnly<ObjTransform>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<GB.Abandoned>());
            using (var arr = rq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    int hh = 0;
                    if (em.HasBuffer<GB.Renter>(e))
                    {
                        var rb = em.GetBuffer<GB.Renter>(e, true);
                        for (int i = 0; i < rb.Length; i++) if (em.HasComponent<GCit.Household>(rb[i].m_Renter)) hh++;
                    }
                    var b = em.GetComponentData<GB.Building>(e);
                    float v = 0f;
                    if (b.m_RoadEdge != Entity.Null && em.HasBuffer<GN.ServiceCoverage>(b.m_RoadEdge))
                        v = GN.NetUtils.GetServiceCoverage(em.GetBuffer<GN.ServiceCoverage>(b.m_RoadEdge, true), svc, b.m_CurvePosition);
                    else noRoad++;
                    homePos.Add(em.GetComponentData<ObjTransform>(e).m_Position.xz);
                    homeHh.Add(hh);
                    homeCov.Add(v);
                    homeId.Add(e);
                }

            // --- scale: the info view's colour range for this service, else the 95th percentile seen
            float scale = 0f;
            var iq = em.CreateEntityQuery(ComponentType.ReadOnly<GP.InfoviewCoverageData>());
            using (var arr = iq.ToComponentDataArray<GP.InfoviewCoverageData>(Allocator.Temp))
                foreach (var ic in arr) if (ic.m_Service == svc) scale = math.max(scale, ic.m_Range.max);
            string scaleSource = "infoview";
            if (scale <= 0f)
            {
                var sorted = edgeCov.Where(v => v > 0).OrderBy(v => v).ToList();
                scale = sorted.Count > 0 ? sorted[math.min(sorted.Count - 1, (int)(sorted.Count * 0.95f))] : 1f;
                scaleSource = "p95 of roads";
            }
            if (scale <= 0f) scale = 1f;

            // --- grid bounds: explicit window, else the housing extent (fallback: roads)
            float2 mn, mx;
            if (p.Query.ContainsKey("x") || p.Query.ContainsKey("z") || p.Query.ContainsKey("size"))
            {
                float size = math.max(cell * 4, p.QF("size", 4000f));
                var c = new float2(p.QF("x", 0f), p.QF("z", 0f));
                mn = c - size / 2; mx = c + size / 2;
            }
            else
            {
                var src = homePos.Count > 0 ? homePos : edgePos;
                if (src.Count == 0) return new ApiError("no roads or housing found", 409);
                mn = new float2(src.Min(v => v.x), src.Min(v => v.y)) - cell;
                mx = new float2(src.Max(v => v.x), src.Max(v => v.y)) + cell;
            }
            float ext = math.cmax(mx - mn);
            if (ext / cell > 150f) cell = (float)Math.Ceiling(ext / 150f / 10f) * 10f; // keep the grid readable
            int nx = math.max(1, (int)math.ceil((mx.x - mn.x) / cell)), nz = math.max(1, (int)math.ceil((mx.y - mn.y) / cell));

            var rLen = new float[nx, nz]; var rCov = new float[nx, nz];
            var hHh = new int[nx, nz]; var hCov = new float[nx, nz]; var hB = new int[nx, nz]; var hMin = new float[nx, nz];
            for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) hMin[i, j] = float.MaxValue;
            Func<float2, int2> idx = pos => new int2((int)math.floor((pos.x - mn.x) / cell), (int)math.floor((pos.y - mn.y) / cell));
            Func<int2, bool> inside = ij => ij.x >= 0 && ij.y >= 0 && ij.x < nx && ij.y < nz;
            for (int i = 0; i < edgePos.Count; i++)
            {
                var ij = idx(edgePos[i]); if (!inside(ij)) continue;
                rLen[ij.x, ij.y] += edgeLen[i]; rCov[ij.x, ij.y] += edgeLen[i] * edgeCov[i];
            }
            var hW = new int[nx, nz];
            for (int i = 0; i < homePos.Count; i++)
            {
                var ij = idx(homePos[i]); if (!inside(ij)) continue;
                int w = math.max(1, homeHh[i]); // household-weighted; empty homes still count once for the map
                hHh[ij.x, ij.y] += homeHh[i]; hB[ij.x, ij.y]++; hW[ij.x, ij.y] += w; hCov[ij.x, ij.y] += w * homeCov[i];
                hMin[ij.x, ij.y] = math.min(hMin[ij.x, ij.y], homeCov[i]);
            }
            Func<int, int, float> homeAvg = (i, j) => hW[i, j] > 0 ? hCov[i, j] / hW[i, j] : 0f;

            Func<float, char> digit = v => v < 0.05f * scale ? '0' : (char)('0' + math.clamp((int)math.round(v / scale * 9f), 1, 9));
            bool homes = layer == "homes" || layer == "housing" || layer == "residential";
            var sb = new StringBuilder();
            for (int r = 0; r < nz; r++)
            {
                int j = nz - 1 - r;
                sb.Append(((int)(mn.y + (j + 0.5f) * cell)).ToString().PadLeft(7)).Append(' ');
                for (int i = 0; i < nx; i++)
                {
                    if (homes) sb.Append(hB[i, j] == 0 ? '.' : digit(homeAvg(i, j)));
                    else sb.Append(rLen[i, j] <= 0 ? '.' : digit(rCov[i, j] / rLen[i, j]));
                }
                sb.Append('\n');
            }

            // --- providers of this coverage (for "how far is the nearest one")
            var providers = new List<KeyValuePair<Entity, float3>>();
            var provRange = new Dictionary<Entity, float>();
            var pq = em.CreateEntityQuery(ComponentType.ReadOnly<GB.Building>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = pq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var pf = em.GetComponentData<GP.PrefabRef>(e).m_Prefab;
                    if (!em.HasComponent<GP.CoverageData>(pf)) continue;
                    var cd = em.GetComponentData<GP.CoverageData>(pf);
                    if (cd.m_Service != svc) continue;
                    providers.Add(new KeyValuePair<Entity, float3>(e, em.GetComponentData<ObjTransform>(e).m_Position));
                    provRange[e] = cd.m_Range;
                }
            Func<float2, object> nearest = pos =>
            {
                if (providers.Count == 0) return null;
                var best = providers.OrderBy(kv => math.distance(kv.Value.xz, pos)).First();
                return new Dictionary<string, object>
                {
                    { "id", Id(best.Key) }, { "prefab", PrefabName(em.GetComponentData<GP.PrefabRef>(best.Key).m_Prefab) },
                    { "distance", Math.Round(math.distance(best.Value.xz, pos)) }, { "range", Math.Round(provRange[best.Key]) },
                };
            };

            // --- happiness view (formulas from CitizenHappinessSystem)
            GP.CitizenHappinessParameterData hp;
            bool hasHp = TrySingleton(out hp);
            Func<float, object> bonus = v =>
            {
                if (!hasHp) return null;
                switch (svc)
                {
                    case GN.CoverageService.Park: return Math.Round(hp.m_EntertainmentWellbeingMultiplier * math.min(1f, math.sqrt(v / 1.5f)), 1);
                    case GN.CoverageService.Healthcare: return Math.Round(hp.m_HealthCareWellbeingMultiplier * v, 1);
                    case GN.CoverageService.Education: return Math.Round(hp.m_EducationWellbeingMultiplier * (v - hp.m_NeutralEducation), 1);
                    default: return null;
                }
            };

            var cells = new List<Dictionary<string, object>>();
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (hHh[i, j] < minHh) continue;
                    float avg = homeAvg(i, j);
                    var center = new float2(mn.x + (i + 0.5f) * cell, mn.y + (j + 0.5f) * cell);
                    var d = new Dictionary<string, object>
                    {
                        { "center", new List<object> { Math.Round(center.x), Math.Round(center.y) } },
                        { "households", hHh[i, j] }, { "buildings", hB[i, j] },
                        { "avgCoverage", Math.Round(avg, 3) }, { "minCoverage", Math.Round(hMin[i, j], 3) },
                        { "pctOfScale", Math.Round(100.0 * avg / scale) },
                    };
                    var bo = bonus(avg); if (bo != null) d["wellbeingBonus"] = bo;
                    d["nearestProvider"] = nearest(center);
                    cells.Add(d);
                }
            var worst = cells.OrderBy(d => (double)d["avgCoverage"]).ThenByDescending(d => (int)d["households"]).Take(limit).Cast<object>().ToList();

            // --- residential summary (household weighted)
            long hhTotal = 0; double covSum = 0, bonusSum = 0;
            long none = 0, low = 0, mid = 0, good = 0;
            for (int i = 0; i < homePos.Count; i++)
            {
                int w = homeHh[i]; if (w <= 0) continue;
                float v = homeCov[i];
                hhTotal += w; covSum += (double)w * v;
                var bo = bonus(v); if (bo is double bd) bonusSum += w * bd;
                float rel = v / scale;
                if (rel < 0.05f) none += w; else if (rel < 0.33f) low += w; else if (rel < 0.66f) mid += w; else good += w;
            }
            Func<long, object> pct = n => hhTotal > 0 ? Math.Round(100.0 * n / hhTotal, 1) : 0.0;
            var summary = new Dictionary<string, object>
            {
                { "households", hhTotal }, { "residentialBuildings", homePos.Count }, { "withoutRoad", noRoad },
                { "avgCoverage", hhTotal > 0 ? Math.Round(covSum / hhTotal, 3) : 0.0 },
                { "householdsPct", new Dictionary<string, object> { { "none(<5%)", pct(none) }, { "low(<33%)", pct(low) }, { "medium(<66%)", pct(mid) }, { "good", pct(good) } } },
                { "providers", providers.Count },
            };
            if (hasHp && (svc == GN.CoverageService.Park || svc == GN.CoverageService.Healthcare || svc == GN.CoverageService.Education))
                summary["avgWellbeingBonus"] = hhTotal > 0 ? Math.Round(bonusSum / hhTotal, 2) : 0.0;

            var res = new Dictionary<string, object>
            {
                { "service", svc.ToString() }, { "layer", homes ? "homes" : "roads" },
                { "x0", mn.x }, { "z0", mn.y }, { "cell", cell }, { "cols", nx }, { "rows", nz },
                { "scale", Math.Round(scale, 3) }, { "scaleSource", scaleSource },
                { "legend", (homes ? ". no housing" : ". no road") + " | 0 uncovered (<5% of scale) | 1-9 coverage as ninths of scale; rows north->south, columns west->east" },
                { "summary", summary },
                { "worstResidentialCells", worst },
                { "grid", sb.ToString() },
            };
            if (hasHp)
            {
                if (svc == GN.CoverageService.Park) res["happiness"] = $"Entertainment wellbeing = {hp.m_EntertainmentWellbeingMultiplier} * min(1, sqrt(coverage/1.5)) (city modifiers ignored); full bonus at coverage >= 1.5";
                else if (svc == GN.CoverageService.Healthcare) res["happiness"] = $"Healthcare: health += {hp.m_HealthCareHealthMultiplier}*coverage, wellbeing += {hp.m_HealthCareWellbeingMultiplier}*coverage";
                else if (svc == GN.CoverageService.Education) res["happiness"] = $"Education wellbeing = sqrt(children) * {hp.m_EducationWellbeingMultiplier} * (coverage - {hp.m_NeutralEducation}); wellbeingBonus shown for one child";
                else if (svc == GN.CoverageService.Welfare) res["happiness"] = $"Welfare multiplier {hp.m_WelfareMultiplier} (see CitizenHappinessSystem.GetWelfareValue)";
                else if (svc == GN.CoverageService.Police) res["happiness"] = "Police coverage lowers crime production (CrimeProducer); the Crime happiness factor comes from crime level, not coverage directly";
            }
            return res;
        }

        // ------------------------------------------------------------------ /hotspots

        static string KindForIcon(string type)
        {
            var t = (type ?? "").ToLowerInvariant();
            if (t.Contains("garbage")) return "garbage";
            if (t.Contains("hearse") || t.Contains("dead") || t.Contains("death") || t.Contains("cemetery")) return "deathcare";
            if (t.Contains("ambulance") || t.Contains("sick") || t.Contains("injur") || t.Contains("health")) return "healthcare";
            if (t.Contains("crime") || t.Contains("police")) return "police";
            if (t.Contains("fire")) return "fire";
            if (t.Contains("mail") || t.Contains("post")) return "post";
            return null;
        }

        /// <summary>
        /// GET /hotspots?type=Garbage&amp;cell=400[&amp;limit=20]: notification icons whose prefab name contains
        /// <c>type</c>, clustered into cells with counts, the dominant building prefabs and the nearest
        /// facility of the matching service (with its status), so the player sees where complaints concentrate.
        /// </summary>
        object Hotspots(Pending p)
        {
            var type = p.Q("type", "Garbage");
            float cell = math.clamp(p.QF("cell", 400f), 50f, 5000f);
            int limit = (int)math.clamp(p.QF("limit", 20f), 1f, 200f);
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Notifications.Icon>(), ComponentType.ReadOnly<GP.PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var groups = new Dictionary<long, HotCell>();
            var iconTypes = new Dictionary<string, int>();
            var allPos = new List<float2>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    Entity owner = em.HasComponent<Owner>(e) ? em.GetComponentData<Owner>(e).m_Owner : Entity.Null;
                    if (owner != Entity.Null && em.HasComponent<Temp>(owner)) continue;
                    var name = PrefabName(em.GetComponentData<GP.PrefabRef>(e).m_Prefab) ?? "?";
                    if (type != null && type != "all" && name.IndexOf(type, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    iconTypes[name] = iconTypes.TryGetValue(name, out var ic) ? ic + 1 : 1;
                    var pos = em.GetComponentData<Game.Notifications.Icon>(e).m_Location.xz;
                    allPos.Add(pos);
                    // the building the complaint is about: the owner itself, a citizen's current building, or a sub-object's owner
                    Entity bld = owner;
                    if (bld != Entity.Null && !em.HasComponent<GB.Building>(bld))
                    {
                        if (em.HasComponent<GCit.CurrentBuilding>(bld)) bld = em.GetComponentData<GCit.CurrentBuilding>(bld).m_CurrentBuilding;
                        else if (em.HasComponent<Owner>(bld)) bld = em.GetComponentData<Owner>(bld).m_Owner;
                    }
                    string bp = bld != Entity.Null && em.Exists(bld) && em.HasComponent<GP.PrefabRef>(bld) ? PrefabName(em.GetComponentData<GP.PrefabRef>(bld).m_Prefab) : "?";
                    long key = ((long)(int)math.floor(pos.x / cell) << 32) ^ (uint)(int)math.floor(pos.y / cell);
                    if (!groups.TryGetValue(key, out var g)) groups[key] = g = new HotCell();
                    g.Count++; g.Sum += pos;
                    g.Prefabs[bp] = g.Prefabs.TryGetValue(bp, out var c) ? c + 1 : 1;
                    if (g.Samples.Count < 3 && bld != Entity.Null) g.Samples.Add(Id(bld));
                }

            var kind = KindForIcon(type);
            List<SvcInfo> facilities = kind != null ? CollectServices(kind) : new List<SvcInfo>();
            Func<float2, object> nearest = pos =>
            {
                if (facilities.Count == 0) return null;
                var f = facilities.OrderBy(s => math.distance(s.Pos.xz, pos)).First();
                var d = new Dictionary<string, object>
                {
                    { "id", Id(f.Entity) }, { "prefab", f.Prefab }, { "type", f.Type }, { "distance", Math.Round(math.distance(f.Pos.xz, pos)) },
                    { "status", f.Status.Count == 0 ? new List<string> { "ok" } : f.Status },
                };
                if (f.VehCapacity > 0) d["vehicles"] = f.VehInUse + "/" + f.VehUsable + " (cap " + f.VehCapacity + ")";
                return d;
            };

            int total = allPos.Count;
            var cellsOut = groups.Values.OrderByDescending(g => g.Count).Take(limit).Select(g =>
            {
                var center = g.Sum / g.Count;
                var d = new Dictionary<string, object>
                {
                    { "center", new List<object> { Math.Round(center.x), Math.Round(center.y) } },
                    { "count", g.Count }, { "pct", total > 0 ? Math.Round(100.0 * g.Count / total, 1) : 0.0 },
                    { "topPrefabs", g.Prefabs.OrderByDescending(kv => kv.Value).Take(3).ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                    { "sampleBuildings", g.Samples },
                };
                var n = nearest(center); if (n != null) d["nearestFacility"] = n;
                return (object)d;
            }).ToList();

            var res = new Dictionary<string, object>
            {
                { "type", type }, { "cell", cell }, { "total", total }, { "cells", groups.Count },
                { "iconTypes", iconTypes.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "hotspots", cellsOut },
            };
            if (kind != null)
            {
                res["service"] = kind;
                if (facilities.Count > 0 && total > 0)
                {
                    var dists = allPos.Select(pp => facilities.Min(s => math.distance(s.Pos.xz, pp))).OrderBy(v => v).ToList();
                    res["distanceToNearestFacility"] = new Dictionary<string, object>
                    {
                        { "median", Math.Round(dists[dists.Count / 2]) }, { "p90", Math.Round(dists[(int)(dists.Count * 0.9f)]) }, { "max", Math.Round(dists[dists.Count - 1]) },
                        { "note", "straight-line metres from each icon to the nearest " + kind + " facility" },
                    };
                }
                else if (facilities.Count == 0) res["distanceToNearestFacility"] = "no " + kind + " facility";
            }
            return res;
        }

        class HotCell
        {
            public int Count;
            public float2 Sum;
            public readonly Dictionary<string, int> Prefabs = new Dictionary<string, int>();
            public readonly List<string> Samples = new List<string>();
        }
    }
}
