using UnityEngine;

namespace ChestDecor
{
    internal sealed class AttachPieceInfo
    {
        public GameObject Object;
        public bool Furniture;   // hammer "Furniture" category, not a container
        public bool Light;       // a light source (torch, brazier, lantern, ...), whatever hammer category it is in
        public bool Listed;      // the player can build it
    }
}
