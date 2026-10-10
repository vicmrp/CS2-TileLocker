import React from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import "./sell-tiles.scss";

const group = "TileLocker";

const panelVisible$ = bindValue<boolean>(group, "sellPanelVisible", false);
const sellModeActive$ = bindValue<boolean>(group, "sellModeActive", false);
const keepMode$ = bindValue<boolean>(group, "sellKeepMode", false);
const selectedCount$ = bindValue<number>(group, "sellSelectedCount", 0);
const refundAmount$ = bindValue<number>(group, "sellRefundAmount", 0);
const refundEstimated$ = bindValue<boolean>(group, "sellRefundEstimated", false);
const canSell$ = bindValue<boolean>(group, "sellCanSell", false);
const status$ = bindValue<string>(group, "sellStatus", "Ready");

export const SellTiles = () => {
  const visible = useValue(panelVisible$);
  const sellModeActive = useValue(sellModeActive$);
  const keepMode = useValue(keepMode$);
  const selectedCount = useValue(selectedCount$);
  const refundAmount = useValue(refundAmount$);
  const refundEstimated = useValue(refundEstimated$);
  const canSell = useValue(canSell$);
  const status = useValue(status$);

  const togglePanel = () =>
    trigger(group, visible ? "sellClosePanel" : "sellOpenPanel");

  return (
    <>
      <button
        type="button"
        className={`tl-toolbar-button ${visible ? "tl-toolbar-button-active" : ""}`}
        onClick={togglePanel}
      >
        TL
      </button>

      {visible && (
        <div className="tl-panel">
          <div className="tl-header">
            <div>
              <div className="tl-title">Tile Locker</div>
              <div className="tl-subtitle">Sell map tiles or choose recovery KEEP tiles</div>
            </div>
            <button
              type="button"
              className="tl-close"
              onClick={() => trigger(group, "sellClosePanel")}
            >
              ×
            </button>
          </div>

          <div className="tl-body">
            <div className="tl-status">{status}</div>

            {!sellModeActive ? (
              <button
                type="button"
                className="tl-primary"
                onClick={() => trigger(group, "sellEnterMode")}
              >
                Enter tile selection mode
              </button>
            ) : (
              <>
                <div className="tl-mode-tabs">
                  <button
                    type="button"
                    className={!keepMode ? "active-red" : ""}
                    onClick={() => trigger(group, "sellSetRedMode")}
                  >
                    Red: SELL
                  </button>

                  <button
                    type="button"
                    className={keepMode ? "active-purple" : ""}
                    onClick={() => trigger(group, "sellSetKeepMode")}
                  >
                    Purple: KEEP
                  </button>
                </div>

                <div className={`tl-help ${keepMode ? "tl-help-purple" : ""}`}>
                  {keepMode
                    ? "Mark the tiles you want to KEEP purple. Then go to Options → Tile Locker → Recover from built-in Unlock Map Tiles. Recovery will re-lock everything, then re-unlock these tiles through vanilla MapTilePurchaseSystem.UnlockTile."
                    : "Mark owned tiles red, then sell them to receive a refund. Starting tiles are free and have no refund."}
                </div>

                <div className="tl-card">
                  <small>{keepMode ? "Purple KEEP tiles" : "Red selected tiles"}</small>
                  <b>{selectedCount}</b>
                </div>

                {!keepMode && (
                  <div className="tl-card">
                    <small>{refundEstimated ? "Refund (includes estimated older purchases)" : "Refund"}</small>
                    <b>₡{Math.max(0, Math.round(refundAmount)).toLocaleString()}</b>
                  </div>
                )}

                <button
                  type="button"
                  className="tl-secondary"
                  onClick={() => trigger(group, "sellClearSelection")}
                  disabled={selectedCount <= 0}
                >
                  Clear marks
                </button>

                {!keepMode ? (
                  <button
                    type="button"
                    className="tl-danger"
                    onClick={() => trigger(group, "sellSelected")}
                    disabled={!canSell}
                  >
                    Sell selected tiles
                  </button>
                ) : (
                  <div className="tl-purple-notice">
                    Purple marks are preserved until you press Clear marks. They are used by the
                    <b> Recover from built-in Unlock Map Tiles </b>
                    action in Options.
                  </div>
                )}

                <button
                  type="button"
                  className="tl-secondary"
                  onClick={() => trigger(group, "sellReturnToBuying")}
                >
                  Return to buying
                </button>
              </>
            )}
          </div>
        </div>
      )}
    </>
  );
};
