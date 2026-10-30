using System.Collections;
using System.Reflection;
using BepInEx.Configuration;
using Kydl.Common;

namespace ChestDecor
{
    /// <summary>
    /// Keeps a player's value when a setting moves to another section or key (settings are grouped by functionality, D-074). BepInEx keeps the
    /// entries of the file that are not bound (any more) in ConfigFile.OrphanedEntries; the value of the old place is copied to the new entry
    /// when the player has not set the new one. Call after every Bind and BEFORE ConfigTools.DropRemoved.
    /// </summary>
    internal static class ConfigMigration
    {
        /// Expected outcome: true when the value of [oldSection] oldKey was copied into `entry`.
        internal static bool Move<T>(ConfigFile c, string oldSection, string oldKey, ConfigEntry<T> entry)
        {
            try
            {
                var prop = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var dict = prop != null ? prop.GetValue(c, null) as IDictionary : null;
                var def = new ConfigDefinition(oldSection, oldKey);
                if (dict == null || !dict.Contains(def)) { ModLog.Verbose("ConfigMigration: nothing to move for [" + oldSection + "] " + oldKey); return false; }
                var raw = dict[def] as string;
                if (!Equals(entry.Value, entry.DefaultValue))
                {
                    ModLog.Verbose("ConfigMigration: [" + entry.Definition.Section + "] " + entry.Definition.Key + " is already set, the old [" + oldSection + "] " + oldKey + " is dropped");
                    return false;
                }
                entry.SetSerializedValue(raw);
                ModLog.Info("Config: [" + oldSection + "] " + oldKey + " = " + raw + " kept as [" + entry.Definition.Section + "] " + entry.Definition.Key);
                return true;
            }
            catch (TargetInvocationException e) { ModLog.Warn("Config: moving [" + oldSection + "] " + oldKey + " failed: " + e.InnerException?.Message); return false; }
            catch (System.ArgumentException e) { ModLog.Warn("Config: moving [" + oldSection + "] " + oldKey + " failed: " + e.Message); return false; }
        }
    }
}
