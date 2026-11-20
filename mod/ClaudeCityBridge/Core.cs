using System;
using Game.Prefabs;
using Unity.Entities;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Endpoint implementation that the bridge loads from a separate DLL so it can be replaced at
    /// runtime (POST /reload) without restarting the game.
    /// </summary>
    public interface IApiPlugin
    {
        void Init(World world);
        /// <summary>Return a result object, an <see cref="ApiError"/>, or an <see cref="IOperation"/>.</summary>
        object Dispatch(Pending p);
        string Version { get; }
    }

    public static class Util
    {
        public static string PrefabName(World w, Entity prefab)
        {
            try
            {
                if (prefab == Entity.Null) return null;
                var ps = w.GetOrCreateSystemManaged<PrefabSystem>();
                if (ps.TryGetPrefab<PrefabBase>(prefab, out var pb) && pb != null) return pb.name;
            }
            catch { }
            return "prefab#" + prefab.Index;
        }

        public static string Id(Entity e) => e.Index + ":" + e.Version;
    }
}
