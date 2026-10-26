using System.Collections.Generic;
using ChestDecor.Ui;
using Kydl.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChestDecor
{
    /// <summary>
    /// The "Position items" tab, game side: the attached items, the seven sliders (their ranges follow the placement limits) and the
    /// Save / Revert / Reset item actions. PositionView (Unity project) draws it; the dots under the sliders are UiSlider marks.
    /// </summary>
    internal sealed class PositionPage : IPositionActions
    {
        static PositionPage _page;
        static ChestDecorator Decor { get { return ChestDecorUI.Decor; } }

        internal static int SelectedIndex;
        internal static bool HasSelection { get { return SelectedIndex >= 0 && SelectedIndex < ChestDecorUI.WorkAtt.Count; } }

        PositionView _view;

        /// The window is opening: the first item is selected.
        internal static void Open(VisualElement page)
        {
            SelectedIndex = 0;
            _page = new PositionPage();
            _page._view = new PositionView(page, ChestDecorUI.T, _page);
            Refresh();
            ModLog.Verbose("PositionPage: opened (" + ChestDecorUI.WorkAtt.Count + " attached item(s))");
        }

        internal static void Close() { _page = null; }

        /// Items, sliders and status from the current state. Expected outcome: returns whether an item is selected.
        internal static bool Refresh()
        {
            if (_page == null || Decor == null) return HasSelection && _page != null;
            SelectedIndex = ChestDecorUI.WorkAtt.Count == 0 ? 0 : Mathf.Clamp(SelectedIndex, 0, ChestDecorUI.WorkAtt.Count - 1);
            _page.ShowItems();
            _page.RefreshSliders();
            _page.ShowStatus();
            return HasSelection;
        }

        void ShowItems()
        {
            var names = new List<string>();
            foreach (var a in ChestDecorUI.WorkAtt) names.Add(NameOf(a.Prefab));
            _view.ShowItems(names, SelectedIndex);
        }

        void ShowStatus() { _view.ShowStatus(!ChestAttachment.Same(ChestDecorUI.WorkAtt, ChestDecorUI.SavedAtt)); }

        static string NameOf(string prefab)
        {
            var entry = AttachCatalog.Find(prefab);
            return entry != null && !string.IsNullOrEmpty(entry.DisplayName) ? entry.DisplayName : prefab;
        }

        /// Puts the selected item's values on the sliders (and sets each slider's range).
        void RefreshSliders()
        {
            bool has = HasSelection;
            _view.SetEnabled(has);
            var b = Decor.LocalBounds;
            if (has) KeepInsideLimits(ChestDecorUI.WorkAtt[SelectedIndex], b);
            for (int k = 0; k < PositionView.Axes; k++)
            {
                float min, max;
                if (k < 3) { min = PlacementLimits.Low(b, k); max = PlacementLimits.High(b, k); }     // the item's middle: 10 cm outside each side
                else if (k < 6) { min = 0f; max = 360f; }
                else { min = ChestAttachment.MinScale; max = ChestAttachment.MaxScale; }
                var value = has ? ValueOf(ChestDecorUI.WorkAtt[SelectedIndex], k) : (min + max) * 0.5f;
                _view.SetSlider(k, min, max, value);
            }
            if (has) ModLog.Verbose("PositionPage: sliders for item " + SelectedIndex + " ('" + ChestDecorUI.WorkAtt[SelectedIndex].Prefab + "'), chest bounds " +
                                    b.size.ToString("F2") + " m, middle ranges x " + Range(b, 0) + ", y " + Range(b, 1) + ", z " + Range(b, 2) +
                                    " (the same at every rotation and size), position " + ChestDecorUI.WorkAtt[SelectedIndex].Position.ToString("F3"));
        }

        static string Range(Bounds b, int k) { return PlacementLimits.Low(b, k).ToString("0.00") + ".." + PlacementLimits.High(b, k).ToString("0.00"); }

        /// <summary>
        /// An item saved before the limits were measured from its middle (2026-10-26) can be further out than now allowed: it is moved
        /// inside when it is selected (only the unsaved working copy; Save keeps it). Expected outcome: returns whether it is allowed now.
        /// </summary>
        static bool KeepInsideLimits(ChestAttachment a, Bounds chest)
        {
            if (PlacementLimits.Allowed(a.Position, chest)) return true;
            var was = a.Position;
            a.Position = PlacementLimits.Clamp(a.Position, chest);
            ModLog.Verbose("PositionPage: '" + a.Prefab + "' moved inside the placement limits, " + was.ToString("F3") + " -> " + a.Position.ToString("F3"));
            Decor.ShowAttachments(ChestDecorUI.WorkAtt);
            return PlacementLimits.Allowed(a.Position, chest);
        }

        static float ValueOf(ChestAttachment a, int axis)
        {
            switch (axis)
            {
                case 0: return a.Position.x;
                case 1: return a.Position.y;
                case 2: return a.Position.z;
                case 3: return Wrap360(a.Euler.x);
                case 4: return Wrap360(a.Euler.y);
                case 5: return Wrap360(a.Euler.z);
                default: return a.Scale;
            }
        }

        static float Wrap360(float degrees) { var d = degrees % 360f; return d < 0f ? d + 360f : d; }

        // --- IPositionActions ------------------------------------------------------------------------------------------------------
        public void Select(int index)
        {
            if (index < 0 || index >= ChestDecorUI.WorkAtt.Count) return;
            SelectedIndex = index;
            ModLog.Verbose("PositionPage: item " + index + " selected");
            ShowItems();
            RefreshSliders();
        }

        public void Remove(int index) { AttachmentRemoval.Remove(index); }

        public void Slider(int axis, float value)
        {
            if (Decor == null || !HasSelection) return;
            var a = ChestDecorUI.WorkAtt[SelectedIndex];
            if (axis < 3)
            {
                // keep the item's middle within 10 cm of the chest surface, outside or inside (see PlacementLimits)
                var moved = a.Position;
                moved[axis] = value;
                a.Position = PlacementLimits.Clamp(moved, Decor.LocalBounds, axis, a.Position);
                for (int k = 0; k < 3; k++) if (k != axis || !Mathf.Approximately(a.Position[k], value)) _view.SetValue(k, a.Position[k]);
                if (!Mathf.Approximately(a.Position[axis], value)) ModLog.Verbose("PositionPage: axis " + axis + " stopped at " + a.Position[axis].ToString("0.000") + " m by the placement limit (asked " + value.ToString("0.000") + ")");
            }
            else
            {
                switch (axis)
                {
                    case 3: a.Euler = new Vector3(value, a.Euler.y, a.Euler.z); break;
                    case 4: a.Euler = new Vector3(a.Euler.x, value, a.Euler.z); break;
                    case 5: a.Euler = new Vector3(a.Euler.x, a.Euler.y, value); break;
                    default: a.Scale = Mathf.Clamp(value, ChestAttachment.MinScale, ChestAttachment.MaxScale); break;
                }
                // the item turns / grows around its middle and the limits are measured from the middle: position and ranges stay as they are
                ModLog.Verbose("PositionPage: item " + SelectedIndex + " " + (axis == 6 ? "size " + a.Scale.ToString("0.00") : "rotation " + a.Euler.ToString("F0")) +
                               ", position unchanged " + a.Position.ToString("F3"));
            }
            Decor.ShowAttachments(ChestDecorUI.WorkAtt);
            ShowStatus();
        }

        public void Save()
        {
            if (Decor == null || !ChestDecorUI.CanChangeAttachments()) return;
            if (Decor.SaveAttachments(ChestDecorUI.WorkAtt))
            {
                ChestDecorUI.SavedAtt = ChestAttachment.CloneList(ChestDecorUI.WorkAtt);
                ModLog.Verbose("PositionPage: positions of " + ChestDecorUI.SavedAtt.Count + " item(s) saved");
                ShowStatus();
                ChestDecorUI.Toast("Positions saved");
            }
            else { ModLog.Warn("PositionPage: saving the positions failed"); ChestDecorUI.Toast("Could not save the positions."); }
        }

        public void Revert()
        {
            if (Decor == null) return;
            ChestDecorUI.WorkAtt = ChestAttachment.CloneList(ChestDecorUI.SavedAtt);
            Decor.ShowAttachments(ChestDecorUI.WorkAtt);
            ModLog.Verbose("PositionPage: positions reverted to the saved ones");
            Refresh();
        }

        /// Puts the selected item back to its starting spot, rotation and size.
        public void ResetItem()
        {
            if (Decor == null || !HasSelection) return;
            // resetting the position must not turn a paid item into a free one (or back), nor lose what was paid
            var previous = ChestDecorUI.WorkAtt[SelectedIndex];
            var reset = Decor.CreateDefault(previous.Prefab, SelectedIndex);
            reset.CopyPaymentFrom(previous);
            ChestDecorUI.WorkAtt[SelectedIndex] = reset;
            Decor.ShowAttachments(ChestDecorUI.WorkAtt);
            ModLog.Verbose("PositionPage: item " + SelectedIndex + " reset to its default place");
            RefreshSliders();
            ShowStatus();
        }
    }
}
