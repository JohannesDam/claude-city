using System;
using System.Collections.Generic;
using System.Linq;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        /// <summary>
        /// GET /happiness: the city-wide happiness factors the game itself computes (same numbers as the
        /// in-game happiness tooltip), sorted from most negative to most positive.
        /// </summary>
        object Happiness()
        {
            var sys = Sys<CitizenHappinessSystem>();
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<HappinessFactorParameterData>());
            Entity holder;
            using (var arr = q.ToEntityArray(Allocator.Temp))
            {
                if (arr.Length == 0) return new ApiError("no happiness parameters", 409);
                holder = arr[0];
            }
            var parameters = em.GetBuffer<HappinessFactorParameterData>(holder, true);
            var locked = sys.GetComponentLookup<Locked>(true);
            var res = new List<Dictionary<string, object>>();
            for (int i = 0; i < (int)CitizenHappinessSystem.HappinessFactor.Count; i++)
            {
                var f = (CitizenHappinessSystem.HappinessFactor)i;
                var v = sys.GetHappinessFactor(f, parameters, ref locked);
                res.Add(new Dictionary<string, object> { { "factor", f.ToString() }, { "x", Math.Round(v.x, 2) }, { "y", Math.Round(v.y, 2) }, { "z", Math.Round(v.z, 2) } });
            }
            return new Dictionary<string, object>
            {
                { "note", "x/y/z as returned by CitizenHappinessSystem.GetHappinessFactor" },
                { "factors", res.OrderBy(d => (double)d["x"]).Cast<object>().ToList() },
            };
        }
    }
}
