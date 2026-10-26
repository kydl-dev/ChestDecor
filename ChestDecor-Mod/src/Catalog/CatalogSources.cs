using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Kydl.Common;
using UnityEngine;
using static ChestDecor.AttachCatalog;
using static ChestDecor.CatalogSources;
using static ChestDecor.CatalogEntries;

namespace ChestDecor
{
    /// <summary>
    /// Part of the attachment catalog (split from AttachCatalog, class-size rule): reads the game's data the catalog is built from -
    /// trader prices, mob drops, craftable recipes, build pieces, uncooked / food preparation items and casts.
    /// </summary>
    internal static class CatalogSources
    {
        // ------------------------------------------------------------------------------------------------
        // Collecting
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Item prefab name -> price (coins) for one unit, over every trader in the game (Haldor, Hildir, the Bog
        /// Witch, ...). <paramref name="sellers"/> gets the names of the traders that sell each item.
        /// Trade entries that are not physical items are skipped: entries without a prefab, and the Wider / Deeper
        /// Pockets inventory upgrades.
        /// </summary>
        internal static Dictionary<string, int> CollectTraderPrices(out Dictionary<string, List<string>> sellers)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            sellers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            var scene = ZNetScene.instance;
            if (scene == null || scene.m_prefabs == null) return result;

            foreach (var prefab in scene.m_prefabs)
            {
                if (prefab == null) continue;

                var trader = prefab.GetComponentInChildren<Trader>(true);
                if (trader == null || trader.m_items == null) continue;

                foreach (var item in trader.m_items)
                {
                    if (item == null) continue;

                    if (item.m_prefab == null)
                    {
                        Excluded.Add($"{prefab.name} sells '{item.m_name}': not an item (no prefab)");
                        continue;
                    }

                    var name = item.m_prefab.gameObject.name;
                    if (IsNonPhysicalTradeItem(item, name))
                    {
                        Excluded.Add($"{name} (sold by {prefab.name}): not a physical item (inventory upgrade)");
                        continue;
                    }

                    var unit = Mathf.Max(1, Mathf.CeilToInt(item.m_price / (float)Mathf.Max(1, item.m_stack)));
                    if (!result.TryGetValue(name, out var known) || unit < known) result[name] = unit;

                    if (!sellers.TryGetValue(name, out var who)) sellers[name] = who = new List<string>();
                    if (!who.Contains(prefab.name)) who.Add(prefab.name);
                }
            }
            return result;
        }

        internal static bool IsNonPhysicalTradeItem(Trader.TradeItem item, string prefabName)
        {
            if (Normalize(item.m_name).Contains(NonPhysicalWord)) return true;
            if (Normalize(prefabName).Contains(NonPhysicalWord)) return true;

            try
            {
                var shared = item.m_prefab.m_itemData != null ? item.m_prefab.m_itemData.m_shared : null;
                if (shared != null && Normalize(shared.m_name).Contains(NonPhysicalWord)) return true;
            }
            catch { /* item data not ready: the names above have to do */ }
            return false;
        }

        /// <summary>
        /// Item prefab name -> the mobs that drop it when they die. Only real drop-table entries count
        /// (CharacterDrop.m_drops with a chance above 0). What a mob only carries or attacks with (default items,
        /// random weapons / armor / shields, attack items, the FW_ / SP_ loadout copies) is not in a drop table, so
        /// it never shows up here.
        /// </summary>
        internal static Dictionary<string, List<string>> CollectMobDrops()
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            var scene = ZNetScene.instance;
            if (scene == null || scene.m_prefabs == null) return result;

            foreach (var prefab in scene.m_prefabs)
            {
                if (prefab == null) continue;

                var table = prefab.GetComponentInChildren<CharacterDrop>(true);
                if (table == null || table.m_drops == null) continue;

                foreach (var drop in table.m_drops)
                {
                    if (drop == null || drop.m_prefab == null) continue;
                    if (drop.m_chance <= 0f || drop.m_amountMax <= 0) continue;   // listed but can never drop
                    if (drop.m_prefab.GetComponent<ItemDrop>() == null) continue;   // not an item

                    var name = drop.m_prefab.name;
                    if (!result.TryGetValue(name, out var mobs)) result[name] = mobs = new List<string>();
                    if (!mobs.Contains(prefab.name)) mobs.Add(prefab.name);
                }
            }
            return result;
        }

        /// <summary>Item prefab name -> the recipe the player follows to craft it. Only craftable recipes are kept.</summary>
        internal static Dictionary<string, Recipe> CollectRecipes(ObjectDB db)
        {
            var result = new Dictionary<string, Recipe>(StringComparer.OrdinalIgnoreCase);
            if (db.m_recipes == null) return result;

            foreach (var r in db.m_recipes)
            {
                if (!IsCraftable(r)) continue;

                var name = r.m_item.gameObject.name;
                if (!result.ContainsKey(name)) result[name] = r;
            }
            return result;
        }

