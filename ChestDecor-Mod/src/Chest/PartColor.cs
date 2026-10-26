using UnityEngine;

namespace ChestDecor
{
    /// <summary>How a chosen colour is applied to a part of the chest.</summary>
    public enum ColorMode
    {
        /// <summary>Multiply the colour with the part's existing texture (dark textures stay dark).</summary>
        Tint = 0,

        /// <summary>Repaint the part: keep the texture's shading/detail but replace its colour and darkness.</summary>
        Full = 1
    }

    /// <summary>The colour chosen for one part of the chest, plus how it is applied.</summary>
    public struct PartColor
    {
        public Color Rgb;
        public ColorMode Mode;

        public PartColor(Color rgb, ColorMode mode)
        {
            Rgb = new Color(rgb.r, rgb.g, rgb.b, 1f);
            Mode = mode;
        }
    }
}
