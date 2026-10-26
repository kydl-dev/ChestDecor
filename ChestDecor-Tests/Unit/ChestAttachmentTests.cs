using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FsCheck;
using UnityEngine;

namespace ChestDecor.Tests.Unit
{
    /// <summary>
    /// The attachment save format (ChestAttachment.Serialize / Parse, "prefab|px|py|pz|rx|ry|rz|scale|paid|cost;..."). It is stored in the
    /// chest's ZDO: a bug here silently changes or loses players' decorations and refunds. Example tests pin the legacy formats; property
    /// tests check the round trip and that ANY stored text is read without an exception and within the limits.
    /// </summary>
    [TestFixture]
    public class ChestAttachmentTests
    {
        const string T = "ChestDecor.ChestAttachment";
        const float PosTolerance = 0.0001f;   // the format keeps 4 decimals ("0.####")

        /// Plain copy of a ChestAttachment (the mod's type is reached by reflection).
        sealed class Att
        {
            public string Prefab;
            public Vector3 Pos, Euler;
            public float Scale = 1f;
            public bool Paid = true;
            public List<KeyValuePair<string, int>> Cost;   // null = not recorded
            public override string ToString()
            {
                return Prefab + " " + Pos.ToString("F4") + " " + Euler.ToString("F4") + " s" + Scale + " paid=" + Paid + " cost=" +
                       (Cost == null ? "null" : string.Join(",", Cost.Select(c => c.Key + ":" + c.Value)));
            }
        }

        static Type AType { get { return ModAssembly.Type(T); } }
        static Type PmType { get { return ModAssembly.Type("ChestDecor.PaidMaterial"); } }

        static object ToMod(Att a)
        {
            var o = Activator.CreateInstance(AType);
            AType.GetField("Prefab").SetValue(o, a.Prefab);
            AType.GetField("Position").SetValue(o, a.Pos);
            AType.GetField("Euler").SetValue(o, a.Euler);
            AType.GetField("Scale").SetValue(o, a.Scale);
            AType.GetField("Paid").SetValue(o, a.Paid);
            if (a.Cost != null)
            {
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(PmType));
                foreach (var c in a.Cost) list.Add(Activator.CreateInstance(PmType, c.Key, c.Value));
                AType.GetField("Cost").SetValue(o, list);
            }
            return o;
        }

        static Att FromMod(object o)
        {
            var cost = (IEnumerable)AType.GetField("Cost").GetValue(o);
            return new Att
            {
                Prefab = (string)AType.GetField("Prefab").GetValue(o),
                Pos = (Vector3)AType.GetField("Position").GetValue(o),
                Euler = (Vector3)AType.GetField("Euler").GetValue(o),
                Scale = (float)AType.GetField("Scale").GetValue(o),
                Paid = (bool)AType.GetField("Paid").GetValue(o),
                Cost = cost == null ? null : cost.Cast<object>().Select(m => new KeyValuePair<string, int>(
                    (string)PmType.GetField("Prefab").GetValue(m), (int)PmType.GetField("Amount").GetValue(m))).ToList()
            };
        }

