using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using NetEdge = Game.Net.Edge;
using ObjTransform = Game.Objects.Transform;
using AreaNode = Game.Areas.Node;

namespace ClaudeCityBridge
{
    /// <summary>Renders an area of the city as an ASCII grid so the agent can "see" without screenshots.</summary>
    public static class MapText
    {
        public static object Render(Api api, Pending p)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            float cell = math.clamp(p.QF("cell", 32f), 4f, 256f);
            float size = math.clamp(p.QF("size", 2048f), cell * 8, cell * 160);
            float cx = p.QF("x", 0f), cz = p.QF("z", 0f);
            int n = (int)(size / cell);
            float x0 = cx - n * cell / 2, z0 = cz - n * cell / 2;
            var g = new char[n, n]; // [row, col], row 0 = north (max z)

            // ownership polygons
            var owned = new List<float2[]>();
            var tq = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Areas.MapTile>(), ComponentType.ReadOnly<AreaNode>(), ComponentType.Exclude<Deleted>());
            using (var tiles = tq.ToEntityArray(Allocator.Temp))
                foreach (var t in tiles)
                {
                    if (em.HasComponent<Native>(t)) continue;
                    var b = em.GetBuffer<AreaNode>(t, true);
                    var poly = new float2[b.Length];
                    for (int i = 0; i < b.Length; i++) poly[i] = b[i].m_Position.xz;
                    owned.Add(poly);
                }

