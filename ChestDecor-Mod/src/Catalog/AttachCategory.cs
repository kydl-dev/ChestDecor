using System;

namespace ChestDecor
{
    [Flags]
    public enum AttachCategory
    {
        None = 0,
        Trophy = 1,
        Weapon = 2,
        Armor = 4,
        /// <summary>
        /// Everything traders sell, plus the valuables: gems (prefab name contains "gem") and the items traders buy
        /// (amber, ruby, silver necklace, ancient coin, ...). There is no separate valuables category any more.
        /// </summary>
        Trader = 8,
        /// <summary>
        /// The hammer's Furniture pieces, every light source (torches, braziers, lanterns, sconces,
        /// the Dverger Circlet, ...; see <see cref="LightSources"/>) and the Extra Decorative Prefabs setting.
        /// </summary>
        Decor = 16,
        // 32 was the old "Valuable" category (now part of Trader); the number is left unused on purpose.
        /// <summary>Arrows and bolts the player can craft.</summary>
        Ammo = 64,
        /// <summary>
        /// Food (items that feed you), anything whose prefab name contains "meat", "mead" or "flour", and the
        /// uncooked food items (what cooking stations cook, bread dough and the other food preparation table items,
        /// and the ingredients of food recipes).
        /// </summary>
        Food = 128,
        /// <summary>Items that mobs really drop when they die (their drop tables, not the gear they carry).</summary>
        Drop = 256,
        /// <summary>
        /// Fruits, vegetables, grains and fibre plants (barley, flax, ...), mushrooms, all seeds, and the plants,
        /// bushes and small tree saplings they grow on (never a grown tree). See <see cref="ProduceCatalog"/>.
        /// </summary>
        Produce = 512,
        /// <summary>
        /// Items and pieces added by the user's other mods (BepInEx GUID "com.kydldev.*", found through Jotunn's ModRegistry), plus every
        /// piece in the shared "KY-Items" build category. They keep their normal categories too (user, 2026-10-26). The filter button is
        /// only shown while at least one of them is listed. See <see cref="KyItems"/>.
        /// </summary>
        KyItems = 1024
    }
}
