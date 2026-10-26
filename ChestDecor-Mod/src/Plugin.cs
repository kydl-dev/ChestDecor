using System;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Jotunn.Utils;
using Kydl.Common;
using UnityEngine;
using ChestDecor.Ui;

namespace ChestDecor
{
    /// <summary>
    /// ChestDecor: decorate chests (colours, attached items, positions). Cosmetic only.
    /// Entry point. Order in Awake matters: log first, then the config (player settings), then text, then everything that uses them.
    /// </summary>
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Purely cosmetic: players without the mod can still join, they just see the normal chest.
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public class Plugin : BaseUnityPlugin
    {
        // ModGuid never changes (it is the config file name of 1.0.0: BepInEx/config/com.kydldev.chestdecor.cfg).
        // ModVersion = manifest.json version_number = newest CHANGELOG heading (tests check it).
        public const string ModGuid = "com.kydldev.chestdecor";
        public const string ModName = "ChestDecor";
        public const string ModVersion = "1.1.0";

        internal static Plugin Instance;
        Harmony _harmony;
        ConfigReloader _cfgWatcher;

        void Awake()
        {
            Instance = this;
            ModLog.Init(Logger, ModName, () => ModSettings.DebugLog != null && ModSettings.DebugLog.Value);
            ModLog.Info(ModName + " " + ModVersion + " loaded (dll built " + File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd HH:mm:ss") + ")");

            // the shared UI kit (Kydl.Ui) logs through the mod's log: debug lines only with the debug switch on
            Kydl.Ui.UiLog.VerboseSink = s => ModLog.Verbose(s);
            Kydl.Ui.UiLog.WarnSink = s => ModLog.Warn(s);
            Kydl.Ui.UiLog.ErrorSink = s => ModLog.Error(s);
            Kydl.Ui.UiKit.Owner = ModName;

            ModSettings.Bind(Config);
            _cfgWatcher = new ConfigReloader(Config);
            Loc.Register(UiText.En);

            UiInputLock.Install(ModGuid);           // the character does not move while the decorate window is open
            _harmony = new Harmony(ModGuid);
            int failed = ApplyPatches();
            ModLog.Info(ModName + " ready: customize hotkey " + ModSettings.Hotkey.Value + (failed > 0 ? ", " + failed + " patch(es) missing" : ""));
        }

        /// <summary>
        /// Applies every Harmony patch on its own. Harmony throws when a patch's target no longer exists or its signature changed (a game
        /// update); PatchAll would then stop at the first one and take the whole mod with it. Here a failing patch is logged with what stops
        /// working, and the others are still applied. A new patch class has to be added to this list.
        /// Expected outcome: returns the number of patches that failed (0 = all applied).
        /// </summary>
        int ApplyPatches()
        {
            int failed = 0;
            failed += TryPatch(typeof(ContainerAwakePatch), "chests can't be decorated at all (nothing gets the decorator)");
            failed += TryPatch(typeof(ContainerHoverPatch), "the '[K] Decorate chest' hint is not shown (the hotkey still works)");
            failed += TryPatch(typeof(MenuUpdatePatch), "Esc also opens the pause menu when it closes the editor");
            failed += TryPatch(typeof(GameCameraLateUpdatePatch), "the free camera is off while the editor is open", FreeCamera.Unavailable);
            failed += TryPatch(typeof(ChestAttachmentDestroyRefundPatch), "attachments are not refunded when a chest is destroyed by damage");
            failed += TryPatch(typeof(ChestAttachmentRemoveRefundPatch), "attachments are not refunded to the player who removes a chest with the hammer");
            if (failed > 0)
                ModLog.Warn(failed + " patch(es) could not be applied, probably because the game changed; the rest of " + ModName + " still works. Please report this with the game version.");
            else
                ModLog.Verbose("Plugin: all 6 patch classes applied");
            return failed;
        }

        /// Applies one patch class. Expected outcome: 0 when it was applied, 1 when it failed.
        int TryPatch(Type patchClass, string consequence, Action onFailure = null)
        {
            try
            {
                var patched = _harmony.CreateClassProcessor(patchClass).Patch();
                ModLog.Verbose("Plugin: patch " + patchClass.Name + " applied (" + (patched != null ? patched.Count : 0) + " method(s))");
                return patched != null && patched.Count > 0 ? 0 : 1;
            }
            catch (HarmonyException e) { return PatchFailed(patchClass, consequence, onFailure, e); }
            catch (Exception e) { return PatchFailed(patchClass, consequence, onFailure, e); }
        }

        static int PatchFailed(Type patchClass, string consequence, Action onFailure, Exception e)
        {
            ModLog.Error("Patch " + patchClass.Name + " failed, so " + consequence + ". " + e.GetType().Name + ": " + e.Message);
            try { onFailure?.Invoke(); }
            catch (Exception inner) { ModLog.Verbose("Plugin: fallback after " + patchClass.Name + " failed as well: " + inner.Message); }
            return 1;
        }

        /// A setting that decides what the Attach items tab lists changed: rebuild the list, also in an open window.
        internal static void OnCatalogSettingChanged(string what)
        {
            ModLog.Verbose("Plugin: '" + what + "' changed - attachment list rebuilt on next use");
            AttachCatalog.Invalidate();
            ChestDecorUI.CatalogChanged();
        }

        void Update()
        {
            if (_cfgWatcher != null) _cfgWatcher.Tick();
            DecorOpener.WatchWorld();
            if (!ModSettings.On) return;
            DecorOpener.Tick();
        }

        void OnDestroy()
        {
            if (_cfgWatcher != null) { _cfgWatcher.Dispose(); _cfgWatcher = null; }
            DecorOpener.Shutdown();
            _harmony?.UnpatchSelf();
            NeutralTextures.Clear();
            ZoneMap.Clear();
            AttachCatalog.Invalidate();
            AttachVisuals.ClearCache();
            AttachCost.ClearCache();
            ModLog.Verbose("Plugin: destroyed (patches removed, caches cleared)");
        }
    }
}
