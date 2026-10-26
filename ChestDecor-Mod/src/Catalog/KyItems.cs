using System;
using System.Collections.Generic;
using Jotunn.Managers;
using Jotunn.Utils;
using Kydl.Common;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// The user's own items (user, 2026-10-26: "if the player has any of my mods installed, have the items from them show up in the chest decor
    /// categories, with a category KY-Items, only visible if any of my mods with items exist").
    /// Found two ways, both read from the game, so a new mod of the user needs no change here:
    ///  * Jotunn's ModRegistry: every CustomItem / CustomPiece whose source mod's GUID starts with "com.kydldev." (ChestDecor itself excluded);
    ///  * every hammer piece in the shared "KY-Items" build category (KyCategory.Name), whoever registered it.
    /// The catalog gives these the KyItems flag on top of their normal categories and lists them by rule (always, when they have a model).
    /// </summary>
    internal static class KyItems
    {
        internal const string GuidPrefix = "com.kydldev.";

        /// One of the user's prefabs and the mod it comes from (for the log and the catalog dump).
        internal sealed class Found
        {
            internal GameObject Prefab;
            internal string Mod;
            internal bool IsPiece;
        }

        // Stryker disable all : game code (Jotunn ModRegistry, Unity prefabs) - no unit test can run it; in-game tests CD-05 / CD-06 (TEST_QUALITY.md)
        /// <summary>
        /// Prefab name -> the user's item / piece. `pieces` are all build pieces the catalog collected (for the KY-Items build category check).
        /// Expected outcome: returns the map (empty when none of the user's mods with items is loaded).
        /// </summary>
        internal static Dictionary<string, Found> Collect(IEnumerable<GameObject> pieces)
        {
            var result = new Dictionary<string, Found>(StringComparer.OrdinalIgnoreCase);
            int items = 0, registeredPieces = 0, categoryPieces = 0;
            try
            {
                foreach (var ci in ModRegistry.GetItems())
                {
                    if (ci == null || ci.ItemPrefab == null || !IsUserMod(ci.SourceMod)) continue;
                    if (Add(result, ci.ItemPrefab, ci.SourceMod.Name, false)) items++;
                }
                foreach (var cp in ModRegistry.GetPieces())
                {
                    if (cp == null || cp.PiecePrefab == null || !IsUserMod(cp.SourceMod)) continue;
                    if (Add(result, cp.PiecePrefab, cp.SourceMod.Name, true)) registeredPieces++;
                }
            }
            catch (TypeLoadException e) { ModLog.Warn("KyItems: Jotunn's ModRegistry is not available (" + e.Message + ") - only the KY-Items build category is used"); }
            catch (MissingMethodException e) { ModLog.Warn("KyItems: Jotunn's ModRegistry is not available (" + e.Message + ") - only the KY-Items build category is used"); }
            catch (Exception e) { ModLog.Warn("KyItems: reading Jotunn's ModRegistry failed: " + e.Message); }

            Piece.PieceCategory? ky = null;
            try { ky = PieceManager.Instance.GetPieceCategory(KyCategory.Name); }
            catch (Exception e) { ModLog.Verbose("KyItems: the '" + KyCategory.Name + "' build category could not be read: " + e.Message); }
            if (ky.HasValue && pieces != null)
            {
                foreach (var go in pieces)
                {
                    var piece = go != null ? go.GetComponent<Piece>() : null;
                    if (piece == null || piece.m_category != ky.Value) continue;
                    if (Add(result, go, "build category " + KyCategory.Name, true)) categoryPieces++;
                }
            }
            ModLog.Verbose("KyItems: " + result.Count + " of the user's prefab(s) found (" + items + " item(s) and " + registeredPieces + " piece(s) from com.kydldev.* mods, " +
                           categoryPieces + " more piece(s) in the '" + KyCategory.Name + "' build category" + (ky.HasValue ? "" : " - category not registered") + ")");
            return result;
        }
        // Stryker restore all

        /// A loaded mod of the user (not ChestDecor itself).
        internal static bool IsUserMod(BepInEx.BepInPlugin mod)
        {
            return mod != null && IsUserGuid(mod.GUID);
        }

        /// Pure rule (unit tested): "com.kydldev.*", ignoring case, but never ChestDecor's own GUID.
        internal static bool IsUserGuid(string guid)
        {
            // Stryker disable once all : always false here (null or empty) - written as the checked end state (STANDARDS), so any mutation is equivalent
            if (string.IsNullOrEmpty(guid)) return guid != null && guid.Length > 0;
            return guid.StartsWith(GuidPrefix, StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(guid, Plugin.ModGuid, StringComparison.OrdinalIgnoreCase) && guid.Length > GuidPrefix.Length;
        }

        // Stryker disable all : game code (GameObject names), see Collect
        static bool Add(Dictionary<string, Found> map, GameObject go, string mod, bool isPiece)
        {
            if (map.ContainsKey(go.name)) return map[go.name].Prefab != go;
            map[go.name] = new Found { Prefab = go, Mod = mod, IsPiece = isPiece };
            ModLog.Verbose("KyItems: '" + go.name + "' (" + (isPiece ? "piece" : "item") + ") from " + mod);
            return map.ContainsKey(go.name);
        }
    }
}
