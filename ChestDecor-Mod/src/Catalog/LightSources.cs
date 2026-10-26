using System.Collections.Generic;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// Decides what counts as a light source, for the Decor filter.
    ///
    /// A light source is found three ways, so vanilla and modded light sources are both caught:
    ///  * by name: the shown names on the Valheim Wiki's "Light source" page (matched completely), and any
    ///    prefab / shown name that contains torch, lantern, brazier or sconce;
    ///  * by what it is made of: it has a Fireplace (campfire, fire pit, braziers, sconces, torches, ...) or a point /
    ///    spot Light somewhere in its prefab. Lights that only switch on with fuel are switched off in the prefab, so
    ///    switched-off ones count too;
    ///  * for items: a held or worn item with a Light in its model (torches, lanterns, the Wisp Torch, the Dverger
    ///    Circlet, ...). Only held / worn item types are checked this way, otherwise every glowing material (Surtling
    ///    Core, ...) would count.
    ///
    /// Machines that merely glow while they work are never light pieces (crafting stations, smelters, cooking stations,
    /// portals, wards, doors, beds, containers, ships, carts, turrets, ...).
    /// </summary>
    internal static class LightSources
    {
        // Shown names from the Valheim Wiki "Light source" page. Compared with everything but letters and digits
        // removed, and they must match completely.
        private static readonly string[] WikiNames =
        {
            "Blue Standing Brazier", "Dverger Circlet", "Dvergr Pole Lantern", "Dvergr Wall Lantern", "Hanging Brazier",
            "Hooded Lantern", "Jack-O-Turnip", "Lava Lantern", "Salvaged Lantern", "Snow Lantern", "Standing Brazier",
            "Standing Iron Torch", "Standing Wood Torch", "Torch", "Wisp Torch"
        };

        // Words that make a prefab name or a shown name a light source (already without spaces and symbols).
        private static readonly string[] NameWords = { "torch", "lantern", "brazier", "sconce" };

        // ItemType names (matched as text, like the rest of the catalog) of items that are held or worn and so can
        // carry a light: the torch type, tools, utility items (Wisp Torch), helmets, capes and trinkets.
        private static readonly HashSet<string> CarriedTypes = new()
        {
            "Torch", "Tool", "Utility", "Helmet", "Shoulder", "Trinket"
        };

        private static HashSet<string> _wikiTokens;

        /// <summary>
        /// True for an item (an ItemDrop prefab) that is a light source. <paramref name="shownName"/> is its
        /// localised name.
        /// </summary>
        public static bool IsLightItem(GameObject item, ItemDrop.ItemData.SharedData shared, string shownName)
        {
            if (item == null || shared == null) return false;

            if (NameIsLight(item.name, shownName)) return true;

            var type = shared.m_itemType.ToString();
            if (type == "Torch") return true;   // the torch item type is only ever used for torches
            return CarriedTypes.Contains(type) && HasLight(item);
        }

        /// <summary>
        /// True for a hammer piece that is a light source. <paramref name="shownName"/> is its localised name.
        /// </summary>
        public static bool IsLightPiece(GameObject piece, string shownName)
        {
            if (piece == null) return false;

            var gives = NameIsLight(piece.name, shownName) ||
                        piece.GetComponentInChildren<Fireplace>(true) != null ||
                        HasLight(piece);
            return gives && !IsMachine(piece);
        }

        /// <summary>True when the prefab (switched on or off) has a point or spot light that reaches somewhere.</summary>
        private static bool HasLight(GameObject go)
        {
            foreach (var light in go.GetComponentsInChildren<Light>(true))
            {
                if (light == null) continue;
                if (light.type != LightType.Point && light.type != LightType.Spot) continue;
                if (light.range > 0f) return true;
            }
            return false;
        }

        /// <summary>Things that glow while they work but are not light sources (and containers, which are never attached).</summary>
        private static bool IsMachine(GameObject go)
        {
            return go.GetComponentInChildren<CraftingStation>(true) != null ||
                   go.GetComponentInChildren<Smelter>(true) != null ||
                   go.GetComponentInChildren<CookingStation>(true) != null ||
                   go.GetComponentInChildren<Fermenter>(true) != null ||
                   go.GetComponentInChildren<Beehive>(true) != null ||
                   go.GetComponentInChildren<TeleportWorld>(true) != null ||
                   go.GetComponentInChildren<PrivateArea>(true) != null ||
                   go.GetComponentInChildren<Incinerator>(true) != null ||
                   go.GetComponentInChildren<Turret>(true) != null ||
                   go.GetComponentInChildren<Door>(true) != null ||
                   go.GetComponentInChildren<Bed>(true) != null ||
                   go.GetComponentInChildren<Container>(true) != null ||
                   go.GetComponentInChildren<Ship>(true) != null ||
                   go.GetComponentInChildren<Vagon>(true) != null;
        }

        private static bool NameIsLight(string prefabName, string shownName)
        {
            if (_wikiTokens == null)
            {
                var tokens = new HashSet<string>();
                foreach (var name in WikiNames) tokens.Add(Normalize(name));
                _wikiTokens = tokens;
            }

            var prefab = Normalize(prefabName);
            var shown = Normalize(shownName);

            if (shown.Length > 0 && _wikiTokens.Contains(shown)) return true;
            if (prefab.Length > 0 && _wikiTokens.Contains(prefab)) return true;

            foreach (var word in NameWords)
                if (prefab.Contains(word) || shown.Contains(word)) return true;
            return false;
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var sb = new System.Text.StringBuilder(text.Length);
            foreach (var c in text)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }
    }
}
