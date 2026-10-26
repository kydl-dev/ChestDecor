using System;
using System.Collections.Generic;
using Kydl.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChestDecor.Ui
{
    /// <summary>One row of the Attach items list as the view shows it.</summary>
    internal struct AttachRowData
    {
        internal Sprite Icon;
        internal string Name;
        internal string Cost;       // cost text (and the reason it can't be attached, on a second line)
        internal bool Affordable;   // cost in green, else red
        internal bool CanAttach;    // Attach button enabled
    }

    /// <summary>The game side of the Attach page (AttachPage in the mod; a stub in the Unity preview).</summary>
    internal interface IAttachActions
    {
        void Filter(string id);
        void Search(string text);
        void Page(int delta);
        void Attach(int row);
    }

    /// <summary>
    /// The "Attach items" page: category filter buttons (the KY-Items button only when the game side passes it), search, paging, the rows
    /// (icon, name, cost, Attach) and the attached-count line. The rows are made once and filled in place, so a refresh never cuts off a click.
    /// </summary>
    internal sealed class AttachView
    {
        sealed class Row
        {
            internal VisualElement Root, Icon;
            internal Label Name, Cost;
            internal Button Attach;
        }

        readonly Func<string, string> _text;
        readonly IAttachActions _act;
        readonly VisualElement _filters, _rowsBox;
        readonly Label _pageLabel, _status;
        readonly TextField _search;
        readonly List<Row> _rows = new List<Row>();

        internal TextField SearchField { get { return _search; } }

        internal AttachView(VisualElement page, Func<string, string> text, IAttachActions act)
        {
            _text = text; _act = act;
            _filters = new VisualElement(); _filters.style.flexShrink = 0; _filters.style.marginTop = 6;
            page.Add(_filters);

            _search = UiControls.TextField("", 40, s => _act.Search(s));
            var prev = UiControls.Button("<", () => _act.Page(-1));
            var next = UiControls.Button(">", () => _act.Page(1));
            _pageLabel = UiKit.Label("", UiKit.FontSize, UiKit.Text);
            _pageLabel.style.width = 110; _pageLabel.style.unityTextAlign = TextAnchor.MiddleCenter; _pageLabel.style.flexShrink = 0;
            var bar = new VisualElement(); bar.style.flexDirection = FlexDirection.Row; bar.style.alignItems = Align.Center; bar.style.flexShrink = 0;
            bar.style.marginTop = 8;
            var searchRow = UiControls.Row(text("chestdecor_search"), _search);
            searchRow.style.flexGrow = 1; searchRow.style.flexShrink = 1;
            searchRow.Q<Label>().style.width = 90;
            bar.Add(searchRow); bar.Add(prev); bar.Add(_pageLabel); bar.Add(next);
            page.Add(bar);

            _rowsBox = new VisualElement(); _rowsBox.style.flexShrink = 0; _rowsBox.style.marginTop = 6;
            page.Add(_rowsBox);
            _status = DecorWindowView.Status("");
            page.Add(_status);
            page.Add(DecorWindowView.Notes(text, new[] { "chestdecor_note_attach1", "chestdecor_note_attach2", "chestdecor_note_attach3" }));
            UiLog.Verbose("AttachView: built");
        }

        /// The filter buttons: (id, text) pairs in screen order; `current` is highlighted. Expected outcome: one button per filter.
        internal int ShowFilters(IList<KeyValuePair<string, string>> filters, string current)
        {
            _filters.Clear();
            var line = UiControls.Line();
            foreach (var f in filters)
            {
                string id = f.Key;
                var b = UiControls.Button(f.Value, () => _act.Filter(id), id == current);
                b.style.minWidth = 108;
                line.Add(b);
            }
            _filters.Add(line);
            return line.childCount;
        }

        Row MakeRow(int index)
        {
            var r = new Row();
            r.Root = new VisualElement(); r.Root.style.flexDirection = FlexDirection.Row; r.Root.style.alignItems = Align.Center; r.Root.style.flexShrink = 0;
            r.Root.style.backgroundColor = UiKit.PanelLight; UiKit.Radius(r.Root, 4); UiKit.Pad(r.Root, 4, 6); r.Root.style.marginTop = 3;
            r.Icon = new VisualElement(); r.Icon.pickingMode = PickingMode.Ignore; r.Icon.style.width = 40; r.Icon.style.height = 40; r.Icon.style.flexShrink = 0;
            r.Icon.style.marginRight = 8;
            r.Name = UiKit.Label("", UiKit.FontSize, UiKit.Text); r.Name.style.width = 230; r.Name.style.flexShrink = 0; r.Name.style.whiteSpace = WhiteSpace.Normal;
            r.Cost = UiKit.Label("", UiKit.SmallSize, UiKit.Text); r.Cost.style.flexGrow = 1; r.Cost.style.flexShrink = 1; r.Cost.style.whiteSpace = WhiteSpace.Normal;
            r.Attach = UiControls.Button(_text("chestdecor_attach"), () => _act.Attach(index), true);
            r.Attach.style.width = 110;
            r.Root.Add(r.Icon); r.Root.Add(r.Name); r.Root.Add(r.Cost); r.Root.Add(r.Attach);
            _rowsBox.Add(r.Root);
            return r;
        }

        /// Fills the rows (`rows.Count` may be less than `perPage`: the rest are hidden). Expected outcome: returns the number shown.
        internal int ShowRows(IList<AttachRowData> rows, int perPage)
        {
            while (_rows.Count < perPage) _rows.Add(MakeRow(_rows.Count));
            int shown = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                bool on = i < rows.Count && i < perPage;
                r.Root.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                var d = rows[i];
                r.Icon.style.backgroundImage = d.Icon != null ? new StyleBackground(d.Icon) : new StyleBackground(StyleKeyword.None);
                r.Name.text = d.Name ?? "";
                r.Cost.text = d.Cost ?? "";
                r.Cost.style.color = d.Affordable ? DecorWindowView.Good : DecorWindowView.Bad;
                DecorWindowView.SetEnabled(r.Attach, d.CanAttach);
                shown++;
            }
            return shown;
        }

        internal void ShowPage(int page, int pages, bool empty)
        {
            _pageLabel.text = empty ? _text("chestdecor_no_items") : _text("chestdecor_page").Replace("{0}", (page + 1).ToString()).Replace("{1}", pages.ToString());
        }

        internal void ShowCount(int count, int max)
        {
            bool full = count >= max;
            _status.text = _text(full ? "chestdecor_full" : "chestdecor_attached_count").Replace("{0}", count.ToString()).Replace("{1}", max.ToString());
            _status.style.color = full ? UiKit.Gold : UiKit.Text;
        }
    }
}
