using System.Collections.Generic;
using ChestDecor.Ui;
using Kydl.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChestDecor
{
    /// <summary>
    /// The "Colours" tab, game side: the saved and working colours of every part, the selected part, Tint / Full colour mode and the
    /// Preview / Save / Revert actions. ColorsView (Unity project) draws it: part list, shared colour editor with brightness, mode buttons.
    /// </summary>
    internal sealed class ColorsPage : IColorsActions
    {
        const int MaxRows = 10;
        static ColorsPage _page;

        static ChestDecorator Decor { get { return ChestDecorUI.Decor; } }

        ColorsView _view;
        Dictionary<string, PartColor> _saved = new Dictionary<string, PartColor>();
        Dictionary<string, PartColor> _working = new Dictionary<string, PartColor>();
        ColorMode _pendingMode = ColorMode.Tint;     // mode given to a part that has no colour yet (the last mode the player clicked)
        int _selected, _count;

        ChestDecorator.Slot SelectedSlot { get { return Decor.Slots[_selected]; } }

        /// The mode the selected part is (or would be) using.
        ColorMode CurrentMode
        {
            get { PartColor part; return _count > 0 && _working.TryGetValue(SelectedSlot.Key, out part) ? part.Mode : _pendingMode; }
        }

        /// The window is opening for `decor`: start from what is saved on the chest and build the page into `page`.
        internal static void Open(ChestDecorator decor, VisualElement page)
        {
            var p = new ChestDecorPageState(decor);
            _page = new ColorsPage();
            _page._saved = p.Saved;
            _page._working = ChestColorData.Clone(p.Saved);
            _page._pendingMode = p.FirstMode ?? ModSettings.DefaultMode.Value;
            _page._count = Mathf.Min(decor.Slots.Count, MaxRows);
            if (decor.Slots.Count > MaxRows) ModLog.Warn("Chest has " + decor.Slots.Count + " colourable parts, only the first " + MaxRows + " are shown.");
            var start = _page._count > 0 ? _page.ColorOf(0) : Color.white;
            _page._view = new ColorsView(page, ChestDecorUI.T, _page, "#" + ColorUtility.ToHtmlStringRGB(start));
            _page.Select(0);
            ModLog.Verbose("ColorsPage: opened (" + _page._count + " part(s), " + _page._saved.Count + " saved colour(s), new parts get " + _page._pendingMode + ")");
        }

        /// The window is closing.
        internal static void Close() { _page = null; }

        /// The tab was chosen again: show the current state.
        internal static void Refresh() { if (_page != null) _page.ShowAll(); }

        Color ColorOf(int index)
        {
            PartColor custom;
            var slot = Decor.Slots[index];
            return _working.TryGetValue(slot.Key, out custom) ? custom.Rgb : slot.OriginalColor;
        }

        void ShowAll()
        {
            var rows = new List<PartRowData>();
            for (int i = 0; i < _count; i++)
            {
                var slot = Decor.Slots[i];
                rows.Add(new PartRowData { Name = slot.DisplayName, Color = ColorOf(i), Custom = _working.ContainsKey(slot.Key) });
            }
            _view.ShowParts(rows, _selected);
            _view.ShowMode(CurrentMode == ColorMode.Full);
            _view.ShowStatus(!ChestColorData.SameColors(_working, _saved));
        }

        void Select(int index)
        {
            if (Decor == null) return;
            _selected = _count > 0 ? Mathf.Clamp(index, 0, _count - 1) : 0;
            if (_count > 0) _view.ShowColor(ColorOf(_selected));
            ShowAll();
        }

        void LiveApply(string why)
        {
            if (!ModSettings.LivePreview.Value) return;
            Decor.Apply(_working);
            ModLog.Verbose("ColorsPage: live preview applied (" + why + ")");
        }

        // --- IColorsActions (clicks from the view) ------------------------------------------------------------------------------------
        public void SelectPart(int index)
        {
            ModLog.Verbose("ColorsPage: part " + index + " selected");
            Select(index);
        }

        public void ResetPart(int index)
        {
            if (Decor == null || index < 0 || index >= _count) return;
            bool removed = _working.Remove(Decor.Slots[index].Key);
            ModLog.Verbose("ColorsPage: part " + index + " reset (" + (removed ? "custom colour dropped" : "had no custom colour") + ")");
            LiveApply("reset");
            if (index == _selected) Select(index); else ShowAll();
        }

        public void ColorPicked(Color c)
        {
            if (Decor == null || _count == 0) return;
            c.a = 1f;
            _working[SelectedSlot.Key] = new PartColor(c, CurrentMode);
            ModLog.Verbose("ColorsPage: part " + _selected + " colour #" + ColorUtility.ToHtmlStringRGB(c) + " (" + CurrentMode + ")");
            ShowAll();
            LiveApply("colour");
        }

        /// Switch the selected part between Tint and Full colour (keeps its colour).
        public void ModeClicked(bool full)
        {
            if (Decor == null || _count == 0) return;
            var mode = full ? ColorMode.Full : ColorMode.Tint;
            _pendingMode = mode;
            PartColor part;
            var key = SelectedSlot.Key;
            if (_working.TryGetValue(key, out part) && part.Mode != mode) { part.Mode = mode; _working[key] = part; LiveApply("mode"); }
            ModLog.Verbose("ColorsPage: mode " + mode + " for part " + _selected);
            ShowAll();
        }

        /// Give every part that already has a colour the currently shown mode.
        public void ModeToAll()
        {
            if (Decor == null || _count == 0) return;
            var mode = CurrentMode;
            _pendingMode = mode;
            foreach (var key in new List<string>(_working.Keys)) { var part = _working[key]; part.Mode = mode; _working[key] = part; }
            ModLog.Verbose("ColorsPage: mode " + mode + " given to " + _working.Count + " coloured part(s)");
            LiveApply("mode to all");
            ShowAll();
        }

        /// Show the working colours on the chest without saving them.
        public void Preview()
        {
            if (Decor == null) return;
            Decor.Apply(_working);
            ModLog.Verbose("ColorsPage: preview of " + _working.Count + " colour(s)");
        }

        /// Persist the working colours to the chest (synced to other players).
        public void Save()
        {
            if (Decor == null || !ChestDecorUI.CanWriteToChest()) return;
            if (Decor.Save(_working))
            {
                _saved = ChestColorData.Clone(_working);
                ModLog.Verbose("ColorsPage: " + _saved.Count + " colour(s) saved");
                ShowAll();
                ChestDecorUI.Toast("Chest colours saved");
            }
            else
            {
                ModLog.Warn("ColorsPage: saving the colours to the chest failed");
                ChestDecorUI.Toast("Could not save the chest colours.");
            }
        }

        /// Throw away all unsaved edits and go back to the last saved look.
        public void Revert()
        {
            if (Decor == null) return;
            _working = ChestColorData.Clone(_saved);
            Decor.Apply(_saved);
            ModLog.Verbose("ColorsPage: reverted to " + _saved.Count + " saved colour(s)");
            Select(_selected);
        }

        /// What is saved on the chest when the window opens.
        sealed class ChestDecorPageState
        {
            internal readonly Dictionary<string, PartColor> Saved;
            internal readonly ColorMode? FirstMode;

            internal ChestDecorPageState(ChestDecorator decor)
            {
                Saved = decor.LoadSaved();
                string first = null;
                foreach (var key in Saved.Keys) if (first == null || string.CompareOrdinal(key, first) < 0) first = key;
                FirstMode = first != null ? Saved[first].Mode : (ColorMode?)null;
            }
        }
    }
}
