using System;
using FsCheck;
using UnityEngine;

namespace ChestDecor.Tests.Unit
{
    /// <summary>
    /// The orbit camera's maths (CameraOrbit; user, 2026-10-26): it circles the chest's middle; the zoom is the distance from the chest's BOX,
    /// with a hard limit of 1 m ("I should hit a hard limit 1 m before it so I can't go into it"). The walls / floor checks are physics
    /// (FreeCamera) and tested in game (CD-10).
    /// </summary>
    [TestFixture]
    public class CameraOrbitTests
    {
        const string T = "ChestDecor.CameraOrbit";
        static Vector3 Offset(float yaw, float pitch) { return (Vector3)ModAssembly.Call(T, "Offset", yaw, pitch); }
        static float YawOf(Vector3 o) { return (float)ModAssembly.Call(T, "YawOf", o); }
        static float PitchOf(Vector3 o) { return (float)ModAssembly.Call(T, "PitchOf", o); }
        static float ClampDistance(float d, float max) { return (float)ModAssembly.Call(T, "ClampDistance", d, max); }
        static float BoxDistance(Bounds b, Vector3 p) { return (float)ModAssembly.Call(T, "BoxDistance", b, p); }
        static float ReachFor(Bounds b, Vector3 dir, float gap) { return (float)ModAssembly.Call(T, "ReachFor", b, dir, gap); }
        static float C(string name) { return (float)ModAssembly.Type(T).GetField(name).GetValue(null); }

        static float AngleDiff(float a, float b) { var d = (a - b) % 360f; if (d > 180f) d -= 360f; if (d < -180f) d += 360f; return Math.Abs(d); }

        /// The chest of the in-game tests (1.84 x 1.09 x 0.97 m).
        static readonly Bounds Chest = new Bounds(new Vector3(0f, 0.545f, 0f), new Vector3(1.84f, 1.09f, 0.97f));

        [Test]
        public void Offset_YawZeroPitchZero_IsBehindTheChestOnMinusZ_LookingAlongPlusZ()
        {
            var o = Offset(0f, 0f);
            Assert.That(o.z, Is.EqualTo(-1f).Within(1e-5f));
            Assert.That(o.x, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(o.y, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void Offset_PositivePitch_IsAboveTheChest()
        {
            Assert.That(Offset(0f, 45f).y, Is.GreaterThan(0.7f));
            Assert.That(Offset(0f, -45f).y, Is.LessThan(-0.7f));
        }

        [Test]
        public void ZoomLimits_HardMinimumIsOneMetre_FurthestIsTheSetting()
        {
            Assert.That(C("MinDistance"), Is.EqualTo(1f), "user: a hard limit 1 m before the chest");
            Assert.That(ClampDistance(0.3f, 5f), Is.EqualTo(1f));
            Assert.That(ClampDistance(7f, 5f), Is.EqualTo(5f));
            Assert.That(ClampDistance(3f, 5f), Is.EqualTo(3f));
            Assert.That(ClampDistance(3f, 0.5f), Is.EqualTo(1f), "a maximum below 1 m still keeps 1 m");
        }

        [Test]
        public void BoxDistance_InsideIsZero_OutsideIsToTheNearestSideEdgeOrCorner()
        {
            var box = new Bounds(Vector3.zero, Vector3.one * 2f);   // -1 .. 1
            Assert.That(BoxDistance(box, Vector3.zero), Is.EqualTo(0f));
            Assert.That(BoxDistance(box, new Vector3(3f, 0f, 0f)), Is.EqualTo(2f).Within(1e-5f), "to a side");
            Assert.That(BoxDistance(box, new Vector3(4f, 5f, 0f)), Is.EqualTo(5f).Within(1e-5f), "to an edge: 3-4-5");
            Assert.That(BoxDistance(box, new Vector3(-2f, -2f, -2f)), Is.EqualTo((float)Math.Sqrt(3)).Within(1e-5f), "to a corner");
        }

        /// The in-game chest: 1 m from its long side is 0.485 + 1 from its middle, 1 m above its lid is 0.545 + 1.
        [Test]
        public void ReachFor_TheTestChest_IsHalfTheChestPlusTheZoom()
        {
            Assert.That(ReachFor(Chest, new Vector3(0, 0, -1), 1f), Is.EqualTo(0.485f + 1f).Within(0.001f));
            Assert.That(ReachFor(Chest, Vector3.up, 1f), Is.EqualTo(0.545f + 1f).Within(0.001f));
            Assert.That(ReachFor(Chest, Vector3.right, 2.5f), Is.EqualTo(0.92f + 2.5f).Within(0.001f));
        }

        [Test]
        public void PitchOf_StraightDownAndUp_StaysInsideThePitchLimits()
        {
            Assert.That(PitchOf(new Vector3(0, 5, 0)), Is.EqualTo(C("MaxPitch")));
            Assert.That(PitchOf(new Vector3(0, -5, 0)), Is.EqualTo(C("MinPitch")), "never from far under the floor");
            Assert.That(PitchOf(Vector3.zero), Is.EqualTo(0f));
        }

        // ---- properties ----------------------------------------------------------------------------------------------------------

        static readonly Gen<float> Yaw = Gen.Choose(-36000, 36000).Select(i => i / 100f);
        static readonly Gen<float> Pitch = Gen.Choose(-6000, 8500).Select(i => i / 100f);

        /// Offset is a unit vector and YawOf / PitchOf give back the angles: taking over from the game camera does not jump.
        [Test]
        public void Property_OffsetAndYawPitchOf_AreInverse()
        {
            Props.Check(Prop.ForAll(Yaw.ToArbitrary(), Pitch.ToArbitrary(), (yaw, pitch) =>
            {
                var o = Offset(yaw, pitch) * 3.7f;
                return Math.Abs(o.magnitude - 3.7f) < 1e-3f && Math.Abs(PitchOf(o) - pitch) < 0.05f && (Math.Abs(pitch) > 89f || AngleDiff(YawOf(o), yaw) < 0.05f);
            }));
        }

        /// THE HARD LIMIT: for any chest, any direction and any zoom the camera ends up exactly `zoom` (so never less than 1 m) from the
        /// chest's box - never inside it, never closer, at every side, edge and corner.
        [Test]
        public void Property_CameraIsAlwaysExactlyTheZoomAwayFromTheChestsBox()
        {
            var gen = from size in Gen.ArrayOf(3, Gen.Choose(100, 3000).Select(i => i / 1000f))
                      from yaw in Yaw
                      from pitch in Pitch
                      from zoomCm in Gen.Choose(100, 1000)
                      select new { Box = new Bounds(new Vector3(0f, size[1] * 0.5f, 0f), new Vector3(size[0], size[1], size[2])), Yaw = yaw, Pitch = pitch, Zoom = ClampDistance(zoomCm / 100f, 10f) };
            Props.Check(Prop.ForAll(gen.ToArbitrary(), c =>
            {
                var dir = Offset(c.Yaw, c.Pitch);
                var camera = c.Box.center + dir * ReachFor(c.Box, dir, c.Zoom);
                var d = BoxDistance(c.Box, camera);
                return Math.Abs(d - c.Zoom) < 0.002f && d >= C("MinDistance") - 0.002f;
            }), 500);
        }
    }
}