            var hd = world.GetOrCreateSystemManaged<TerrainSystem>().GetHeightData(false);
            JobHandle deps;
            var wd = world.GetOrCreateSystemManaged<WaterSystem>().GetSurfaceData(out deps);
            deps.Complete();
            var heights = new float[n, n];
            float hmin = float.MaxValue, hmax = float.MinValue;
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    var pos = Center(r, c, n, cell, x0, z0);
                    float h = TerrainUtils.SampleHeight(ref hd, pos);
                    heights[r, c] = h;
                    float depth = WaterUtils.SampleDepth(ref wd, pos);
                    bool own = InAny(owned, pos.xz);
                    if (depth > 0.5f) g[r, c] = '~';
                    else { g[r, c] = own ? '.' : ','; hmin = math.min(hmin, h); hmax = math.max(hmax, h); }
                }
            // steep terrain marker
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    if (g[r, c] == '~') continue;
                    float m = 0;
                    if (r > 0) m = math.max(m, math.abs(heights[r, c] - heights[r - 1, c]));
                    if (c > 0) m = math.max(m, math.abs(heights[r, c] - heights[r, c - 1]));
                    if (m / cell > 0.2f) g[r, c] = '^';
                }

            // zones
            var zoneSys = world.GetOrCreateSystemManaged<Game.Prefabs.ZoneSystem>();
            var zoneChar = new Dictionary<ushort, char>();
            var bq = em.CreateEntityQuery(ComponentType.ReadOnly<Block>(), ComponentType.ReadOnly<Cell>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var blocks = bq.ToEntityArray(Allocator.Temp))
                foreach (var be in blocks)
                {
                    var block = em.GetComponentData<Block>(be);
                    if (math.distance(block.m_Position.xz, new float2(cx, cz)) > size) continue;
                    var cells = em.GetBuffer<Cell>(be, true);
                    for (int y = 0; y < block.m_Size.y; y++)
                        for (int x = 0; x < block.m_Size.x; x++)
                        {
                            int idx = y * block.m_Size.x + x;
                            if (idx >= cells.Length) continue;
                            var cl = cells[idx];
                            if ((cl.m_State & CellFlags.Visible) == 0) continue;
                            if (cl.m_Zone.m_Index == 0) continue;
                            var cp = ZoneUtils.GetCellPosition(block, new int2(x, y));
                            if (!Grid(cp, n, cell, x0, z0, out int gr, out int gc)) continue;
                            if (!zoneChar.TryGetValue(cl.m_Zone.m_Index, out var ch))
                            {
                                ch = ZoneChar(api.PrefabName(zoneSys.GetPrefab(cl.m_Zone)));
                                zoneChar[cl.m_Zone.m_Index] = ch;
                            }
                            if (g[gr, gc] == '.' || g[gr, gc] == ',' || g[gr, gc] == '^') g[gr, gc] = ch;
                        }
                }

            // nets
            var nq = em.CreateEntityQuery(ComponentType.ReadOnly<NetEdge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var edges = nq.ToEntityArray(Allocator.Temp))
                foreach (var e in edges)
                {
                    var cv = em.GetComponentData<Curve>(e);
                    bool road = em.HasComponent<Road>(e);
                    bool owned2 = em.HasComponent<Owner>(e);
                    var name = api.PrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab) ?? "";
                    char ch;
                    if (road) ch = owned2 ? ':' : (name.IndexOf("Highway", StringComparison.OrdinalIgnoreCase) >= 0 ? 'H' : '#');
                    else if (name.IndexOf("Train", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Track", StringComparison.OrdinalIgnoreCase) >= 0) ch = owned2 ? ':' : 'T';
                    else if (name.IndexOf("Power", StringComparison.OrdinalIgnoreCase) >= 0) ch = 'p';
                    else continue; // pipes, paths, lanes etc.
                    int steps = math.max(2, (int)(cv.m_Length / math.min(cell * 0.5f, 4f)));
                    for (int s = 0; s <= steps; s++)
                    {
                        var pos = MathUtils.Position(cv.m_Bezier, s / (float)steps);
                        if (!Grid(pos, n, cell, x0, z0, out int gr, out int gc)) continue;
                        if (ch == 'p' && (g[gr, gc] == '#' || g[gr, gc] == 'H')) continue;
                        if (ch == ':' && g[gr, gc] != '.' && g[gr, gc] != ',') continue;
                        g[gr, gc] = ch;
                    }
                }

            // buildings
            var buq = em.CreateEntityQuery(ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            int bcount = 0;
            using (var arr = buq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                {
                    var pos = em.GetComponentData<ObjTransform>(e).m_Position;
                    if (!Grid(pos, n, cell, x0, z0, out int gr, out int gc)) continue;
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    char ch = 'B';
                    if (em.HasComponent<ServiceObjectData>(prefab)) ch = 'S';
                    else if (em.HasComponent<SpawnableBuildingData>(prefab))
                        ch = char.ToUpperInvariant(ZoneChar(api.PrefabName(em.GetComponentData<SpawnableBuildingData>(prefab).m_ZonePrefab)));
                    if (em.HasComponent<Abandoned>(e)) ch = 'A';
                    if (em.HasComponent<Game.Objects.OutsideConnection>(e)) ch = 'X';
                    g[gr, gc] = ch;
                    bcount++;
                }
            var oq = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Objects.OutsideConnection>(), ComponentType.ReadOnly<ObjTransform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var arr = oq.ToEntityArray(Allocator.Temp))
                foreach (var e in arr)
                    if (Grid(em.GetComponentData<ObjTransform>(e).m_Position, n, cell, x0, z0, out int gr, out int gc)) g[gr, gc] = 'X';

            var sb = new StringBuilder();
            // column ruler: x coordinate every 10 columns
            sb.Append("        ");
            for (int c = 0; c < n; c += 10)
            {
                var lbl = ((int)(x0 + c * cell)).ToString();
                sb.Append(lbl.PadRight(10).Substring(0, 10));
            }
            sb.Append('\n');
            for (int r = 0; r < n; r++)
            {
                float zr = z0 + (n - 1 - r + 0.5f) * cell;
                sb.Append(((int)zr).ToString().PadLeft(7)).Append(' ');
                for (int c = 0; c < n; c++) sb.Append(g[r, c]);
                sb.Append('\n');
            }
            return new Dictionary<string, object>
            {
                { "x0", x0 }, { "z0", z0 }, { "cell", cell }, { "cols", n }, { "rows", n },
                { "orientation", "row 0 is the north edge (max z); column 0 is the west edge (min x). Row label = z of cell centre, ruler = x of column start." },
                { "terrainHeight", new List<object> { Math.Round(hmin, 1), Math.Round(hmax, 1) } },
                { "legend", ". owned land | , land outside owned tiles | ~ water | ^ steep | # road | H highway | : building-internal path | T rail | p power line | r/m/h residential low/med/high zone | c commercial | i industrial | o office | x mixed | uppercase = building of that zone | S service building | A abandoned | B other building | X outside connection" },
                { "buildingsShown", bcount },
                { "grid", sb.ToString() },
            };
        }

        static float3 Center(int r, int c, int n, float cell, float x0, float z0) => new float3(x0 + (c + 0.5f) * cell, 0, z0 + (n - 1 - r + 0.5f) * cell);

        static bool Grid(float3 p, int n, float cell, float x0, float z0, out int r, out int c)
        {
            c = (int)math.floor((p.x - x0) / cell);
            int rz = (int)math.floor((p.z - z0) / cell);
            r = n - 1 - rz;
            return c >= 0 && c < n && r >= 0 && r < n;
        }

        static bool InAny(List<float2[]> polys, float2 p)
        {
            foreach (var poly in polys) if (Inside(poly, p)) return true;
            return false;
        }

        static bool Inside(float2[] poly, float2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                    (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                    inside = !inside;
            }
            return inside;
        }

        public static char ZoneChar(string name)
        {
            if (string.IsNullOrEmpty(name)) return 'z';
            var n = name.ToLowerInvariant();
            if (n.Contains("mixed")) return 'x';
            if (n.Contains("office")) return 'o';
            if (n.Contains("commercial")) return 'c';
            if (n.Contains("industr") || n.Contains("manufactur") || n.Contains("agricult") || n.Contains("forest") || n.Contains("ore") || n.Contains("oil")) return 'i';
            if (n.Contains("residential"))
            {
                if (n.Contains("high")) return 'h';
                if (n.Contains("medium") || n.Contains("row") || n.Contains("mid")) return 'm';
                return 'r';
            }
            return 'z';
        }
    }
}
