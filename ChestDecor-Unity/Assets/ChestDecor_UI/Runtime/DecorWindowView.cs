using System;
using System.Collections.Generic;
using Kydl.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChestDecor.Ui
{
    /// <summary>
    /// The decorate window (shared Kydl.Ui look): title, the chest's name, a tab bar (Colours / Attach items / Position items) under the title,
    /// and one scrolled body that holds the three pages (only the chosen one is shown). Compiled into the mod AND shown by the Unity preview:
    /// no game types here; the game side (ChestDecorUI) passes text and callbacks in.
    /// </summary>
    internal sealed class DecorWindowView
    {
        internal const int TabCount = 3;
        internal static readonly Color Good = new Color(0.62f, 0.92f, 0.50f, 1f), Bad = new Color(1f, 0.50f, 0.45f, 1f);

        internal readonly VisualElement Window;
        internal readonly UiScroll Body;
        internal readonly VisualElement[] Pages = new VisualElement[TabCount];
        readonly VisualElement _tabs;
        readonly Func<string, string> _text;
        readonly Action<int> _onTab;
        static readonly string[] TabKeys = { "chestdecor_tab_colors", "chestdecor_tab_attach", "chestdecor_tab_position" };
        int _tab;

        internal int Tab { get { return _tab; } }

        internal DecorWindowView(Func<string, string> text, string chestName, Action onClose, Action<int> onTab)
        {
            _text = text; _onTab = onTab;
            Window = UiKit.Window(text("chestdecor_title"), text("chestdecor_footer"), onClose, out Body);
            Window.name = "chestdecor-window";

            var top = new VisualElement(); top.style.flexShrink = 0; UiKit.Pad(top, 8, 18);
            top.style.borderBottomWidth = 1; top.style.borderBottomColor = UiKit.Border;
            var name = UiKit.Label(chestName ?? "", UiKit.FontSize, UiKit.Text);
            name.style.unityTextAlign = TextAnchor.MiddleCenter; name.style.marginBottom = 6;
            top.Add(name);
            _tabs = new VisualElement(); _tabs.style.flexDirection = FlexDirection.Row; _tabs.style.flexShrink = 0;
            top.Add(_tabs);
            Window.Insert(1, top);                       // between the title bar and the scrolled body

            for (int i = 0; i < TabCount; i++)
            {
                var p = new VisualElement { name = "chestdecor-page-" + i };
                p.style.flexShrink = 0;
                Pages[i] = p;
                Body.Content.Add(p);
            }
            ShowTab(0, false);
            UiLog.Verbose("DecorWindowView: built for '" + chestName + "'");
        }

        /// Shows page `tab` (0 colours, 1 attach, 2 position). `notify` calls the game's onTab. Expected outcome: Tab == tab.
        internal bool ShowTab(int tab, bool notify)
        {
            _tab = Mathf.Clamp(tab, 0, TabCount - 1);
            for (int i = 0; i < TabCount; i++) Pages[i].style.display = i == _tab ? DisplayStyle.Flex : DisplayStyle.None;
            _tabs.Clear();
            for (int i = 0; i < TabCount; i++)
            {
                int idx = i;
                var b = UiControls.Button(_text(TabKeys[i]), () => ShowTab(idx, true), i == _tab);
                b.style.flexGrow = 1;
                _tabs.Add(b);
            }
            Body.ScrollTo(0f);
            if (notify) { UiLog.Verbose("DecorWindowView: tab " + _tab + " chosen"); _onTab?.Invoke(_tab); }
            return _tab == tab;
        }

        // --- small builders shared by the three page views --------------------------------------------------------------------------
        internal static Label Status(string text)
        {
            var l = UiKit.Label(text, UiKit.FontSize, UiKit.Text);
            l.style.unityTextAlign = TextAnchor.MiddleCenter; l.style.marginTop = 8; l.style.flexShrink = 0;
            return l;
        }

        internal static void SetStatus(Label l, bool dirty, Func<string, string> text)
        {
            l.text = text(dirty ? "chestdecor_unsaved" : "chestdecor_saved");
            l.style.color = dirty ? UiKit.Gold : UiKit.TextDim;
        }

        /// A row of equal buttons (Preview / Save / Revert).
        internal static VisualElement Actions(params Button[] buttons)
        {
            var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row; r.style.flexShrink = 0; r.style.marginTop = 12;
            foreach (var b in buttons) { b.style.flexGrow = 1; b.style.height = 44; b.style.fontSize = UiKit.FontSize; r.Add(b); }
            return r;
        }

        /// A flat selectable row button (part / attached item) with an optional colour swatch on the left.
        internal static Button RowButton(string text, bool selected, Color? swatch, Action onClick)
        {
            var b = UiControls.Button(text, onClick, selected);
            b.style.flexGrow = 1; b.style.unityTextAlign = TextAnchor.MiddleLeft; b.style.height = 38;
            if (swatch.HasValue)
            {
                b.style.paddingLeft = 46;
                var sw = new VisualElement(); sw.pickingMode = PickingMode.Ignore;
                sw.style.position = Position.Absolute; sw.style.left = 8; sw.style.top = 7; sw.style.width = 28; sw.style.height = 22;
                sw.style.backgroundColor = swatch.Value; UiKit.Radius(sw, 3); UiKit.Borders(sw, 1, UiKit.OnDark);
                b.Add(sw);
            }
            return b;
        }

        internal static void SetEnabled(Button b, bool on)
        {
            b.SetEnabled(on);
            b.style.opacity = on ? 1f : 0.45f;
        }

        internal static VisualElement Notes(Func<string, string> text, IEnumerable<string> keys)
        {
            var box = new VisualElement(); box.style.flexShrink = 0; box.style.marginTop = 10;
            foreach (var k in keys) box.Add(UiControls.Note(text(k)));
            return box;
        }
    }
}
