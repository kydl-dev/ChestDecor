using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// Where an attached item may be, in the chest's local space.
    ///
    /// The position of an item (what the Left / right, Up / down and Front / back sliders set) is the origin of its
    /// model, but the limits are measured from the item's own <b>edges</b>, not from that origin. For this the
    /// functions take <c>item</c>: the bounds of the item's real geometry around its position, in the chest's axes
    /// (so it already includes the item's rotation and size, see <c>ChestDecorator.ExtentOf</c>).
    ///
    ///   * Outwards: at the end of a slider the item's nearest edge is exactly <see cref="Margin"/> (10 cm) away from
    ///     the chest, on every side, however the item is turned or sized. Nothing of it is inside the chest there.
    ///   * Inwards: the item's position may not sink deeper than <see cref="Margin"/> (10 cm) below the chest
    ///     surface, measured from the nearest side. An item can slide along a face, but can't be pushed deep into
    ///     the chest.
    /// </summary>
    internal static class PlacementLimits
    {
        /// <summary>Metres an item's edge may be away from the chest, and metres its position may sink into it.</summary>
        public const float Margin = 0.10f;

        private const float Eps = 0.0005f;

        /// <summary>
        /// The position (on one axis) at which the item's edge on the chest's side <paramref name="side"/>
        /// (0 = low side, 1 = high side) is <paramref name="distance"/> away from the chest surface. A positive
        /// distance is clear of the chest, 0 touches it, a negative distance sinks the item's edge into the chest.
        /// </summary>
        public static float EdgeAt(Bounds chest, Bounds item, int axis, int side, float distance)
        {
            return side == 0
                ? chest.min[axis] - distance - item.max[axis]   // the item's top edge ends up 'distance' below the chest's low side
                : chest.max[axis] + distance - item.min[axis];  // the item's bottom edge ends up 'distance' above the chest's high side
        }

        /// <summary>Lowest allowed position on an axis: the item's edge is <see cref="Margin"/> clear of the chest's low side.</summary>
        public static float Low(Bounds chest, Bounds item, int axis) => EdgeAt(chest, item, axis, 0, Margin);

        /// <summary>Highest allowed position on an axis: the item's edge is <see cref="Margin"/> clear of the chest's high side.</summary>
        public static float High(Bounds chest, Bounds item, int axis) => EdgeAt(chest, item, axis, 1, Margin);

        /// <summary>True when the position is deeper than <see cref="Margin"/> inside the chest.</summary>
        public static bool TooDeep(Vector3 p, Bounds chest)
        {
            for (int k = 0; k < 3; k++)
            {
                var innerLow = chest.min[k] + Margin;
                var innerHigh = chest.max[k] - Margin;

                if (innerLow >= innerHigh) return false;                          // chest too thin here: nothing is "deep"
                if (p[k] <= innerLow + Eps || p[k] >= innerHigh - Eps) return false; // within 10 cm of a side on this axis
            }
            return true;
        }

        /// <summary>
        /// Only keeps every axis inside its range (<see cref="Low"/> .. <see cref="High"/>). Used after a rotation or
        /// size change, which moves the range but must not move an item that is still inside it.
        /// </summary>
        public static Vector3 ClampToRange(Vector3 p, Bounds chest, Bounds item)
        {
            for (int k = 0; k < 3; k++)
                p[k] = Mathf.Clamp(p[k], Low(chest, item, k), High(chest, item, k));
            return p;
        }

        /// <summary>
        /// Limits a position after the slider of <paramref name="changedAxis"/> was moved. Every axis is kept inside
        /// its range; if the move would sink the item too deep, that axis stops at the edge of the allowed
        /// depth on the side it came from (<paramref name="previous"/> is where the item was before the move).
        /// </summary>
        public static Vector3 Clamp(Vector3 p, Bounds chest, Bounds item, int changedAxis, Vector3 previous)
        {
            p = ClampToRange(p, chest, item);

            if (!TooDeep(p, chest)) return p;

            var a = changedAxis;
            var innerLow = chest.min[a] + Margin;
            var innerHigh = chest.max[a] - Margin;

            float target;
            if (previous[a] <= innerLow) target = innerLow;
            else if (previous[a] >= innerHigh) target = innerHigh;
            else target = (p[a] - innerLow < innerHigh - p[a]) ? innerLow : innerHigh;

            p[a] = target;
            return p;
        }

        /// <summary>Limits a position that was not produced by a slider (a default spot): pushes it out the shortest way.</summary>
        public static Vector3 Clamp(Vector3 p, Bounds chest, Bounds item)
        {
            p = ClampToRange(p, chest, item);

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
                if (toLow < bestDistance) { bestDistance = toLow; bestAxis = k; bestTarget = innerLow; }
                if (toHigh < bestDistance) { bestDistance = toHigh; bestAxis = k; bestTarget = innerHigh; }
            }

            p[bestAxis] = bestTarget;
            return p;
        }
    }
}
