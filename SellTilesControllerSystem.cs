using System;
using System.IO;
using System.Text;
using Game;
using Game.Input;
using UnityEngine.InputSystem;

namespace CS2_TileLocker
{
    /// <summary>
    /// Always-updating controller for sell mode.
    ///
    /// UI triggers land here even when SellTilesToolSystem is not active.
    /// It also latches world clicks before ToolBaseSystem update ordering can lose them.
    /// </summary>
    public sealed partial class SellTilesControllerSystem : GameSystemBase
    {
        private static bool s_activate;
        private static bool s_deactivate;
        private static bool s_clear;
        private static bool s_sell;
        private static bool s_returnToBuying;
        private static bool s_setRedMode;
        private static bool s_setKeepMode;
        private static bool s_worldClick;

        private SellTilesToolSystem _tool;

        internal static void RequestActivate() => s_activate = true;
        internal static void RequestDeactivate() => s_deactivate = true;
        internal static void RequestClearSelection() => s_clear = true;
        internal static void RequestSellSelected() => s_sell = true;
        internal static void RequestReturnToBuying() => s_returnToBuying = true;
        internal static void RequestSetRedMode() => s_setRedMode = true;
        internal static void RequestSetKeepMode() => s_setKeepMode = true;

        internal static bool ConsumeWorldClick()
        {
            bool value = s_worldClick;
            s_worldClick = false;
            return value;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            _tool = World.GetOrCreateSystemManaged<SellTilesToolSystem>();
        }

        protected override void OnUpdate()
        {
            if (s_activate)
            {
                s_activate = false;
                _tool.ActivateFromController();
            }

            if (s_clear)
            {
                s_clear = false;
                _tool.ClearSelectionFromController();
            }

            if (s_sell)
            {
                s_sell = false;
                _tool.SellSelectedFromController();
            }

            if (s_returnToBuying)
            {
                s_returnToBuying = false;
                _tool.DeactivateFromController(true);
            }

            if (s_setRedMode)
            {
                s_setRedMode = false;
                _tool.SetKeepModeFromController(false);
            }

            if (s_setKeepMode)
            {
                s_setKeepMode = false;
                _tool.SetKeepModeFromController(true);
            }

            if (s_deactivate)
            {
                s_deactivate = false;
                _tool.DeactivateFromController(false);
            }

            if (_tool.IsSellToolActive && !InputManager.instance.mouseOverUI)
            {
                Mouse mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                    s_worldClick = true;
            }
            else
            {
                s_worldClick = false;
            }
        }

        protected override void OnDestroy()
        {
            s_activate = s_deactivate = s_clear = s_sell = s_returnToBuying = false;
            s_setRedMode = s_setKeepMode = s_worldClick = false;
            base.OnDestroy();
        }
    }
}
