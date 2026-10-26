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
    /// Part of the attachment catalog (split from AttachCatalog, class-size rule): turns a prefab into an AttachEntry - the inclusion rules
    /// (trophy, trader, ammo, food, light, produce, drop), item / piece / plant entries, the Extra Decorative Prefabs and the KY-Items.
    /// </summary>
    internal static class CatalogEntries
    {
        internal static AttachEntry MakeItemEntry(GameObject go, Dictionary<string, int> prices, Dictionary<string, Recipe> recipes,
            Dictionary<string, List<string>> mobDrops, Dictionary<string, List<string>> sellers, ProduceInfo produce,
            Dictionary<string, string> rawFood, Dictionary<string, CastInfo> castItems)
        {
            if (go == null) return null;

            var drop = go.GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) return null;

            var shared = drop.m_itemData.m_shared;
            var cats = AttachCategory.None;

            // Weapons and armor are listed only when the player can craft them (see BuildItemEntry).
            var type = shared.m_itemType.ToString();
            if (WeaponTypes.Contains(type)) cats |= AttachCategory.Weapon;
            else if (ArmorTypes.Contains(type)) cats |= AttachCategory.Armor;

            // The inclusion rules: items that are listed whether or not they have a recipe.
            var rules = AttachCategory.None;
            var sources = new List<string>();

            if(Contains(go.name, "carrion")) rules |= AttachCategory.Trophy;
            if (Contains(go.name, "trophy")) rules |= AttachCategory.Trophy;

            // gems and the items traders buy are valuables, which are listed under Trader together with what traders sell
            if (Contains(go.name, "gem") || IsTraderValuable(go, shared))
            {
                rules |= AttachCategory.Trader;
                sources.Add("valuable");
            }

            var price = 0;
            if (prices.TryGetValue(go.name, out price))
            {
                rules |= AttachCategory.Trader;
                if (sellers.TryGetValue(go.name, out var who)) sources.Add("sold by " + string.Join("/", who));
            }

            if (IsCraftableArrowOrBolt(go.name, shared, recipes)) rules |= AttachCategory.Ammo;

            // food: it feeds you, has a food word in its name (meat, mead, flour), or is an uncooked base food item
            // A trophy is never food, even when a recipe (fishing bait) uses it as an ingredient.
            string rawFoodWhy = null;
            var isTrophy = (rules & AttachCategory.Trophy) != 0;

            // A cast (a mold or blank that a cooking station "cooks" into a weapon, armor or other non-food item) is
            // listed under Armor or Weapons, never under Food.
            CastInfo castInfo = null;
            var isCast = castItems != null && castItems.TryGetValue(go.name, out castInfo);
            if (isCast)
            {
                rules |= castInfo.Category;
                sources.Add("cast item (" + castInfo.Why + ")");
            }

            var notFood = isTrophy || isCast;
            var uncookedBase = !notFood && rawFood != null && rawFood.TryGetValue(go.name, out rawFoodWhy);
            if (!notFood && (uncookedBase || IsFood(go.name, shared))) rules |= AttachCategory.Food;
            if (uncookedBase) sources.Add("uncooked base food (" + rawFoodWhy + ")");

            // every light source is decor (torches, lanterns, the Dverger Circlet, ...); the ones in HiddenNames are never listed
            if (LightSources.IsLightItem(go, shared, Localize(shared.m_name, "")))
            {
                rules |= AttachCategory.Decor;
                sources.Add("light source");
            }

            if (produce != null && produce.IsProduce(go.name, shared)) rules |= AttachCategory.Produce;

            if (mobDrops.TryGetValue(go.name, out var mobs))
            {
                rules |= AttachCategory.Drop;
                sources.Add("dropped by " + Shorten(mobs, 8));
            }

            // Something that can't be shown (no mesh) can't be attached: left out of every rule.
            var qualified = false;
            if (rules != AttachCategory.None)
            {
                if (AttachVisuals.GetModel(go).Valid) qualified = true;
                else
                {
                    Excluded.Add($"{go.name}: no physical model to show ({rules})");
                    rules = AttachCategory.None;
                    sources.Clear();
                }
            }

            cats |= rules;
            if (cats == AttachCategory.None) return null;

            var entry = BuildItemEntry(go, drop, cats, price, recipes, qualified);
            entry.Sources = string.Join("; ", sources);
            return entry;
        }

        internal static AttachEntry BuildItemEntry(GameObject go, ItemDrop drop, AttachCategory cats, int price,
            Dictionary<string, Recipe> recipes, bool qualified)
        {
            var shared = drop.m_itemData.m_shared;
            var entry = new AttachEntry
            {
                Prefab = go.name,
                Object = go,
                Categories = cats,
                SharedName = shared.m_name,
                Icon = shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null,
                IsPiece = false,
                CoinPrice = price,
                Qualified = qualified
            };

            recipes.TryGetValue(go.name, out var recipe);

            // A recipe that makes several items at once (20 arrows) would make one attachment cost the whole batch,
            // so for rule-based entries the cost is then the item itself (or coins) instead.
            var batch = qualified && recipe != null && recipe.m_amount > 1;

            if (recipe != null && !batch)
            {
                entry.Recipe = recipe;
                entry.Cost = CostKind.Recipe;
                entry.Listed = true;   // the player can craft it
            }
            else if (price > 0)
            {
                entry.Cost = CostKind.Coins;
            }
            else
            {
                entry.Cost = CostKind.Item;
            }

            if (qualified) entry.Listed = true;
            return entry;
        }

        // ------------------------------------------------------------------------------------------------
        // Inclusion rules
        // ------------------------------------------------------------------------------------------------

        /// <summary>One of the wiki's "List of valuables": matched on the prefab name or the shown name, completely.</summary>
        internal static bool IsTraderValuable(GameObject go, ItemDrop.ItemData.SharedData shared)
        {
            if (ValuableNames.Contains(Normalize(go.name))) return true;
            return ValuableNames.Contains(Normalize(Localize(shared.m_name, "")));
        }

        /// <summary>An arrow or bolt the player can craft (an enabled recipe at a station that can be built).</summary>
        internal static bool IsCraftableArrowOrBolt(string name, ItemDrop.ItemData.SharedData shared, Dictionary<string, Recipe> recipes)
        {
            // "Ammo" is what bows and crossbows load. Turret bolts and catapult payloads are AmmoNonEquipable.
            if (shared.m_itemType.ToString() != "Ammo") return false;
            if (!recipes.ContainsKey(name)) return false;

            var ammoType = shared.m_ammoType ?? "";
            if (Contains(ammoType, "arrow") || Contains(ammoType, "bolt")) return true;
            return name.StartsWith("Arrow", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Bolt", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Food (it feeds the player) or a prefab whose name contains one of the <see cref="FoodWords"/>.</summary>
        internal static bool IsFood(string name, ItemDrop.ItemData.SharedData shared)
        {
            // "Meadows" in a name (Skeleton_Bow_Meadows) is the biome, not the "mead" drink
            var text = Regex.Replace(name, "meadows", "", RegexOptions.IgnoreCase);
            foreach (var word in FoodWords)
                if (Contains(text, word)) return true;

            return HasFoodStats(shared);
        }

        /// <summary>A consumable that feeds the player (food, stamina or eitr value above 0).</summary>
        internal static bool HasFoodStats(ItemDrop.ItemData.SharedData shared)
        {
            return shared != null && shared.m_itemType.ToString() == "Consumable" &&
                   (shared.m_food > 0f || shared.m_foodStamina > 0f || shared.m_foodEitr > 0f);
        }

        /// <summary>A plant or bush from the produce rules: shown from its own prefab, paid with a seed, a fruit or Wood.</summary>
        internal static AttachEntry MakePlantEntry(PlantInfo plant)
        {
            return new AttachEntry
            {
                Prefab = plant.Object.name,
                Object = plant.Object,
                Categories = AttachCategory.Produce,
                SharedName = plant.SharedName,
                Icon = plant.Icon,
                IsPiece = false,
                IsPlant = true,
                Cost = CostKind.Material,
                CostItem = plant.CostItem,
                CostAmount = plant.CostAmount,
                Qualified = true,   // listed by rule, never merged with look-alike rows
                Listed = true,
                Sources = plant.Source
            };
        }

        internal static AttachEntry MakePieceEntry(GameObject go, bool listed)
        {
            var piece = go.GetComponent<Piece>();
            if (piece == null) return null;

            return new AttachEntry
            {
                Prefab = go.name,
                Object = go,
                Categories = AttachCategory.Decor,
                SharedName = piece.m_name,
                Icon = piece.m_icon,
                IsPiece = true,
                Piece = piece,
                Cost = CostKind.Piece,
                Listed = listed
            };
        }

        internal static void AddExtras(List<AttachEntry> entries, Dictionary<string, AttachEntry> byName,
            Dictionary<string, int> prices, Dictionary<string, Recipe> recipes, Dictionary<string, AttachPieceInfo> pieces)
        {
            var text = ModSettings.ExtraDecorPrefabs.Value;
            if (string.IsNullOrWhiteSpace(text)) return;

            foreach (var raw in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = raw.Trim();
                if (name.Length == 0) continue;

                if (byName.TryGetValue(name, out var existing))
                {
                    existing.Categories |= AttachCategory.Decor;
                    continue;
                }

                // a piece that is not furniture (so not collected above) but is in a build table
                if (pieces.TryGetValue(name, out var info))
                {
                    var pieceEntry = MakePieceEntry(info.Object, info.Listed);
                    if (pieceEntry != null)
                    {
                        entries.Add(pieceEntry);
                        byName[pieceEntry.Prefab] = pieceEntry;
                        continue;
                    }
                }

                var go = ResolvePrefab(name);
                if (go == null)
                {
                    ModLog.Warn($"Extra decor prefab '{name}' was not found.");
                    continue;
                }

                AttachEntry entry = null;
                var drop = go.GetComponent<ItemDrop>();
                if (drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null)
                {
                    prices.TryGetValue(go.name, out var price);
                    entry = BuildItemEntry(go, drop, AttachCategory.Decor, price, recipes, false);
                }
                else if (go.GetComponent<Piece>() != null)
                {
                    entry = MakePieceEntry(go, false);   // not in any build table: the player can't build it
                }

                if (entry == null)
                {
                    ModLog.Warn($"Extra decor prefab '{name}' is neither an item nor a piece, ignored.");
                    continue;
                }

                entries.Add(entry);
                byName[entry.Prefab] = entry;
            }
        }

        /// <summary>
        /// The user's own items and pieces (see <see cref="KyItems"/>): an entry that is already in the list (it matched a normal rule) gets the
        /// KyItems flag on top; any other one is added with KyItems as its category, listed by rule (an item costs its recipe, or the item
        /// itself; a piece its build cost). Expected outcome: returns how many entries carry the KyItems flag afterwards.
        /// </summary>
        internal static int AddKyItems(List<AttachEntry> entries, Dictionary<string, AttachEntry> byName,
            Dictionary<string, int> prices, Dictionary<string, Recipe> recipes, Dictionary<string, AttachPieceInfo> pieces)
        {
            var piecePrefabs = new List<GameObject>();
            foreach (var info in pieces.Values) if (info.Object != null) piecePrefabs.Add(info.Object);
            var found = KyItems.Collect(piecePrefabs);
            int tagged = 0, added = 0;
            foreach (var pair in found)
            {
                var go = pair.Value.Prefab;
                if (byName.TryGetValue(go.name, out var existing))
                {
                    existing.Categories |= AttachCategory.KyItems;
                    existing.Qualified = true;          // asked for by mod: always listed, never merged with a look-alike row
                    if (existing.IsPiece && pieces.TryGetValue(go.name, out var known)) existing.Listed |= known.Listed;
                    else if (!existing.IsPiece) existing.Listed = true;
                    existing.Sources = Join(existing.Sources, "KY-Items (" + pair.Value.Mod + ")");
                    tagged++;
                    continue;
                }

                if (!AttachVisuals.GetModel(go).Valid)
                {
                    Excluded.Add($"{go.name}: KY-Items from {pair.Value.Mod}, but no physical model to show");
                    continue;
                }

                AttachEntry entry = null;
                var drop = go.GetComponent<ItemDrop>();
                if (drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null)
                {
                    prices.TryGetValue(go.name, out var price);
                    entry = BuildItemEntry(go, drop, AttachCategory.KyItems, price, recipes, true);
                }
                else if (go.GetComponent<Piece>() != null)
                {
                    var listed = pieces.TryGetValue(go.name, out var info) ? info.Listed : HasIngredients(go.GetComponent<Piece>().m_resources);
                    entry = MakePieceEntry(go, listed);
                    if (entry != null) { entry.Categories = AttachCategory.KyItems; entry.Qualified = true; }
                }
                if (entry == null)
                {
                    Excluded.Add($"{go.name}: KY-Items from {pair.Value.Mod}, but neither an item nor a piece");
                    continue;
                }
                entry.Sources = "KY-Items (" + pair.Value.Mod + ")";
                entries.Add(entry);
                byName[entry.Prefab] = entry;
                added++;
            }
            ModLog.Verbose($"CatalogEntries: KY-Items - {tagged} already listed entr(ies) tagged, {added} added, {found.Count - tagged - added} left out");
            return tagged + added;
        }

        static string Join(string a, string b) { return string.IsNullOrEmpty(a) ? b : a + "; " + b; }
    }
}
