using System;
using System.IO;
using System.Reflection;
using System.Text;
using Game;
using Unity.Entities;

namespace CS2_TileLocker
{
    /// <summary>
    /// Experimental repair for cities saved with vanilla "Unlock Map Tiles".
    ///
    /// v0.8 diagnostics proved the relevant difference:
    ///
    /// NORMAL:
    ///   CityConfigurationSystem.unlockMapTiles = false
    ///   overrideUnlockMapTiles = false
    ///   m_UnlockMapTiles = false
    ///   m_LoadedUnlockMapTiles = false
    ///   UserState.unlockMapTiles = false
    ///   MapTilePurchaseSystem.GetMapTileUpkeepEnabled() = true
    ///
    /// UNLOCK-MAP-TILES SAVE:
    ///   all of the above booleans = true
    ///   GetMapTileUpkeepEnabled() = false
    ///
    /// This system resets ONLY that higher-level vanilla mode state.
    /// It deliberately does NOT re-lock hundreds of tiles automatically.
    /// After this succeeds, use the existing Sell Tiles tool to re-lock a test tile.
    /// </summary>
    public sealed partial class VanillaTileModeRepairSystem : GameSystemBase
    {
        private static bool s_requestRepair;
        private static bool s_requestStatus;
        private static bool s_requestRepairAndRelockAll;

        private string _logPath;

        internal static void RequestRepair()
        {
            s_requestRepair = true;
        }

        internal static void RequestStatus()
        {
            s_requestStatus = true;
        }

