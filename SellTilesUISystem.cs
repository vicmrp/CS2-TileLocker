using Colossal.UI.Binding;
using Game;
using Game.UI;

namespace CS2_TileLocker
{
    public sealed partial class SellTilesUISystem : UISystemBase
    {
        private ValueBinding<bool> _panelVisible;
        private ValueBinding<bool> _sellModeActive;
        private ValueBinding<bool> _keepMode;
        private ValueBinding<int> _selectedCount;
        private ValueBinding<int> _refundAmount;
        private ValueBinding<bool> _refundEstimated;
        private ValueBinding<bool> _canSell;
        private ValueBinding<string> _sellStatus;

        internal static SellTilesUISystem Instance { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;

            AddBinding(_panelVisible = new ValueBinding<bool>("TileLocker", "sellPanelVisible", false));
            AddBinding(_sellModeActive = new ValueBinding<bool>("TileLocker", "sellModeActive", false));
            AddBinding(_keepMode = new ValueBinding<bool>("TileLocker", "sellKeepMode", false));
            AddBinding(_selectedCount = new ValueBinding<int>("TileLocker", "sellSelectedCount", 0));
            AddBinding(_refundAmount = new ValueBinding<int>("TileLocker", "sellRefundAmount", 0));
            AddBinding(_refundEstimated = new ValueBinding<bool>("TileLocker", "sellRefundEstimated", false));
            AddBinding(_canSell = new ValueBinding<bool>("TileLocker", "sellCanSell", false));
            AddBinding(_sellStatus = new ValueBinding<string>("TileLocker", "sellStatus", "Ready"));

            AddBinding(new TriggerBinding("TileLocker", "sellOpenPanel", OpenPanel));
            AddBinding(new TriggerBinding("TileLocker", "sellClosePanel", ClosePanel));
            AddBinding(new TriggerBinding("TileLocker", "sellEnterMode", EnterSellMode));
            AddBinding(new TriggerBinding("TileLocker", "sellClearSelection", () => SellTilesControllerSystem.RequestClearSelection()));
            AddBinding(new TriggerBinding("TileLocker", "sellSelected", () => SellTilesControllerSystem.RequestSellSelected()));
            AddBinding(new TriggerBinding("TileLocker", "sellSetRedMode", SetRedMode));
            AddBinding(new TriggerBinding("TileLocker", "sellSetKeepMode", SetKeepMode));
            AddBinding(new TriggerBinding("TileLocker", "sellReturnToBuying", ReturnToBuying));
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;
            base.OnDestroy();
        }

        private void OpenPanel()
        {
            _panelVisible.Update(true);
            _sellStatus.Update("Ready");
        }

        private void ClosePanel()
        {
            _panelVisible.Update(false);
            ExitSellMode();
        }

        private void EnterSellMode()
        {
            _panelVisible.Update(true);
            _sellModeActive.Update(true);
            _sellStatus.Update(
                _keepMode.value
                    ? "Purple KEEP mode - mark the tiles recovery should preserve"
                    : "Red SELL mode - mark owned tiles to re-lock");
            SellTilesControllerSystem.RequestActivate();
        }

        private void SetRedMode()
        {
            _keepMode.Update(false);
            _sellStatus.Update("Red SELL mode - marked tiles will be re-locked");
            SellTilesControllerSystem.RequestSetRedMode();
        }

        private void SetKeepMode()
        {
            _keepMode.Update(true);
            _sellStatus.Update(
                "Purple KEEP mode - mark tiles to preserve, then use Options → Tile Locker → Recover from built-in Unlock Map Tiles");
            SellTilesControllerSystem.RequestSetKeepMode();
        }

        private void ExitSellMode()
        {
            _sellModeActive.Update(false);
            _sellStatus.Update("Selection mode closed. Marks are preserved.");
            SellTilesControllerSystem.RequestDeactivate();
        }

        private void ReturnToBuying()
        {
            _panelVisible.Update(true);
            _sellModeActive.Update(false);
            _sellStatus.Update("Returned to buying. Marks are preserved.");
            SellTilesControllerSystem.RequestReturnToBuying();
        }

        internal void SetSelectionState(int count, int refund, bool canSell, bool estimated = false)
        {
            _selectedCount.Update(count);
            _refundAmount.Update(refund);
            _refundEstimated.Update(estimated);
            _canSell.Update(canSell);
        }

        internal void SetStatus(string text)
        {
            _sellStatus.Update(text ?? string.Empty);
        }

        internal void SetMode(bool active)
        {
            _sellModeActive.Update(active);
        }

        internal void SetKeepMode(bool keepMode)
        {
            _keepMode.Update(keepMode);
        }
    }
}
