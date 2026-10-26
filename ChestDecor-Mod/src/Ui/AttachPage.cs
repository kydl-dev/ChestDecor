using System;
using System.Collections.Generic;
using ChestDecor.Ui;
using Kydl.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChestDecor
{
    /// <summary>
    /// The "Attach items" tab, game side: category filter (KY-Items only while one of the user's own items is listed), search, paging, the
    /// rows' cost check and the attach action (pay and save). AttachView (Unity project) draws it.
    /// </summary>
    internal sealed class AttachPage : IAttachActions
    {
        static AttachPage _page;
        static ChestDecorator Decor { get { return ChestDecorUI.Decor; } }

        // filter id -> category. Order = order on screen; "kyitems" is appended only when the catalog has KY-Items.
        static readonly string[] FilterIds = { "all", "trophy", "trader", "produce", "drop", "food", "ammo", "weapon", "armor", "decor" };
        static readonly AttachCategory[] FilterValues =
        {
            AttachCategory.None, AttachCategory.Trophy, AttachCategory.Trader, AttachCategory.Produce, AttachCategory.Drop,
            AttachCategory.Food, AttachCategory.Ammo, AttachCategory.Weapon, AttachCategory.Armor, AttachCategory.Decor
        };
        internal const string KyFilterId = "kyitems";

        AttachView _view;
        readonly List<AttachEntry> _filtered = new List<AttachEntry>();
        readonly List<AttachEntry> _shown = new List<AttachEntry>();
        string _filter = "all", _search = "";
        int _pageIndex;
        float _nextRefresh;
        bool _built;

        static int PerPage { get { return ModSettings.AttachRowsPerPage.Value; } }

        /// The window is opening: filter, search and paging start fresh. The list itself is built when the tab is first shown (it is expensive).
        internal static void Open(VisualElement page)
        {
            _page = new AttachPage();
            _page._view = new AttachView(page, ChestDecorUI.T, _page);
            ModLog.Verbose("AttachPage: opened (list is built when the tab is shown)");
        }

        internal static void Close() { _page = null; }

        /// Per-frame hook: keeps the affordable / missing state of the visible rows current.
        internal static void Tick()
        {
            if (_page == null || !_page._built || ChestDecorUI.Tab != ChestDecorTab.Attach) return;
            if (Time.unscaledTime < _page._nextRefresh) return;
            RefreshRows();
        }

        static AttachCategory CategoryOf(string id)
        {
            if (id == KyFilterId) return AttachCategory.KyItems;
            int i = Array.IndexOf(FilterIds, id);
            return i >= 0 ? FilterValues[i] : AttachCategory.None;
        }

        /// Filters, search and the page; then the rows. Expected outcome: returns the number of entries that pass the filter.
        internal static int RefreshList()
        {
            if (_page == null || Decor == null) return 0;
            var p = _page;
            p._built = true;
            bool ky = AttachCatalog.HasKyItems;
            if (p._filter == KyFilterId && !ky) { ModLog.Verbose("AttachPage: KY-Items filter gone (no KY-Items listed any more) - back to All"); p._filter = "all"; }
            var filters = new List<KeyValuePair<string, string>>();
            foreach (var id in FilterIds) filters.Add(new KeyValuePair<string, string>(id, ChestDecorUI.T("chestdecor_filter_" + id)));
            if (ky) filters.Add(new KeyValuePair<string, string>(KyFilterId, ChestDecorUI.T("chestdecor_filter_kyitems")));
            p._view.ShowFilters(filters, p._filter);

            p._filtered.Clear();
            var cat = CategoryOf(p._filter);
            var search = p._search != null ? p._search.Trim() : "";
            foreach (var entry in AttachCatalog.Entries)
            {
                if (!entry.Listed) continue;      // the catalog already left out the hidden items, and the test items unless they are wanted
                if (cat != AttachCategory.None && (entry.Categories & cat) == 0) continue;
                if (search.Length > 0 && entry.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    entry.Prefab.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                p._filtered.Add(entry);
            }
            int pages = Mathf.Max(1, Mathf.CeilToInt(p._filtered.Count / (float)PerPage));
            p._pageIndex = Mathf.Clamp(p._pageIndex, 0, pages - 1);
            p._view.ShowPage(p._pageIndex, pages, p._filtered.Count == 0);
            ModLog.Verbose("AttachPage: filter '" + p._filter + "', search '" + search + "' -> " + p._filtered.Count + " entr(ies), page " + (p._pageIndex + 1) + " / " + pages +
                           (ky ? " (KY-Items button shown, " + AttachCatalog.KyListed + " item(s))" : ""));
            RefreshRows();
            return p._filtered.Count;
        }

        /// The rows of the current page with their cost state. Expected outcome: returns the number of rows shown.
        internal static int RefreshRows()
        {
            if (_page == null || Decor == null || !_page._built) return 0;
            var p = _page;
            p._nextRefresh = Time.unscaledTime + ModSettings.AttachRefreshSeconds.Value;
            var player = Player.m_localPlayer;
            int max = Decor.MaxAttachments;
            bool full = ChestDecorUI.SavedAtt.Count >= max;
            p._shown.Clear();
            var rows = new List<AttachRowData>();
            for (int i = p._pageIndex * PerPage; i < p._filtered.Count && rows.Count < PerPage; i++)
            {
                var entry = p._filtered[i];
                string reason;
                bool ok = AttachCost.Evaluate(player, entry, out reason);
                var cost = AttachCost.Describe(entry);
                rows.Add(new AttachRowData
                {
                    Icon = entry.Icon, Name = entry.RowLabel ?? entry.DisplayName,
                    Cost = ok || string.IsNullOrEmpty(reason) ? cost : cost + "\n" + reason,
                    Affordable = ok, CanAttach = ok && !full
                });
                p._shown.Add(entry);
            }
            int shown = p._view.ShowRows(rows, PerPage);
            p._view.ShowCount(ChestDecorUI.SavedAtt.Count, max);
            return shown;
        }

        // --- IAttachActions --------------------------------------------------------------------------------------------------------
        public void Filter(string id)
        {
            ModLog.Verbose("AttachPage: filter '" + id + "' chosen");
            _filter = id; _pageIndex = 0;
            RefreshList();
        }

        public void Search(string text)
        {
            _search = text ?? ""; _pageIndex = 0;
            RefreshList();
        }

        public void Page(int delta)
        {
            _pageIndex += delta;
            RefreshList();
        }

        public void Attach(int row)
        {
            if (Decor == null || row < 0 || row >= _shown.Count) return;
            var entry = _shown[row];
            var player = Player.m_localPlayer;
            if (entry == null || player == null) return;
            ModLog.Verbose("AttachPage: attach '" + entry.Prefab + "' clicked (" + entry.Categories + ", cost " + entry.Cost + ")");
            if (!ChestDecorUI.CanChangeAttachments()) return;      // before anything is paid

            if (ChestDecorUI.SavedAtt.Count >= Decor.MaxAttachments) { ChestDecorUI.Toast("This chest can't hold more attachments."); return; }
            if (!AttachVisuals.GetModel(entry.Object).Valid) { ChestDecorUI.Toast(entry.DisplayName + " has no model that can be shown."); return; }
            string reason;
            if (!AttachCost.Evaluate(player, entry, out reason))
            {
                ChestDecorUI.Toast(string.IsNullOrEmpty(reason) ? "You can't afford that." : reason);
                RefreshRows();
                return;
            }

            var charged = !AttachCost.IsFree(player);     // a free attachment is never refunded later
            // Exactly what is taken now. It is stored on the attachment, so removing it later returns these materials
            // even if the recipe, the build cost or the config has changed by then.
            var cost = charged ? AttachPayment.CostOf(entry) : new List<PaidMaterial>();
            if (!AttachPayment.Pay(player, entry)) { ModLog.Warn("AttachPage: paying for '" + entry.Prefab + "' failed"); ChestDecorUI.Toast("Could not pay for " + entry.DisplayName + "."); return; }

            var attachment = Decor.CreateDefault(entry.Prefab, ChestDecorUI.SavedAtt.Count);
            attachment.Paid = charged;
            attachment.Cost = cost;
            ChestDecorUI.SavedAtt.Add(attachment);
            ChestDecorUI.WorkAtt.Add(attachment.Clone());

            if (!Decor.SaveAttachments(ChestDecorUI.SavedAtt))
            {
                // couldn't write to the chest: undo, and give the cost back
                ChestDecorUI.SavedAtt.RemoveAt(ChestDecorUI.SavedAtt.Count - 1);
                ChestDecorUI.WorkAtt.RemoveAt(ChestDecorUI.WorkAtt.Count - 1);
                AttachPayment.Refund(player, player.transform.position, attachment);   // gives back exactly what was just paid
                ModLog.Warn("AttachPage: saving '" + entry.Prefab + "' to the chest failed - payment returned");
                ChestDecorUI.Toast("Could not attach to the chest.");
                RefreshRows();
                return;
            }

            Decor.ShowAttachments(ChestDecorUI.WorkAtt);      // keeps any unsaved position edits of the other items
            PositionPage.SelectedIndex = ChestDecorUI.WorkAtt.Count - 1;
            ModLog.Check("AttachPage: attachments on the chest after attaching", ChestDecorUI.SavedAtt.Count, ChestDecorUI.WorkAtt.Count, 0, "");
            ModLog.Verbose("AttachPage: attached '" + entry.Prefab + "' (" + (charged ? cost.Count + " material(s) paid" : "free") + "), now " + ChestDecorUI.SavedAtt.Count + " / " + Decor.MaxAttachments);
            ChestDecorUI.Toast("Attached " + entry.DisplayName);
            RefreshRows();
        }
    }
}
