using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game.Areas;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;

namespace CS2_TileLocker
{
    // Optional save data attached to native tiles; no replacement tile entities.
    public struct TilePurchaseReceipt : IComponentData, ISerializable
    {
        public int Amount;
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write(Amount);
        public void Deserialize<TReader>(TReader reader) where TReader : IReader => reader.Read(out Amount);
    }

    internal static class TileRefunds
    {
        internal static double BaseCost(EntityManager manager, Entity tile)
        {
            if (!manager.HasComponent<PrefabRef>(tile) || !manager.HasBuffer<MapFeatureElement>(tile)) return 0;
            Entity prefab = manager.GetComponentData<PrefabRef>(tile).m_Prefab;
            if (!manager.HasComponent<TilePurchaseCostFactor>(prefab) || !manager.HasBuffer<MapFeatureData>(prefab)) return 0;
            var features = manager.GetBuffer<MapFeatureElement>(tile, true);
            var prices = manager.GetBuffer<MapFeatureData>(prefab, true);
            float factor = manager.GetComponentData<TilePurchaseCostFactor>(prefab).m_Amount;
            float total = 0;
            for (int i = 0; i < Math.Min(features.Length, prices.Length); i++)
            {
                double baseline = i < 2 ? 1.0 / Math.Pow(623.304347826087, 2.0)
                    : (i < 6 || i == 7) ? 8.0718994140625E-07 : 1.0;
                total += (float)(features[i].m_Amount * baseline * 10.0 * prices[i].m_Cost * factor);
            }
            return Math.Max(0, total);
        }

        internal static int Refund(World world, Entity tile, int ownedCount, out bool estimated)
        {
            var manager = world.EntityManager;
            estimated = false;
            if (manager.HasComponent<TilePurchaseReceipt>(tile))
                return Math.Max(0, manager.GetComponentData<TilePurchaseReceipt>(tile).Amount);
            var starts = world.GetOrCreateSystemManaged<MapTileSystem>().GetStartTiles();
            for (int i = 0; i < starts.Length; i++) if (starts[i] == tile) return 0;
            if (world.GetOrCreateSystemManaged<CityConfigurationSystem>().unlockMapTiles) return 0;
            estimated = true;
            // Old saves have no invoice. Estimate the price of the most recently
            // purchased tile using the game's resource cost and ownership multiplier.
            return (int)Math.Min(2000000000, Math.Round(BaseCost(manager, tile) * Math.Max(0, ownedCount - 1)));
        }

        internal static void Record(EntityManager manager, Entity tile, int amount)
        {
            var receipt = new TilePurchaseReceipt { Amount = amount };
            if (manager.HasComponent<TilePurchaseReceipt>(tile)) manager.SetComponentData(tile, receipt);
            else manager.AddComponentData(tile, receipt);
        }
    }

    [HarmonyPatch(typeof(MapTilePurchaseSystem), nameof(MapTilePurchaseSystem.PurchaseSelection))]
    internal static class RecordTilePurchase
    {
        internal sealed class Invoice
        {
            internal Entity City;
            internal int Money;
            internal bool Unlimited;
            internal readonly List<Entity> Tiles = new List<Entity>();
            internal double[] Weights;
        }

        static void Prefix(MapTilePurchaseSystem __instance, out Invoice __state)
        {
            __state = null;
            if (!__instance.selecting) return;
            try
            {
                var manager = __instance.EntityManager;
                var city = __instance.World.GetOrCreateSystemManaged<CitySystem>().City;
                if (!manager.HasComponent<PlayerMoney>(city)) return;
                var money = manager.GetComponentData<PlayerMoney>(city);
                var invoice = new Invoice { City = city, Money = money.money, Unlimited = money.m_Unlimited };
                using (var query = manager.CreateEntityQuery(ComponentType.ReadOnly<SelectionElement>()))
                {
                    if (query.CalculateEntityCount() != 1) return;
                    foreach (var item in manager.GetBuffer<SelectionElement>(query.GetSingletonEntity(), true))
                        if (manager.HasComponent<MapTile>(item.m_Entity) && manager.HasComponent<Native>(item.m_Entity)
                            && !invoice.Tiles.Contains(item.m_Entity)) invoice.Tiles.Add(item.m_Entity);
                }
                invoice.Tiles.Sort((a, b) => {
                    int comparison = TileRefunds.BaseCost(manager, b).CompareTo(TileRefunds.BaseCost(manager, a));
                    return comparison != 0 ? comparison : a.Index.CompareTo(b.Index);
                });
                using (var query = manager.CreateEntityQuery(ComponentType.ReadOnly<MapTile>(), ComponentType.Exclude<Native>()))
                {
                    int owned = query.CalculateEntityCount();
                    invoice.Weights = new double[invoice.Tiles.Count];
                    for (int i = 0; i < invoice.Tiles.Count; i++)
                        invoice.Weights[i] = TileRefunds.BaseCost(manager, invoice.Tiles[i]) * (owned + i);
                }
                __state = invoice;
            }
            catch (Exception ex) { Mod.Instance?.Log?.Warn("Could not prepare tile receipt: " + ex.Message); }
        }

        static void Postfix(MapTilePurchaseSystem __instance, Invoice __state)
        {
            if (__state == null || __state.Tiles.Count == 0) return;
            try
            {
                var manager = __instance.EntityManager;
                // A rejected purchase leaves the selected tiles locked.
                foreach (Entity tile in __state.Tiles)
                    if (!manager.Exists(tile) || manager.HasComponent<Native>(tile)) return;
                int paid = __state.Unlimited ? 0 : Math.Max(0, __state.Money - manager.GetComponentData<PlayerMoney>(__state.City).money);
                var amounts = TileRefundMath.Allocate(__state.Weights, paid);
                for (int i = 0; i < amounts.Length; i++) TileRefunds.Record(manager, __state.Tiles[i], amounts[i]);
            }
            catch (Exception ex) { Mod.Instance?.Log?.Warn("Could not save tile receipt: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(MapTilePurchaseSystem), nameof(MapTilePurchaseSystem.UnlockTile))]
    internal static class RecordFreeTileUnlock
    {
        static void Prefix(EntityManager entityManager, Entity area, out bool __state)
            => __state = entityManager.HasComponent<Native>(area);

        static void Postfix(EntityManager entityManager, Entity area, bool __state)
        {
            // PurchaseSelection replaces this zero receipt with the actual invoice.
            // Recovery and other free unlocks must never create a cash refund.
            if (__state && entityManager.Exists(area) && !entityManager.HasComponent<Native>(area))
                TileRefunds.Record(entityManager, area, 0);
        }
    }
}
