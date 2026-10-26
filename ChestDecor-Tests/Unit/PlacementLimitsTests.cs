using System;
using FsCheck;
using UnityEngine;

namespace ChestDecor.Tests.Unit
{
    /// <summary>
    /// Where an attached item may be (PlacementLimits; user, 2026-10-26 after the spear test): everything is measured from the item's MIDDLE.
    /// The middle may be at most 10 cm outside the chest's box and at most 10 cm below its surface, the same at every rotation and size.
    /// Bounds / Vector3 / Mathf are plain managed Unity code.
    /// </summary>
    [TestFixture]
    public class PlacementLimitsTests
    {
        const string T = "ChestDecor.PlacementLimits";
        const float Tol = 1e-4f;
        static float Margin { get { return (float)ModAssembly.Type(T).GetField("Margin").GetValue(null); } }

        static float Low(Bounds chest, int axis) { return (float)ModAssembly.Call(T, "Low", chest, axis); }
        static float High(Bounds chest, int axis) { return (float)ModAssembly.Call(T, "High", chest, axis); }
        static bool TooDeep(Vector3 p, Bounds chest) { return (bool)ModAssembly.Call(T, "TooDeep", p, chest); }
        static bool Allowed(Vector3 p, Bounds chest) { return (bool)ModAssembly.Call(T, "Allowed", p, chest); }
        static Vector3 ClampToRange(Vector3 p, Bounds chest) { return (Vector3)ModAssembly.Call(T, "ClampToRange", p, chest); }
        static Vector3 Clamp(Vector3 p, Bounds chest) { return (Vector3)ModAssembly.Call(T, "Clamp", p, chest); }
        static Vector3 Clamp(Vector3 p, Bounds chest, int axis, Vector3 previous) { return (Vector3)ModAssembly.Call(T, "Clamp", p, chest, axis, previous); }

        sealed class Case
        {
            public Bounds Chest;
            public Vector3 P, Previous;
            public int Axis;
            public override string ToString() { return "chest " + Chest.ToString("F3") + " p " + P.ToString("F3") + " prev " + Previous.ToString("F3") + " axis " + Axis; }
        }

        static Gen<Vector3> V(Gen<float> g) { return Gen.ArrayOf(3, g).Select(a => new Vector3(a[0], a[1], a[2])); }

        /// Chests from tiny to big; positions half anywhere around the chest, half INSIDE it (else the depth rule is hardly ever reached -
        /// the first version of these properties used only "anywhere" and Stryker showed 26 changes to the depth code that no test noticed).
        static Gen<Case> Cases()
        {
            Gen<float> fraction = Gen.Choose(-500, 500).Select(i => i / 1000f);
            return from chestCenter in V(Props.GameFloat(1f))
                   from chestSize in V(Gen.Choose(50, 3000).Select(i => i / 1000f))
                   from inside in V(fraction)
                   from anywhere in V(Props.GameFloat(4f))
                   from pickInside in Arb.Generate<bool>()
                   from prevFraction in V(fraction.Select(f => f * 1.6f))
                   from axis in Gen.Choose(0, 2)
                   let p = pickInside ? chestCenter + Vector3.Scale(inside, chestSize) : anywhere
                   select new Case { Chest = new Bounds(chestCenter, chestSize), P = p, Previous = chestCenter + Vector3.Scale(prevFraction, chestSize), Axis = axis };
        }

        static float InnerLow(Bounds chest, int k) { return chest.min[k] + Margin; }
        static float InnerHigh(Bounds chest, int k) { return chest.max[k] - Margin; }

        static bool InRange(Vector3 p, Bounds chest)
        {
            for (int k = 0; k < 3; k++)
                if (p[k] < chest.min[k] - Margin - Tol || p[k] > chest.max[k] + Margin + Tol) return false;
            return true;
        }

        // ---- examples ------------------------------------------------------------------------------------------------------------

        [Test]
        public void LowAndHigh_AreTenCentimetresOutsideTheChest_ForTheItemsMiddle()
        {
            var chest = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(1.84f, 1.09f, 0.97f));    // the chest of the in-game test

