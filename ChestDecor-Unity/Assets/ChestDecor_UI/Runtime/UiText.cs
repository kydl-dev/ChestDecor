using System.Collections.Generic;

namespace ChestDecor.Ui
{
    /// <summary>
    /// Player-facing text of the decorate window, English only (user rule: no other languages). Shared by the game (registered with Jotunn in
    /// Plugin.Awake through Kydl.Common.Loc) and the Unity preview (English). Every key starts with "chestdecor_" so it never clashes with another mod.
    /// {0} {1} are filled in by the caller.
    /// </summary>
    internal static class UiText
    {
        internal static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "chestdecor_title", "Chest Decor" },
            { "chestdecor_footer", "Esc closes  ·  right mouse + WASD: free camera  ·  F: normal camera" },
            { "chestdecor_footer_nocam", "Esc closes" },
            { "chestdecor_tab_colors", "Colours" },
            { "chestdecor_tab_attach", "Attach items" },
            { "chestdecor_tab_position", "Position items" },
            // colours
            { "chestdecor_parts", "Parts" },
            { "chestdecor_no_parts", "This chest has no colourable parts. You can still attach items to it." },
            { "chestdecor_reset", "Reset" },
            { "chestdecor_colour", "Colour" },
            { "chestdecor_mode", "Colour mode" },
            { "chestdecor_mode_tint", "Tint" },
            { "chestdecor_mode_full", "Full colour" },
            { "chestdecor_mode_all", "Apply mode to all parts" },
            { "chestdecor_preview", "Preview" },
            { "chestdecor_save", "Save" },
            { "chestdecor_revert", "Revert" },
            { "chestdecor_unsaved", "Unsaved changes" },
            { "chestdecor_saved", "All changes saved" },
            { "chestdecor_note_tint", "Tint: multiplies the chest's texture, so dark chests stay dark." },
            { "chestdecor_note_full", "Full colour: repaints the texture but keeps its grain and shading." },
            // attach
            { "chestdecor_filter_all", "All" },
            { "chestdecor_filter_trophy", "Trophies" },
            { "chestdecor_filter_trader", "Trader" },
            { "chestdecor_filter_produce", "Produce" },
            { "chestdecor_filter_drop", "Drops" },
            { "chestdecor_filter_food", "Food" },
            { "chestdecor_filter_ammo", "Ammo" },
            { "chestdecor_filter_weapon", "Weapons" },
            { "chestdecor_filter_armor", "Armor" },
            { "chestdecor_filter_decor", "Decor" },
            { "chestdecor_filter_kyitems", "KY-Items" },
            { "chestdecor_search", "Search" },
            { "chestdecor_no_items", "No items" },
            { "chestdecor_page", "{0} / {1}" },
            { "chestdecor_attach", "Attach" },
            { "chestdecor_attached_count", "Attached to this chest: {0} / {1}" },
            { "chestdecor_full", "This chest is full ({0} / {1}). Remove one in the Position tab." },
            { "chestdecor_note_attach1", "Attaching pays the cost and saves immediately. Move items in the Position tab." },
            { "chestdecor_note_attach2", "Costs use your inventory and, with crafting-from-storage mods, nearby chests." },
            { "chestdecor_note_attach3", "Listed: things you can craft or build, plus the items in the filters above." },
            // position
            { "chestdecor_attached_items", "Attached items" },
            { "chestdecor_nothing_attached", "Nothing attached yet. Use the \"Attach items\" tab." },
            { "chestdecor_remove", "Remove" },
            { "chestdecor_axis_x", "Left / right (m)" },
            { "chestdecor_axis_y", "Up / down (m)" },
            { "chestdecor_axis_z", "Front / back (m)" },
            { "chestdecor_axis_tilt", "Tilt (°)" },
            { "chestdecor_axis_turn", "Turn (°)" },
            { "chestdecor_axis_roll", "Roll (°)" },
            { "chestdecor_axis_size", "Size (x)" },
            { "chestdecor_reset_item", "Reset item" },
            { "chestdecor_note_dots", "Click a dot under a slider to jump there: there is a dot every 12.5% of the slider, on every slider." },
            { "chestdecor_note_pos1", "Changes show on the chest right away. Save keeps them; closing the window drops unsaved moves." },
            { "chestdecor_note_pos2", "Remove takes the item off the chest (and drops the materials it cost at your feet)." },
        };

        /// English text (the Unity preview; the game reads the same table through Jotunn).
        internal static string English(string key)
        {
            string s;
            return En.TryGetValue(key, out s) ? s : key;
        }
    }
}
