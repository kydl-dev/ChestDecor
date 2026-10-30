using System;
using System.Collections.Generic;
using Kydl.Common;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// The user's own items (user, 2026-10-26: "if the player has any of my mods installed, have the items from them show up in the chest decor
    /// categories, with a category KY-Items, only visible if any of my mods with items exist").
    /// Since 2.0.0 the list comes from KydlCore's KY-Items registry (<c>KydlCore.KyItems</c>: the Jotunn items / pieces of every
    /// "com.kydldev.*" mod, the hammer pieces of the KY-Items build category and what mods registered), so a new mod of the user needs no
    /// change here. ChestDecor leaves out its own entries (nothing to attach to itself) and gives the rest the KyItems flag on top of their
    /// normal categories (the catalog lists them by rule: always, when they have a model).
    /// </summary>
    internal static class ModItems
    {
        /// One of the user's prefabs and the mod it comes from (for the log and the catalog dump).
        internal sealed class Found
        {
            internal GameObject Prefab;
            internal string Mod;
            internal bool IsPiece;
        }

        // Stryker disable all : game code (Core's registry holds Unity prefabs) - no unit test can run it; in-game tests CD-05 / CD-06 (TEST_QUALITY.md)
        /// <summary>
        /// Prefab name -> the user's item / piece, read from Core's registry without ChestDecor's own entries.
        /// Expected outcome: returns the map (empty when none of the user's mods with items is loaded, or Core has not read the game yet).
        /// </summary>
        internal static Dictionary<string, Found> Collect()
        {
            var result = new Dictionary<string, Found>(StringComparer.OrdinalIgnoreCase);
            int own = 0, noPrefab = 0;
            try
            {
                foreach (var e in KydlCore.KyItems.All)
                {
                    if (e == null) continue;
                    if (e.PrefabObject == null) { noPrefab++; continue; }
                    if (IsOwnGuid(e.ModGuid)) { own++; continue; }
                    if (result.ContainsKey(e.PrefabObject.name)) continue;
                    result[e.PrefabObject.name] = new Found { Prefab = e.PrefabObject, Mod = string.IsNullOrEmpty(e.ModName) ? e.Source : e.ModName, IsPiece = e.Kind == KydlCore.KyKind.Piece };
                    ModLog.Verbose("ModItems: '" + e.PrefabObject.name + "' (" + e.Kind + ") from " + (e.ModName ?? e.Source));
                }
            }
            catch (Exception ex) { ModLog.Warn("ModItems: reading the KY-Items registry of KydlCore failed: " + ex.Message); }
            ModLog.Verbose("ModItems: " + result.Count + " prefab(s) of the user's other mods (Core registry ready " + KydlCore.KyItems.Ready + ", " + own + " of ChestDecor itself left out, " + noPrefab + " without a prefab)");
            return result;
        }
        // Stryker restore all

        /// Pure rule (unit tested): ChestDecor's own GUID, ignoring case. An empty GUID is not ChestDecor's.
        internal static bool IsOwnGuid(string guid)
        {
            return !string.IsNullOrEmpty(guid) && string.Equals(guid, Plugin.ModGuid, StringComparison.OrdinalIgnoreCase);
        }

        /// A mod of the user (KydlCore's rule: "com.kydldev.*" ignoring case, a name after the prefix) other than ChestDecor.
        internal static bool IsUserGuid(string guid)
        {
            return !IsOwnGuid(guid) && KydlCore.KyItems.IsUserGuid(guid);
        }
    }
}