            Assert.That(High(chest, 1), Is.EqualTo(chest.max.y + 0.1f).Within(Tol), "a standing spear's middle at most 10 cm above the lid");
            Assert.That(Low(chest, 0), Is.EqualTo(chest.min.x - 0.1f).Within(Tol));
        }

        [Test]
        public void TooDeep_CentreOfABigChest_IsTooDeep_NearASide_IsNot()
        {
            var chest = new Bounds(Vector3.zero, Vector3.one);

            Assert.That(TooDeep(Vector3.zero, chest), Is.True);
            Assert.That(TooDeep(new Vector3(0.4f, 0, 0), chest), Is.False, "within 10 cm of the +x side");
        }

        [Test]
        public void TooDeep_ChestThinnerThanTwoMargins_IsNeverTooDeep()
        {
            Assert.That(TooDeep(Vector3.zero, new Bounds(Vector3.zero, new Vector3(1f, 0.15f, 1f))), Is.False);
        }

        [Test]
        public void TooDeep_ExactlyAtTheDepthLimitPlusTheHalfMillimetreSlack_IsNotTooDeep()
        {
            var chest = new Bounds(Vector3.zero, Vector3.one);
            float eps = 0.0005f;
            float atLow = chest.min.x + Margin + eps, atHigh = chest.max.x - Margin - eps;

            Assert.That(TooDeep(new Vector3(atLow, 0, 0), chest), Is.False, "on the limit counts as 'within 10 cm of the side'");
            Assert.That(TooDeep(new Vector3(atHigh, 0, 0), chest), Is.False);
            Assert.That(TooDeep(new Vector3(atLow + 0.001f, 0, 0), chest), Is.True);
        }

        [Test]
        public void Clamp_SliderMovedIntoTheChest_StopsAtTheDepthLimitOnTheSideItCameFrom()
        {
            var chest = new Bounds(Vector3.zero, Vector3.one);

            var p = Clamp(new Vector3(0.1f, 0, 0), chest, 0, new Vector3(0.45f, 0, 0));

            Assert.That(p.x, Is.EqualTo(0.5f - Margin).Within(Tol));
        }

        /// The everyday case after one clamp: the item already sits ON the depth limit and the slider pushes it further in. It must stay
        /// on that side, even when the new spot is nearer the other side (it may not jump through the chest).
        [Test]
        public void Clamp_SliderFromExactlyOnTheDepthLimit_StaysOnThatSide()
        {
            var chest = new Bounds(Vector3.zero, Vector3.one);
            float lo = chest.min.x + Margin, hi = chest.max.x - Margin;

            Assert.That(Clamp(new Vector3(0.3f, 0, 0), chest, 0, new Vector3(lo, 0, 0)).x, Is.EqualTo(lo), "came from the low limit: stays there");
            Assert.That(Clamp(new Vector3(-0.3f, 0, 0), chest, 0, new Vector3(hi, 0, 0)).x, Is.EqualTo(hi));
        }

        /// The in-game spear (CD-08, 2026-10-26): before, its slider reached 0.99 m above a 1.09 m chest because the limit used its edges.
        [Test]
        public void Clamp_SpearFarAboveTheChest_StopsTenCentimetresAboveTheLid()
        {
            var chest = new Bounds(new Vector3(0, 0.545f, 0), new Vector3(1.84f, 1.09f, 0.97f));

            var p = Clamp(new Vector3(0, 2.0f, 0), chest, 1, new Vector3(0, 1.0f, 0));

            Assert.That(p.y, Is.EqualTo(chest.max.y + 0.1f).Within(Tol));
        }

        [Test]
        public void Allowed_InTheShell_Yes_TooFarOrTooDeep_No()
        {
            var chest = new Bounds(Vector3.zero, Vector3.one);

            Assert.That(Allowed(new Vector3(0, 0.55f, 0), chest), Is.True, "5 cm above the lid");
            Assert.That(Allowed(new Vector3(0, 0.45f, 0), chest), Is.True, "5 cm into the lid");
            Assert.That(Allowed(new Vector3(0, 0.65f, 0), chest), Is.False, "15 cm above");
            Assert.That(Allowed(new Vector3(0, 0.3f, 0), chest), Is.False, "20 cm deep");
        }

