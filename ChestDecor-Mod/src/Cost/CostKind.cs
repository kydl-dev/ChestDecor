namespace ChestDecor
{
    /// <summary>What the player pays to attach an entry.</summary>
    public enum CostKind
    {
        /// <summary>The item's crafting recipe (weapons, armor, ...).</summary>
        Recipe,
        /// <summary>The piece's build cost (decorative furniture).</summary>
        Piece,
        /// <summary>The trader's price in coins (trader items that cannot be crafted).</summary>
        Coins,
        /// <summary>The item itself, taken from the inventory (trophies and other drop-only items).</summary>
        Item,
        /// <summary>
        /// A set amount of one other item (<see cref="AttachEntry.CostItem"/> x <see cref="AttachEntry.CostAmount"/>):
        /// the seed or fruit a plant is planted with, or one Wood when a plant has neither.
        /// </summary>
        Material
    }
}
