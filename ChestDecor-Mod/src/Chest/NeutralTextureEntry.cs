using UnityEngine;

namespace ChestDecor
{
    internal sealed class NeutralTextureEntry
    {
        public Texture2D Texture;

        /// <summary>
        /// Multiply the chosen colour by this when applying it, so that the average pixel of the neutral
        /// texture times the colour equals the colour the player picked.
        /// </summary>
        public float Gain;
    }
}
