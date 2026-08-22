using Colossal;
using System.Collections.Generic;

namespace CS2_TileLocker
{
    public sealed class ModSettingsDefaultLocale : IDictionarySource
    {
        private readonly Dictionary<string, string> _entries;

        public ModSettingsDefaultLocale(ModSettings settings)
        {
            _entries = LoadSettingsLocale(settings);
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts)
        {
            return _entries;
        }

        public void Unload()
        {
        }

        private static Dictionary<string, string> LoadSettingsLocale(ModSettings settings)
        {
            return new Dictionary<string, string>
            {
                { settings.GetSettingsLocaleID(), "Tile Locker" },

                { settings.GetOptionGroupLocaleID("Recovery"), "Recovery" },

                {
                    settings.GetOptionLabelLocaleID(nameof(ModSettings.RecoverBuiltInUnlockAll)),
                    "Recover from built-in Unlock Map Tiles"
                },

                {
                    settings.GetOptionDescLocaleID(nameof(ModSettings.RecoverBuiltInUnlockAll)),
                    "WARNING: Make sure to save a copy of your city before you click this button. " +
                    "There might be bugs. So far I have not discovered any game-breaking bugs myself. " +
                    "Tip: Open TL and select the tiles you want to keep before clicking this button."
                },

                // Exact warning localization key requested by Cities: Skylines II.
                {
                    "Options.WARNING[CS2-TileLocker.CS2_TileLocker.Mod.ModSettings.RecoverBuiltInUnlockAll]",
                    "Save a copy of your city before you continue."
                },
            };
        }
    }
}