using System;
using System.Collections.Generic;
using Kydl.Ui;
using UnityEngine.UIElements;

namespace ChestDecor.Ui
{
    /// <summary>The game side of the Position page (PositionPage in the mod; a stub in the Unity preview).</summary>
    internal interface IPositionActions
    {
        void Select(int index);
        void Remove(int index);
        void Slider(int axis, float value);
        void Save();
        void Revert();
        void ResetItem();
    }

    /// <summary>
    /// The "Position items" page: the attached items (select / Remove), seven sliders (left / right, up / down, front / back in metres, tilt,
    /// turn, roll in degrees, size) with clickable dots under them, Save / Revert / Reset item and the status line. The sliders are UiSlider
    /// objects: the game side moves their ranges and values when the selection or the limits change, without rebuilding them.
    /// </summary>
    internal sealed class PositionView
    {
        internal const int Axes = 7;
        static readonly string[] AxisKeys =
            { "chestdecor_axis_x", "chestdecor_axis_y", "chestdecor_axis_z", "chestdecor_axis_tilt", "chestdecor_axis_turn", "chestdecor_axis_roll", "chestdecor_axis_size" };
        static readonly string[] Formats = { "0.00", "0.00", "0.00", "0", "0", "0", "0.00" };
        // a dot every 12.5 % of the slider (nine dots) on EVERY axis: the three directions (-100 % ... 0 ... 100 %) and tilt, turn, roll and size
        static readonly float[] Marks = { 0f, 0.125f, 0.25f, 0.375f, 0.5f, 0.625f, 0.75f, 0.875f, 1f };

        readonly Func<string, string> _text;
        readonly IPositionActions _act;
        readonly VisualElement _items;
        readonly UiSlider[] _sliders = new UiSlider[Axes];
        readonly Label _status;

        internal PositionView(VisualElement page, Func<string, string> text, IPositionActions act)
        {
            _text = text; _act = act;
            page.Add(UiControls.Heading(text("chestdecor_attached_items")));
            _items = new VisualElement(); _items.style.flexShrink = 0;
            page.Add(_items);

            page.Add(UiControls.Heading(text("chestdecor_tab_position")));
            for (int k = 0; k < Axes; k++)
            {
                int axis = k;
                _sliders[k] = new UiSlider(0f, 1f, 0.5f, Formats[k], v => _act.Slider(axis, v), Marks);
                var row = UiControls.Row(text(AxisKeys[k]), _sliders[k].Root);
                row.Q<Label>().style.width = 190;
                page.Add(row);
            }
            page.Add(UiControls.Note(text("chestdecor_note_dots")));

            var save = UiControls.Button(text("chestdecor_save"), () => _act.Save(), true);
            var revert = UiControls.Button(text("chestdecor_revert"), () => _act.Revert());
            var reset = UiControls.Button(text("chestdecor_reset_item"), () => _act.ResetItem());
            page.Add(DecorWindowView.Actions(save, revert, reset));
            _status = DecorWindowView.Status("");
            page.Add(_status);
            page.Add(DecorWindowView.Notes(text, new[] { "chestdecor_note_pos1", "chestdecor_note_pos2" }));
            UiLog.Verbose("PositionView: built");
        }

        /// The attached items. Expected outcome: one row per item (or the "nothing attached" note).
        internal int ShowItems(IList<string> names, int selected)
        {
            _items.Clear();
            if (names.Count == 0) { _items.Add(UiControls.Note(_text("chestdecor_nothing_attached"))); return 0; }
            for (int i = 0; i < names.Count; i++)
            {
                int idx = i;
                var line = new VisualElement(); line.style.flexDirection = FlexDirection.Row; line.style.flexShrink = 0;
                line.Add(DecorWindowView.RowButton(names[i], i == selected, null, () => _act.Select(idx)));
                var remove = UiControls.Button(_text("chestdecor_remove"), () => _act.Remove(idx));
                remove.style.width = 110;
                line.Add(remove);
                _items.Add(line);
            }
            return _items.childCount;
        }

        /// Range and value of one slider (no callback). Expected outcome: the slider shows the clamped value.
        internal bool SetSlider(int axis, float min, float max, float value)
        {
            var s = _sliders[axis];
            s.SetRange(min, max);
            return s.SetValueWithoutNotify(value);
        }

        internal bool SetValue(int axis, float value) { return _sliders[axis].SetValueWithoutNotify(value); }
        internal float Min(int axis) { return _sliders[axis].Min; }
        internal float Max(int axis) { return _sliders[axis].Max; }

        internal void SetEnabled(bool on) { foreach (var s in _sliders) s.Enabled = on; }

        internal void ShowStatus(bool dirty) { DecorWindowView.SetStatus(_status, dirty, _text); }
    }
}
