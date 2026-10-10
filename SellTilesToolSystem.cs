using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Colossal.Mathematics;
using Game.Areas;
using Game.City;
using Game.Common;
using Game.Input;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.InputSystem;

namespace CS2_TileLocker
{
    /// <summary>
    /// Sell Tiles v0.9 - Unlock-All restore test
    ///
    /// Fixes two issues proven by the latest test:
    /// 1) owned-tile hover/red overlay works, but click did not enter _selected;
    /// 2) after Return to buying, activation could become unreliable.
    ///
    /// Changes:
    /// - activation requests are processed by SellTilesControllerSystem (always-updating GameSystemBase)
    /// - click is latched by the controller using Mouse.current.leftButton.wasPressedThisFrame
    /// - tool consumes a static click request, instead of sampling input only inside ToolBaseSystem.OnUpdate
    /// - dedicated sell log under C:\Temp\TileLocker
    /// </summary>
    public sealed partial class SellTilesToolSystem : ToolBaseSystem
    {
        private ToolSystem _toolSystem;
        private ToolBaseSystem _previousTool;

        private NativeHashSet<Entity> _selected;
        private EntityQuery _ownedTileQuery;
        private EntityQuery _lockedTileQuery;

        private Entity _hovered;
        private bool _toolActive;

        // After a sale we wait a few ToolUpdate frames and verify that vanilla ECS
        // did not immediately remove Native again.
        private int _verifyCountdown;
        private int _expectedOwnedAfterSale = -1;
        private int _expectedLockedAfterSale = -1;

        private string _logPath;

        private bool _keepMode;

        internal bool IsKeepMode => _keepMode;

        internal void SetKeepModeFromController(bool keepMode)
        {
            _keepMode = keepMode;
            PublishSelection();

            // Marks are persistent until the user explicitly presses "Clear marks".
            SellTilesUISystem.Instance?.SetKeepMode(keepMode);
            SellTilesUISystem.Instance?.SetStatus(
                keepMode
                    ? "Purple KEEP mode - mark tiles to preserve during recovery"
                    : "Red SELL mode - mark tiles to re-lock");
        }

        internal void ClearRecoverySelection()
        {
            ClearSelection();
        }

        internal bool IsSellToolActive =>
            _toolActive && _toolSystem != null && _toolSystem.activeTool == this;

        internal Entity HoveredTile => _hovered;

        public override string toolID => "Tile Locker Sell Tiles";

