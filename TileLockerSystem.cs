using System;
using System.Reflection;
using System.IO;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Serialization;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace CS2_TileLocker
{
    /// <summary>
    /// Enables the vanilla map-tile purchasing feature immediately and can make
    /// every map tile purchasable without automatically unlocking those tiles.
    ///
    /// Locked/owned tile state is left to the vanilla game:
    /// MapTile + Native = locked; MapTile without Native = owned/unlocked.
    /// </summary>
    public sealed partial class TileLockerSystem : GameSystemBase, IPostDeserialize
    {
        private static readonly string DiagnosticDirectory = @"C:\Temp\TileLocker";
        private static readonly object DiagnosticLock = new object();
        private static string s_diagnosticLogPath;
        private static bool s_lockAllRequested;
        private static bool s_unlockAllRequested;
        private static bool s_refreshRequested;
        private static bool s_applyAccessRequested;
        private static bool s_recoverUnlockAllRequested;

        private EntityQuery _lockedTiles;
        private EntityQuery _unlockedTiles;
        private EntityQuery _featureQuery;
        private EntityQuery _permitBonusQuery;

        private MethodInfo _getAvailableTilesMethod;

        internal static void RequestLockAll() => s_lockAllRequested = true;
        internal static void RequestUnlockAll() => s_unlockAllRequested = true;
        internal static void RequestRefresh() => s_refreshRequested = true;
        internal static void RequestApplyAccess() => s_applyAccessRequested = true;
        internal static void RequestRecoverUnlockAll() => s_recoverUnlockAllRequested = true;

        protected override void OnCreate()
        {
            base.OnCreate();

            _lockedTiles = GetEntityQuery(
                ComponentType.ReadOnly<MapTile>(),
                ComponentType.ReadOnly<Native>());

            _unlockedTiles = GetEntityQuery(
                ComponentType.ReadOnly<MapTile>(),
                ComponentType.Exclude<Native>());

            _featureQuery = GetEntityQuery(
                ComponentType.ReadOnly<FeatureData>(),
                ComponentType.ReadOnly<PrefabData>(),
                ComponentType.ReadWrite<Locked>());

            _permitBonusQuery = GetEntityQuery(
                ComponentType.ReadOnly<TileLockerPermitBonus>());

            _getAvailableTilesMethod = typeof(MapTilePurchaseSystem).GetMethod(
                "GetAvailableTiles",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            InitializeDiagnosticLog();
            WriteDiagnostic("SYSTEM CREATE", $"GetAvailableTiles reflection found={_getAvailableTilesMethod != null}");
        }

        public void PostDeserialize(Context context)
        {
            WriteDiagnostic("POST DESERIALIZE", $"purpose={context.purpose}");
            WriteDiagnosticSnapshot("POST DESERIALIZE - BEFORE CHANGES");

            // Remove a previously-created runtime bonus before recalculating it.
            RemovePermitBonus();

            UpdateStatus();
            WriteDiagnosticSnapshot("POST DESERIALIZE - AFTER CHANGES");
        }

        protected override void OnUpdate()
        {
            if (s_lockAllRequested)
            {
                s_lockAllRequested = false;
                WriteDiagnostic("ACTION", "Lock all tiles requested from settings UI.");
                LockAllTiles();
            }

            if (s_unlockAllRequested)
            {
                s_unlockAllRequested = false;
                WriteDiagnostic("ACTION", "Unlock all tiles requested from settings UI.");
                UnlockAllTiles();
            }

            if (s_applyAccessRequested)
            {
                s_applyAccessRequested = false;
                WriteDiagnostic("ACTION", "Apply full tile access requested from settings UI.");
                WriteDiagnosticSnapshot("BEFORE APPLY FULL TILE ACCESS");
                EnableTilePurchasing();
                RemovePermitBonus();
                GrantEnoughPermitsForWholeMap();
                UpdateStatus();
                WriteDiagnosticSnapshot("AFTER APPLY FULL TILE ACCESS");
            }

            if (s_recoverUnlockAllRequested)
            {
                s_recoverUnlockAllRequested = false;

                WriteDiagnostic(
                    "ACTION",
                    "Full 529-style recovery requested: repair vanilla flags, enable Map Tiles feature, re-lock all owned tiles, clear starting tiles, remove TileLocker permit bonus.");

                // First normalize the built-in "Unlock Map Tiles" runtime/save flags.
                VanillaTileModeRepairSystem.RequestRepairAndRelockAll();
            }

            if (s_refreshRequested)
            {
                s_refreshRequested = false;
                UpdateStatus();
                WriteDiagnosticSnapshot("MANUAL REFRESH");
            }
        }

        private void LockAllTiles()
        {
            RelockAllLike529();
        }

        /// <summary>
        /// Re-locks all currently-owned tiles using the same non-Harmony core
        /// transaction used by FiveTwentyNineTiles:
        ///
        ///   1. enable the "Map Tiles" feature,
        ///   2. add Native to every unlocked MapTile,
        ///   3. add/set Updated,
        ///   4. clear MapTileSystem.m_StartTiles.
        ///
        /// Unlike the old Tile Locker implementation, this method deliberately
        /// does NOT create a synthetic milestone permit bonus. Vanilla milestones
        /// remain the source of purchase permits.
        /// </summary>
        internal void RelockAllLike529()
        {
            using NativeArray<Entity> keepTiles =
                new NativeArray<Entity>(0, Allocator.Temp);

            RelockAllLike529(keepTiles);
        }

        /// <summary>
        /// 529-style full re-lock with optional preserved tiles.
        ///
        /// The transaction is deliberately:
        ///   1. enable the vanilla Map Tiles feature;
        ///   2. add Native + Updated to ALL currently owned tiles;
        ///   3. clear MapTileSystem.m_StartTiles;
        ///   4. remove Tile Locker synthetic permit bonuses;
        ///   5. re-unlock each purple KEEP tile through the VANILLA helper
        ///      MapTilePurchaseSystem.UnlockTile(EntityManager, tile);
        ///   6. register up to nine preserved tiles as vanilla starting tiles.
        ///
        /// This gives the kept tiles the same ownership transition used by the
        /// game's normal tile-purchase implementation instead of simply leaving
        /// them in a synthetic state.
        /// </summary>
        internal void RelockAllLike529(NativeArray<Entity> keepTiles)
        {
            try
            {
                EnableTilePurchasing();

                using NativeArray<Entity> tiles =
                    _unlockedTiles.ToEntityArray(Allocator.Temp);

                int relocked = 0;

                foreach (Entity tile in tiles)
                {
                    if (!EntityManager.HasComponent<Native>(tile))
                    {
                        EntityManager.AddComponent<Native>(tile);
                        relocked++;
                    }

                    if (!EntityManager.HasComponent<Updated>(tile))
                        EntityManager.AddComponentData(tile, default(Updated));
                    else
                        EntityManager.SetComponentData(tile, default(Updated));
                }

                int startTilesBefore = GetStartingTileCount();
                ClearStartingTiles();

                // 529 Tiles does not manufacture extra permits during re-lock.
                RemovePermitBonus();

                int kept = 0;
                int starting = 0;

                for (int i = 0; i < keepTiles.Length; i++)
                {
                    Entity tile = keepTiles[i];

                    if (tile == Entity.Null ||
                        !EntityManager.Exists(tile) ||
                        !EntityManager.HasComponent<MapTile>(tile))
                    {
                        continue;
                    }

                    // After the full re-lock this tile should have Native.
                    // Use the exact vanilla ownership helper used by 529 Tiles
                    // when unlocking tiles.
                    if (EntityManager.HasComponent<Native>(tile))
                    {
                        MapTilePurchaseSystem.UnlockTile(EntityManager, tile);
                    }

                    if (!EntityManager.HasComponent<Native>(tile))
                    {
                        kept++;

                        // 529 Tiles normally maintains m_StartTiles through its
                        // Harmony postfix. We do the same explicitly, without
                        // Harmony, for the first nine preserved tiles.
                        if (starting < 9 && AddStartingTile(tile))
                            starting++;
                    }
                }

                int startTilesAfter = GetStartingTileCount();

                Mod.Instance?.Log?.Info(
                    $"529-style recovery complete. Re-locked={relocked}; kept={kept}; " +
                    $"startingTiles={startTilesAfter}; oldStartingTiles={startTilesBefore}.");

                WriteDiagnostic(
                    "529-STYLE RECOVERY",
                    $"relocked={relocked}; keepRequested={keepTiles.Length}; kept={kept}; " +
                    $"startTilesBefore={startTilesBefore}; startTilesAfter={startTilesAfter}; " +
                    $"availableAfter={TryGetAvailableTiles()}");
            }
            catch (Exception ex)
            {
                Mod.Instance?.Log?.Error(ex, "529-style recovery failed.");
                WriteDiagnostic("529-STYLE RECOVERY ERROR", ex.ToString());
            }

            UpdateStatus();
        }

        private bool AddStartingTile(Entity tile)
        {
            try
            {
                MapTileSystem mapTileSystem =
                    World.GetOrCreateSystemManaged<MapTileSystem>();

                FieldInfo field = typeof(MapTileSystem).GetField(
                    "m_StartTiles",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (!(field?.GetValue(mapTileSystem) is NativeList<Entity> startTiles))
                    return false;

                for (int i = 0; i < startTiles.Length; i++)
                {
                    if (startTiles[i] == tile)
                        return false;
                }

                if (startTiles.Length >= 9)
                    return false;

                startTiles.Add(tile);
                return true;
            }
            catch (Exception ex)
            {
                Mod.Instance?.Log?.Warn(
                    $"Could not add preserved tile to m_StartTiles: {ex.GetBaseException().Message}");
                return false;
            }
        }

        /// <summary>
        /// Prepares vanilla tile purchasing before an individual Tile Locker re-lock.
        /// This mirrors FiveTwentyNineTiles.EnableTilePurchasing() without Harmony.
        /// </summary>
        internal void PrepareForIndividualRelock()
        {
            EnableTilePurchasing();

            WriteDiagnostic(
                "529-STYLE PREPARE",
                $"individualRelock=True; startTiles={GetStartingTileCount()}; " +
                $"available={TryGetAvailableTiles()}");
        }

        private void UnlockAllTiles()
        {
            try
            {
                EnableTilePurchasing();

                using NativeArray<Entity> tiles = _lockedTiles.ToEntityArray(Allocator.Temp);

                foreach (Entity tile in tiles)
                {
                    MapTilePurchaseSystem.UnlockTile(EntityManager, tile);
                }

                RemovePermitBonus();
                Mod.Instance?.Log?.Info($"Unlocked {tiles.Length} map tiles.");
                WriteDiagnostic("UNLOCK ALL", $"Unlocked {tiles.Length} map tiles.");
            }
            catch (Exception ex)
            {
                Mod.Instance?.Log?.Error(ex, "Failed to unlock map tiles.");
            }

            UpdateStatus();
        }

        private int GetStartingTileCount()
        {
            try
            {
                MapTileSystem mapTileSystem =
                    World.GetOrCreateSystemManaged<MapTileSystem>();

                FieldInfo field = typeof(MapTileSystem).GetField(
                    "m_StartTiles",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (field?.GetValue(mapTileSystem) is NativeList<Entity> startTiles)
                    return startTiles.Length;
            }
            catch
            {
            }

            return -1;
        }

        private void ClearStartingTiles()
        {
            try
            {
                MapTileSystem mapTileSystem = World.GetOrCreateSystemManaged<MapTileSystem>();

                FieldInfo field = typeof(MapTileSystem).GetField(
                    "m_StartTiles",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (field?.GetValue(mapTileSystem) is NativeList<Entity> startTiles)
                {
                    startTiles.Clear();
                }
                else
                {
                    Mod.Instance?.Log?.Warn(
                        "Could not access MapTileSystem.m_StartTiles. Tiles were still re-locked.");
                }
            }
            catch (Exception ex)
            {
                Mod.Instance?.Log?.Warn(
                    $"Could not clear the starting-tile list: {ex.GetBaseException().Message}");
            }
        }

        private void EnableTilePurchasing()
        {
            try
            {
                PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

                using NativeArray<Entity> features = _featureQuery.ToEntityArray(Allocator.Temp);

                foreach (Entity entity in features)
                {
                    if (!EntityManager.HasComponent<PrefabData>(entity))
                        continue;

                    PrefabData prefabData = EntityManager.GetComponentData<PrefabData>(entity);
                    PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(prefabData);

                    if (prefab == null || !string.Equals(prefab.name, "Map Tiles", StringComparison.Ordinal))
                        continue;

                    if (EntityManager.HasComponent<Locked>(entity))
                        EntityManager.RemoveComponent<Locked>(entity);

                    if (EntityManager.HasComponent<UnlockRequirement>(entity))
                        EntityManager.RemoveComponent<UnlockRequirement>(entity);

                    Mod.Instance?.Log?.Info("Map Tiles purchasing feature enabled.");
                    WriteDiagnostic("FEATURE", $"Map Tiles feature entity={entity.Index}:{entity.Version}; removed Locked/UnlockRequirement where present.");
                    return;
                }

                Mod.Instance?.Log?.Warn("Could not find the locked 'Map Tiles' feature entity.");
                WriteDiagnostic("FEATURE WARNING", "Could not find the locked Map Tiles feature entity.");
            }
            catch (Exception ex)
            {
                Mod.Instance?.Log?.Error(ex, "Failed to enable the Map Tiles purchasing feature.");
            }
        }

        /// <summary>
        /// Adds only the number of permits needed to make every currently locked
        /// tile purchasable. This does not unlock any MapTile entities.
        ///
        /// The game method GetAvailableTiles() is invoked via reflection so this
        /// remains Harmony-free. If reflection is unavailable after a game update,
        /// the fallback grants one permit per currently locked tile (safe but may
        /// over-count the displayed permit total in an established city).
        /// </summary>
        private void GrantEnoughPermitsForWholeMap()
        {
            try
            {
                int locked = _lockedTiles.CalculateEntityCount();
                if (locked <= 0)
                    return;

                int currentlyAvailable = TryGetAvailableTiles();
                int bonus;

                if (currentlyAvailable >= 0)
                {
                    bonus = Math.Max(0, locked - currentlyAvailable);
                }
                else
                {
                    bonus = locked;
                    Mod.Instance?.Log?.Warn(
                        "GetAvailableTiles could not be read; using safe fallback permit count.");
                }

                if (bonus <= 0)
                {
                    Mod.Instance?.Log?.Info(
                        $"No permit bonus needed. Locked={locked}, available={currentlyAvailable}.");
                    WriteDiagnostic("PERMITS", $"No bonus needed. locked={locked}, currentlyAvailable={currentlyAvailable}.");
                    return;
                }

                Entity extraMilestone = EntityManager.CreateEntity();
                EntityManager.AddComponentData(extraMilestone, new MilestoneData { m_MapTiles = bonus });
                EntityManager.AddComponentData(extraMilestone, new TileLockerPermitBonus());

                Mod.Instance?.Log?.Info(
                    $"Added {bonus} map-tile permits. Locked={locked}, previously available={currentlyAvailable}.");
                WriteDiagnostic("PERMITS", $"Created TileLockerPermitBonus entity={extraMilestone.Index}:{extraMilestone.Version}; bonus={bonus}; locked={locked}; previousAvailable={currentlyAvailable}; availableAfter={TryGetAvailableTiles()}.");
            }
            catch (Exception ex)
            {
                Mod.Instance?.Log?.Error(ex, "Failed to grant map-tile permits.");
            }
        }

        private int TryGetAvailableTiles()
        {
            try
            {
                if (_getAvailableTilesMethod == null)
                    return -1;

                MapTilePurchaseSystem purchaseSystem =
                    World.GetOrCreateSystemManaged<MapTilePurchaseSystem>();

                object value = _getAvailableTilesMethod.Invoke(purchaseSystem, null);
                return value is int count ? count : -1;
            }
            catch (Exception ex)
            {
                Mod.Instance?.Log?.Warn(
                    $"Could not invoke MapTilePurchaseSystem.GetAvailableTiles: {ex.GetBaseException().Message}");
                return -1;
            }
        }

        private void RemovePermitBonus()
        {
            try
            {
                using NativeArray<Entity> bonuses = _permitBonusQuery.ToEntityArray(Allocator.Temp);
                foreach (Entity entity in bonuses)
                {
                    if (!EntityManager.HasComponent<Deleted>(entity))
                        EntityManager.AddComponent<Deleted>(entity);
                }
            }
            catch (Exception ex)
            {
                Mod.Instance?.Log?.Warn(
                    $"Could not remove previous permit bonus: {ex.GetBaseException().Message}");
            }
        }

        private void UpdateStatus()
        {
            if (Mod.Instance?.Settings == null)
                return;

            int locked = _lockedTiles.CalculateEntityCount();
            int unlocked = _unlockedTiles.CalculateEntityCount();
            int available = TryGetAvailableTiles();

            string permits = available >= 0
                ? $" | Purchases available now: {available}"
                : " | Purchases available now: unknown";
            Mod.Instance.Settings.ApplyAndSave();
        }

        private void InitializeDiagnosticLog()
        {
            try
            {
                Directory.CreateDirectory(DiagnosticDirectory);
                if (string.IsNullOrEmpty(s_diagnosticLogPath))
                {
                    s_diagnosticLogPath = Path.Combine(
                        DiagnosticDirectory,
                        $"TileLocker-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                }

                WriteDiagnostic("START", $"Tile Locker {Mod.Version} diagnostic log started. Directory={DiagnosticDirectory}");
            }
            catch (Exception ex)
            {
                Mod.Instance?.Log?.Warn($"Could not initialize diagnostic logging: {ex.GetBaseException().Message}");
            }
        }

        private void WriteDiagnosticSnapshot(string reason)
        {
            try
            {
                int locked = _lockedTiles.CalculateEntityCount();
                int unlocked = _unlockedTiles.CalculateEntityCount();
                int available = TryGetAvailableTiles();
                int bonusEntities = _permitBonusQuery.CalculateEntityCount();

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"reason={reason}");
                sb.AppendLine($"lockedTiles={locked}");
                sb.AppendLine($"unlockedTiles={unlocked}");
                sb.AppendLine($"totalTiles={(locked >= 0 && unlocked >= 0 ? locked + unlocked : -1)}");
                sb.AppendLine($"availablePurchases={available}");
                sb.AppendLine($"tileLockerPermitBonusEntities={bonusEntities}");
                sb.AppendLine($"getAvailableTilesReflectionFound={_getAvailableTilesMethod != null}");

                WriteDiagnostic("SNAPSHOT", sb.ToString().TrimEnd());
                WriteMilestoneDiagnostics(reason);
            }
            catch (Exception ex)
            {
                WriteDiagnostic("SNAPSHOT ERROR", ex.ToString());
            }
        }

        private void WriteMilestoneDiagnostics(string reason)
        {
            try
            {
                EntityQuery milestones = GetEntityQuery(ComponentType.ReadOnly<MilestoneData>());
                using NativeArray<Entity> entities = milestones.ToEntityArray(Allocator.Temp);

                int count = 0;
                long permitSum = 0;
                StringBuilder details = new StringBuilder();

                foreach (Entity entity in entities)
                {
                    MilestoneData data = EntityManager.GetComponentData<MilestoneData>(entity);
                    permitSum += data.m_MapTiles;
                    count++;

                    if (data.m_MapTiles != 0 || EntityManager.HasComponent<TileLockerPermitBonus>(entity))
                    {
                        details.AppendLine(
                            $"  entity={entity.Index}:{entity.Version} mapTiles={data.m_MapTiles} tileLockerBonus={EntityManager.HasComponent<TileLockerPermitBonus>(entity)}");
                    }
                }

                WriteDiagnostic(
                    "MILESTONES",
                    $"reason={reason}\r\n" +
                    $"count={count}\r\n" +
                    $"sumOfMilestoneMapTiles={permitSum}\r\n" +
                    "nonZeroOrTileLockerBonus:\r\n" +
                    details.ToString().TrimEnd());
            }
            catch (Exception ex)
            {
                WriteDiagnostic("MILESTONE ERROR", ex.ToString());
            }
        }

        private static void WriteDiagnostic(string section, string message)
        {
            try
            {
                if (string.IsNullOrEmpty(s_diagnosticLogPath))
                {
                    Directory.CreateDirectory(DiagnosticDirectory);
                    s_diagnosticLogPath = Path.Combine(
                        DiagnosticDirectory,
                        $"TileLocker-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                }

                string entry =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{section}]\r\n" +
                    message + "\r\n" +
                    new string('-', 80) + "\r\n";

                lock (DiagnosticLock)
                {
                    File.AppendAllText(s_diagnosticLogPath, entry, Encoding.UTF8);
                }
            }
            catch
            {
                // Diagnostic logging must never break gameplay or the mod itself.
            }
        }

        /// <summary>
        /// Marker for the runtime milestone used to provide additional map-tile permits.
        /// </summary>
        private struct TileLockerPermitBonus : IComponentData
        {
        }
    }
}
