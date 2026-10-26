using UnityEngine;

namespace ChestDecor
{
    /// <summary>One plant or bush that can be attached to a chest: what is shown, and what "planting" it costs.</summary>
    internal sealed class PlantInfo
    {
        public GameObject Object;      // the prefab whose model is shown (the grown plant, or the sapling when that has no model)
        public string SharedName;      // translation key (or plain text) of its name
        public Sprite Icon;
        public GameObject CostItem;    // the seed or fruit it is planted with, or Wood when it has neither
        public int CostAmount;
        public string Source;          // where it comes from, for the catalog dump only
    }
}