        /// <summary>
        /// A recipe the player can really follow: enabled, makes an item, has at least one real ingredient, the item
        /// shows in the crafting list (has an icon) and the station it needs is not a disabled piece.
        /// </summary>
        internal static bool IsCraftable(Recipe r)
        {
            if (r == null || !r.m_enabled || r.m_item == null) return false;
            if (!HasIngredients(r.m_resources)) return false;

            var shared = r.m_item.m_itemData != null ? r.m_item.m_itemData.m_shared : null;
            if (shared == null || shared.m_icons == null || shared.m_icons.Length == 0) return false;

            try
            {
                var station = r.GetRequiredStation(1);
                if (station != null)
                {
                    var stationPiece = station.GetComponent<Piece>();
                    if (stationPiece != null && !stationPiece.m_enabled) return false;
                }
            }
            catch { /* can't read the station: treat the recipe as craftable */ }

            return true;
        }

        internal static bool HasIngredients(Piece.Requirement[] requirements)
        {
            if (requirements == null) return false;
            foreach (var req in requirements)
                if (req != null && req.m_resItem != null && req.m_amount > 0 && !IgnoredIngredients.IsIgnored(req)) return true;
            return false;
        }

        /// <summary>
        /// Every piece found in a build table (hammer, hoe, ...). A piece is Listed when its table belongs to an item
        /// the player can craft, the piece is enabled and it has real build materials.
        /// </summary>
        internal static Dictionary<string, AttachPieceInfo> CollectBuildPieces(ObjectDB db, Dictionary<string, Recipe> recipes)
        {
            // a table counts as usable when at least one item that carries it has a craftable recipe
            var tables = new Dictionary<PieceTable, bool>();
            foreach (var go in db.m_items)
            {
                if (go == null) continue;
                var drop = go.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) continue;

                var table = drop.m_itemData.m_shared.m_buildPieces;
                if (table == null || table.m_pieces == null) continue;

                var usable = recipes.ContainsKey(go.name);
                tables[table] = tables.TryGetValue(table, out var before) ? before || usable : usable;
            }

