using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Areas;
using Game.Modding;
using Game.SceneFlow;
using Game.Serialization;
using HarmonyLib;

namespace CS2_TileLocker
{
    public sealed class Mod : IMod
    {
        public const string ModName = "TileLocker";
        public const string Version = "1.1.1";
        private Harmony _harmony;

        internal static Mod Instance { get; private set; }
        internal ILog Log { get; private set; }
        internal ModSettings Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Instance = this;
            Log = LogManager.GetLogger(ModName);
            Log.Info($"Loading Tile Locker {Version}");
            _harmony = new Harmony("net.vezit.tilelocker");
            _harmony.PatchAll(typeof(Mod).Assembly);

            Settings = new ModSettings(this);
            Settings.RegisterInOptionsUI();

            GameManager.instance.localizationManager.AddSource(
                "en-US",
                new ModSettingsDefaultLocale(Settings));

            AssetDatabase.global.LoadSettings(
                ModName,
                Settings,
                new ModSettings(this));

            updateSystem.UpdateAfter<PostDeserialize<TileLockerSystem>, PostDeserialize<MapTileSystem>>(
                SystemUpdatePhase.Deserialize);

            updateSystem.UpdateAt<TileLockerSystem>(SystemUpdatePhase.GameSimulation);

            // Experimental higher-level vanilla Unlock Map Tiles repair.
            updateSystem.UpdateAt<VanillaTileModeRepairSystem>(SystemUpdatePhase.GameSimulation);

            // Sell Tiles: always-updating request/input controller + custom tool + UI + overlay.
            updateSystem.UpdateBefore<SellTilesControllerSystem, SellTilesToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<SellTilesUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<SellTilesToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<SellTilesOverlaySystem>(SystemUpdatePhase.Rendering);
        }

        public void OnDispose()
        {
            _harmony?.UnpatchAll("net.vezit.tilelocker");
            Settings?.UnregisterInOptionsUI();
            Instance = null;
        }
    }
}
