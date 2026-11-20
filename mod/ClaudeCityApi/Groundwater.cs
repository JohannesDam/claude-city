using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Simulation;
using Unity.Jobs;
using Unity.Mathematics;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        /// <summary>
        /// GET /groundwater?x&amp;z&amp;size&amp;cell: grid of groundwater amount and pollution, plus the best
        /// clean spots. Char: '.' none/too little, 1-9 amount (clean), letters a-i amount but polluted.
        /// </summary>
        object Groundwater(Pending p)
        {
            float cell = math.clamp(p.QF("cell", 64f), 16f, 256f);
            float size = math.clamp(p.QF("size", 3000f), cell * 4, cell * 120);
            float cx = p.QF("x", 0f), cz = p.QF("z", 0f);
            int n = (int)(size / cell);
            float x0 = cx - n * cell / 2, z0 = cz - n * cell / 2;
            var gws = Sys<GroundWaterSystem>();
            JobHandle deps;
            var map = gws.GetMap(true, out deps);
            deps.Complete();
            var sb = new StringBuilder();
            var spots = new List<KeyValuePair<float, float3>>();
            for (int r = 0; r < n; r++)
            {
                float z = z0 + (n - 1 - r + 0.5f) * cell;
                sb.Append(((int)z).ToString().PadLeft(7)).Append(' ');
                for (int c = 0; c < n; c++)
                {
                    var pos = new float3(x0 + (c + 0.5f) * cell, 0, z);
                    var g = GroundWaterSystem.GetGroundWater(pos, map);
                    if (g.m_Amount < GroundWaterSystem.kMinGroundWaterThreshold) { sb.Append('.'); continue; }
                    int lvl = math.clamp(g.m_Amount * 9 / GroundWaterSystem.kMaxGroundWater, 1, 9);
                    bool dirty = g.m_Polluted > 0;
                    sb.Append(dirty ? (char)('a' + lvl - 1) : (char)('0' + lvl));
                    if (!dirty) spots.Add(new KeyValuePair<float, float3>(g.m_Amount, pos));
                }
                sb.Append('\n');
            }
            return new Dictionary<string, object>
            {
                { "x0", x0 }, { "z0", z0 }, { "cell", cell },
                { "legend", ". none | 1-9 clean amount | a-i polluted amount; rows north->south, columns west->east" },
                { "bestClean", spots.OrderByDescending(s => s.Key).Take(15).Select(s => (object)new List<object> { Math.Round(s.Value.x), Math.Round(s.Value.z), s.Key }).ToList() },
                { "grid", sb.ToString() },
            };
        }
    }
}
