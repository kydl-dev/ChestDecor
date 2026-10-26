using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// The maths of the orbit camera (user, 2026-10-26: "make the directional keys always rotate around the chest ... self centre around the
    /// chest from the distance set by zoom, minimum 1 m"; after the 2nd test: "hit a hard limit 1 m before the chest so I can't go into it").
    /// Pure: no engine calls (unit tested); FreeCamera does the input and the physics.
    /// Angles in degrees: yaw = compass direction the camera LOOKS (0 = +z), pitch = how far it looks DOWN (positive = camera above the chest).
    /// The zoom is the camera's distance from the chest's BOX (its nearest side / edge / corner), never from its middle.
    /// </summary>
    internal static class CameraOrbit
    {
        /// <summary>Hard limit: the camera is never closer than this to the chest's box, whatever else happens.</summary>
        public const float MinDistance = 1f;
        /// <summary>Looking up at the chest from below the floor shows nothing; looking straight down is allowed up to here.</summary>
        public const float MinPitch = -60f;
        public const float MaxPitch = 85f;
        /// <summary>Radius around the camera that must be free of walls, ceilings and floors (else the near clip plane shows through them).</summary>
        public const float Clearance = 0.3f;

        /// <summary>Unit vector from the orbit centre to the camera. Expected: length 1, pointing opposite to where the camera looks.</summary>
        public static Vector3 Offset(float yaw, float pitch)
        {
            float y = yaw * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad, cp = Mathf.Cos(p);
            return new Vector3(-Mathf.Sin(y) * cp, Mathf.Sin(p), -Mathf.Cos(y) * cp);
        }

        /// <summary>Yaw of a camera at <paramref name="offset"/> from the centre, looking at it (the inverse of <see cref="Offset"/>).</summary>
        public static float YawOf(Vector3 offset) { return Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg; }

        /// <summary>Pitch of a camera at <paramref name="offset"/> from the centre, looking at it, inside the allowed pitch.</summary>
        public static float PitchOf(Vector3 offset)
        {
            var len = offset.magnitude;
            // Stryker disable once Equality : < vs <= differ only at a length of exactly 1e-5 (both give 0 there)
            return len < 1e-5f ? 0f : ClampPitch(Mathf.Asin(Mathf.Clamp(offset.y / len, -1f, 1f)) * Mathf.Rad2Deg);
        }

        public static float ClampPitch(float pitch) { return Mathf.Clamp(pitch, MinPitch, MaxPitch); }

        /// <summary>The zoom (distance from the chest's box) kept between <see cref="MinDistance"/> and the configured maximum.</summary>
        public static float ClampDistance(float distance, float max) { return Mathf.Clamp(distance, MinDistance, Mathf.Max(MinDistance, max)); }

        /// <summary>Distance from a point to a box (0 inside it): to its nearest side, edge or corner.</summary>
        public static float BoxDistance(Bounds box, Vector3 p)
        {
            float sum = 0f;
            for (int k = 0; k < 3; k++)
            {
                float d = Mathf.Max(Mathf.Max(box.min[k] - p[k], 0f), p[k] - box.max[k]);
                sum += d * d;
            }
            return Mathf.Sqrt(sum);
        }

        /// <summary>
        /// How far from the box's middle the camera must be, along the unit direction <paramref name="dir"/>, to be exactly
        /// <paramref name="gap"/> from the box. The box distance only grows along such a ray, so a bisection finds it.
        /// Expected outcome: BoxDistance(centre + dir * result) == gap (within a millimetre).
        /// </summary>
        public static float ReachFor(Bounds box, Vector3 dir, float gap)
        {
            float lo = 0f, hi = box.extents.magnitude + gap + 0.01f;
            // Stryker disable once Equality : one bisection step more or less is below float precision here
            for (int i = 0; i < 32; i++)
            {
                float mid = (lo + hi) * 0.5f;
                // Stryker disable once all : a box is symmetric about its middle (so + / - dir give the same distance) and '<' vs '<=' only differ at exactly 'gap'
                if (BoxDistance(box, box.center + dir * mid) < gap) lo = mid; else hi = mid;
            }
            return hi;
        }
    }
}
