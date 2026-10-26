using Kydl.Common;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// Works out what the Produce filter holds.
    ///
    /// Items: fruits, vegetables, grains and fibre plants (barley, flax, oats), mushrooms, and all seeds (crop seeds,
    /// and the tree seeds / cones / acorns that saplings are planted with).
    /// The vanilla names are listed below, and every plant the Cultivator can grow adds what it yields and what it is
    /// planted with, so new or modded crops are picked up by themselves. Not produce: the Ancient Seed (a boss item),
    /// herbs, honey, royal jelly.
    ///
    /// Plants, bushes and saplings (never a full-grown tree):
    ///  * every plant the Cultivator can plant that grows into something that can be picked. It is shown as the grown
    ///    plant. Planting cost: the seed or fruit the game asks for when planting it.
    ///  * every sapling of a tree (when the "Include Tree Saplings" setting is on). Only the small sapling is shown,
    ///    never the tree it grows into. Planting cost: the seed, cone or acorn the game asks for.
    ///  * every wild bush (a prefab whose name contains "bush" or "shrub", such as the berry bushes) and every other
    ///    wild pickable that gives produce (fiddlehead, ashvine, ...). Planting cost: the fruit it gives; a bush
    ///    that gives no seed or fruit costs 1 Wood.
    ///  * decorative bushes that have nothing to pick (plain scenery) are listed too, for 1 Wood.
    ///  * wild mushrooms are not listed as plants (the mushroom item is the same thing to look at); the ones the
    ///    Cultivator plants are.
    /// </summary>
    internal static class ProduceCatalog
    {
        // Prefab names of the vanilla produce items. A name the game doesn't have is simply never found.
        private static readonly string[] KnownItems =
        {
            // fruits
            "Raspberry", "Blueberries", "Cloudberry", "Lingonberry", "Vineberry",
            // vegetables, grains and fibre plants
            "Carrot", "Turnip", "Onion", "Kale", "Poteitr", "Fiddleheadfern", "Barley", "Flax", "Oat",
            // mushrooms
            "Mushroom", "MushroomYellow", "MushroomBlue", "MushroomSmokePuff", "MushroomJotunPuffs", "MushroomMagecap"
        };

        // Crop seeds, and tree seeds / cones / acorns. All of them are also found by themselves (they are what the
        // saplings are planted with); listed so they are found even when the sapling can't be read.
        private static readonly string[] KnownSeeds =
        {
            "CarrotSeeds", "TurnipSeeds", "OnionSeeds", "KaleSeeds", "PoteitrSeeds", "OatSeeds",
            "VineberrySeeds", "VineGreenSeeds",
            "BeechSeeds", "BirchSeeds", "FirCone", "FirConeFrost", "PineCone", "Acorn"
        };

        private sealed class Sapling
        {
            public GameObject Prefab;
            public Plant Plant;
            public Piece Piece;
            public bool Tree;            // grows into a tree: only the sapling itself is ever shown
            public GameObject Grown;     // not a tree: the plant that grows (null for a tree)
            public Pickable Pick;        // not a tree: what can be picked from it (null for a tree)
            public GameObject CostItem;
            public int CostAmount;
        }

        public static ProduceInfo Collect(ObjectDB db, List<string> excluded)
        {
            var info = new ProduceInfo();
            foreach (var name in KnownItems) info.Items.Add(name);
            foreach (var name in KnownSeeds) info.Seeds.Add(name);

            var scene = ZNetScene.instance;
            if (scene == null || scene.m_prefabs == null) return info;

            var wood = db != null ? db.GetItemPrefab("Wood") : null;

            // --- 1. what the Cultivator can plant (a tree is never shown grown: only its sapling, if at all)
            var includeTreeSaplings = ModSettings.IncludeTreeSaplings == null || ModSettings.IncludeTreeSaplings.Value;
            var saplings = new List<Sapling>();
            foreach (var prefab in scene.m_prefabs)
            {
                if (prefab == null) continue;

                var plant = prefab.GetComponent<Plant>();
                if (plant == null) continue;

                var piece = prefab.GetComponent<Piece>();
                var cost = FirstRequirement(piece, out var amount);

                GameObject grown = null;
                Pickable pick = null;
                var tree = false;
                if (plant.m_grownPrefabs != null)
                {
                    foreach (var candidate in plant.m_grownPrefabs)
                    {
                        if (candidate == null) continue;
                        if (candidate.GetComponentInChildren<TreeBase>(true) != null) { tree = true; continue; }

                        var candidatePick = candidate.GetComponentInChildren<Pickable>(true);
                        if (candidatePick != null && grown == null)
                        {
                            grown = candidate;
                            pick = candidatePick;
                        }
                    }
                }

                if (tree)
                {
                    // what a tree sapling is planted with (a tree seed, cone or acorn) is a seed like any other
                    if (cost != null) info.Seeds.Add(cost.name);

                    if (!includeTreeSaplings)
                    {
                        excluded.Add($"{prefab.name}: sapling of a tree (the 'Include Tree Saplings' setting is off)");
                        continue;
                    }

                    // only the sapling itself is ever shown, never the tree it grows into
                    saplings.Add(new Sapling { Prefab = prefab, Plant = plant, Piece = piece, Tree = true, CostItem = cost, CostAmount = amount });
                    continue;
                }

                if (grown == null)
                {
                    excluded.Add($"{prefab.name}: nothing that can be picked grows from it (not produce)");
                    continue;
                }

                saplings.Add(new Sapling { Prefab = prefab, Plant = plant, Piece = piece, Grown = grown, Pick = pick, CostItem = cost, CostAmount = amount });
            }

            // what the plants give is produce (or a seed, for the seed plants)
            foreach (var sapling in saplings)
                if (sapling.Pick != null && sapling.Pick.m_itemPrefab != null)
                    info.Items.Add(sapling.Pick.m_itemPrefab.name);

            // what they are planted with: a fruit or vegetable (already known as produce), otherwise a seed
            foreach (var sapling in saplings)
                if (sapling.CostItem != null && !info.Items.Contains(sapling.CostItem.name))
                    info.Seeds.Add(sapling.CostItem.name);

            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sapling in saplings)
            {
                var plantCost = sapling.CostItem;
                var plantAmount = sapling.CostAmount;
                string note;
                if (plantCost != null)
                {
                    note = "planted with " + plantAmount + "x " + plantCost.name;
                }
                else
                {
                    plantCost = wood;   // nothing to plant it with: 1 Wood
                    plantAmount = 1;
                    note = "no seed or fruit to plant it with: 1 Wood";
                }

                // the grown plant looks best; the sapling only when the grown plant has no model to show. A tree is
                // never shown grown: its sapling is all there is.
                var shown = !sapling.Tree && AttachVisuals.GetModel(sapling.Grown).Valid ? sapling.Grown : sapling.Prefab;
                if (!AttachVisuals.GetModel(shown).Valid)
                {
                    excluded.Add($"{sapling.Prefab.name}: no physical model to show (plant)");
                    continue;
                }

                var harvest = sapling.Pick != null ? sapling.Pick.m_itemPrefab : null;   // none for a tree sapling
                var name = !string.IsNullOrEmpty(sapling.Piece != null ? sapling.Piece.m_name : null)
                    ? sapling.Piece.m_name
                    : !string.IsNullOrEmpty(sapling.Plant.m_name) ? sapling.Plant.m_name : ItemNameOf(harvest) ?? shown.name;

                Sprite icon = sapling.Piece != null ? sapling.Piece.m_icon : null;
                if (icon == null) icon = IconOf(harvest);
                if (icon == null) icon = IconOf(plantCost);

                AddPlant(info, taken, excluded, shown, name, icon, plantCost, plantAmount,
                    (sapling.Tree ? "tree sapling " : "sapling ") + sapling.Prefab.name + "; " + note);
            }

            // --- 2. wild bushes (with or without anything to pick) and other wild pickables that give produce
            foreach (var prefab in scene.m_prefabs)
            {
                if (prefab == null || taken.Contains(prefab.name)) continue;
                if (prefab.GetComponent<Plant>() != null) continue;

                var isBush = Contains(prefab.name, "bush") || Contains(prefab.name, "shrub");
                var pick = prefab.GetComponent<Pickable>();
                if (pick == null && isBush) pick = prefab.GetComponentInChildren<Pickable>(true);   // a bush may keep its pickable on a child
                if (pick == null && !isBush) continue;
                if (prefab.GetComponentInChildren<TreeBase>(true) != null) continue;

                // A decorative bush: nothing can be picked from it. It is scenery, shown as it stands, and costs 1 Wood.
                // (Items and creatures that merely have "bush" in their name are not plants.)
                if (pick == null)
                {
                    if (prefab.GetComponent<ItemDrop>() != null || prefab.GetComponent<Character>() != null) continue;

                    if (wood == null)
                    {
                        excluded.Add($"{prefab.name}: no Wood to use as the cost (decorative bush)");
                        continue;
                    }

                    if (!AttachVisuals.GetModel(prefab).Valid)
                    {
                        excluded.Add($"{prefab.name}: no physical model to show (decorative bush)");
                        continue;
                    }

                    AddPlant(info, taken, excluded, prefab, prefab.name.Replace('_', ' '), IconOf(wood), wood, 1,
                        "decorative bush, nothing to pick: 1 Wood");
                    continue;
                }

                var harvest = pick.m_itemPrefab;
                var harvestName = harvest != null ? harvest.name : null;
                var gives = harvestName != null &&
                            (info.Items.Contains(harvestName) || info.Seeds.Contains(harvestName));

                // wild mushrooms lie on the ground and look like the item itself: they are items, not plants
                if (gives && !isBush && Contains(harvestName, "mushroom")) continue;

                if (!gives && !isBush) continue;

                GameObject plantCost;
                var plantAmount = 1;
                string note;
                if (gives || (harvest != null && IsFood(harvest)))
                {
                    plantCost = harvest;   // the fruit it gives
                    note = "wild; planting cost is its fruit, " + harvestName;
                }
                else
                {
                    plantCost = wood;    // a bush that gives no seed or fruit
                    note = "wild bush without a seed or fruit: 1 Wood";
                }

                if (plantCost == null)
                {
                    excluded.Add($"{prefab.name}: no seed, fruit or Wood to use as the cost (plant)");
                    continue;
                }

                if (!AttachVisuals.GetModel(prefab).Valid)
                {
                    excluded.Add($"{prefab.name}: no physical model to show (plant)");
                    continue;
                }

                var name = ItemNameOf(harvest) ?? prefab.name;
                var icon = IconOf(harvest) ?? IconOf(plantCost);
                AddPlant(info, taken, excluded, prefab, name, icon, plantCost, plantAmount, note);
            }

            return info;
        }

        private static void AddPlant(ProduceInfo info, HashSet<string> taken, List<string> excluded, GameObject shown,
            string sharedName, Sprite icon, GameObject costItem, int costAmount, string source)
        {
            if (shown == null || costItem == null || costAmount <= 0) return;
            if (!taken.Add(shown.name)) return;   // the same prefab is never listed twice

            info.Plants.Add(new PlantInfo
            {
                Object = shown,
                SharedName = sharedName,
                Icon = icon,
                CostItem = costItem,
                CostAmount = costAmount,
                Source = source
            });
        }

        /// <summary>The first real ingredient of a piece (what the game asks for when planting a sapling).</summary>
        private static GameObject FirstRequirement(Piece piece, out int amount)
        {
            amount = 0;
            if (piece == null || piece.m_resources == null) return null;

            foreach (var req in piece.m_resources)
            {
                if (req == null || req.m_resItem == null || req.m_amount <= 0) continue;
                if (IgnoredIngredients.IsIgnored(req)) continue;

                amount = req.m_amount;
                return req.m_resItem.gameObject;
            }
            return null;
        }

        private static string ItemNameOf(GameObject item)
        {
            if (item == null) return null;
            var drop = item.GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) return null;
            var key = drop.m_itemData.m_shared.m_name;
            return string.IsNullOrEmpty(key) ? null : key;
        }

        private static Sprite IconOf(GameObject item)
        {
            if (item == null) return null;
            var drop = item.GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) return null;
            var icons = drop.m_itemData.m_shared.m_icons;
            return icons != null && icons.Length > 0 ? icons[0] : null;
        }

        /// <summary>Food: a consumable that feeds the player.</summary>
        private static bool IsFood(GameObject item)
        {
            var drop = item.GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) return false;

            var shared = drop.m_itemData.m_shared;
            return shared.m_itemType.ToString() == "Consumable" &&
                   (shared.m_food > 0f || shared.m_foodStamina > 0f || shared.m_foodEitr > 0f);
        }

        private static bool Contains(string text, string part)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
