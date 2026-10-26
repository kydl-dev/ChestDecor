using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>What <see cref="ProduceCatalog"/> found: the produce items, the seeds, and the plants / bushes.</summary>
    internal sealed class ProduceInfo
    {
        /// <summary>Prefab names of fruits, vegetables, grains, fibre plants and mushrooms (and the harvest of seed plants).</summary>
        public readonly HashSet<string> Items = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Prefab names of seeds: the seeds of crops, and what tree saplings are planted with (tree seeds, cones, acorns).
        /// A seed is a seed whatever it grows into; only the grown trees themselves are never listed.
        /// </summary>
        public readonly HashSet<string> Seeds = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The plants, bushes and (small) tree saplings. Never a full-grown tree.</summary>
        public readonly List<PlantInfo> Plants = new();

        // Named "seed" but not something that is planted: the boss summoning item.
        private static readonly HashSet<string> NotProduce = new(StringComparer.OrdinalIgnoreCase) { "AncientSeed" };

        /// <summary>True when this item belongs in the Produce filter: a fruit, vegetable, mushroom, grain, fibre plant or seed.</summary>
        public bool IsProduce(string prefabName, ItemDrop.ItemData.SharedData shared)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;
            if (NotProduce.Contains(prefabName)) return false;
            if (Items.Contains(prefabName) || Seeds.Contains(prefabName)) return true;

            // any other seed the game (or a mod) adds: a plain material whose name says seed. This leaves out
            // things like the Ancient Seed, which is a boss item, not something that is planted.
            return shared != null &&
                   shared.m_itemType.ToString() == "Material" &&
                   prefabName.IndexOf("seed", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
