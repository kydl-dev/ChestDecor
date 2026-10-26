using System.Collections.Generic;
using Kydl.Ui;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ChestDecor.Ui
{
    /// <summary>
    /// Unity-only preview of the decorate window (NOT compiled into the mod: only Runtime/ is). Add this component to any GameObject of an empty
    /// scene and press Play: the window opens in the Game view in the same look as in game, filled with sample data; buttons log to the console.
    /// Set the Game view to 1920x1080 (the panel scales from that like in game). `startTab`: 0 colours, 1 attach, 2 position.
    /// </summary>
    public sealed class MenuPreview : MonoBehaviour, IColorsActions, IAttachActions, IPositionActions
    {
        const string FontFolder = "Assets/Kydl.Ui/Preview/GameAssets/";
        [Range(0, 2)] public int startTab;
        [Tooltip("Show the KY-Items filter (in game: only when one of the user's mods with items is loaded)")] public bool kyItems = true;

        DecorWindowView _win;
        ColorsView _colors;
        AttachView _attach;
        PositionView _pos;
        string _filter = "all";
        int _selPart, _selItem;
        bool _full;
        readonly List<PartRowData> _parts = new List<PartRowData>
        {
            new PartRowData { Name = "Wood (base)", Color = new Color(0.55f, 0.36f, 0.2f), Custom = true },
            new PartRowData { Name = "Metal (trim)", Color = new Color(0.4f, 0.4f, 0.42f), Custom = false },
        };
        readonly List<string> _items = new List<string> { "Deer Trophy", "Torch", "Copper Knife" };

        void OnEnable()
        {
            LoadGameFonts();
            UiKit.Reset();
            var root = UiKit.Root;
            if (root == null) { Debug.LogError("[MenuPreview] UI Toolkit panel could not be made"); return; }
            _win = new DecorWindowView(UiText.English, "Reinforced Chest", () => Debug.Log("[MenuPreview] close pressed"), t => Debug.Log("[MenuPreview] tab " + t));
            _colors = new ColorsView(_win.Pages[0], UiText.English, this, "#8C5C33");
            _attach = new AttachView(_win.Pages[1], UiText.English, this);
            _pos = new PositionView(_win.Pages[2], UiText.English, this);
            root.Add(_win.Window);
            _colors.ShowParts(_parts, _selPart); _colors.ShowMode(_full); _colors.ShowStatus(true);
            ShowFilters();
            _attach.ShowRows(new List<AttachRowData>
            {
                new AttachRowData { Name = "Deer Trophy", Cost = "1x Deer Trophy", Affordable = true, CanAttach = true },
                new AttachRowData { Name = "Bronze Axe", Cost = "8x Bronze, 4x Wood\nYou don't have the materials", Affordable = false, CanAttach = false },
                new AttachRowData { Name = "Standing Iron Torch", Cost = "2x Iron, 2x Resin", Affordable = true, CanAttach = true },
            }, 8);
            _attach.ShowPage(0, 12, false); _attach.ShowCount(3, 5);
            _pos.ShowItems(_items, _selItem);
            for (int k = 0; k < PositionView.Axes; k++) _pos.SetSlider(k, k < 3 ? -0.4f : k < 6 ? 0f : 0.25f, k < 3 ? 0.4f : k < 6 ? 360f : 2f, k < 3 ? 0f : k < 6 ? 0f : 1f);
            _pos.ShowStatus(false);
            _win.ShowTab(startTab, false);
            Debug.Log("[MenuPreview] ChestDecor window shown");
        }

        void OnDisable() { UiKit.Reset(); }

        void ShowFilters()
        {
            var f = new List<KeyValuePair<string, string>>();
            foreach (var id in new[] { "all", "trophy", "trader", "produce", "drop", "food", "ammo", "weapon", "armor", "decor" })
                f.Add(new KeyValuePair<string, string>(id, UiText.English("chestdecor_filter_" + id)));
            if (kyItems) f.Add(new KeyValuePair<string, string>("kyitems", UiText.English("chestdecor_filter_kyitems")));
            _attach.ShowFilters(f, _filter);
        }

        // --- stub actions: log and show the obvious reaction ---------------------------------------------------------------------
        public void SelectPart(int i) { _selPart = i; _colors.ShowParts(_parts, i); _colors.ShowColor(_parts[i].Color); }
        public void ResetPart(int i) { Debug.Log("[MenuPreview] reset part " + i); }
        public void ColorPicked(Color c) { var p = _parts[_selPart]; p.Color = c; p.Custom = true; _parts[_selPart] = p; _colors.ShowParts(_parts, _selPart); _colors.ShowStatus(true); }
        public void ModeClicked(bool full) { _full = full; _colors.ShowMode(full); }
        public void ModeToAll() { Debug.Log("[MenuPreview] mode to all"); }
        public void Preview() { Debug.Log("[MenuPreview] preview"); }
        public void Save() { Debug.Log("[MenuPreview] save"); _colors.ShowStatus(false); _pos.ShowStatus(false); }
        public void Revert() { Debug.Log("[MenuPreview] revert"); }
        public void Filter(string id) { _filter = id; ShowFilters(); }
        public void Search(string text) { Debug.Log("[MenuPreview] search '" + text + "'"); }
        public void Page(int d) { Debug.Log("[MenuPreview] page " + d); }
        public void Attach(int row) { Debug.Log("[MenuPreview] attach row " + row); }
        public void Select(int i) { _selItem = i; _pos.ShowItems(_items, i); }
        public void Remove(int i) { Debug.Log("[MenuPreview] remove " + i); }
        public void Slider(int axis, float v) { _pos.ShowStatus(true); }
        public void ResetItem() { Debug.Log("[MenuPreview] reset item"); }

        // UiKit finds the game fonts by name among the loaded fonts (Norse, Norsebold, NotoEmoji-Regular): load them first.
        static void LoadGameFonts()
        {
#if UNITY_EDITOR
            foreach (var f in new[] { "Norse.otf", "Norsebold.otf", "NotoEmoji-Regular.ttf" })
                if (AssetDatabase.LoadAssetAtPath<Font>(FontFolder + f) == null) Debug.LogWarning("[MenuPreview] font missing: " + FontFolder + f + " (Kydl > Sync shared UI kit)");
#endif
        }
    }
}