            var result = new Dictionary<string, AttachPieceInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in tables)
            {
                foreach (var pieceGo in pair.Key.m_pieces)
                {
                    if (pieceGo == null) continue;

                    var piece = pieceGo.GetComponent<Piece>();
                    if (piece == null || !piece.m_enabled) continue;

                    var listed = pair.Value && HasIngredients(piece.m_resources);
                    var furniture = piece.m_category.ToString() == "Furniture" &&
                                    // chests, barrels, carts: attaching a container to a chest makes no sense
                                    pieceGo.GetComponentInChildren<Container>(true) == null;

                    // Light sources (torches, braziers, lanterns, sconces, fire pits, ...) are decor too, even when the
                    // hammer keeps them under another tab. Furniture is decor already, so it isn't checked again.
                    var light = !furniture && LightSources.IsLightPiece(pieceGo, Localize(piece.m_name, pieceGo.name));

                    if (result.TryGetValue(pieceGo.name, out var known))
                    {
                        known.Listed |= listed;
                        known.Furniture |= furniture;
                        known.Light |= light;
                    }
                    else
                    {
                        result[pieceGo.name] = new AttachPieceInfo { Object = pieceGo, Furniture = furniture, Light = light, Listed = listed };
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Item prefab name -> why it counts as an uncooked / food preparation item. Read from the game's data, so food
        /// added later or by mods is found by itself. An item counts when
        ///  * a cooking station cooks it (the "from" side of a cooking station conversion: raw meat, raw fish, bread
        ///    dough, ...), or
        ///  * it is made, or used, by an enabled recipe at the food preparation table (dough and the other prepared, not
        ///    yet cooked, food), whatever that recipe costs, or
        ///  * it is an ingredient of a food recipe (a craftable recipe whose result feeds you: honey, mushrooms,
        ///    carrots, ...), unless a cooking station makes it (cooked food is not uncooked).
        /// Items the player crafts by a recipe of their own are included: dough is uncooked food even though it is made.
        /// Flour has its own name rule (see <see cref="FoodWords"/>), so it doesn't depend on this.
        /// A cooking station that makes something that is not food (a casting foundry turning a cast into a weapon or
        /// armor) doesn't make its input food: those inputs come back in <paramref name="castItems"/> instead.
        /// </summary>
        internal static Dictionary<string, string> CollectRawFood(ObjectDB db, out Dictionary<string, CastInfo> castItems)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            castItems = new Dictionary<string, CastInfo>(StringComparer.OrdinalIgnoreCase);

            // what cooking stations make: cooked food is not an uncooked ingredient, even when a recipe uses it
            var cooked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var scene = ZNetScene.instance;
            if (scene != null && scene.m_prefabs != null)
            {
                foreach (var prefab in scene.m_prefabs)
                {
                    if (prefab == null) continue;

                    var station = prefab.GetComponentInChildren<CookingStation>(true);
                    if (station == null || station.m_conversion == null) continue;

                    foreach (var conversion in station.m_conversion)
                    {
                        if (conversion == null) continue;
                        if (conversion.m_to != null) cooked.Add(conversion.m_to.gameObject.name);
                        if (conversion.m_from == null) continue;

                        var fromName = conversion.m_from.gameObject.name;
                        var castCategory = CastCategory(conversion.m_to);
                        if (castCategory != AttachCategory.None)
                        {
                            // the station makes a weapon, armor or other non-food item: its input is a cast, not food
                            if (!castItems.ContainsKey(fromName))
                                castItems[fromName] = new CastInfo { Category = castCategory, Why = "cast at " + prefab.name };
                        }
                        else
                        {
                            AddRawFood(result, fromName, "cooked at " + prefab.name);
                        }
                    }
                }
            }

            var preparationRecipes = 0;
            if (db != null && db.m_recipes != null)
            {
                foreach (var recipe in db.m_recipes)
                {
                    // The food preparation table is checked first and on its own: every enabled recipe it offers counts,
                    // whatever the recipe costs, so nothing the table makes is lost because of the extra checks that
                    // IsCraftable does for weapons and armor (an icon, a non-ignored ingredient, an enabled station piece).
                    if (IsPreparationRecipe(recipe))
                    {
                        preparationRecipes++;
                        var madeName = recipe.m_item.gameObject.name;
                        AddRawFood(result, madeName, "made at the food preparation table");

                        // what the table uses is preparation food too (dough is not edible, so the food-recipe rule
                        // below would never reach its ingredients). Cooked food stays out, as below.
                        if (recipe.m_resources != null)
                        {
                            foreach (var req in recipe.m_resources)
                            {
                                if (req == null || req.m_resItem == null || req.m_amount <= 0) continue;

                                var ingredient = req.m_resItem.gameObject.name;
                                if (cooked.Contains(ingredient)) continue;
                                AddRawFood(result, ingredient, "ingredient of " + madeName + " at the food preparation table");
                            }
                        }
                    }

                    // IsCraftable has already made sure the result and its item data exist
                    if (!IsCraftable(recipe)) continue;

                    if (!HasFoodStats(recipe.m_item.m_itemData.m_shared)) continue;

                    foreach (var req in recipe.m_resources)
                    {
                        if (req == null || req.m_resItem == null || req.m_amount <= 0) continue;

                        var name = req.m_resItem.gameObject.name;
                        if (cooked.Contains(name)) continue;
                        AddRawFood(result, name, "ingredient of " + recipe.m_item.gameObject.name);
                    }
                }
            }

            // for the catalog dump: tells "the table makes nothing" from "the table was not recognised"
            if (db != null && preparationRecipes == 0)
                Excluded.Add("food preparation table: no enabled recipe found at a crafting station whose prefab or shown name contains 'prep'");
            return result;
        }

        /// <summary>A cast item: what a cooking station "cooks" into something that is not food, and where it is listed.</summary>
        internal sealed class CastInfo
        {
            public AttachCategory Category;
            public string Why;
        }

        /// <summary>
        /// Where the input of a cooking station conversion is listed when the conversion makes <paramref name="result"/>.
        /// None when it makes food (the input is uncooked food, as before, or the result is unknown). Armor when the
        /// result is armor, otherwise Weapons (a weapon, or another non-food item such as a key).
        /// </summary>
        internal static AttachCategory CastCategory(ItemDrop result)
        {
            if (result == null) return AttachCategory.None;

            var shared = result.m_itemData != null ? result.m_itemData.m_shared : null;
            if (shared == null) return AttachCategory.None;
            if (IsFood(result.gameObject.name, shared)) return AttachCategory.None;

            return ArmorTypes.Contains(shared.m_itemType.ToString()) ? AttachCategory.Armor : AttachCategory.Weapon;
        }

        internal static void AddRawFood(Dictionary<string, string> result, string name, string why)
        {
            if (string.IsNullOrEmpty(name) || result.ContainsKey(name)) return;
            result[name] = why;
        }

        /// <summary>
        /// An enabled recipe made at the food preparation table whose result is a real item. Nothing else is asked of it
        /// (no icon, ingredient or station-piece check): everything the table offers counts as preparation food.
        /// </summary>
        internal static bool IsPreparationRecipe(Recipe recipe)
        {
            if (recipe == null || !recipe.m_enabled || recipe.m_item == null) return false;
            if (recipe.m_item.m_itemData == null || recipe.m_item.m_itemData.m_shared == null) return false;
            return IsPreparationTable(recipe.m_craftingStation);
        }

        /// <summary>The food preparation table (prefab name or shown name contains "prep").</summary>
        internal static bool IsPreparationTable(CraftingStation station)
        {
            if (station == null) return false;
            return Normalize(station.gameObject.name).Contains("prep") || Normalize(station.m_name).Contains("prep");
        }
    }
}
