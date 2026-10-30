using System;
using System.Collections.Generic;
using ChestDecor.Ui;
using Kydl.Common;
using Kydl.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChestDecor
{
    /// <summary>
    /// The decorate window, GAME side: opens and closes it, owns the state the pages share (the chest, the saved and shown attachments, the
    /// tab), keeps the chest's editor lock alive and is the only class the rest of the plugin talks to. What the window shows is built by
    /// DecorWindowView / ColorsView / AttachView / PositionView (Unity project, ChestDecor-Unity/Assets/ChestDecor_UI/Runtime, previewed there).
    /// The pages' game logic lives in ColorsPage, AttachPage and PositionPage.
    /// </summary>
    internal static class ChestDecorUI
    {
        const string WindowId = "editor";          // KyUi window id (Core builds guid + ":" + id); the input lock and Escape belong to Core

        static DecorWindowView _view;
        static KydlCore.KyWindow _win;             // the window as Core knows it (null while closed)
        static ChestDecorator _decor;
        static int _closedFrame = -100;
        static float _nextBeat, _nextDistanceLog;

        /// The chest the window is open for (null while closed). For the pages.
        internal static ChestDecorator Decor { get { return _decor; } }
        internal static ChestDecorTab Tab { get { return _view != null ? (ChestDecorTab)_view.Tab : ChestDecorTab.Colors; } }

        // attachments of the open chest: what is saved, and what is shown (saved + unsaved position edits)
        internal static List<ChestAttachment> SavedAtt = new List<ChestAttachment>();
        internal static List<ChestAttachment> WorkAtt = new List<ChestAttachment>();

        public static bool IsOpen { get; private set; }

        /// True while the window is open and for the frame after it closes, so Esc doesn't also open the game menu.
        public static bool BlocksMenu { get { return IsOpen || Time.frameCount <= _closedFrame + 1; } }

        /// Localized text of a key (the pages and views use it).
        internal static string T(string key) { return Loc.T(key); }

        // --- pointer / keyboard (the free camera asks) -------------------------------------------------------------------------------
        static Vector2 PanelPoint(IPanel panel)
        {
            var m = Input.mousePosition;
            return RuntimePanelUtils.ScreenToPanel(panel, new Vector2(m.x, Screen.height - m.y));   // screen y is up, panel y is down
        }

        /// True while the mouse is over the decorate window (or one of its open drop-down lists): clicks and the wheel belong to the window.
        internal static bool PointerOverWindow()
        {
            if (!IsOpen || _view == null || _view.Window.panel == null) return false;
            var p = PanelPoint(_view.Window.panel);
            return _view.Window.worldBound.Contains(p);
        }

        static VisualElement Focused()
        {
            var panel = _view != null ? _view.Window.panel : null;
            return panel != null && panel.focusController != null ? panel.focusController.focusedElement as VisualElement : null;
        }

        /// True while a text box (hex code, search, a slider's number box) has the keyboard, so letters must not move the camera.
        internal static bool IsTyping
        {
            get
            {
                if (!IsOpen) return false;
                for (var e = Focused(); e != null; e = e.parent) if (e is TextField) return true;
                return false;
            }
        }

        /// Takes the keyboard away from a text box, e.g. when the player clicks out in the world. Expected outcome: nothing typed into any more.
        internal static bool ReleaseTextFocus()
        {
            var f = Focused();
            if (f != null) { f.Blur(); ModLog.Verbose("ChestDecorUI: text box lost the keyboard (click outside the window)"); }
            return !IsTyping;
        }

        // --- open / close ------------------------------------------------------------------------------------------------------------
        public static void Open(ChestDecorator decor)
        {
            if (IsOpen) Close();
            if (KydlCore.KyUi.Root == null) { Toast("Chest Decor: UI is not available yet."); ModLog.Warn("ChestDecorUI: the Core UI root is missing - window not opened"); return; }

            // the caller took the chest's editor lock; make sure we still are the one editor
            if (!decor.HoldsLock())
            {
                var holder = decor.LockHolderName();
                Toast((holder ?? "Another player") + " is already editing this chest.");
                ModLog.Verbose("ChestDecorUI: lock lost before opening (holder " + (holder ?? "?") + ")");
                return;
            }

            // Anything that throws from here on must not leave input blocked or the chest locked for other players.
            try
            {
                _decor = decor;
                _nextBeat = Time.unscaledTime + ChestDecorator.LockHeartbeatSeconds;
                SavedAtt = decor.LoadSavedAttachments();
                WorkAtt = ChestAttachment.CloneList(SavedAtt);
                decor.BeginEdit();
                IsOpen = true;

                _view = new DecorWindowView(T, ChestName(), Close, OnTab);
                ColorsPage.Open(decor, _view.Pages[0]);
                AttachPage.Open(_view.Pages[1]);
                PositionPage.Open(_view.Pages[2]);
                // Core puts the window on its UI root, holds the one input lock and closes it on Escape (then calls OnWindowClosed)
                _win = KydlCore.KyUi.Open(Plugin.ModGuid, WindowId, _view.Window, OnWindowClosed);
                if (_win == null) throw new InvalidOperationException("KyUi.Open refused the window");
                FreeCamera.Begin();
                ModLog.Verbose("ChestDecorUI: window opened for '" + decor.name + "' (" + decor.Slots.Count + " colour part(s), " + SavedAtt.Count + " attachment(s))");
            }
            catch (Exception e)
            {
                ModLog.Warn("Could not open the Chest Decor window: " + e);
                Teardown(decor);
                Toast("Chest Decor: the window could not be opened.");
            }
        }

        public static void Close()
        {
            if (!IsOpen) return;
            Teardown(null);
        }

        /// Core closed the window (Escape): do the same clean-up as Close(). Not called again for a window that Teardown closes itself.
        static void OnWindowClosed()
        {
            if (_win == null) return;
            ModLog.Verbose("ChestDecorUI: Core closed the window (Escape)");
            Teardown(null);
        }

        /// <summary>
        /// Puts everything back the way it was before the window opened. Every step has its own try / catch, so a failing step can never skip
        /// the ones after it: the chest is always given back and the game's input always released. Also used when opening fails half way,
        /// where `fallback` is the chest in case the window state does not hold it yet.
        /// </summary>
        static void Teardown(ChestDecorator fallback)
        {
            var decor = _decor ?? fallback;
            IsOpen = false;
            _closedFrame = Time.frameCount;
            try { FreeCamera.End(); } catch (Exception e) { ModLog.Warn("Releasing the camera failed: " + e.Message); }
            try { if (decor != null) decor.EndEdit(); }          // drops any unsaved preview and goes back to what is saved on the chest
            catch (Exception e) { ModLog.Warn("Restoring chest visuals failed: " + e.Message); }
            try { if (decor != null) decor.Unlock(); }           // give the chest back to the other players
            catch (Exception e) { ModLog.Warn("Releasing the chest failed: " + e.Message); }
            try
            {
                var win = _win;
                _win = null;                                 // first: Core calls OnWindowClosed back, which must find nothing to do
                if (win != null) win.Close();                // off Core's UI root, input lock released with the last window
                else if (_view != null) _view.Window.RemoveFromHierarchy();
            }
            catch (Exception e) { ModLog.Warn("Removing the window failed: " + e.Message); }
            _view = null;
            _decor = null;
            try { ColorsPage.Close(); AttachPage.Close(); PositionPage.Close(); }
            catch (Exception e) { ModLog.Warn("Clearing the pages failed: " + e.Message); }
            SavedAtt = new List<ChestAttachment>();
            WorkAtt = new List<ChestAttachment>();
            ModLog.Verbose("ChestDecorUI: window closed, chest " + (decor != null ? "'" + decor.name + "' released" : "(none)") + ", Core windows still open: " + KydlCore.KyUi.AnyOpen);
        }

        /// Every frame while the window is open (DecorOpener.Tick).
        public static void Tick()
        {
            if (!IsOpen) return;
            if (_view == null || _decor == null || Player.m_localPlayer == null) { ModLog.Verbose("ChestDecorUI: chest or player gone - closing"); Close(); return; }

            var dist = Vector3.Distance(Player.m_localPlayer.transform.position, _decor.transform.position);
            float max = ModSettings.MaxWindowDistance.Value;
            if (Time.unscaledTime >= _nextDistanceLog) { _nextDistanceLog = Time.unscaledTime + 5f; ModLog.Verbose("ChestDecorUI: open, player " + dist.ToString("0.0") + " m from the chest (closes beyond " + max + " m), free camera " + (FreeCamera.IsArmed ? "armed" : "off")); }
            if (dist > max) { ModLog.Verbose("ChestDecorUI: player walked away (" + dist.ToString("0.0") + " m > " + max + " m) - closing"); Close(); return; }

            // keep the editor lock alive; if somebody else took the chest over, this window must go
            if (Time.unscaledTime >= _nextBeat)
            {
                _nextBeat = Time.unscaledTime + ChestDecorator.LockHeartbeatSeconds;
                if (!_decor.Heartbeat())
                {
                    Toast("Another player has taken over this chest. Unsaved changes were dropped.");
                    ModLog.Info("ChestDecorUI: editor lock taken over by another player - window closed, unsaved changes dropped");
                    Close();
                    return;
                }
            }
            AttachPage.Tick();
        }

        static void OnTab(int tab)
        {
            ModLog.Verbose("ChestDecorUI: tab " + (ChestDecorTab)tab);
            if (tab == (int)ChestDecorTab.Attach) AttachPage.RefreshList();
            else if (tab == (int)ChestDecorTab.Position) PositionPage.Refresh();
            else ColorsPage.Refresh();
        }

        /// <summary>
        /// Call before anything that writes to the chest (attach, remove, save). Confirms this player is still the chest's one editor; if not,
        /// the window closes and nothing is written. Expected outcome: true only while this player holds the lock.
        /// </summary>
        internal static bool CanWriteToChest()
        {
            if (_decor != null && (_decor.HoldsLock() || (_decor.Heartbeat() && _decor.HoldsLock()))) return _decor.HoldsLock();
            var holder = _decor != null ? _decor.LockHolderName() : null;
            Toast(holder != null ? holder + " has taken over this chest." : "You are no longer editing this chest.");
            ModLog.Info("ChestDecorUI: write refused - lock held by " + (holder ?? "nobody"));
            Close();
            return IsOpen;
        }

        /// <summary>
        /// The attachments saved on the chest must still be exactly what this window loaded. If not (the chest was changed behind this window's
        /// back) the window reloads them and the action is cancelled, so an item that is already gone can never be removed, refunded or saved
        /// back twice. Expected outcome: true when nothing changed.
        /// </summary>
        static bool AttachmentsUnchanged()
        {
            // Both sides go through the same save-format round trip, so rounding or an older save format can never make an unchanged chest look changed.
            var fresh = _decor.LoadSavedAttachments();
            var mine = ChestAttachment.Parse(ChestAttachment.Serialize(SavedAtt), ChestDecorator.HardMaxAttachments);
            bool same = ChestAttachment.Same(fresh, mine);
            if (same) return same;
            ModLog.Warn("ChestDecorUI: attachments on the chest changed behind the window (" + mine.Count + " -> " + fresh.Count + ") - reloaded, action cancelled");
            SavedAtt = fresh;
            WorkAtt = ChestAttachment.CloneList(fresh);
            _decor.ShowAttachments(WorkAtt);
            AttachPage.RefreshRows();
            PositionPage.Refresh();
            Toast("The chest was changed by someone else. The list was reloaded; nothing was done.");
            return same;
        }

        /// Both checks, for an action that changes the attachments.
        internal static bool CanChangeAttachments() { return CanWriteToChest() && AttachmentsUnchanged(); }

        static string ChestName()
        {
            var raw = _decor != null && _decor.Container != null ? _decor.Container.m_name : "";
            if (string.IsNullOrEmpty(raw)) return "Chest";
            return Localization.instance != null ? Localization.instance.Localize(raw) : raw;
        }

        internal static void Toast(string message)
        {
            ModLog.Verbose("Toast: " + message);
            if (MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, message);
        }

        /// The catalog was invalidated (a setting that decides what is listed changed): show the new list.
        internal static void CatalogChanged()
        {
            if (!IsOpen) return;
            AttachPage.RefreshList();
        }

        /// A player setting that the open window shows changed (max attachments, costs): refresh what depends on it (hot reload, rule 17).
        internal static void SettingsChanged(string what)
        {
            if (!IsOpen) return;
            ModLog.Verbose("ChestDecorUI: '" + what + "' changed - open window refreshed");
            AttachPage.RefreshList();
            PositionPage.Refresh();
        }
    }
}
