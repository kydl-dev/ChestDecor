using Kydl.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static ChestDecor.CatalogSources;
using static ChestDecor.CatalogEntries;
using static ChestDecor.CatalogDump;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// Builds (once per world load) the list of prefabs that may be attached to a chest.
    ///
    /// Two kinds of entries are listed:
    ///  * things the player can craft or build by following a recipe: weapons, armor, decorative furniture (and
    ///    anything else with a craftable recipe that falls in one of the categories below);
    ///  * items that match an inclusion rule, whether or not they have a recipe (see <see cref="AttachCategory"/>):
    ///    trophies, everything traders sell plus gems and trader valuables, craftable arrows and bolts, food
    ///    (including flour and the uncooked / food preparation items), everything mobs drop when they die, produce (fruits,
    ///    vegetables, seeds, and the plants and bushes they grow on, see <see cref="ProduceCatalog"/>) and every
    ///    light source (see <see cref="LightSources"/>), which is listed under Decor, and the casts that a cooking
    ///    station turns into weapons / armor, which are listed under Weapons / Armor.
    ///
    /// Mob equipment and attacks (weapons a mob carries, "attack" items, FW_ / SP_ loadout copies) are not listed
    /// unless that very item is also in a mob's drop table.
    /// </summary>
    internal static class AttachCatalog
    {
        // ItemType names are matched as text so a renamed enum member in a future game version can't break the build.
        internal static readonly HashSet<string> WeaponTypes = new()
        {
            "OneHandedWeapon", "TwoHandedWeapon", "TwoHandedWeaponLeft", "Bow", "Shield", "Attach_Atgeir"
        };

        internal static readonly HashSet<string> ArmorTypes = new()
        {
            "Helmet", "Chest", "Legs", "Shoulder", "Cape", "Trinket"
        };

        // The nine items of the wiki's "List of valuables" (traders buy them). Compared against the prefab name and the
        // shown name with everything but letters and digits removed, and they must match completely (so "Ruby" does
        // not pull in a "RubyStaff"). Prefabs whose name contains "gem" (the four ancient gemstones) are valuables too.
        // Valuables are listed under the Trader filter.
        internal static readonly HashSet<string> ValuableNames = new()
        {
            "amber", "amberpearl", "ruby", "silvernecklace", "ancientcoin",
            "grimvarn", "solryth", "veydris", "draumyx"
        };

        // Prefab-name words that make an item count as food even when it has no food stats (raw meat, mead, flour:
        // both the barley flour and the plain flour have no food stats).
        // "seed", "berry", "oat" and "barley" used to be here too; seeds, berries and grains are Produce now (see
        // ProduceCatalog), so the name rules for them are gone and nothing is listed twice because of a name.
        // Items that really feed the player (an eatable berry, a carrot) are still food through the food-stats rule.
        internal static readonly string[] FoodWords = { "meat", "mead", "flour" };

        // Things traders list that are not items (the Wider / Deeper Pockets inventory upgrades): they have no
        // physical form, so they are never listed. Matched on the trade entry's name and its prefab name.
        internal const string NonPhysicalWord = "pocket";

        // Why candidates were left out, for the catalog dump.
        internal static readonly List<string> Excluded = new();

        internal static ObjectDB _builtFor;

        // The scene whose prefabs the trader prices and mob drops were read from. The item database can exist
        // before the scene does (the main menu, a world that is still loading); a list built then has no trader or
        // drop information, so it counts as stale as soon as a different scene (or the first one) shows up.
        internal static ZNetScene _builtForScene;
        internal static List<AttachEntry> _entries = new();
        internal static Dictionary<string, AttachEntry> _byName = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, GameObject> PrefabCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>How many listed entries carry the KyItems flag (the KY-Items filter button is shown only when this is above 0).</summary>
        internal static int KyListed { get; private set; }

        /// <summary>True when the list has at least one of the user's own items (builds the list if needed).</summary>
        internal static bool HasKyItems
        {
            get { EnsureBuilt(); return KyListed > 0; }
        }

        public static IReadOnlyList<AttachEntry> Entries
        {
            get { EnsureBuilt(); return _entries; }
        }

        public static AttachEntry Find(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            EnsureBuilt();
            return _byName.TryGetValue(prefab, out var entry) ? entry : null;
        }

        /// <summary>True once the catalog has been built for the current item database (see <see cref="FindIfBuilt"/>).</summary>
        public static bool IsBuilt
        {
            get
            {
                var db = ObjectDB.instance;
                return db != null && _builtFor == db && _byName.Count > 0 && SceneReady();
            }
        }

        /// <summary>
        /// Like <see cref="Find"/>, but never builds the catalog. For work that must not trigger a build at an
        /// arbitrary moment (e.g. while a world is still loading). Returns null when it isn't built yet, so check
        /// <see cref="IsBuilt"/> first to tell "not built yet" from "not in the catalog".
        /// </summary>
        public static AttachEntry FindIfBuilt(string prefab)
        {
            if (string.IsNullOrEmpty(prefab) || !IsBuilt) return null;
            return _byName.TryGetValue(prefab, out var entry) ? entry : null;
        }

        /// <summary>
        /// True for developer / test items ("test" anywhere in the prefab name or the shown name). They are kept in
        /// the catalog (so an item that is already attached still resolves its name and refund) but are only listed
        /// while the "Show Test Items" setting is on.
        /// </summary>
        public static bool IsTestEntry(AttachEntry entry)
        {
            if (entry == null) return false;
            return Contains(entry.Prefab, "test") || Contains(entry.DisplayName, "test");
        }

        // Items that are not test items but must never be listed, whatever the settings say. Matched on the prefab
        // name and the shown name, ignoring case, spaces, underscores and symbols ("Draugr_Axe" = "Draugr Axe").
        internal static readonly string[] HiddenNames =
        {
            "DeerWhite", "CargoCrate", "Cargo", "Skeleton_Bow_Meadows", "KnifeVoid", "Draugr_Axe",
            // light sources that are not for decorating: the Wisp Torch piece and its item, the goblins' torches
            // ("GoblinTorch" also covers GoblinTorchDeepNorth) and the Elaking's attack lantern
            "piece_groundtorch_mist", "TorchMist", "GoblinTorch", "GoblinTorchDeepNorth", "Elaking_AttackLantern",
            "DvergerStaffFire", "DvergerStaffHeal", "DvergerStaffIce", "MushroomBlue", "piece_barber"
        };

        internal static string[] _hiddenTokens;

        /// <summary>
        /// True for the items that are never listed (see <see cref="HiddenNames"/>). Like test items they stay in the
        /// catalog, so an item that is already attached still resolves its name and refund.
        /// </summary>
        public static bool IsHiddenEntry(AttachEntry entry)
        {
            if (entry == null) return false;

            if (_hiddenTokens == null)
            {
                var tokens = new List<string>(HiddenNames.Length);
                foreach (var name in HiddenNames)
                {
                    var token = Normalize(name);
                    if (token.Length > 0) tokens.Add(token);
                }
                _hiddenTokens = tokens.ToArray();
            }

            var prefab = Normalize(entry.Prefab);
            var shown = Normalize(entry.DisplayName);
            foreach (var token in _hiddenTokens)
                if (prefab.Contains(token) || shown.Contains(token)) return true;
            return false;
        }

        internal static bool Contains(string text, string part)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void Invalidate()
        {
            _builtFor = null;
            _builtForScene = null;
            KyListed = 0;
            _entries = new List<AttachEntry>();
            _byName = new Dictionary<string, AttachEntry>(StringComparer.OrdinalIgnoreCase);
            PrefabCache.Clear();
            AttachCost.ClearCache();
        }

        /// <summary>Finds a prefab by name without needing the catalog (used when only showing saved attachments).</summary>
        public static GameObject ResolvePrefab(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (PrefabCache.TryGetValue(name, out var cached) && cached != null) return cached;

            GameObject found = null;
            var scene = ZNetScene.instance;
            if (scene != null) found = scene.GetPrefab(name);

            if (found == null && ObjectDB.instance != null && ObjectDB.instance.m_items != null)
            {
                foreach (var go in ObjectDB.instance.m_items)
                {
                    if (go != null && string.Equals(go.name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        found = go;
                        break;
                    }
                }
            }

            if (found != null) PrefabCache[name] = found;
            return found;
        }

        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// True when the scene is up and is the one the current list was built from, i.e. the list has the trader
        /// prices and mob drops. (Unity equality: a destroyed scene compares equal to null.)
        /// </summary>
        internal static bool SceneReady()
        {
            var scene = ZNetScene.instance;
            return scene != null && scene == _builtForScene;
        }

        internal static void EnsureBuilt()
        {
            var db = ObjectDB.instance;
            if (db == null || db.m_items == null || db.m_items.Count == 0) return;

            // Built for this item database, and (once a scene exists) from that scene's prefabs: nothing to do. While
            // there is no scene the list stays as it is; it is rebuilt the moment one appears.
            if (_builtFor == db && _byName.Count > 0 && (ZNetScene.instance == null || ZNetScene.instance == _builtForScene))
                return;

            _builtFor = db;
            _builtForScene = ZNetScene.instance;
            PrefabCache.Clear();
            AttachVisuals.ClearCache();
            AttachCost.ClearCache();
            Excluded.Clear();
            var entries = new List<AttachEntry>();
            var byName = new Dictionary<string, AttachEntry>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var prices = CollectTraderPrices(out var sellers);
                var recipes = CollectRecipes(db);
                var pieces = CollectBuildPieces(db, recipes);
                var mobDrops = CollectMobDrops();
                var rawFood = CollectRawFood(db, out var castItems);
                var produce = ProduceCatalog.Collect(db, Excluded);

                // --- items: trophies, trader items (and gems / valuables), ammo, food, mob drops, produce, weapons, armor
                foreach (var go in db.m_items)
                {
                    var entry = MakeItemEntry(go, prices, recipes, mobDrops, sellers, produce, rawFood, castItems);
                    if (entry == null || entry.Categories == AttachCategory.None) continue;
                    if (byName.ContainsKey(entry.Prefab)) continue;

                    entries.Add(entry);
                    byName[entry.Prefab] = entry;
                }

                // --- decorative pieces (hammer "Furniture" category, and every light source whatever its category)
                foreach (var info in pieces.Values)
                {
                    if (!info.Furniture && !info.Light) continue;
                    if (byName.ContainsKey(info.Object.name)) continue;
                    var entry = MakePieceEntry(info.Object, info.Listed);
                    if (entry == null) continue;

                    entries.Add(entry);
                    byName[entry.Prefab] = entry;
                }

                // --- plants and bushes (produce): not items and not hammer pieces
                foreach (var plant in produce.Plants)
                {
                    if (plant.Object == null || byName.ContainsKey(plant.Object.name)) continue;

                    var entry = MakePlantEntry(plant);
                    entries.Add(entry);
                    byName[entry.Prefab] = entry;
                }

                // --- extra prefabs from the config count as decorative
                AddExtras(entries, byName, prices, recipes, pieces);

                // --- the user's own items and pieces (other com.kydldev.* mods, the KY-Items build category): KY-Items + their normal category
                AddKyItems(entries, byName, prices, recipes, pieces);
            }
            catch (Exception e)
            {
                ModLog.Warn($"Building the attachment list failed: {e}");
            }

            foreach (var entry in entries)
            {
                entry.DisplayName = Localize(entry.SharedName, entry.Prefab);
                if (entry.IsPlant) entry.DisplayName += " (plant)";   // tells "Carrot (plant)" from the carrot item
            }
            entries.Sort((a, b) =>
            {
                var byText = string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
                return byText != 0 ? byText : string.Compare(a.Prefab, b.Prefab, StringComparison.OrdinalIgnoreCase);
            });

            // Listed: things with a recipe the player can follow, plus the items that match an inclusion rule. The
            // hidden items are never listed; test items only while the "Show Test Items" setting is on. Rows that look
            // identical (same name, same icon, same recipe) are listed once, but only among recipe-based rows: an item
            // that was asked for by rule (a trophy, a mob drop, ...) is always listed, because each prefab was wanted
            // by name. Everything stays in byName so an attachment saved earlier still finds its name and refund.
            var showTest = ModSettings.ShowTestItems != null && ModSettings.ShowTestItems.Value;
            var listed = new List<AttachEntry>();
            var seenRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!entry.Listed) continue;

                // the user's own items are never hidden by the name list (CargoToGo's crates contain "Cargo"): they were asked for by mod
                if ((entry.Categories & AttachCategory.KyItems) == 0 && IsHiddenEntry(entry))
                {
                    entry.Listed = false;
                    Excluded.Add($"{entry.Prefab}: hidden item, never listed");
                    continue;
                }

                if (!showTest && IsTestEntry(entry))
                {
                    entry.Listed = false;
                    Excluded.Add($"{entry.Prefab}: test item (listed only when the 'Show Test Items' setting is on)");
                    continue;
                }

                // Nothing to show means nothing to attach. Items that match an inclusion rule were checked when they
                // were collected; recipe-based weapons / armor, hammer pieces and extra prefabs were not, and would
                // otherwise sit in the list with an enabled Attach button that can only answer "no model".
                if (!AttachVisuals.GetModel(entry.Object).Valid)
                {
                    entry.Listed = false;
                    Excluded.Add($"{entry.Prefab}: no physical model to show");
                    continue;
                }

                if (!entry.Qualified && !seenRows.Add(RowSignature(entry)))
                {
                    entry.Listed = false;
                    continue;
                }
                listed.Add(entry);
            }
            entries = listed;

            // Two different prefabs can share one shown name (TrophyDraugr / TrophyDraugrFem both read "Draugr
            // Trophy"). Rule-based rows in such a group get their prefab name appended so they can be told apart.
            var byLabel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                entry.RowLabel = entry.DisplayName;
                byLabel[entry.DisplayName] = byLabel.TryGetValue(entry.DisplayName, out var n) ? n + 1 : 1;
            }
            foreach (var entry in entries)
                if (entry.Qualified && byLabel[entry.DisplayName] > 1)
                    entry.RowLabel = entry.DisplayName + " (" + entry.Prefab + ")";

            _entries = entries;
            _byName = byName;

            KyListed = 0;
            int trophies = 0, weapons = 0, armor = 0, trader = 0, decor = 0, ammo = 0, food = 0, drops = 0, produceCount = 0, plants = 0, prepared = 0;
            foreach (var item in entries)
            {
                if ((item.Categories & AttachCategory.Trophy) != 0) trophies++;
                if ((item.Categories & AttachCategory.Weapon) != 0) weapons++;
                if ((item.Categories & AttachCategory.Armor) != 0) armor++;
                if ((item.Categories & AttachCategory.Trader) != 0) trader++;
                if ((item.Categories & AttachCategory.Decor) != 0) decor++;
                if ((item.Categories & AttachCategory.Produce) != 0) produceCount++;
                if (item.IsPlant) plants++;
                if ((item.Categories & AttachCategory.Ammo) != 0) ammo++;
                if ((item.Categories & AttachCategory.Food) != 0) food++;
                if ((item.Categories & AttachCategory.Food) != 0 && !string.IsNullOrEmpty(item.Sources) &&
                    item.Sources.IndexOf("food preparation table", StringComparison.OrdinalIgnoreCase) >= 0) prepared++;
                if ((item.Categories & AttachCategory.Drop) != 0) drops++;
                if ((item.Categories & AttachCategory.KyItems) != 0) KyListed++;
            }
            if (KyListed > 0) ModLog.Info($"Attachment list: {KyListed} KY-Items from the user's other mods are listed (filter button shown)");
            else ModLog.Verbose("Attachment list: no KY-Items listed (no other com.kydldev.* mod with items is loaded) - filter button hidden");
            ModLog.Info(
                $"Attachment list: {entries.Count} entries ({trophies} trophies, {trader} trader items / valuables, " +
                $"{produceCount} produce incl. {plants} plants, " +
                $"{ammo} arrows / bolts, {food} food ({prepared} from the food preparation table), {drops} mob drops, {weapons} weapons, {armor} armor, {decor} decorative); " +
                $"{Excluded.Count} candidates left out");

            WriteDump(entries);
        }

        internal static string Localize(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            try
            {
                var text = Localization.instance.Localize(key);
                return string.IsNullOrEmpty(text) ? fallback : text;
            }
            catch { return fallback; }
        }
        /// <summary>What a row looks like to the player: its name, icon and recipe. Equal signatures are duplicates.</summary>
        internal static string RowSignature(AttachEntry entry)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(entry.DisplayName).Append('|');
            sb.Append(entry.Icon != null ? entry.Icon.GetInstanceID().ToString() : entry.Prefab).Append('|');

            var reqs = entry.Requirements;
            if (reqs != null)
            {
                foreach (var req in reqs)
                {
                    if (req == null || req.m_resItem == null || req.m_amount <= 0 || IgnoredIngredients.IsIgnored(req)) continue;
                    sb.Append(req.m_resItem.gameObject.name).Append('x').Append(req.m_amount).Append(',');
                }
            }
            return sb.ToString();
        }

        internal static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var sb = new System.Text.StringBuilder(text.Length);
            foreach (var c in text)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        internal static string Shorten(List<string> names, int max)
        {
            if (names.Count <= max) return string.Join(", ", names);
            return string.Join(", ", names.GetRange(0, max)) + $" +{names.Count - max} more";
        }
    }
}