        // ---- properties ----------------------------------------------------------------------------------------------------------

        /// The user's rule: the middle always ends up within 10 cm of the chest surface, outside or inside - whatever is asked.
        [Test]
        public void Property_EveryClamp_EndsInTheShellAroundTheChest()
        {
            Props.Check(Prop.ForAll(Cases().ToArbitrary(), c =>
            {
                var slider = Clamp(c.P, c.Chest, c.Axis, c.Previous);
                var spot = Clamp(c.P, c.Chest);
                return InRange(slider, c.Chest) && !TooDeep(slider, c.Chest) && InRange(spot, c.Chest) && !TooDeep(spot, c.Chest)
                       && Allowed(spot, c.Chest);
            }));
        }

        [Test]
        public void Property_ClampToRange_IsInsideTheRangeAndKeepsAPositionAlreadyInside()
        {
            Props.Check(Prop.ForAll(Cases().ToArbitrary(), c =>
            {
                var once = ClampToRange(c.P, c.Chest);
                bool unchangedWhenInside = !InRange(c.P, c.Chest) || once == c.P;
                return InRange(once, c.Chest) && ClampToRange(once, c.Chest) == once && unchangedWhenInside;
            }));
        }

        /// A position that is already allowed is never moved (opening the Position tab must not shift an item that is fine).
        [Test]
        public void Property_AllowedPosition_IsLeftAlone()
        {
            Props.Check(Prop.ForAll(Cases().ToArbitrary(), c =>
                !Allowed(c.P, c.Chest) || (Clamp(c.P, c.Chest) == c.P && Clamp(c.P, c.Chest, c.Axis, c.Previous) == c.P)));
        }

        /// Documented: a spot that is too deep is "pushed out the shortest way" - one axis moves, onto the depth limit, and no other
        /// axis / side would have been a shorter move.
        [Test]
        public void Property_ClampForADefaultSpot_PushesOutTheShortestWay()
        {
            Props.Check(Prop.ForAll(Cases().ToArbitrary(), c =>
            {
                var ranged = ClampToRange(c.P, c.Chest);
                var r = Clamp(c.P, c.Chest);
                if (!TooDeep(ranged, c.Chest)) return r == ranged;
                int moved = -1; float shortest = float.MaxValue;
                for (int k = 0; k < 3; k++)
                {
                    shortest = Math.Min(shortest, Math.Min(ranged[k] - InnerLow(c.Chest, k), InnerHigh(c.Chest, k) - ranged[k]));
                    if (Math.Abs(r[k] - ranged[k]) > Tol) { if (moved >= 0) return false; moved = k; }
                }
                if (moved < 0) return false;
                bool onTheLimit = Math.Abs(r[moved] - InnerLow(c.Chest, moved)) <= Tol || Math.Abs(r[moved] - InnerHigh(c.Chest, moved)) <= Tol;
                return onTheLimit && Math.Abs(Math.Abs(r[moved] - ranged[moved]) - shortest) <= Tol;
            }));
        }

        /// Documented: after a slider move that would sink the middle too deep, only that slider's axis stops - at the depth limit on the
        /// side the item came from (the nearer side when it was already inside the limits); the other axes only get the range clamp.
        [Test]
        public void Property_ClampAfterASlider_StopsTheMovedAxisOnTheSideItCameFrom()
        {
            Props.Check(Prop.ForAll(Cases().ToArbitrary(), c =>
            {
                var ranged = ClampToRange(c.P, c.Chest);
                var r = Clamp(c.P, c.Chest, c.Axis, c.Previous);
                if (!TooDeep(ranged, c.Chest)) return r == ranged;
                for (int k = 0; k < 3; k++)
                    if (k != c.Axis && Math.Abs(r[k] - ranged[k]) > Tol) return false;
                int a = c.Axis;
                float lo = InnerLow(c.Chest, a), hi = InnerHigh(c.Chest, a);
                float want = c.Previous[a] <= lo ? lo : c.Previous[a] >= hi ? hi : (ranged[a] - lo < hi - ranged[a] ? lo : hi);
                return Math.Abs(r[a] - want) <= Tol && !TooDeep(r, c.Chest);
            }));
        }
    }
}