        internal static void RequestRepairAndRelockAll()
        {
            s_requestRepairAndRelockAll = true;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            Directory.CreateDirectory(@"C:\Temp\TileLocker");
            _logPath = Path.Combine(
                @"C:\Temp\TileLocker",
                $"VanillaTileModeRepair-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        }

        protected override void OnUpdate()
        {
            if (s_requestStatus)
            {
                s_requestStatus = false;
                WriteCurrentState("STATUS");
            }

            if (s_requestRepairAndRelockAll)
            {
                s_requestRepairAndRelockAll = false;

                try
                {
                    WriteLog(
                        "FULL RECOVERY BEGIN",
                        "Repair built-in Unlock Map Tiles flags, then perform 529-style full re-lock.");

                    Repair();

                    TileLockerSystem tileLocker =
                        World.GetOrCreateSystemManaged<TileLockerSystem>();

                    SellTilesToolSystem sellTool =
                        World.GetOrCreateSystemManaged<SellTilesToolSystem>();

                    using Unity.Collections.NativeArray<Entity> keepTiles =
                        sellTool.IsKeepMode
                            ? sellTool.GetSelectedTiles(Unity.Collections.Allocator.Temp)
                            : new Unity.Collections.NativeArray<Entity>(
                                0,
                                Unity.Collections.Allocator.Temp);

                    WriteLog(
                        "FULL RECOVERY KEEP SELECTION",
                        $"keepMode={sellTool.IsKeepMode}; purpleCount={keepTiles.Length}");

                    tileLocker.RelockAllLike529(keepTiles);

                    // Keep the purple marks after recovery. The user can remove
                    // them explicitly with "Clear marks".
                    WriteCurrentState("FULL RECOVERY AFTER");

WriteLog("FULL RECOVERY COMPLETE", "Success.");
                }
                catch (Exception ex)
                {
                    WriteLog("FULL RECOVERY ERROR", ex.ToString());

Mod.Instance?.Log?.Error(ex, "Full vanilla tile recovery failed.");
                }

                return;
            }

            if (!s_requestRepair)
                return;

            s_requestRepair = false;

            try
            {
                Repair();
            }
            catch (Exception ex)
            {
                WriteLog("REPAIR ERROR", ex.ToString());

Mod.Instance?.Log?.Error(ex, "Vanilla tile mode repair failed.");
            }
        }

        private void Repair()
        {
            object cityConfig = FindSystem("Game.City.CityConfigurationSystem");
            object mapTilePurchase = FindSystem("Game.Simulation.MapTilePurchaseSystem");
            object userState = GetUserState();

            WriteLog(
                "REPAIR BEGIN",
                BuildState(cityConfig, userState, mapTilePurchase));

            if (cityConfig == null)
                throw new InvalidOperationException("CityConfigurationSystem was not found.");

            // 1. Use the actual vanilla properties first.
            SetProperty(cityConfig, "overrideUnlockMapTiles", false);
            SetProperty(cityConfig, "unlockMapTiles", false);

            // 2. Normalize all known backing/load state discovered by v0.8.
            //    These are private fields, so reflection is intentional.
            SetField(cityConfig, "<overrideUnlockMapTiles>k__BackingField", false);
            SetField(cityConfig, "m_UnlockMapTiles", false);
            SetField(cityConfig, "m_LoadedUnlockMapTiles", false);

            // Normal save had this false as well.
            SetProperty(cityConfig, "overrideLoadedOptions", false);
            SetField(cityConfig, "<overrideLoadedOptions>k__BackingField", false);

            // 3. Vanilla MenuUISystem.ApplyOptions also writes UserState.unlockMapTiles
            //    and then calls Setting.ApplyAndSave(). Mirror that behavior.
            if (userState != null)
            {
                SetProperty(userState, "unlockMapTiles", false);
                SetField(userState, "<unlockMapTiles>k__BackingField", false);
                InvokeNoArgs(userState, "ApplyAndSave", required: false);
            }

            // 4. Ask MapTilePurchaseSystem to recalculate status immediately if possible.
            if (mapTilePurchase != null)
            {
                InvokeNoArgs(mapTilePurchase, "UpdateStatus", required: false);
            }

            string after = BuildState(cityConfig, userState, mapTilePurchase);
            WriteLog("REPAIR AFTER", after);

            bool configOff = ReadBool(cityConfig, "unlockMapTiles") == false;
            bool overrideOff = ReadBool(cityConfig, "overrideUnlockMapTiles") == false;
            bool loadedOff = ReadBoolField(cityConfig, "m_LoadedUnlockMapTiles") == false;
            bool upkeepEnabled = ReadBoolMethod(mapTilePurchase, "GetMapTileUpkeepEnabled");

            string result =
                $"configOff={configOff}; overrideOff={overrideOff}; loadedOff={loadedOff}; " +
                $"tileUpkeepEnabled={upkeepEnabled}";

            WriteLog("REPAIR RESULT", result);

            if (Mod.Instance?.Settings != null)
            {
            }
        }

        private void WriteCurrentState(string section)
        {
            object cityConfig = FindSystem("Game.City.CityConfigurationSystem");
            object mapTilePurchase = FindSystem("Game.Simulation.MapTilePurchaseSystem");
            object userState = GetUserState();

            string state = BuildState(cityConfig, userState, mapTilePurchase);
            WriteLog(section, state);

            if (Mod.Instance?.Settings != null)
            {
            }
        }

        private string BuildState(
            object cityConfig,
            object userState,
            object mapTilePurchase)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"CityConfigurationSystem.unlockMapTiles={FormatBool(ReadBool(cityConfig, "unlockMapTiles"))}");
            sb.AppendLine($"CityConfigurationSystem.overrideUnlockMapTiles={FormatBool(ReadBool(cityConfig, "overrideUnlockMapTiles"))}");
            sb.AppendLine($"CityConfigurationSystem.m_UnlockMapTiles={FormatBool(ReadBoolField(cityConfig, "m_UnlockMapTiles"))}");
            sb.AppendLine($"CityConfigurationSystem.m_LoadedUnlockMapTiles={FormatBool(ReadBoolField(cityConfig, "m_LoadedUnlockMapTiles"))}");
            sb.AppendLine($"CityConfigurationSystem.overrideLoadedOptions={FormatBool(ReadBool(cityConfig, "overrideLoadedOptions"))}");
            sb.AppendLine($"UserState.unlockMapTiles={FormatBool(ReadBool(userState, "unlockMapTiles"))}");

            if (mapTilePurchase != null)
            {
                sb.AppendLine($"GetMapTileUpkeepEnabled()={ReadMethodValue(mapTilePurchase, "GetMapTileUpkeepEnabled")}");
                sb.AppendLine($"GetAvailableTiles()={ReadMethodValue(mapTilePurchase, "GetAvailableTiles")}");
                sb.AppendLine($"CalculateOwnedTiles()={ReadMethodValue(mapTilePurchase, "CalculateOwnedTiles")}");
                sb.AppendLine($"IsMilestonesLeft()={ReadMethodValue(mapTilePurchase, "IsMilestonesLeft")}");
            }
            else
            {
                sb.AppendLine("MapTilePurchaseSystem=<not found>");
            }

            return sb.ToString().TrimEnd();
        }

