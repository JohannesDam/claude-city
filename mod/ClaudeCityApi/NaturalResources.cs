using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using GA = Game.Areas;
using GO = Game.Objects;
using ObjTransform = Game.Objects.Transform;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        static readonly string[] CellResources = { "fertility", "ore", "oil", "fish" };
        const float MaxBaseResource = 10000f; // NaturalResourceSystem.MAX_BASE_RESOURCES

        /// <summary>Available share (0..1) of a cell resource at a position: (base - used) / 10000, bilinear like the game.</summary>
        static float CellResource(string kind, float3 pos, NativeArray<NaturalResourceCell> map)
        {
            NaturalResourceAmount a;
            switch (kind)
            {
                case "fertility": a = NaturalResourceSystem.GetFertilityAmount(pos, map); break;
                case "ore": a = NaturalResourceSystem.GetOreAmount(pos, map); break;
                case "oil": a = NaturalResourceSystem.GetOilAmount(pos, map); break;
                case "fish": a = NaturalResourceSystem.GetFishAmount(pos, map); break;
                default: return 0f;
            }
            return math.max(0f, (float)a.m_Base - a.m_Used) / MaxBaseResource;
        }

        /// <summary>
        /// Wood of harvestable trees (the query NaturalResourcesInfoviewUISystem uses: Tree + Plant with
        /// Decoration disabled), binned into an nx*nz grid starting at (x0, z0). Damage is ignored.
        /// </summary>
        float[] BinWood(float x0, float z0, float cell, int nx, int nz, out int trees)
        {
            var bins = new float[nx * nz];
            trees = 0;
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<GO.Tree, GO.Plant, ObjTransform, PrefabRef>()
                .WithDisabled<GO.Decoration>()
                .WithNone<Deleted, Temp>();
            EntityQuery q;
            try { q = qb.Build(em); }
            finally { qb.Dispose(); }
            var treeData = new Dictionary<Entity, TreeData>();
            using (var tr = q.ToComponentDataArray<ObjTransform>(Allocator.Temp))
            using (var tt = q.ToComponentDataArray<GO.Tree>(Allocator.Temp))
            using (var pl = q.ToComponentDataArray<GO.Plant>(Allocator.Temp))
            using (var pr = q.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < tr.Length; i++)
                {
                    var pos = tr[i].m_Position;
                    int ix = (int)math.floor((pos.x - x0) / cell), iz = (int)math.floor((pos.z - z0) / cell);
                    if (ix < 0 || iz < 0 || ix >= nx || iz >= nz) continue;
                    var prefab = pr[i].m_Prefab;
                    if (!treeData.TryGetValue(prefab, out var td))
                    {
                        td = em.HasComponent<TreeData>(prefab) ? em.GetComponentData<TreeData>(prefab) : default(TreeData);
                        treeData[prefab] = td;
                    }
                    float w = GO.ObjectUtils.CalculateWoodAmount(tt[i], pl[i], default(GO.Damaged), td, false);
                    if (w <= 0) continue;
                    bins[iz * nx + ix] += w;
                    trees++;
                }
            }
            return bins;
        }

        /// <summary>Best windows (k x k cells, owned cells only) with non-maximum suppression.</summary>
        static List<object> BestSpots(float[] v, bool[] owned, int nx, int nz, float x0, float z0, float cell, int k, int top, bool wood, float minValue)
        {
            var cand = new List<KeyValuePair<float, int2>>();
            int h = k / 2;
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    if (!owned[iz * nx + ix]) continue;
                    float s = 0; int cnt = 0;
                    for (int dz = -h; dz <= h; dz++)
                        for (int dx = -h; dx <= h; dx++)
                        {
                            int jx = ix + dx, jz = iz + dz;
                            if (jx < 0 || jz < 0 || jx >= nx || jz >= nz || !owned[jz * nx + jx]) continue;
                            s += v[jz * nx + jx]; cnt++;
                        }
                    if (cnt == 0 || s <= minValue) continue;
                    cand.Add(new KeyValuePair<float, int2>(s, new int2(ix, iz)));
                }
            cand.Sort((a, b) => b.Key.CompareTo(a.Key));
            var picked = new List<int2>();
            var res = new List<object>();
            float sep = math.max(k, 2) * 1.5f;
            foreach (var c in cand)
            {
                if (picked.Any(pp => math.distance((float2)pp, (float2)c.Value) < sep)) continue;
                picked.Add(c.Value);
                float cx = x0 + (c.Value.x + 0.5f) * cell, cz = z0 + (c.Value.y + 0.5f) * cell;
                int cells = k * k;
                var d = new Dictionary<string, object> { { "x", Math.Round(cx) }, { "z", Math.Round(cz) }, { "window", k * cell } };
                if (wood) { d["wood"] = Math.Round(c.Key); d["woodPerHa"] = Math.Round(c.Key / (cells * cell * cell / 10000f), 1); }
                else { d["avg"] = Math.Round(c.Key / cells, 3); d["peak"] = Math.Round(MaxIn(v, nx, nz, c.Value, h), 3); }
                res.Add(d);
                if (res.Count >= top) break;
            }
            return res;
        }

        static float MaxIn(float[] v, int nx, int nz, int2 c, int h)
        {
            float m = 0;
            for (int dz = -h; dz <= h; dz++)
                for (int dx = -h; dx <= h; dx++)
                {
                    int jx = c.x + dx, jz = c.y + dz;
                    if (jx < 0 || jz < 0 || jx >= nx || jz >= nz) continue;
                    m = math.max(m, v[jz * nx + jx]);
                }
            return m;
        }

        /// <summary>
        /// GET /resources?x&amp;z&amp;radius=1500&amp;cell=64&amp;resource=all|fertility|forest|ore|oil|fish&amp;top=8&amp;mapCell=128&amp;window=200
        /// Natural resources from NaturalResourceSystem cells (fertility/ore/oil/fish, available = base - used)
        /// and harvestable trees (forest). Per resource an ASCII grid of the window, the best spots in the
        /// window and map-wide inside owned tiles, plus per-map-tile totals (MapFeatureElement, what the
        /// natural resources info view sums).
        /// </summary>
        object NaturalResources(Pending p)
        {
            float cell = math.clamp(p.QF("cell", 64f), 16f, 512f);
            float radius = p.Query.ContainsKey("radius") ? p.QF("radius", 1500f) : p.QF("size", 3000f) / 2f;
            int n = math.clamp((int)math.ceil(2f * radius / cell), 4, 120);
            float cx = p.QF("x", 0f), cz = p.QF("z", 0f);
            float x0 = cx - n * cell / 2f, z0 = cz - n * cell / 2f;
            int top = math.clamp((int)p.QF("top", 8f), 1, 30);
            float mapCell = math.clamp(p.QF("mapCell", 128f), 32f, 512f);
            float windowM = math.clamp(p.QF("window", 200f), 16f, 2000f);
            var want = (p.Q("resource", "all") ?? "all").ToLowerInvariant();
            var kinds = want == "all" ? new List<string> { "fertility", "forest", "ore", "oil" }
                : want.Split(',').Select(s => s.Trim()).Where(s => s == "forest" || CellResources.Contains(s)).ToList();
            if (kinds.Count == 0) return new ApiError("resource must be all or a comma list of fertility, forest, ore, oil, fish");

            var nrs = Sys<NaturalResourceSystem>();
            JobHandle deps;
            var map = nrs.GetMap(true, out deps);
            deps.Complete();
            var tiles = OwnedTilePolys();

            // ---- window grid
            var owned = new bool[n * n];
            for (int iz = 0; iz < n; iz++)
                for (int ix = 0; ix < n; ix++)
                    owned[iz * n + ix] = InOwned(tiles, new float2(x0 + (ix + 0.5f) * cell, z0 + (iz + 0.5f) * cell));
            int kWin = math.max(1, (int)math.round(windowM / cell)); if (kWin % 2 == 0) kWin++;
            var grids = new Dictionary<string, object>();
            var best = new Dictionary<string, object>();
            var scales = new Dictionary<string, object>();
            int treesInWindow = 0;
            foreach (var kind in kinds)
            {
                float[] v;
                bool wood = kind == "forest";
                if (wood) v = BinWood(x0, z0, cell, n, n, out treesInWindow);
                else
                {
                    v = new float[n * n];
                    for (int iz = 0; iz < n; iz++)
                        for (int ix = 0; ix < n; ix++)
                            v[iz * n + ix] = CellResource(kind, new float3(x0 + (ix + 0.5f) * cell, 0, z0 + (iz + 0.5f) * cell), map);
                }
                float scale = 1f;
                if (wood) { scale = v.Length > 0 ? v.Max() : 0f; scales[kind] = new Dictionary<string, object> { { "level9Wood", Math.Round(scale) }, { "trees", treesInWindow } }; }
                var sb = new StringBuilder();
                for (int r = 0; r < n; r++)
                {
                    int iz = n - 1 - r;
                    float z = z0 + (iz + 0.5f) * cell;
                    sb.Append(((int)z).ToString().PadLeft(7)).Append(' ');
                    for (int ix = 0; ix < n; ix++)
                    {
                        float val = v[iz * n + ix];
                        float rel = wood ? (scale > 0 ? val / scale : 0) : val;
                        bool own = owned[iz * n + ix];
                        if (rel < 0.03f) { sb.Append(own ? '.' : ','); continue; }
                        int lvl = math.clamp((int)math.ceil(rel * 9f), 1, 9);
                        sb.Append(own ? (char)('0' + lvl) : (char)('a' + lvl - 1));
                    }
                    sb.Append('\n');
                }
                grids[kind] = sb.ToString();
                best[kind] = BestSpots(v, owned, n, n, x0, z0, cell, kWin, top, wood, wood ? 0f : 0.05f * kWin * kWin);
            }

            // ---- map-wide inside owned tiles (coarse grid over the owned bounds)
            var mapWide = new Dictionary<string, object>();
            if (tiles.Count > 0)
            {
                var ob = new Bounds2(new float2(float.MaxValue), new float2(float.MinValue));
                foreach (var t in tiles) { ob.min = math.min(ob.min, t.Key.min); ob.max = math.max(ob.max, t.Key.max); }
                int mx = math.clamp((int)math.ceil((ob.max.x - ob.min.x) / mapCell), 1, 400);
                int mz = math.clamp((int)math.ceil((ob.max.y - ob.min.y) / mapCell), 1, 400);
                var mOwned = new bool[mx * mz];
                for (int iz = 0; iz < mz; iz++)
                    for (int ix = 0; ix < mx; ix++)
                        mOwned[iz * mx + ix] = InOwned(tiles, new float2(ob.min.x + (ix + 0.5f) * mapCell, ob.min.y + (iz + 0.5f) * mapCell));
                int kMap = math.max(1, (int)math.round(windowM * 1.5f / mapCell)); if (kMap % 2 == 0) kMap++;
                foreach (var kind in kinds)
                {
                    bool wood = kind == "forest";
                    float[] v;
                    if (wood) v = BinWood(ob.min.x, ob.min.y, mapCell, mx, mz, out _);
                    else
                    {
                        v = new float[mx * mz];
                        for (int iz = 0; iz < mz; iz++)
                            for (int ix = 0; ix < mx; ix++)
                                if (mOwned[iz * mx + ix]) v[iz * mx + ix] = CellResource(kind, new float3(ob.min.x + (ix + 0.5f) * mapCell, 0, ob.min.y + (iz + 0.5f) * mapCell), map);
                    }
                    mapWide[kind] = BestSpots(v, mOwned, mx, mz, ob.min.x, ob.min.y, mapCell, kMap, top, wood, wood ? 0f : 0.05f * kMap * kMap);
                }
                mapWide["ownedBounds"] = new List<object> { Math.Round(ob.min.x), Math.Round(ob.min.y), Math.Round(ob.max.x), Math.Round(ob.max.y) };
                mapWide["cell"] = mapCell;
            }

            // ---- per map tile (MapFeatureElement, as summed by the natural resources info view)
            var tileInfo = TileFeatures(kinds);

            return new Dictionary<string, object>
            {
                { "x0", x0 }, { "z0", z0 }, { "cell", cell }, { "n", n },
                { "legend", "rows north->south, columns west->east. fertility/ore/oil/fish: 1-9 = available share (base-used)/10000 in tenths, '.' < 3%; forest: 1-9 relative to the richest cell (level9Wood). digits = inside owned tiles, letters a-i = same levels outside owned tiles (',' = empty there)" },
                { "grids", grids },
                { "best", best },
                { "bestNote", "best = " + (kWin * cell) + " m windows of owned cells; avg = mean available share (fertility/ore/oil/fish, 1 = full), wood = summed wood; check free land with /freeland or /map before placing" },
                { "scales", scales },
                { "mapWide", mapWide },
                { "tiles", tileInfo },
            };
        }

        /// <summary>Per owned tile MapFeatureElement amounts: totals, top tiles, and best unowned tiles.</summary>
        Dictionary<string, object> TileFeatures(List<string> kinds)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<GA.MapTile>(), ComponentType.ReadOnly<GA.Node>(), ComponentType.ReadOnly<GA.MapFeatureElement>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var feat = new Dictionary<string, GA.MapFeature> { { "fertility", GA.MapFeature.FertileLand }, { "forest", GA.MapFeature.Forest }, { "ore", GA.MapFeature.Ore }, { "oil", GA.MapFeature.Oil }, { "fish", GA.MapFeature.Fish } };
            var rows = new List<KeyValuePair<Dictionary<string, object>, float[]>>();
            var ownedFlags = new List<bool>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var t in arr)
                {
                    var nodes = em.GetBuffer<GA.Node>(t, true);
                    var b = new Bounds2(new float2(float.MaxValue), new float2(float.MinValue));
                    for (int i = 0; i < nodes.Length; i++) { b.min = math.min(b.min, nodes[i].m_Position.xz); b.max = math.max(b.max, nodes[i].m_Position.xz); }
                    var mf = em.GetBuffer<GA.MapFeatureElement>(t, true);
                    var vals = new float[(int)GA.MapFeature.Count * 2];
                    for (int i = 0; i < mf.Length && i < (int)GA.MapFeature.Count; i++) { vals[i * 2] = mf[i].m_Amount; vals[i * 2 + 1] = mf[i].m_RenewalRate; }
                    var c = (b.min + b.max) * 0.5f;
                    rows.Add(new KeyValuePair<Dictionary<string, object>, float[]>(new Dictionary<string, object>
                    {
                        { "id", Id(t) }, { "center", new List<object> { Math.Round(c.x), Math.Round(c.y) } },
                        { "bounds", new List<object> { Math.Round(b.min.x), Math.Round(b.min.y), Math.Round(b.max.x), Math.Round(b.max.y) } },
                    }, vals));
                    ownedFlags.Add(!em.HasComponent<Native>(t));
                }
            var res = new Dictionary<string, object>();
            var totals = new Dictionary<string, object>();
            foreach (var kind in kinds)
            {
                int fi = (int)feat[kind];
                double sum = 0, ren = 0;
                var own = new List<KeyValuePair<float, Dictionary<string, object>>>();
                var other = new List<KeyValuePair<float, Dictionary<string, object>>>();
                for (int i = 0; i < rows.Count; i++)
                {
                    float amt = rows[i].Value[fi * 2];
                    if (ownedFlags[i]) { sum += amt; ren += rows[i].Value[fi * 2 + 1]; }
                    if (amt <= 0) continue;
                    var d = new Dictionary<string, object>(rows[i].Key) { { "amount", Math.Round(amt) } };
                    (ownedFlags[i] ? own : other).Add(new KeyValuePair<float, Dictionary<string, object>>(amt, d));
                }
                totals[kind] = new Dictionary<string, object> { { "amount", Math.Round(sum) }, { "renewalRate", Math.Round(ren, 2) } };
                res[kind] = new Dictionary<string, object>
                {
                    { "ownedTop", own.OrderByDescending(x => x.Key).Take(5).Select(x => (object)x.Value).ToList() },
                    { "unownedTop", other.OrderByDescending(x => x.Key).Take(3).Select(x => (object)x.Value).ToList() },
                };
            }
            res["ownedTotals"] = totals;
            res["note"] = "MapFeatureElement per map tile (the natural resources info view sums these over owned tiles); unownedTop = candidates for /buytile";
            return res;
        }
    }
}
