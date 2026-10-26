using UnityEngine;

namespace ChestDecor
{
    /// <summary>One thing that can be attached to a chest.</summary>
    public sealed class AttachEntry
    {
        public string Prefab;
        public GameObject Object;
        public AttachCategory Categories;
        public string SharedName;      // localisation key of the item / piece
        public string DisplayName;     // localised
        public string RowLabel;        // what the list shows: DisplayName, plus the prefab when two rows share a name
        public string Sources;         // where it comes from (traders / mobs), for the catalog dump only
        public Sprite Icon;

        public bool IsPiece;
        public Piece Piece;
        public Recipe Recipe;
        public int CoinPrice;
        public CostKind Cost;

        /// <summary>For <see cref="CostKind.Material"/>: the item that is taken, and how many.</summary>
        public GameObject CostItem;
        public int CostAmount;

        /// <summary>True for a plant or bush (Produce), shown as "Name (plant)". It is not an item and not a hammer piece.</summary>
        public bool IsPlant;

        /// <summary>
        /// True when the entry is shown in the Attach tab: the player can craft it (an enabled recipe at a station
        /// that can be built) or build it (a hammer piece). Unlisted entries stay in the catalog only so an item
        /// that is already attached to a chest still resolves its name and refund.
        /// </summary>
        public bool Listed;

        /// <summary>
        /// True when the entry is here because it matches one of the inclusion rules (trophy, gem / valuable, sold by a
        /// trader, craftable arrow / bolt, food, mob drop, produce, light source) rather than because it has a recipe. Such entries are
        /// listed even without a recipe, and are never merged with look-alike rows.
        /// </summary>
        public bool Qualified;

        public Piece.Requirement[] Requirements
        {
            get
            {
                // without the ingredients that are only needed for upgrades (see IgnoredIngredients)
                if (IsPiece) return IgnoredIngredients.Filter(Piece != null ? Piece.m_resources : null);
                return IgnoredIngredients.Filter(Recipe != null ? Recipe.m_resources : null);
            }
        }
    }
}