        private object FindSystem(string fullName)
        {
            for (int i = 0; i < World.Systems.Count; i++)
            {
                ComponentSystemBase system = World.Systems[i];

                if (system != null &&
                    system.GetType().FullName == fullName)
                {
                    return system;
                }
            }

            return null;
        }

        private static object GetUserState()
        {
            Type sharedType = typeof(GameSystemBase).Assembly.GetType(
                "Game.Settings.SharedSettings",
                false,
                false);

            if (sharedType == null)
                return null;

            const BindingFlags flags =
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Static |
                BindingFlags.Instance;

            object shared = null;

            PropertyInfo instanceProp = sharedType.GetProperty("instance", flags);
            if (instanceProp != null)
                shared = instanceProp.GetValue(null, null);

            if (shared == null)
            {
                FieldInfo instanceField = sharedType.GetField("instance", flags);
                if (instanceField != null)
                    shared = instanceField.GetValue(null);
            }

            if (shared == null)
                return null;

            PropertyInfo userStateProp = sharedType.GetProperty("userState", flags);
            if (userStateProp != null)
                return userStateProp.GetValue(shared, null);

            FieldInfo userStateField = sharedType.GetField("userState", flags);
            return userStateField?.GetValue(shared);
        }

        private static void SetProperty(object obj, string name, bool value)
        {
            if (obj == null)
                return;

            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            PropertyInfo prop = obj.GetType().GetProperty(name, flags);

            if (prop == null)
                throw new MissingMemberException(obj.GetType().FullName, name);

            MethodInfo setter = prop.GetSetMethod(true);

            if (setter == null)
                throw new MissingMethodException(obj.GetType().FullName, "set_" + name);

            setter.Invoke(obj, new object[] { value });
        }

        private static void SetField(object obj, string name, bool value)
        {
            if (obj == null)
                return;

            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            FieldInfo field = obj.GetType().GetField(name, flags);

            if (field == null)
                throw new MissingFieldException(obj.GetType().FullName, name);

            field.SetValue(obj, value);
        }

        private static void InvokeNoArgs(
            object obj,
            string name,
            bool required)
        {
            if (obj == null)
                return;

            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            MethodInfo method = obj.GetType().GetMethod(
                name,
                flags,
                null,
                Type.EmptyTypes,
                null);

            if (method == null)
            {
                if (required)
                    throw new MissingMethodException(obj.GetType().FullName, name);

                return;
            }

            method.Invoke(obj, null);
        }

        private static bool? ReadBool(object obj, string name)
        {
            if (obj == null)
                return null;

            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            PropertyInfo prop = obj.GetType().GetProperty(name, flags);

            if (prop != null)
            {
                MethodInfo getter = prop.GetGetMethod(true);

                if (getter != null)
                    return (bool)getter.Invoke(obj, null);
            }

            return ReadBoolField(obj, name);
        }

        private static bool? ReadBoolField(object obj, string name)
        {
            if (obj == null)
                return null;

            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            FieldInfo field = obj.GetType().GetField(name, flags);

            if (field == null || field.FieldType != typeof(bool))
                return null;

            return (bool)field.GetValue(obj);
        }

        private static bool ReadBoolMethod(object obj, string name)
        {
            object value = ReadMethodValueObject(obj, name);

            return value is bool b && b;
        }

        private static string ReadMethodValue(object obj, string name)
        {
            object value = ReadMethodValueObject(obj, name);
            return value?.ToString() ?? "<unavailable>";
        }

        private static object ReadMethodValueObject(object obj, string name)
        {
            if (obj == null)
                return null;

            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            MethodInfo method = obj.GetType().GetMethod(
                name,
                flags,
                null,
                Type.EmptyTypes,
                null);

            return method?.Invoke(obj, null);
        }

        private static string FormatBool(bool? value)
        {
            return value.HasValue ? value.Value.ToString() : "<unavailable>";
        }

        private void WriteLog(string section, string message)
        {
            try
            {
                File.AppendAllText(
                    _logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{section}]\r\n" +
                    message + "\r\n" +
                    new string('-', 100) + "\r\n",
                    Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}
