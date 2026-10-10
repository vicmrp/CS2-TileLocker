Tile Locker lets you sell and re-lock map tiles in Cities: Skylines II.

Use the **TL** button in the top-right toolbar to open Tile Locker.

### Sell tiles

Select **Red: SELL**, mark the currently owned tiles you want to sell, and re-lock them.

Selection and selling work while the game is paused. The panel previews your refund before you select **Sell selected tiles**. Purchases made with version 1.1.0 or later retain their exact paid cost, including batch rounding, in the save. Existing purchases have no recorded invoice and use a clearly labelled estimate based on the game's tile price and current ownership count. Free starting tiles refund nothing. Purchases made with unlimited money refund nothing.

The tiles are returned to the game's normal map-tile system so they can be purchased again using the normal map tile interface.

### Keep tiles

Select **Purple: KEEP** to mark tiles you want to preserve when recovering a city that was started or loaded using the game's built-in **Unlock Map Tiles** option.

After selecting the tiles you want to keep, open:

**Options → Tile Locker → Recover from built-in Unlock Map Tiles**

Tile Locker will disable the built-in unlock-all state, restore the normal map tile system, re-lock the other tiles, and preserve the tiles you selected.

**WARNING:** Make sure to save a copy of your city before using the recovery function. While testing has not revealed any game-breaking issues, recovery modifies the ownership state of map tiles.

### Save compatibility

Tile Locker works with the game's native MapTile ECS state. Optional purchase receipts are saved on those tiles so refunds survive reloads. Keep Tile Locker enabled to retain that purchase history.

### Credits

Implementation was informed by the Apache-2.0 licensed **FiveTwentyNineTiles** project by algernon.
