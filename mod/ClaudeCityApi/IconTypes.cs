using System.Collections.Generic;
using System.Linq;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        /// <summary>GET /icontypes: every notification icon prefab in the game (the complaint types a city can show).</summary>
        object IconTypes()
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<NotificationIconData>(), ComponentType.ReadOnly<PrefabData>());
            var names = new List<string>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var e in arr) names.Add(PrefabName(e));
            names = names.Distinct().OrderBy(n => n).ToList();
            return new Dictionary<string, object> { { "count", names.Count }, { "icons", names.Cast<object>().ToList() } };
        }
    }
}