        static IList ModList(IEnumerable<Att> items)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(AType));
            if (items != null) foreach (var a in items) list.Add(a == null ? null : ToMod(a));
            return list;
        }

        static string Serialize(IEnumerable<Att> items) { return (string)ModAssembly.Call(T, "Serialize", ModList(items)); }
        static List<Att> Parse(string data, int max = 16) { return ((IEnumerable)ModAssembly.Call(T, "Parse", data, max)).Cast<object>().Select(FromMod).ToList(); }
        static int MaxPaid { get { return (int)AType.GetField("MaxPaidAmount").GetValue(null); } }

        static bool Near(float a, float b, float tol) { return Math.Abs(a - b) <= tol; }
        static bool Near(Vector3 a, Vector3 b, float tol) { return Near(a.x, b.x, tol) && Near(a.y, b.y, tol) && Near(a.z, b.z, tol); }

        // ---- examples: the stored formats that exist in players' worlds ----------------------------------------------------------

        [Test]
        public void Parse_OldSaveWithoutPaidField_CountsAsPaidWithCostNotRecorded()
        {
            var a = Parse("TrophyBoar|0.1|0.2|0.3|0|90|0|1").Single();

            Assert.That(a.Prefab, Is.EqualTo("TrophyBoar"));
            Assert.That(a.Pos, Is.EqualTo(new Vector3(0.1f, 0.2f, 0.3f)));
            Assert.That(a.Paid, Is.True);
            Assert.That(a.Cost, Is.Null, "no cost field = not recorded (refunded from the catalog)");
        }

        [Test]
        public void Parse_PaidZeroAndEmptyCost_IsFreeAndPaidNothing()
        {
            var a = Parse("Sword|0|0|0|0|0|0|1|0|").Single();

            Assert.That(a.Paid, Is.False);
            Assert.That(a.Cost, Is.Not.Null.And.Empty, "an empty cost field = paid nothing, not 'not recorded'");
        }

        [Test]
        public void Parse_CostField_ReadsEscapedNamesAndCapsAmounts()
        {
            var a = Parse("X|0|0|0|0|0|0|1|1|Wood:3,Fine%3AWood:999999999,Bad:-2,:5,NoAmount").Single();

            Assert.That(a.Cost.Select(c => c.Key + "=" + c.Value), Is.EqualTo(new[] { "Wood=3", "Fine:Wood=" + MaxPaid }),
                "escaped ':' is read back; a huge amount is capped; negative, nameless and amount-less parts are dropped");
        }

        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("-Infinity")]
        [TestCase("1,5")]
        [TestCase("")]
        public void Parse_UnreadableNumber_SkipsOnlyThatEntry(string bad)
        {
            var list = Parse("Bad|" + bad + "|0|0|0|0|0|1;Good|1|2|3|0|0|0|1");

            Assert.That(list.Select(a => a.Prefab), Is.EqualTo(new[] { "Good" }));
        }

        [TestCase("5", 2f)]
        [TestCase("0", 0.05f)]
        [TestCase("-1", 0.05f)]
        [TestCase("0.5", 0.5f)]
        public void Parse_Scale_IsClampedToTheAllowedRange(string stored, float expected)
        {
            Assert.That(Parse("X|0|0|0|0|0|0|" + stored).Single().Scale, Is.EqualTo(expected));
        }

        /// The exact stored text (golden format): changing it breaks every saved world, so it may only change on purpose.
        [Test]
        public void Serialize_WritesTheExactSaveFormat()
        {
            var text = Serialize(new[]
            {
                new Att { Prefab = "TrophyBoar", Pos = new Vector3(0.1f, -0.25f, 1f), Euler = new Vector3(0, 90, 0), Scale = 1.5f, Paid = true,
                          Cost = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("Wood", 3), new KeyValuePair<string, int>("Fine Wood", 2) } },
                new Att { Prefab = "Sword", Paid = false },
            });

            Assert.That(text, Is.EqualTo("TrophyBoar|0.1|-0.25|1|0|90|0|1.5|1|Wood:3,Fine%20Wood:2;Sword|0|0|0|0|0|0|1|0"));
        }

        [Test]
        public void Serialize_CostDropsNamelessAndNonPositiveAmounts_AndCapsHugeOnes()
        {
            var text = Serialize(new[] { new Att { Prefab = "X", Cost = new List<KeyValuePair<string, int>> {
                new KeyValuePair<string, int>("", 3), new KeyValuePair<string, int>("Zero", 0), new KeyValuePair<string, int>("Neg", -1),
                new KeyValuePair<string, int>("Wood", 2), new KeyValuePair<string, int>("Iron", int.MaxValue) } } });

            Assert.That(text, Does.EndWith("|1|Wood:2,Iron:" + MaxPaid));
        }

        [Test]
        public void Parse_ZeroAmount_IsDropped()
        {
            Assert.That(Parse("X|0|0|0|0|0|0|1|1|Zero:0,Wood:1").Single().Cost.Select(c => c.Key), Is.EqualTo(new[] { "Wood" }));
        }

        [Test]
        public void NewAttachment_CountsAsPaidWithScaleOne()
        {
            var fresh = FromMod(Activator.CreateInstance(AType));

            Assert.That(fresh.Paid, Is.True, "documented: only a free attach sets Paid = false");
            Assert.That(fresh.Scale, Is.EqualTo(1f));
        }

        [Test]
        public void Serialize_PrefabWithSeparators_IsMadeSafe()
        {
            var text = Serialize(new[] { new Att { Prefab = "a;b|c", Cost = new List<KeyValuePair<string, int>>() } });

            Assert.That(Parse(text).Single().Prefab, Is.EqualTo("a_b_c"));
        }

        [Test]
        public void Serialize_SkipsNullAndNamelessEntries()
        {
            var text = Serialize(new[] { null, new Att { Prefab = "" }, new Att { Prefab = "Keep" } });

            Assert.That(Parse(text).Select(a => a.Prefab), Is.EqualTo(new[] { "Keep" }));
        }

        [Test]
        public void Serialize_NullOrEmptyList_IsEmptyText()
        {
            Assert.That((string)ModAssembly.Call(T, "Serialize", new object[] { null }), Is.Empty);
            Assert.That(Serialize(new Att[0]), Is.Empty);
        }

        [Test]
        public void Clone_CostListIsNotShared()
        {
            var original = ToMod(new Att { Prefab = "X", Cost = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("Wood", 2) } });
            var copy = AType.GetMethod("Clone").Invoke(original, null);

            ((IList)AType.GetField("Cost").GetValue(copy)).Clear();

            Assert.That(FromMod(original).Cost, Has.Count.EqualTo(1), "clearing the copy's cost must not change the original's refund");
        }

        [Test]
        public void CopyPaymentFrom_TakesPaidAndACopyOfTheCost_NullChangesNothing()
        {
            var target = ToMod(new Att { Prefab = "T", Paid = true });
            var source = ToMod(new Att { Prefab = "S", Paid = false, Cost = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("Wood", 4) } });
            var copy = AType.GetMethod("CopyPaymentFrom");

            copy.Invoke(target, new object[] { null });
            Assert.That(FromMod(target).Paid, Is.True, "null source: nothing taken over");

            copy.Invoke(target, new[] { source });
            ((IList)AType.GetField("Cost").GetValue(source)).Clear();
            var t = FromMod(target);
            Assert.That(t.Paid, Is.False);
            Assert.That(t.Cost.Select(c => c.Key + "=" + c.Value), Is.EqualTo(new[] { "Wood=4" }), "its own copy of the list");
        }

        [Test]
        public void CopyPaymentFrom_SourceWithoutRecordedCost_LeavesCostNotRecorded()
        {
            var target = ToMod(new Att { Prefab = "T", Cost = new List<KeyValuePair<string, int>>() });
            AType.GetMethod("CopyPaymentFrom").Invoke(target, new[] { ToMod(new Att { Prefab = "S" }) });

            Assert.That(FromMod(target).Cost, Is.Null);
        }

        [Test]
        public void CloneList_NullIsEmpty_OtherwiseIndependentCopies()
        {
            Assert.That((ICollection)ModAssembly.Call(T, "CloneList", new object[] { null }), Is.Empty);

            var source = ModList(new[] { new Att { Prefab = "A" } });
            var clone = (IList)ModAssembly.Call(T, "CloneList", source);
            AType.GetField("Prefab").SetValue(clone[0], "B");

            Assert.That(FromMod(source[0]).Prefab, Is.EqualTo("A"));
        }

        [Test]
        public void Same_ComparesTheSavedForm()
        {
            var a = ModList(new[] { new Att { Prefab = "A", Pos = new Vector3(1, 2, 3) } });
            var b = ModList(new[] { new Att { Prefab = "A", Pos = new Vector3(1, 2, 3.00001f) } });   // below the 4 stored decimals
            var c = ModList(new[] { new Att { Prefab = "A", Pos = new Vector3(1, 2, 3.1f) } });

            Assert.That((bool)ModAssembly.Call(T, "Same", a, b), Is.True);
            Assert.That((bool)ModAssembly.Call(T, "Same", a, c), Is.False);
        }

        // ---- properties ------------------------------------------------------------------------------------------------------

        static readonly Gen<KeyValuePair<string, int>> CostGen =
            from name in Props.TextFrom("abcWOOD_09 :;|,%", 12)
            from amount in Gen.Choose(1, 100000)
            select new KeyValuePair<string, int>(name, amount);

        static readonly Gen<Att> AttGen =
            from prefab in Props.TextFrom("abcXYZ_09 ;|,:-", 16)
            from p in Gen.ArrayOf(3, Props.GameFloat(50f))
            from e in Gen.ArrayOf(3, Props.GameFloat(360f))
            from scale in Gen.Choose(50, 2000).Select(i => i / 1000f)
            from paid in Arb.Generate<bool>()
            from hasCost in Arb.Generate<bool>()
            from n in Gen.Choose(0, 4)
            from cost in Gen.ListOf(n, CostGen)
            select new Att { Prefab = prefab, Pos = new Vector3(p[0], p[1], p[2]), Euler = new Vector3(e[0], e[1], e[2]), Scale = scale, Paid = paid,
                             Cost = hasCost ? cost.ToList() : null };

        [Test]
        public void Property_RoundTrip_KeepsEveryAttachment()
        {
            var listGen = from n in Gen.Choose(0, 16) from l in Gen.ListOf(n, AttGen) select l.ToList();

            Props.Check(Prop.ForAll(listGen.ToArbitrary(), list =>
            {
                var back = Parse(Serialize(list));
                if (back.Count != list.Count) return false;
                for (int i = 0; i < list.Count; i++)
                {
                    var a = list[i]; var b = back[i];
                    bool costSame = a.Cost == null ? b.Cost == null : b.Cost != null && a.Cost.SequenceEqual(b.Cost);
                    if (b.Prefab != a.Prefab.Replace(';', '_').Replace('|', '_') || !Near(a.Pos, b.Pos, PosTolerance) || !Near(a.Euler, b.Euler, PosTolerance) ||
                        !Near(a.Scale, b.Scale, PosTolerance) || a.Paid != b.Paid || !costSame)
                        return false;
                }
                return true;
            }).Label("Parse(Serialize(list)) == list"));
        }

        [Test]
        public void Property_SerializeOfParsed_IsStable()
        {
            Props.Check(Prop.ForAll(Gen.ListOf(AttGen).Select(l => l.ToList()).ToArbitrary(), list =>
            {
                var once = Serialize(Parse(Serialize(list), int.MaxValue));
                return Serialize(Parse(once, int.MaxValue)) == once;
            }).Label("saving what was loaded never changes the text again"));
        }

        [Test]
        public void Property_AnyFiniteFloat_SurvivesTheRoundTrip()
        {
            Props.Check(Prop.ForAll(Props.AnyFiniteFloat().ToArbitrary(), f =>
            {
                var back = Parse(Serialize(new[] { new Att { Prefab = "X", Pos = new Vector3(f, 0, 0) } })).SingleOrDefault();
                return back != null && Near(back.Pos.x, f, Math.Max(PosTolerance, Math.Abs(f) * 1e-6f));
            }).Label("a position is kept to 4 decimals / 7 digits"));
        }

        [Test]
        public void Property_AnyStoredText_IsReadWithoutExceptionAndWithinLimits()
        {
            var noise = Props.TextFrom("0123456789.-eE|;,:%abcNaInfity ", 120);
            var text = Gen.OneOf(noise, Arb.Generate<string>().Where(s => s != null), Gen.ListOf(AttGen).Select(l => Serialize(l.ToList())));

            Props.Check(Prop.ForAll(text.ToArbitrary(), Arb.From(Gen.Choose(0, 20)), (data, max) =>
            {
                var list = Parse(data, max);
                return list.Count <= max && list.All(a =>
                    a.Prefab.Length > 0 && Finite(a.Pos) && Finite(a.Euler) && a.Scale >= 0.05f && a.Scale <= 2f &&
                    (a.Cost == null || a.Cost.All(c => c.Key.Length > 0 && c.Value >= 1 && c.Value <= MaxPaid)));
            }), 500);
        }

        [Test]
        public void Property_MoreEntriesThanMax_KeepsTheFirstMax()
        {
            Props.Check(Prop.ForAll(Gen.ListOf(AttGen).Select(l => l.ToList()).ToArbitrary(), Arb.From(Gen.Choose(0, 8)), (list, max) =>
            {
                var back = Parse(Serialize(list), max);
                var want = list.Take(max).Select(a => a.Prefab.Replace(';', '_').Replace('|', '_'));
                return back.Select(a => a.Prefab).SequenceEqual(want);
            }));
        }

        static bool Finite(Vector3 v) { return !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z); }
    }
}
