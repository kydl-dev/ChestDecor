using BepInEx.Configuration;
using Kydl.Common;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// All settings in one place (user rule, every mod):
    ///  * PLAYER CONFIG (BepInEx entries below, bound once from Plugin.Awake): only what a player should change. Clear descriptions, ranges,
    ///    drop-downs (enums). Sections and key names are the ones ChestDecor 1.0.0 used, so players keep their settings.
    ///  * DEV-ONLY values (Tunable fields below): fixed values, NOT in the player's file. KydlTools (F7) finds every static Tunable here.
    /// Every value is read where it is used (.Value), so a change applies live (PROJECT_RULES 17).
    /// </summary>
    internal static class ModSettings
    {
        // [General]
        public static ConfigEntry<bool> EnableAll;
        public static ConfigEntry<KeyboardShortcut> Hotkey;
        public static ConfigEntry<bool> RequireAccess;
        public static ConfigEntry<bool> OnlyPlayerBuilt;
        public static ConfigEntry<bool> ShowHoverHint;
        // [Colours]
        public static ConfigEntry<bool> ColoursEnable;
        public static ConfigEntry<bool> LivePreview;
        public static ConfigEntry<ColorMode> DefaultMode;
        public static ConfigEntry<bool> SplitParts;
        // [Attachments]
        public static ConfigEntry<bool> AttachmentsEnable;
        public static ConfigEntry<int> MaxAttachments;
        public static ConfigEntry<bool> FreeAttachments;
        public static ConfigEntry<bool> RequireKnown;
        public static ConfigEntry<bool> RefundOnRemove;
        public static ConfigEntry<string> ExtraDecorPrefabs;
        public static ConfigEntry<bool> IncludeTreeSaplings;
        public static ConfigEntry<string> IgnoredIngredientsSetting;
        // [Camera]
        public static ConfigEntry<bool> FreeCameraEnabled;      // [Camera] Enable
        public static ConfigEntry<float> FreeCameraMaxDistance;
        public static ConfigEntry<float> FreeCameraSpeed;
        public static ConfigEntry<float> FreeCameraLookSpeed;
        // [DebugLog]
        public static ConfigEntry<bool> DebugLog;

        /// Is the mod switched on ([General] EnableAll)? Off = inert: no hotkey, no hint, no window; decorated chests keep their look.
        public static bool On { get { return EnableAll == null || EnableAll.Value; } }

        /// Each functionality works only when EnableAll AND its own Enable are on (PROJECT_RULES 11); off = does nothing, its UI is hidden.
        public static bool ColoursOn { get { return Functions.IsOn(On, ColoursEnable == null || ColoursEnable.Value); } }
        public static bool AttachmentsOn { get { return Functions.IsOn(On, AttachmentsEnable == null || AttachmentsEnable.Value); } }
        public static bool CameraOn { get { return Functions.IsOn(On, FreeCameraEnabled == null || FreeCameraEnabled.Value); } }
        /// The window has something to show (colours or attachments).
        public static bool AnyEditorPage { get { return ColoursOn || AttachmentsOn; } }

        // --- dev-only values (Tunables; the [Debug] entries of 1.0.0 live here now): KydlTools changes them for testing ------------------------
        public static readonly Tunable<bool> DumpTextures = new Tunable<bool>("Debug", "DumpTextures", false,
            "Writes the chest textures and the detected part masks as PNG files to BepInEx/cache/ChestDecorDump (once per texture per game start).");
        public static readonly Tunable<bool> DumpCatalog = new Tunable<bool>("Debug", "DumpCatalog", false,
            "Writes the attachable items (categories, costs, sources) and the left-out candidates (with the reason) to BepInEx/cache/ChestDecorDump/AttachCatalog.txt whenever the list is built.");
        public static readonly Tunable<bool> VerboseModelDiagnostics = new Tunable<bool>("Debug", "VerboseModelDiagnostics", false,
            "Logs renderer / LOD filtering details for prefabs that have no usable model (why an item is left out).");
        public static readonly Tunable<bool> ShowTestItems = new Tunable<bool>("Debug", "ShowTestItems", false,
            "Lists developer / test items (\"test\" in the prefab or shown name) in the Attach items tab. Applies at once.");
        public static readonly Tunable<bool> LogPartScan = new Tunable<bool>("Debug", "LogPartScan", false,
            "Writes a one-time report per chest type listing every renderer and material and which became colourable parts.");
        public static readonly Tunable<float> MaxWindowDistance = new Tunable<float>("Window", "MaxWindowDistance", 10f, 3f, 30f,
            "The decorate window closes when the player is further than this from the chest (metres).");
        public static readonly Tunable<float> AttachRefreshSeconds = new Tunable<float>("Window", "AttachRefreshSeconds", 1.5f, 0.25f, 10f,
            "How often the Attach items rows re-check what the player can afford (seconds).");
        public static readonly Tunable<int> AttachRowsPerPage = new Tunable<int>("Window", "AttachRowsPerPage", 8, 4, 20,
            "Rows on one page of the Attach items list (the window scrolls when they do not fit).");

        /// Expected outcome: every player entry is bound (returns that check).
        public static bool Bind(ConfigFile c)
        {
            var savedLines = ConfigTools.Capture(c);     // before the first Bind: every Bind saves the file with what BepInEx parsed

            EnableAll = c.Bind("General", "EnableAll", true,
                "Switches Chest Decor on / off while playing. Off: no hotkey, no hover hint and no window; decorated chests keep their look.");
            Hotkey = c.Bind("General", "Customize Hotkey", new KeyboardShortcut(KeyCode.K),
                "Look at a chest and press this key to open the decorate window (colours, attachments, positions).");
            RequireAccess = c.Bind("General", "Require Chest Access", true,
                "Only allow decorating chests you could also open (private chests, wards).");
            OnlyPlayerBuilt = c.Bind("General", "Only Player Built Chests", true,
                "Only allow decorating chests that players built (not world-generated loot chests).");
            ShowHoverHint = c.Bind("General", "Show Hover Hint", true,
                "Add a '[key] Decorate chest' line to the chest hover text.");
            ColoursEnable = c.Bind("Colours", "Enable", true,
                "The Colours page of the decorate window (colour the parts of a chest). Off: the page is hidden. Needs EnableAll to be on as well.");
            LivePreview = c.Bind("Colours", "Live Preview", false,
                "Apply colour changes to the chest immediately while editing, instead of only when pressing Preview.");
            DefaultMode = c.Bind("Colours", "Default Colour Mode", ColorMode.Tint,
                "Colour mode used for parts that have no colour yet. Tint multiplies the chest's existing texture (dark textures stay dark). " +
                "Full repaints the texture so dark chests change colour too. You can still switch per part in the editor.");
            SplitParts = c.Bind("Colours", "Split Texture Into Parts", true,
                "Most chests use one material and one texture for both wood and metal. When enabled, the texture is analysed and split into two " +
                "colour regions (base / trim) that can be coloured separately. Disable to colour each material as a single part.");

            AttachmentsEnable = c.Bind("Attachments", "Enable", true,
                "The Attach items and Position items pages of the decorate window (attach decorations to a chest). Off: the pages are hidden; " +
                "attachments already on chests stay visible. Needs EnableAll to be on as well.");
            MaxAttachments = c.Bind("Attachments", "Max Attachments Per Chest", 5,
                new ConfigDescription("How many items can be attached to one chest.", new AcceptableValueRange<int>(1, 8)));
            FreeAttachments = c.Bind("Attachments", "Free Attachments", false,
                "Attaching costs nothing. Mostly useful for admins / testing. Items attached while this is on are remembered as free and are never " +
                "refunded, even after you turn it off. Items that were paid for are still refunded (exactly what was paid) when removed.");
            RequireKnown = c.Bind("Attachments", "Require Known Recipe", true,
                "Only allow attaching things whose recipe / piece you have already unlocked.");
            RefundOnRemove = c.Bind("Attachments", "Refund On Remove", true,
                "Removing an attachment drops the materials it cost at your feet: crafted items and furniture are broken down into their ingredients, " +
                "trader goods give back the coins. The finished item is never returned. Each attachment remembers exactly what was paid, so recipe or " +
                "config changes made afterwards do not change the refund.");
            ExtraDecorPrefabs = c.Bind("Attachments", "Extra Decorative Prefabs", "",
                "Comma separated prefab names (items or pieces) to list under 'Decor' in addition to the hammer's Furniture category, " +
                "e.g. 'Cloudberry,Pickable_Mushroom'. Takes effect immediately.");
            IncludeTreeSaplings = c.Bind("Attachments", "Include Tree Saplings", true,
                "List the small saplings of trees (Beech, Birch, Oak, Fir, Pine, ...) under 'Produce', planted with the tree's seed, cone or acorn. " +
                "Only the sapling is ever shown, never the grown tree. Off: no trees at all (the seeds, cones and acorns stay listed). Takes effect immediately.");
            IgnoredIngredientsSetting = c.Bind("Attachments", "Ignored Ingredients", "BattleIdol,ProtectionIdol",
                "Comma separated ingredient names that are left out of every attachment cost, e.g. items some recipes list only for upgrading. " +
                "The Battle / Protection Idols (Upgrader1..7Weapon / Armor) are always left out. Matches the prefab or item name, ignoring case, " +
                "spaces and symbols. Takes effect immediately.");

            FreeCameraEnabled = c.Bind("Camera", "Enable", true,
                "While the decorate window is open, let the camera circle the chest (WASD / arrows or hold right mouse to circle, mouse wheel " +
                "or E / Q to zoom, F to go back to the normal camera). Needs no admin rights.");
            FreeCameraMaxDistance = c.Bind("Camera", "Camera Max Distance", 5f,
                new ConfigDescription("Furthest the camera may zoom out from the chest, in metres (closest is always 1 m from it).", new AcceptableValueRange<float>(3f, 10f)));
            FreeCameraSpeed = c.Bind("Camera", "Camera Speed", 3f,
                new ConfigDescription("How fast the keys circle (x 30 degrees per second) and zoom (metres per second); hold Shift for 3x.", new AcceptableValueRange<float>(0.5f, 20f)));
            FreeCameraLookSpeed = c.Bind("Camera", "Camera Look Sensitivity", 0.2f,
                new ConfigDescription("How far the camera circles per pixel of mouse movement (right mouse drag), in degrees.", new AcceptableValueRange<float>(0.02f, 1f)));

            DebugLog = c.Bind("DebugLog", "Enable", false,
                "Writes detailed lines about everything the mod does to the BepInEx log (for bug reports). Leave off while playing normally.");

            ConfigTools.ApplyCommaFix(c, savedLines);    // after every Bind: "3,5" typed by hand means 3.5, not 35
            // settings are grouped by functionality now: the player's values of the old places are kept
            ConfigMigration.Move(c, "General", "Live Preview", LivePreview);
            ConfigMigration.Move(c, "General", "Default Colour Mode", DefaultMode);
            ConfigMigration.Move(c, "General", "Split Texture Into Parts", SplitParts);
            ConfigMigration.Move(c, "Camera", "Free Camera", FreeCameraEnabled);
            ConfigTools.DropRemoved(c);                  // the 1.0.0 [Debug] entries are Tunables now, the moved entries are gone from the file

            EnableAll.SettingChanged += (s, e) =>
            {
                ModLog.Info("Config: EnableAll = " + EnableAll.Value + (EnableAll.Value ? ", Chest Decor is on" : ", Chest Decor is inert (no hotkey, hint or window)"));
                if (!EnableAll.Value) ChestDecorUI.Close();
            };
            // a functionality switched off while the window is open: close it, it shows the pages again when it is opened next
            ColoursEnable.SettingChanged += (s, e) => { ModLog.Info("Config: [Colours] Enable = " + ColoursEnable.Value); ChestDecorUI.Close(); };
            AttachmentsEnable.SettingChanged += (s, e) => { ModLog.Info("Config: [Attachments] Enable = " + AttachmentsEnable.Value); ChestDecorUI.Close(); };
            FreeCameraEnabled.SettingChanged += (s, e) => { ModLog.Info("Config: [Camera] Enable = " + FreeCameraEnabled.Value); ChestDecorUI.Close(); };   // the footer hint follows it
            ShowTestItems.SettingChanged += (s, e) => Plugin.OnCatalogSettingChanged("ShowTestItems");
            ExtraDecorPrefabs.SettingChanged += (s, e) => Plugin.OnCatalogSettingChanged("Extra Decorative Prefabs");
            IncludeTreeSaplings.SettingChanged += (s, e) => Plugin.OnCatalogSettingChanged("Include Tree Saplings");
            IgnoredIngredientsSetting.SettingChanged += (s, e) => { IgnoredIngredients.Invalidate(); Plugin.OnCatalogSettingChanged("Ignored Ingredients"); };
            MaxAttachments.SettingChanged += (s, e) => ChestDecorUI.SettingsChanged("Max Attachments Per Chest");
            FreeAttachments.SettingChanged += (s, e) => ChestDecorUI.SettingsChanged("Free Attachments");
            RequireKnown.SettingChanged += (s, e) => ChestDecorUI.SettingsChanged("Require Known Recipe");
            AttachRowsPerPage.SettingChanged += (s, e) => ChestDecorUI.SettingsChanged("AttachRowsPerPage");

            ModLog.Verbose("Config: bound (EnableAll " + EnableAll.Value + ", hotkey " + Hotkey.Value + ", max attachments " + MaxAttachments.Value +
                           ", colours " + ColoursEnable.Value + ", attachments " + AttachmentsEnable.Value + ", camera " + FreeCameraEnabled.Value + ", debug log " + DebugLog.Value + ")");
            return EnableAll != null && Hotkey != null && FreeCameraLookSpeed != null && DebugLog != null;
        }
    }
}
