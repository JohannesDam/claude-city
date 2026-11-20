using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Net;
using Unity.Entities;
using Unity.Mathematics;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Edge ids after a build that rewrites existing edges (/roadupgrade, /upgrade): applying such a
    /// preview can replace the edge entities, so ids passed to the call go stale. The jobs snapshot all
    /// edges and the curves of the requested edges before the tool runs (real builds only); after the
    /// apply has settled each requested edge is matched to the live edge(s) lying on its old curve.
    /// </summary>
    public partial class Api
    {
        /// <summary>Requested edges with their curves, taken before the build (for ReportRenewedEdges).</summary>
        internal List<KeyValuePair<Entity, Bezier4x3>> EdgeCurves(IEnumerable<Entity> edges)
        {
            var res = new List<KeyValuePair<Entity, Bezier4x3>>();
            foreach (var e in edges)
                if (em.Exists(e) && em.HasComponent<Curve>(e)) res.Add(new KeyValuePair<Entity, Bezier4x3>(e, em.GetComponentData<Curve>(e).m_Bezier));
            return res;
        }

        /// <summary>True when the edge's curve lies along the old curve (all samples within tol, xz).</summary>
        bool EdgeOnCurve(Entity e, Bezier4x3 old, float tol, out float along)
        {
            along = 0;
            var cv = em.GetComponentData<Curve>(e).m_Bezier;
            foreach (var t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            {
                float d = MathUtils.Distance(old.xz, MathUtils.Position(cv, t).xz, out float ot);
                if (d > tol) return false;
                if (t == 0.5f) along = ot;
            }
            return true;
        }

        /// <summary>
        /// Adds edgeMap (old id -> current ids, in request order), edgeIdsAfter (current ids of all requested
        /// edges, for a follow-up call), newEdgeIds (those that got a new id), staleEdgeIds / staleEdgeCount
        /// and otherNewEdgeIds (new edges elsewhere, e.g. neighbours regenerated with the nodes).
        /// </summary>
        internal void ReportRenewedEdges(List<KeyValuePair<Entity, Bezier4x3>> requested, HashSet<Entity> before, Dictionary<string, object> result)
        {
            var live = SnapshotEdges();
            var fresh = live.Where(e => !before.Contains(e)).ToList();
            var stale = before.Where(e => !live.Contains(e)).ToList();
            var used = new HashSet<Entity>();
            var map = new List<object>();
            var after = new List<object>();
            var newIds = new List<object>();
            int unmatched = 0;
            foreach (var kv in requested)
            {
                var old = kv.Key;
                List<Entity> now;
                if (live.Contains(old)) now = new List<Entity> { old }; // kept its id
                else
                {
                    var hits = new List<KeyValuePair<float, Entity>>();
                    foreach (var e in fresh)
                        if (EdgeOnCurve(e, kv.Value, 1.5f, out float along)) hits.Add(new KeyValuePair<float, Entity>(along, e));
                    now = hits.OrderBy(h => h.Key).Select(h => h.Value).ToList(); // ordered along the old curve
                    if (now.Count == 0) unmatched++;
                }
                var ids = new List<object>();
                foreach (var e in now)
                {
                    ids.Add(Id(e));
                    if (!used.Add(e)) continue;
                    after.Add(Id(e));
                    if (!before.Contains(e)) newIds.Add(Id(e));
                }
                map.Add(new Dictionary<string, object> { { "old", Id(old) }, { "now", ids }, { "renewed", !live.Contains(old) } });
            }
            var others = fresh.Where(e => !used.Contains(e)).ToList();
            result["edgeMap"] = map;
            result["edgeIdsAfter"] = after;
            result["newEdgeIds"] = newIds;
            result["staleEdgeCount"] = stale.Count;
            result["staleEdgeIds"] = stale.Take(100).Select(e => (object)Id(e)).ToList();
            if (others.Count > 0)
            {
                result["otherNewEdgeIds"] = others.Take(50).Select(e => (object)Id(e)).ToList();
                if (others.Count > 50) result["otherNewEdgesTotal"] = others.Count;
            }
            if (unmatched > 0) result["edgeMapNote"] = unmatched + " requested edge(s) are gone and no new edge lies on their old curve; re-select with box/polyline";
        }
    }
}
