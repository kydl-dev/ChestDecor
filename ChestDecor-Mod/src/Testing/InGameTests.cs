using Kydl.Common;

namespace ChestDecor.Testing
{
    /// <summary>
    /// In-game tests run by the user's Kydl.GameProbe (dev tool, never a dependency: it finds this class by its NAME `InGameTests`).
    ///   Startup_*  run when the main menu is reached;   World_*  run 5 s after a world is loaded; both again on GameProbe's RetestKey (PageDown).
    /// Each method: static, no parameters, returns a text starting with PASS / FAIL / SKIP and the detail with numbers + units.
    /// Keep them cheap and read-only: they also run in players' games when GameProbe is installed there (it never is for players).
    /// </summary>
    internal static class InGameTests
    {
        /// The player config is bound and the mod is switched on.
        static string Startup_ConfigBound()
        {
            if (ModSettings.EnableAll == null) return "FAIL: ModSettings.Bind did not run (EnableAll is null)";
            return ModSettings.On ? "PASS: EnableAll on, customize hotkey " + ModSettings.Hotkey.Value + ", max attachments " + ModSettings.MaxAttachments.Value
                                  : "SKIP: EnableAll is off in the config";
        }

        /// KydlCore (hard dependency, 2.0.0) is loaded: it owns the KY hub (F6) with this mod's settings page, the one UI root and the input lock.
        static string Startup_CoreLoaded()
        {
            bool loaded = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(KydlCore.Plugin.ModGuid);
            ModLog.Verbose("InGameTests: Startup_CoreLoaded " + loaded);
            return loaded ? "PASS: " + KydlCore.Plugin.ModGuid + " is loaded" : "FAIL: " + KydlCore.Plugin.ModGuid + " is not loaded (this mod requires it)";
        }

        /// The UI Toolkit panel of Core's UI root can be made (the decorate window is opened on it through KyUi).
        static string Startup_UiPanel()
        {
            return KydlCore.KyUi.Root != null ? "PASS: Core's UI Toolkit root ready" : "FAIL: KyUi.Root is null - the decorate window cannot open";
        }

        /// The attachment list builds in a loaded world and is not empty; reports the KY-Items count (0 is fine without other KydlDev mods).
        static string World_CatalogBuilt()
        {
            var n = AttachCatalog.Entries.Count;
            ModLog.Verbose("InGameTests: World_CatalogBuilt " + n + " entries, " + AttachCatalog.KyListed + " KY-Items");
            if (n == 0) return "FAIL: the attachment list is empty 5 s after the world loaded";
            return "PASS: " + n + " attachable entries, " + AttachCatalog.KyListed + " KY-Items (KY-Items filter " + (AttachCatalog.KyListed > 0 ? "shown" : "hidden") + ")";
        }

        /// Every listed KY-Items entry really comes from a com.kydldev.* mod or the KY-Items build category (its Sources say so).
        static string World_KyItemsSourced()
        {
            int bad = 0, ky = 0;
            string first = null;
            foreach (var e in AttachCatalog.Entries)
            {
                if ((e.Categories & AttachCategory.KyItems) == 0) continue;
                ky++;
                if (e.Sources == null || e.Sources.IndexOf("KY-Items", System.StringComparison.Ordinal) < 0) { bad++; if (first == null) first = e.Prefab; }
            }
            if (ky == 0) return "SKIP: no KY-Items listed (no other com.kydldev.* mod with items is loaded)";
            return bad == 0 ? "PASS: " + ky + " KY-Items, all with their source mod" : "FAIL: " + bad + " of " + ky + " KY-Items without a source, first '" + first + "'";
        }
    }
}
