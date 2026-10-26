using System;
using System.Collections.Generic;
using Kydl.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChestDecor.Ui
{
    /// <summary>One colourable part as the Colours page shows it.</summary>
    internal struct PartRowData
    {
        internal string Name;
        internal Color Color;     // the custom colour, or the part's own colour when it has none
        internal bool Custom;     // has a custom colour (Reset is enabled)
    }

    /// <summary>The game side of the Colours page (ColorsPage in the mod; a stub in the Unity preview).</summary>
    internal interface IColorsActions
    {
        void SelectPart(int index);
        void ResetPart(int index);
        void ColorPicked(Color c);
        void ModeClicked(bool full);
        void ModeToAll();
        void Preview();
        void Save();
        void Revert();
    }

    /// <summary>
    /// The "Colours" page: the part list (swatch + Reset), the shared colour editor with brightness (user: "shared + brightness adjustments"),
    /// Tint / Full colour mode, Preview / Save / Revert and the status line. No game types: data in through Show*, clicks out through IColorsActions.
    /// </summary>
    internal sealed class ColorsView
    {
        readonly Func<string, string> _text;
        readonly IColorsActions _act;
        readonly VisualElement _parts, _mode;
        readonly UiColorPicker _picker;
        readonly Label _status;
        bool _setting;

        internal ColorsView(VisualElement page, Func<string, string> text, IColorsActions act, string startHex)
        {
            _text = text; _act = act;
            page.Add(UiControls.Heading(text("chestdecor_parts")));
            _parts = new VisualElement(); _parts.style.flexShrink = 0;
            page.Add(_parts);

            page.Add(UiControls.Heading(text("chestdecor_colour")));
            _picker = new UiColorPicker(startHex, OnPicked, true);
            page.Add(_picker.Root);

            page.Add(UiControls.Heading(text("chestdecor_mode")));
            _mode = new VisualElement(); _mode.style.flexShrink = 0;
            page.Add(_mode);

            var preview = UiControls.Button(text("chestdecor_preview"), () => _act.Preview());
            var save = UiControls.Button(text("chestdecor_save"), () => _act.Save(), true);
            var revert = UiControls.Button(text("chestdecor_revert"), () => _act.Revert());
            page.Add(DecorWindowView.Actions(preview, save, revert));
            _status = DecorWindowView.Status("");
            page.Add(_status);
            page.Add(DecorWindowView.Notes(text, new[] { "chestdecor_note_tint", "chestdecor_note_full" }));
            UiLog.Verbose("ColorsView: built");
        }

        void OnPicked(string hex)
        {
            if (_setting) return;
            Color c;
            if (ColorUtility.TryParseHtmlString(hex, out c)) _act.ColorPicked(c);
        }

        /// The part list. Expected outcome: one row per part (or the "no parts" note).
        internal int ShowParts(IList<PartRowData> rows, int selected)
        {
            _parts.Clear();
            if (rows.Count == 0) { _parts.Add(UiControls.Note(_text("chestdecor_no_parts"))); return _parts.childCount - 1; }
            for (int i = 0; i < rows.Count; i++)
            {
                int idx = i;
                var line = new VisualElement(); line.style.flexDirection = FlexDirection.Row; line.style.flexShrink = 0;
                line.Add(DecorWindowView.RowButton(rows[i].Name, i == selected, rows[i].Color, () => _act.SelectPart(idx)));
                var reset = UiControls.Button(_text("chestdecor_reset"), () => _act.ResetPart(idx));
                reset.style.width = 100;
                DecorWindowView.SetEnabled(reset, rows[i].Custom);
                line.Add(reset);
                _parts.Add(line);
            }
            return _parts.childCount;
        }

        /// The colour in the editor (a part was selected or reset). Does not call ColorPicked.
        internal bool ShowColor(Color c)
        {
            _setting = true;
            try { return _picker.Set(c); }
            finally { _setting = false; }
        }

        /// Tint / Full colour of the selected part.
        internal void ShowMode(bool full)
        {
            _mode.Clear();
            var tint = UiControls.Button(_text("chestdecor_mode_tint"), () => _act.ModeClicked(false), !full);
            var fullB = UiControls.Button(_text("chestdecor_mode_full"), () => _act.ModeClicked(true), full);
            var all = UiControls.Button(_text("chestdecor_mode_all"), () => _act.ModeToAll());
            tint.style.flexGrow = 1; fullB.style.flexGrow = 1;
            var r = UiControls.Line(tint, fullB); r.style.flexWrap = Wrap.NoWrap;
            _mode.Add(r);
            _mode.Add(UiControls.Line(all));
        }

        internal void ShowStatus(bool dirty) { DecorWindowView.SetStatus(_status, dirty, _text); }
    }
}
