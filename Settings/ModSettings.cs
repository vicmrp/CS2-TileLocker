using System.Xml.Serialization;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace CS2_TileLocker
{
    [FileLocation("ModsSettings/TileLocker/TileLocker")]
    public sealed class ModSettings : ModSetting
    {
        public ModSettings(IMod mod) : base(mod)
        {
        }

        [XmlIgnore]
        [SettingsUIButton]
        [SettingsUISection("Recovery")]
        [SettingsUIConfirmation]
        public bool RecoverBuiltInUnlockAll
        {
            set
            {
                if (value)
                    TileLockerSystem.RequestRecoverUnlockAll();
            }
        }

        public bool AlwaysDisable() => true;

        public override void SetDefaults()
        {
        }
    }
}