        protected override void OnCreate()
        {
            base.OnCreate();

            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _selected = new NativeHashSet<Entity>(64, Allocator.Persistent);

            _ownedTileQuery = GetEntityQuery(
                ComponentType.ReadOnly<MapTile>(),
                ComponentType.ReadOnly<Game.Areas.Node>(),
                ComponentType.ReadOnly<Triangle>(),
                ComponentType.Exclude<Native>());

            _lockedTileQuery = GetEntityQuery(
                ComponentType.ReadOnly<MapTile>(),
                ComponentType.ReadOnly<Game.Areas.Node>(),
                ComponentType.ReadOnly<Triangle>(),
                ComponentType.ReadOnly<Native>());

            List<ToolBaseSystem> tools = _toolSystem.tools;
            if (!tools.Contains(this))
                tools.Add(this);

            Directory.CreateDirectory(@"C:\Temp\TileLocker");
            _logPath = Path.Combine(
                @"C:\Temp\TileLocker",
                $"SellTiles-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            WriteTileState("CREATE", $"tool={toolID}");

            Enabled = true;
        }

        protected override void OnDestroy()
        {
            if (_selected.IsCreated)
                _selected.Dispose();

            base.OnDestroy();
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            // Use the active game viewer, including its render scale and camera.
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain | TypeMask.Water;
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Verify a sale a few frames later. This is important for cities created
            // with the built-in "Unlock Map Tiles" option: if some vanilla system
            // force-unlocks the tile again, this log will expose it immediately.
            if (_verifyCountdown > 0)
            {
                _verifyCountdown--;

                if (_verifyCountdown == 0)
                {
                    int ownedNow = _ownedTileQuery.CalculateEntityCount();
                    int lockedNow = _lockedTileQuery.CalculateEntityCount();

                    WriteTileState(
                        "POST-SELL VERIFY",
                        $"expectedOwned={_expectedOwnedAfterSale}; expectedLocked={_expectedLockedAfterSale}; " +
                        $"match={(ownedNow == _expectedOwnedAfterSale && lockedNow == _expectedLockedAfterSale)}");

                    SellTilesUISystem.Instance?.SetStatus(
                        $"Vanilla tile state: owned={ownedNow}, locked={lockedNow}. Save/reload and check again.");
                }
            }

            // Requests are driven by SellTilesControllerSystem so reactivation works
            // even when this ToolBaseSystem is not the currently active tool.
            if (!IsSellToolActive)
                return inputDeps;

            inputDeps.Complete();
            Entity newHover = InputManager.instance.mouseOverUI ? Entity.Null : FindOwnedTileUnderMouse();

            if (newHover != _hovered)
            {
                _hovered = newHover;

                if (_hovered != Entity.Null)
                {
                    SellTilesUISystem.Instance?.SetStatus(
                        $"Owned tile under cursor: {_hovered.Index}:{_hovered.Version}");

                    WriteLog("HOVER", $"tile={Fmt(_hovered)} selected={IsSelected(_hovered)}");
                }
                else
                {
                    SellTilesUISystem.Instance?.SetStatus(
                        "Sell mode active - move over an owned tile");
                }
            }

            if (SellTilesControllerSystem.ConsumeWorldClick())
            {
                WriteLog(
                    "CLICK CONSUMED",
                    $"active={IsSellToolActive}; hovered={Fmt(_hovered)}");

                if (_hovered != Entity.Null)
                    ToggleSelection(_hovered);
            }

            return inputDeps;
        }

        internal void ActivateFromController()
        {
            if (IsSellToolActive)
            {
                WriteLog("ACTIVATE", "Already active.");
                return;
            }

            _previousTool = _toolSystem.activeTool;
            _toolSystem.activeTool = this;
            _toolActive = true;
            _hovered = Entity.Null;

            SellTilesUISystem.Instance?.SetMode(true);
            SellTilesUISystem.Instance?.SetStatus(
                $"Sell mode active - owned tiles detected: {_ownedTileQuery.CalculateEntityCount()}");

            PublishSelection();

            WriteTileState(
                "ACTIVATE",
                $"previousTool={_previousTool?.toolID ?? "NULL"}");
        }

        internal void DeactivateFromController(bool returnToBuying)
        {
            _hovered = Entity.Null;
            _toolActive = false;

            // Do not clear marks when the tool is closed, Escape is pressed,
            // or the user returns to vanilla buying. Marks are cleared only by
            // the explicit "Clear marks" UI action.
            PublishSelection();

            if (_previousTool != null && _toolSystem.activeTool == this)
                _toolSystem.activeTool = _previousTool;

            SellTilesUISystem.Instance?.SetMode(false);
            SellTilesUISystem.Instance?.SetStatus(
                returnToBuying ? "Returned to buying. Press TL to enter sell mode again." : "Sell mode closed.");

            WriteLog(
                "DEACTIVATE",
                $"returnToBuying={returnToBuying}; restoredTool={_previousTool?.toolID ?? "NULL"}");
        }

        internal void ClearSelectionFromController() => ClearSelection();

        internal void SellSelectedFromController() => SellSelected();

        private Entity FindOwnedTileUnderMouse()
        {
            if (!GetRaycastResult(out Entity _, out RaycastHit hit))
                return Entity.Null;
            float2 position = hit.m_HitPosition.xz;

            using NativeArray<Entity> tiles =
                _ownedTileQuery.ToEntityArray(Allocator.Temp);

            foreach (Entity tile in tiles)
            {
                if (!EntityManager.Exists(tile))
                    continue;

                DynamicBuffer<Game.Areas.Node> nodes =
                    EntityManager.GetBuffer<Game.Areas.Node>(tile, true);

                DynamicBuffer<Triangle> triangles =
                    EntityManager.GetBuffer<Triangle>(tile, true);

                for (int i = 0; i < triangles.Length; i++)
                {
                    Triangle3 triangle =
                        AreaUtils.GetTriangle3(nodes, triangles[i]);

                    if (ContainsPoint(position, triangle.a.xz, triangle.b.xz, triangle.c.xz))
                        return tile;
                }
            }

            return Entity.Null;
        }

        private static bool ContainsPoint(float2 point, float2 a, float2 b, float2 c)
        {
            float Cross(float2 u, float2 v) => u.x * v.y - u.y * v.x;
            if (math.abs(Cross(b - a, c - a)) < 0.001f) return false;
            float ab = Cross(b - a, point - a);
            float bc = Cross(c - b, point - b);
            float ca = Cross(a - c, point - c);
            return (ab >= -0.001f && bc >= -0.001f && ca >= -0.001f)
                || (ab <= 0.001f && bc <= 0.001f && ca <= 0.001f);
        }

        private bool IsOwnedMapTile(Entity entity)
        {
            return entity != Entity.Null
                && EntityManager.Exists(entity)
                && EntityManager.HasComponent<MapTile>(entity)
                && !EntityManager.HasComponent<Native>(entity);
        }

        private void ToggleSelection(Entity tile)
        {
            if (!IsOwnedMapTile(tile))
            {
                WriteLog("TOGGLE REJECT", $"tile={Fmt(tile)} isOwned=false");
                return;
            }

            bool selectedNow;

            if (_selected.Contains(tile))
            {
                _selected.Remove(tile);
                selectedNow = false;
            }
            else
            {
                _selected.Add(tile);
                selectedNow = true;
            }

            PublishSelection();

            SellTilesUISystem.Instance?.SetStatus(
                selectedNow
                    ? (_keepMode
                        ? $"KEEP tile {tile.Index}:{tile.Version} - {_selected.Count} purple"
                        : $"SELL tile {tile.Index}:{tile.Version} - {_selected.Count} red")
                    : (_keepMode
                        ? $"Removed KEEP mark - {_selected.Count} purple"
                        : $"Removed SELL mark - {_selected.Count} red"));

            WriteLog(
                "TOGGLE",
                $"tile={Fmt(tile)}; selectedNow={selectedNow}; count={_selected.Count}");
        }

        private void ClearSelection()
        {
            if (!_selected.IsCreated)
                return;

            int before = _selected.Count;
            _selected.Clear();
            PublishSelection();

            WriteLog("CLEAR", $"before={before}; after=0");
        }

        private void SellSelected()
        {
            if (_keepMode)
            {
                SellTilesUISystem.Instance?.SetStatus(
                    "Purple KEEP mode does not sell directly. Mark the tiles to preserve, then use Recover from built-in Unlock Map Tiles in Options.");
                WriteTileState("SELL", "Rejected because keepMode=true.");
                return;
            }

            if (!_selected.IsCreated || _selected.Count == 0)
            {
                SellTilesUISystem.Instance?.SetStatus("No tiles selected");
                WriteTileState("SELL", "Rejected because selectedCount=0.");
                return;
            }

            int requested = _selected.Count;
            int ownedBefore = _ownedTileQuery.CalculateEntityCount();
            int lockedBefore = _lockedTileQuery.CalculateEntityCount();

            Entity city = World.GetOrCreateSystemManaged<CitySystem>().City;
            if (!EntityManager.HasComponent<PlayerMoney>(city)) return;
            if (World.GetOrCreateSystemManaged<CityConfigurationSystem>().unlockMapTiles)
            {
                SellTilesUISystem.Instance?.SetStatus("Use Recover from built-in Unlock Map Tiles in Options before selling.");
                return;
            }
            var refunds = new Dictionary<Entity, int>();
            long totalRefund = 0;
            using (var tiles = _selected.ToNativeArray(Allocator.Temp))
                foreach (Entity tile in tiles)
                    if (IsOwnedMapTile(tile))
                    {
                        int amount = TileRefunds.Refund(World, tile, ownedBefore, out _);
                        refunds.Add(tile, amount);
                        totalRefund += amount;
                    }
            var money = EntityManager.GetComponentData<PlayerMoney>(city);
            if (totalRefund > int.MaxValue || (!money.m_Unlimited && (long)money.money + totalRefund > PlayerMoney.kMaxMoney))
            {
                SellTilesUISystem.Instance?.SetStatus("The refund would exceed the city's money limit. Sell fewer tiles or spend money first.");
                return;
            }

            // FiveTwentyNineTiles always enables the vanilla "Map Tiles" feature
            // before performing a re-lock operation.  Do the same here, using
            // normal ECS/reflection only (no Harmony).
            World.GetOrCreateSystemManaged<TileLockerSystem>()
                .PrepareForIndividualRelock();

            WriteTileState(
                "SELL BEGIN",
                $"requested={requested}");

            int relocked = 0;
            int skipped = 0;

            using NativeArray<Entity> selected =
                _selected.ToNativeArray(Allocator.Temp);

            foreach (Entity tile in selected)
            {
                if (!IsOwnedMapTile(tile))
                {
                    skipped++;

                    WriteLog(
                        "SELL SKIP",
                        $"tile={Fmt(tile)} exists={EntityManager.Exists(tile)} " +
                        $"hasMapTile={(EntityManager.Exists(tile) && EntityManager.HasComponent<MapTile>(tile))} " +
                        $"hasNative={(EntityManager.Exists(tile) && EntityManager.HasComponent<Native>(tile))}");
                    continue;
                }

                // This is the key vanilla ownership transition proven by the
                // NORMAL vs UNLOCK-ALL diagnostic comparison:
                //
                //   owned MapTile   = MapTile WITHOUT Game.Common.Native
                //   locked MapTile  = MapTile WITH    Game.Common.Native
                //
                // Therefore "selling" is not a fake UI state. We restore the same
                // Native component that exists on the 520 vanilla locked tiles.
                EntityManager.AddComponent<Native>(tile);

                // Consume the receipt and credit only a tile whose ownership changed.
                TileRefunds.Record(EntityManager, tile, 0);
                if (!money.m_Unlimited)
                {
                    money.Add(refunds[tile]);
                    EntityManager.SetComponentData(city, money);
                }
                var starts = World.GetOrCreateSystemManaged<MapTileSystem>().GetStartTiles();
                for (int i = starts.Length - 1; i >= 0; i--)
                    if (starts[i] == tile) starts.RemoveAtSwapBack(i);

                // Tell vanilla systems/rendering that the area changed.
                if (!EntityManager.HasComponent<Updated>(tile))
                    EntityManager.AddComponentData(tile, default(Updated));
                else
                    EntityManager.SetComponentData(tile, default(Updated));

                relocked++;

                WriteLog(
                    "SELL TILE",
                    $"tile={Fmt(tile)}; Native=True; Updated=True; " +
                    $"stillExists={EntityManager.Exists(tile)}");
            }

            _selected.Clear();
            _hovered = Entity.Null;
            PublishSelection();

            int ownedAfter = _ownedTileQuery.CalculateEntityCount();
            int lockedAfter = _lockedTileQuery.CalculateEntityCount();

            _expectedOwnedAfterSale = ownedBefore - relocked;
            _expectedLockedAfterSale = lockedBefore + relocked;
            _verifyCountdown = 20;

            bool immediateMatch =
                ownedAfter == _expectedOwnedAfterSale &&
                lockedAfter == _expectedLockedAfterSale;

            WriteTileState(
                "SELL COMPLETE",
                $"requested={requested}; relocked={relocked}; skipped={skipped}; " +
                $"ownedBefore={ownedBefore}; lockedBefore={lockedBefore}; " +
                $"expectedOwned={_expectedOwnedAfterSale}; expectedLocked={_expectedLockedAfterSale}; " +
                $"immediateMatch={immediateMatch}");

            SellTilesUISystem.Instance?.SetStatus(
                relocked > 0
                    ? $"Sold {relocked} tile(s). Refunded ₡{totalRefund:N0}."
                    : "No valid owned tiles were re-locked.");
        }

        private void PublishSelection()
        {
            int count = _selected.IsCreated ? _selected.Count : 0;
            long refund = 0;
            bool estimated = false;
            if (!_keepMode && count > 0)
            {
                int owned = _ownedTileQuery.CalculateEntityCount();
                using (var tiles = _selected.ToNativeArray(Allocator.Temp))
                    foreach (Entity tile in tiles)
                        if (IsOwnedMapTile(tile))
                        {
                            refund += TileRefunds.Refund(World, tile, owned, out bool legacy);
                            estimated |= legacy;
                        }
            }
            SellTilesUISystem.Instance?.SetSelectionState(count, (int)Math.Min(refund, int.MaxValue), count > 0 && !_keepMode, estimated);
        }

        internal NativeArray<Entity> GetSelectedTiles(Allocator allocator)
        {
            if (!_selected.IsCreated || _selected.Count == 0)
                return new NativeArray<Entity>(0, allocator);

            return _selected.ToNativeArray(allocator);
        }

        internal bool IsSelected(Entity entity)
        {
            return _selected.IsCreated && _selected.Contains(entity);
        }

        private static string Fmt(Entity e) =>
            e == Entity.Null ? "NULL" : $"{e.Index}:{e.Version}";

        private void WriteTileState(string section, string message)
        {
            int owned = -1;
            int locked = -1;

            try
            {
                owned = _ownedTileQuery.IsEmptyIgnoreFilter
                    ? 0
                    : _ownedTileQuery.CalculateEntityCount();

                locked = _lockedTileQuery.IsEmptyIgnoreFilter
                    ? 0
                    : _lockedTileQuery.CalculateEntityCount();
            }
            catch
            {
                // Keep diagnostic logging alive even if a query is temporarily invalid.
            }

            WriteLog(
                section,
                $"{message}; owned/nonNative={owned}; locked/native={locked}; total={Math.Max(0, owned) + Math.Max(0, locked)}");
        }

        private void WriteLog(string section, string message)
        {
            try
            {
                File.AppendAllText(
                    _logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{section}]\r\n" +
                    message + "\r\n" +
                    new string('-', 80) + "\r\n",
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;
    }
}
