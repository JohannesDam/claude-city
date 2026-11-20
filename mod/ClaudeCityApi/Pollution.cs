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
        /// GET /pollution?type=air|ground|noise&amp;x&amp;z&amp;size&amp;cell: pollution grid (0-9 relative to the
        /// maximum seen) plus the most polluted cells, to find sources without screenshots.
        /// </summary>
        object Pollution(Pending p)
        {
            var type = p.Q("type", "air");
            float cell = math.clamp(p.QF("cell", 80f), 16f, 256f);
            float size = math.clamp(p.QF("size", 4000f), cell * 4, cell * 120);
            float cx = p.QF("x", 0f), cz = p.QF("z", 0f);
            int n = (int)(size / cell);
            float x0 = cx - n * cell / 2, z0 = cz - n * cell / 2;
            JobHandle deps;
            Func<float3, int> sample;
            if (type == "ground")
            {
                var map = Sys<GroundPollutionSystem>().GetMap(true, out deps); deps.Complete();
                sample = pos => GroundPollutionSystem.GetPollution(pos, map).m_Pollution;
            }
            else if (type == "noise")
            {
                var map = Sys<NoisePollutionSystem>().GetMap(true, out deps); deps.Complete();
                sample = pos => NoisePollutionSystem.GetPollution(pos, map).m_Pollution;
            }
            else
            {
                var map = Sys<AirPollutionSystem>().GetMap(true, out deps); deps.Complete();
                sample = pos => AirPollutionSystem.GetPollution(pos, map).m_Pollution;
            }
            var vals = new int[n, n];
            int max = 1;
            var cells = new List<KeyValuePair<int, float3>>();
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    var pos = new float3(x0 + (c + 0.5f) * cell, 0, z0 + (n - 1 - r + 0.5f) * cell);
                    int v = sample(pos);
                    vals[r, c] = v;
                    max = Math.Max(max, v);
                    if (v > 0) cells.Add(new KeyValuePair<int, float3>(v, pos));
                }
            var sb = new StringBuilder();
            for (int r = 0; r < n; r++)
            {
                sb.Append(((int)(z0 + (n - 1 - r + 0.5f) * cell)).ToString().PadLeft(7)).Append(' ');
                for (int c = 0; c < n; c++)
                {
                    int v = vals[r, c];
                    sb.Append(v <= 0 ? '.' : (char)('0' + Math.Min(9, v * 10 / (max + 1))));
                }
                sb.Append('\n');
            }
            return new Dictionary<string, object>
            {
                { "type", type }, { "x0", x0 }, { "z0", z0 }, { "cell", cell }, { "max", max },
                { "legend", ". none | 0-9 relative to max; rows north->south, columns west->east" },
                { "worst", cells.OrderByDescending(k => k.Key).Take(12).Select(k => (object)new List<object> { Math.Round(k.Value.x), Math.Round(k.Value.z), k.Key }).ToList() },
                { "grid", sb.ToString() },
            };
        }
    }
}
