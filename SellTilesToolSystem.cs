using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Colossal.Mathematics;
using Game.Areas;
using Game.Common;
using Game.Input;
using Game.Prefabs;
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
            // Owned tile hit testing is done from Camera -> MapTile triangles.
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

            Entity newHover = FindOwnedTileUnderMouse();

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

            if (_previousTool != null)
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
            Mouse mouse = Mouse.current;
            UnityEngine.Camera camera = UnityEngine.Camera.main;

            if (mouse == null || camera == null)
                return Entity.Null;

            UnityEngine.Vector2 mousePos = mouse.position.ReadValue();
            UnityEngine.Ray ray = camera.ScreenPointToRay(
                new UnityEngine.Vector3(mousePos.x, mousePos.y, 0f));

            float3 origin =
                new float3(ray.origin.x, ray.origin.y, ray.origin.z);

            float3 direction = math.normalizesafe(
                new float3(ray.direction.x, ray.direction.y, ray.direction.z));

            Entity bestEntity = Entity.Null;
            float bestDistance = float.MaxValue;

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

                    if (RayTriangle(
                        origin,
                        direction,
                        triangle.a,
                        triangle.b,
                        triangle.c,
                        out float distance) &&
                        distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestEntity = tile;
                    }
                }
            }

            return bestEntity;
        }

        private static bool RayTriangle(
            float3 origin,
            float3 direction,
            float3 v0,
            float3 v1,
            float3 v2,
            out float distance)
        {
            const float epsilon = 0.00001f;

            float3 edge1 = v1 - v0;
            float3 edge2 = v2 - v0;
            float3 pvec = math.cross(direction, edge2);
            float det = math.dot(edge1, pvec);

            if (math.abs(det) < epsilon)
            {
                distance = 0f;
                return false;
            }

            float invDet = 1f / det;
            float3 tvec = origin - v0;
            float u = math.dot(tvec, pvec) * invDet;

            if (u < 0f || u > 1f)
            {
                distance = 0f;
                return false;
            }

            float3 qvec = math.cross(tvec, edge1);
            float v = math.dot(direction, qvec) * invDet;

            if (v < 0f || u + v > 1f)
            {
                distance = 0f;
                return false;
            }

            distance = math.dot(edge2, qvec) * invDet;
            return distance >= 0f;
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
                    ? $"Re-locked {relocked} tile(s): owned={ownedAfter}, locked={lockedAfter}. Wait for verification."
                    : "No valid owned tiles were re-locked.");
        }

        private void PublishSelection()
        {
            int count = _selected.IsCreated ? _selected.Count : 0;
            SellTilesUISystem.Instance?.SetSelectionState(count, 0, count > 0);
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
