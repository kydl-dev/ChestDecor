using System;
using System.Reflection;
using HarmonyLib;
using Kydl.Common;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// The way into the decorate window: the hotkey, the checks (looked-at chest, built by a player, access), the chest's editor lock and the
    /// short wait that settles a race between two players. Also drops the per-world texture caches when the world goes away.
    /// </summary>
    internal static class DecorOpener
    {
        // Container.CheckAccess is private; resolved once.
        static readonly MethodInfo CheckAccessMethod = AccessTools.Method(typeof(Container), "CheckAccess");
        // The game's own "is the player allowed to act right now" check (not in menus, chat, inventory...).
        static readonly MethodInfo TakeInputMethod = AccessTools.Method(typeof(Player), "TakeInput", Type.EmptyTypes);

        /// How long (seconds) to wait after taking a chest's editor lock before opening the window. Two players who press the key in the same
        /// instant both write the lock; once the data has synced only one of them is still the holder, the other one is told the chest is busy.
        const float LockConfirmSeconds = 0.5f;

        // A chest we hold the editor lock on whose window has not opened yet (see FinishOpen).
        static ChestDecorator _pending;
        static float _pendingAt;
        // The scene (world) the texture caches were filled in; 0 = no world loaded.
        static int _worldId;

        /// Every frame (Plugin.Update, mod on). Expected outcome: returns whether the window is open after this frame.
        internal static bool Tick()
        {
            if (ChestDecorUI.IsOpen) { ChestDecorUI.Tick(); return ChestDecorUI.IsOpen; }
            if (_pending != null)
            {
                if (Time.unscaledTime >= _pendingAt) FinishOpen();
                return ChestDecorUI.IsOpen;
            }

            var player = Player.m_localPlayer;
            if (player == null || ModSettings.Hotkey == null || !ModSettings.Hotkey.Value.IsDown()) return ChestDecorUI.IsOpen;
            ModLog.Verbose("DecorOpener: hotkey " + ModSettings.Hotkey.Value + " pressed");
            if (!PlayerCanAct(player)) { ModLog.Verbose("DecorOpener: the player can't act right now (menu, chat, inventory) - ignored"); return ChestDecorUI.IsOpen; }

            if (!ModSettings.AnyEditorPage) return Refuse("Chest Decor: colours and attachments are both switched off in the settings.", "[Colours] and [Attachments] Enable are both off");
            var container = GetLookedAtContainer(player);
            if (container == null) return Refuse("Look at a chest to customize it.", "nothing looked at is a container");
            var decorator = container.GetComponent<ChestDecorator>();
            if (decorator == null) return Refuse("This chest can't be customized.", "'" + container.name + "' has no ChestDecorator");
            if (!decorator.CanBeDecorated) return Refuse("Only chests built by players can be customized.", "'" + container.name + "' was not built by a player");
            if (!CanCustomize(container, player)) return Refuse("You don't have access to this chest.", "no access to '" + container.name + "'");

            // Only one player at a time may use a chest's Chest Decor window.
            string holder;
            var result = decorator.TryLock(player, out holder);
            ModLog.Verbose("DecorOpener: lock on '" + container.name + "' -> " + result + (holder != null ? " (holder " + holder + ")" : ""));
            if (result == ChestDecorator.LockResult.Busy) return Refuse(holder + " is already editing this chest.", "busy");
            if (result == ChestDecorator.LockResult.Unavailable) return Refuse("This chest can't be customized right now.", "lock unavailable");

            _pending = decorator;
            bool alone = AloneInWorld();
            _pendingAt = Time.unscaledTime + (alone ? 0f : LockConfirmSeconds);
            ModLog.Verbose("DecorOpener: lock taken, window opens in " + (alone ? 0f : LockConfirmSeconds) + " s" + (alone ? " (alone in the world)" : " (lock settles first)"));
            if (_pendingAt <= Time.unscaledTime) FinishOpen();
            return ChestDecorUI.IsOpen;
        }

        static bool Refuse(string toast, string why)
        {
            ModLog.Verbose("DecorOpener: not opened - " + why);
            ChestDecorUI.Toast(toast);
            return ChestDecorUI.IsOpen;
        }

        /// True in a world nobody else is connected to, where nobody can be racing for the lock.
        static bool AloneInWorld()
        {
            try { return ZNet.instance != null && ZNet.instance.IsServer() && ZNet.instance.GetPeerConnections() == 0; }
            catch (NullReferenceException e) { ModLog.Verbose("DecorOpener: peer count not readable (" + e.Message + ") - waiting for the lock"); return ZNet.instance == null; }
        }

        /// Opens the window once the lock is confirmed to still be ours after the data had time to sync.
        static void FinishOpen()
        {
            var decorator = _pending;
            _pending = null;
            if (decorator == null) { ModLog.Verbose("DecorOpener: the chest is gone before the window opened"); return; }
            if (Player.m_localPlayer == null) { decorator.Unlock(); ModLog.Verbose("DecorOpener: no local player any more - lock released"); return; }
            if (!decorator.HoldsLock())
            {
                var holder = decorator.LockHolderName();
                decorator.Unlock();
                Refuse((holder ?? "Another player") + " is already editing this chest.", "lost the lock race to " + (holder ?? "?"));
                return;
            }
            ChestDecorUI.Open(decorator);
            if (!ChestDecorUI.IsOpen) { decorator.Unlock(); ModLog.Warn("DecorOpener: the window could not be built - chest lock released"); }
        }

        /// <summary>
        /// The neutral textures and zone maps are per source texture and shared by every chest, but they belong to the world that was loaded
        /// when they were made. When that world goes away (main menu, another world) they are dropped, so memory does not grow per world.
        /// Expected outcome: returns whether the caches were cleared in this call.
        /// </summary>
        internal static bool WatchWorld()
        {
            var scene = ZNetScene.instance;
            var id = scene != null ? scene.GetInstanceID() : 0;   // by id: a destroyed scene compares equal to null
            if (id == _worldId) return false;
            var hadWorld = _worldId != 0;
            ModLog.Verbose("DecorOpener: world changed (scene id " + _worldId + " -> " + id + ")");
            _worldId = id;
            if (!hadWorld) return false;
            try { NeutralTextures.Clear(); ZoneMap.Clear(); ModLog.Verbose("DecorOpener: texture caches cleared after leaving a world"); return id == _worldId; }
            catch (Exception e) { ModLog.Warn("Clearing the texture caches after leaving a world failed: " + e.Message); return id != _worldId; }
        }

        /// Plugin.OnDestroy. Expected outcome: no pending lock and no open window afterwards.
        internal static bool Shutdown()
        {
            if (_pending != null) { _pending.Unlock(); _pending = null; }
            ChestDecorUI.Close();
            return _pending == null && !ChestDecorUI.IsOpen;
        }

        static bool PlayerCanAct(Player player)
        {
            if (TakeInputMethod != null)
            {
                try { return (bool)TakeInputMethod.Invoke(player, null); }
                catch (TargetInvocationException e) { ModLog.Verbose("DecorOpener: TakeInput check failed: " + e.InnerException?.Message); }
            }
            // fallback if the game's method can't be called
            return !(Chat.instance != null && Chat.instance.HasFocus());
        }

        internal static Container GetLookedAtContainer(Player player)
        {
            var hovered = player.GetHoverObject();
            return hovered != null ? hovered.GetComponentInParent<Container>() : null;
        }

        /// Same rules as opening the chest: privacy setting plus any ward on the spot.
        internal static bool CanCustomize(Container container, Player player)
        {
            if (!ModSettings.RequireAccess.Value) return container != null;
            if (CheckAccessMethod != null)
            {
                try
                {
                    if (!(bool)CheckAccessMethod.Invoke(container, new object[] { player.GetPlayerID() })) return container == null;
                }
                catch (TargetInvocationException e) { ModLog.Verbose("DecorOpener: container access check failed: " + e.InnerException?.Message); }
            }
            return PrivateArea.CheckAccess(container.transform.position, 0f, false, false);
        }
    }
}
