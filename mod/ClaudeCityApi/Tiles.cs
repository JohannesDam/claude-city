using System;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using AreaNode = Game.Areas.Node;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        struct TileInfo { public Entity e; public float2 centre; public bool owned; public int index; }

        List<TileInfo> AllTiles()
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Areas.MapTile>(), ComponentType.ReadOnly<AreaNode>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            var list = new List<TileInfo>();
            using (var arr = q.ToEntityArray(Allocator.Temp))
                foreach (var t in arr)
                {
                    var nodes = em.GetBuffer<AreaNode>(t, true);
                    float2 c = 0;
                    for (int i = 0; i < nodes.Length; i++) c += nodes[i].m_Position.xz;
                    c /= math.max(1, nodes.Length);
                    list.Add(new TileInfo { e = t, centre = c, owned = !em.HasComponent<Native>(t) });
                }
            list = list.OrderBy(t => t.centre.y).ThenBy(t => t.centre.x).ToList();
            for (int i = 0; i < list.Count; i++) { var t = list[i]; t.index = i; list[i] = t; }
            return list;
        }

        /// <summary>Owned tiles and the unowned tiles that touch them (purchase candidates).</summary>
        object Tiles()
        {
            var mtp = Sys<MapTilePurchaseSystem>();
            var tiles = AllTiles();
            var owned = tiles.Where(t => t.owned).ToList();
            float size = 0;
            if (tiles.Count > 1)
            {
                // typical spacing between neighbouring tile centres
                var t0 = tiles[0];
                size = tiles.Skip(1).Select(t => math.distance(t.centre, t0.centre)).Where(d => d > 1).DefaultIfEmpty(600).Min();
            }
            var cands = new List<object>();
            var quote = TileQuoteContext(); // InsightUpkeep.cs: price + upkeep change per candidate
            foreach (var t in tiles)
            {
                if (t.owned) continue;
                if (!owned.Any(o => math.abs(o.centre.x - t.centre.x) < size * 1.2f && math.abs(o.centre.y - t.centre.y) < size * 1.2f)) continue;
                var cd = new Dictionary<string, object> { { "index", t.index }, { "id", Id(t.e) }, { "centre", new List<object> { Math.Round(t.centre.x), Math.Round(t.centre.y) } } };
                AddTileQuote(cd, t.e, quote);
                cands.Add(cd);
            }
            return new Dictionary<string, object>
            {
                { "owned", owned.Count }, { "tileSize", Math.Round(size) },
                { "availableToBuy", mtp.GetAvailableTiles() }, { "milestonesLeft", mtp.IsMilestonesLeft() },
                { "ownedCentres", owned.Select(o => (object)new List<object> { Math.Round(o.centre.x), Math.Round(o.centre.y) }).ToList() },
                { "candidates", cands },
                { "tileUpkeep", TileUpkeepSummary(quote) },
            };
        }

        /// <summary>
        /// Buys one map tile the way the tile purchase panel does: put it in the selection buffer,
        /// let MapTilePurchaseSystem compute cost/status, then PurchaseSelection (money is charged).
        /// </summary>
        object BuyTile(Args a)
        {
            var mtp = Sys<MapTilePurchaseSystem>();
            var tiles = AllTiles();
            TileInfo? pick = null;
            if (a.Has("index")) { int i = a.I("index"); if (i >= 0 && i < tiles.Count) pick = tiles[i]; }
            else if (a.Has("x"))
            {
                var p = new float2(a.F("x"), a.F("z"));
                pick = tiles.OrderBy(t => math.distance(t.centre, p)).FirstOrDefault();
            }
            if (pick == null) return new ApiError("give index or x,z");
            var tile = pick.Value;
            if (tile.owned) return new ApiError("tile already owned");

            var sq = em.CreateEntityQuery(ComponentType.ReadWrite<SelectionElement>());
            Entity sel;
            bool created = false;
            using (var arr = sq.ToEntityArray(Allocator.Temp))
            {
                if (arr.Length > 0) sel = arr[0];
                else
                {
                    sel = em.CreateEntity();
                    em.AddComponentData(sel, new SelectionInfo { m_SelectionType = SelectionType.MapTiles, m_AreaType = Game.Areas.AreaType.MapTile });
                    em.AddBuffer<SelectionElement>(sel);
                    created = true;
                }
            }
            var buf = em.GetBuffer<SelectionElement>(sel);
            buf.Clear();
            buf.Add(new SelectionElement { m_Entity = tile.e });
            bool wasSelecting = mtp.selecting;
            mtp.selecting = true;
            try
            {
                Invoke(mtp, "UpdateStatus");
                var status = mtp.status;
                int cost = mtp.cost, upkeep = mtp.upkeep;
                var r = new Dictionary<string, object> { { "tile", tile.index }, { "centre", new List<object> { Math.Round(tile.centre.x), Math.Round(tile.centre.y) } }, { "cost", cost }, { "upkeep", upkeep }, { "status", status.ToString() } };
                if (status != TilePurchaseErrorFlags.None || a.B("dryRun")) { r["bought"] = false; return r; }
                mtp.PurchaseSelection();
                r["bought"] = !em.HasComponent<Native>(tile.e);
                return r;
            }
            finally
            {
                if (em.Exists(sel) && em.HasBuffer<SelectionElement>(sel)) em.GetBuffer<SelectionElement>(sel).Clear();
                mtp.selecting = wasSelecting;
                if (created && em.Exists(sel)) em.DestroyEntity(sel);
            }
        }
    }
}
