using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// Where an attached item may be, in the chest's local space. Everything is measured from the item's MIDDLE (its position: the
    /// visible model is centred on it, see AttachVisuals.Build), never from its edges, so turning or resizing an item never changes
    /// how far it may go (user, 2026-10-26, after the in-game test with a spear):
    ///
    ///   * Outwards: the middle may be at most <see cref="Margin"/> (10 cm) outside the chest's box, on every side. That is the end of
    ///     the Left / right, Up / down and Front / back sliders, and it is the same at every rotation and size.
    ///   * Inwards: the middle may sink at most <see cref="Margin"/> (10 cm) below the chest surface, measured from the nearest side.
    ///     A flat item (a spear lying down) is then nearly all outside; a standing one can be half inside, like a spike.
    /// So the middle always stays in a 20 cm shell around the chest surface.
    /// (Before 2026-10-26 the outward limit used the item's rotated edges: a standing spear could go 1 m+ away and every turn changed it.)
    /// </summary>
    internal static class PlacementLimits
    {
        /// <summary>Metres the item's middle may be outside the chest, and metres it may sink into it.</summary>
        public const float Margin = 0.10f;

        private const float Eps = 0.0005f;

        /// <summary>Lowest allowed position (the item's middle) on an axis: 10 cm outside the chest's low side.</summary>
        public static float Low(Bounds chest, int axis) { return chest.min[axis] - Margin; }

        /// <summary>Highest allowed position (the item's middle) on an axis: 10 cm outside the chest's high side.</summary>
        public static float High(Bounds chest, int axis) { return chest.max[axis] + Margin; }

        /// <summary>True when the position is deeper than <see cref="Margin"/> inside the chest.</summary>
        public static bool TooDeep(Vector3 p, Bounds chest)
        {
            for (int k = 0; k < 3; k++)
            {
                var innerLow = chest.min[k] + Margin;
                var innerHigh = chest.max[k] - Margin;

                // Stryker disable once Equality : at innerLow == innerHigh the next line returns false anyway
                if (innerLow >= innerHigh) return false;                          // chest too thin here: nothing is "deep"
                if (p[k] <= innerLow + Eps || p[k] >= innerHigh - Eps) return false; // within 10 cm of a side on this axis
            }
            return true;
        }

        /// <summary>Only keeps every axis inside its range (<see cref="Low"/> .. <see cref="High"/>).</summary>
        public static Vector3 ClampToRange(Vector3 p, Bounds chest)
        {
            for (int k = 0; k < 3; k++)
                p[k] = Mathf.Clamp(p[k], Low(chest, k), High(chest, k));
            return p;
        }

        /// <summary>True when the position breaks neither rule (inside the range, not too deep).</summary>
        public static bool Allowed(Vector3 p, Bounds chest) { return ClampToRange(p, chest) == p && !TooDeep(p, chest); }

        /// <summary>
        /// Limits a position after the slider of <paramref name="changedAxis"/> was moved. Every axis is kept inside its range; if the
        /// move would sink the middle too deep, that axis stops at the allowed depth on the side it came from (<paramref name="previous"/>
        /// is where the item was before the move).
        /// </summary>
        public static Vector3 Clamp(Vector3 p, Bounds chest, int changedAxis, Vector3 previous)
        {
            p = ClampToRange(p, chest);

            if (!TooDeep(p, chest)) return p;

            var a = changedAxis;
            var innerLow = chest.min[a] + Margin;
            var innerHigh = chest.max[a] - Margin;

            float target;
            if (previous[a] <= innerLow) target = innerLow;
            else if (previous[a] >= innerHigh) target = innerHigh;
            // Stryker disable once Equality : only an exact tie (item in the very middle) is decided differently, either side is right
            else target = (p[a] - innerLow < innerHigh - p[a]) ? innerLow : innerHigh;

            p[a] = target;
            return p;
        }

        /// <summary>Limits a position that was not produced by a slider (a default spot, an old save): pushes it out the shortest way.</summary>
        public static Vector3 Clamp(Vector3 p, Bounds chest)
        {
            p = ClampToRange(p, chest);

            if (!TooDeep(p, chest)) return p;

            var bestAxis = 0;
            var bestTarget = 0f;
            var bestDistance = float.MaxValue;
            for (int k = 0; k < 3; k++)
            {
                var innerLow = chest.min[k] + Margin;
                var innerHigh = chest.max[k] - Margin;

                var toLow = p[k] - innerLow;
                var toHigh = innerHigh - p[k];
                // Stryker disable Equality : < vs <= only decide exact ties between equally short ways out
                if (toLow < bestDistance) { bestDistance = toLow; bestAxis = k; bestTarget = innerLow; }
                // Stryker restore Equality
                // Stryker disable once Equality : as above
                if (toHigh < bestDistance) { bestDistance = toHigh; bestAxis = k; bestTarget = innerHigh; }
            }

            p[bestAxis] = bestTarget;
            return p;
        }
    }
}
