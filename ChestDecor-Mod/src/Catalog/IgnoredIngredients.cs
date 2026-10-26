using Kydl.Common;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// Ingredients that are left out of every recipe / piece cost in this mod (shown cost, can-I-afford check, payment,
    /// refund). Some recipes list extra items such as the Battle Idol or Protection Idol that are only needed to
    /// upgrade the item, never to craft it, so attachments must not ask for them.
    ///
    /// The Battle Idols (prefabs Upgrader1Weapon .. Upgrader7Weapon) and Protection Idols (Upgrader1Armor ..
    /// Upgrader7Armor) are always ignored; the "Ignored Ingredients" setting adds more.
    ///
    /// Matching is forgiving about spelling but exact about the name: names are lower-cased and stripped of spaces,
    /// underscores and symbols, and an ingredient is ignored when its prefab name or its item name is one of the
    /// configured names. A part of a name is not enough, so a short entry can't hide unrelated ingredients.
    /// ("BattleIdol" matches the prefab "BattleIdol", the item name "$item_battleidol" and "Battle Idol", but
    /// not "BattleIdolUpgrade".)
    /// </summary>
    internal static class IgnoredIngredients
    {
        private static string[] _tokens;

        public static void Invalidate() => _tokens = null;

        /// <summary>True when this ingredient should not count towards the cost.</summary>
        public static bool IsIgnored(Piece.Requirement req)
        {
            if (req == null || req.m_resItem == null) return false;

            var tokens = Tokens();
            if (tokens.Length == 0) return false;

            var prefab = Normalize(req.m_resItem.gameObject != null ? req.m_resItem.gameObject.name : null);
            string shared = null;
            string sharedBare = null;
            try
            {
                var data = req.m_resItem.m_itemData;
                var key = data != null && data.m_shared != null ? data.m_shared.m_name : null;
                shared = Normalize(key);
                sharedBare = Normalize(StripKey(key));   // "$item_battleidol" -> "battleidol"
            }
            catch { /* item data not ready: the prefab name has to do */ }

            foreach (var token in tokens)
            {
                if (prefab == token) return true;
                if (shared != null && (shared == token || sharedBare == token)) return true;
            }
            return false;
        }

        /// <summary>A translation key without its "$" and "item_" prefix.</summary>
        private static string StripKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";

            var text = key.TrimStart('$');
            return text.StartsWith("item_", StringComparison.OrdinalIgnoreCase) ? text.Substring(5) : text;
        }

        /// <summary>The requirements without the ignored ones. Returns the same array when nothing is ignored.</summary>
        public static Piece.Requirement[] Filter(Piece.Requirement[] requirements)
        {
            if (requirements == null) return null;

            var drop = false;
            foreach (var req in requirements)
                if (IsIgnored(req)) { drop = true; break; }
            if (!drop) return requirements;

            var kept = new List<Piece.Requirement>(requirements.Length);
            foreach (var req in requirements)
                if (!IsIgnored(req)) kept.Add(req);
            return kept.ToArray();
        }

        private static string[] Tokens()
        {
            if (_tokens != null) return _tokens;

            var list = new List<string>();

            // Always ignored, whatever the config says (a config file keeps the defaults it was first created with):
            // the Battle Idols (Upgrader1Weapon .. Upgrader7Weapon) and Protection Idols (Upgrader1Armor .. Upgrader7Armor).
            for (int level = 1; level <= 7; level++)
            {
                list.Add("upgrader" + level + "weapon");
                list.Add("upgrader" + level + "armor");
            }

            var raw = ModSettings.IgnoredIngredientsSetting != null ? ModSettings.IgnoredIngredientsSetting.Value : "";
            foreach (var part in (raw ?? "").Split(','))
            {
                var token = Normalize(part);
                if (token.Length > 0 && !list.Contains(token)) list.Add(token);
            }

            _tokens = list.ToArray();
            return _tokens;
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
