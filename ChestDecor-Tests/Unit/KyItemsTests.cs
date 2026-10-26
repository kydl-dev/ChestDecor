using System.Linq;
using FsCheck;

namespace ChestDecor.Tests.Unit
{
    /// <summary>KY-Items (user, 2026-10-26): which mods count as the user's own. Pure rule in KyItems.IsUserGuid, reached in the built DLL.</summary>
    [TestFixture]
    public class KyItemsTests
    {
        const string Prefix = "com.kydldev.";
        static bool IsUser(string guid) { return (bool)ModAssembly.Call("ChestDecor.KyItems", "IsUserGuid", guid); }

        [TestCase("com.kydldev.DrainTarPit")]
        [TestCase("com.kydldev.CargoToGo")]
        [TestCase("COM.KYDLDEV.WhipItUp")]
        public void UserMods_Count(string guid) { Assert.That(IsUser(guid), Is.True); }

        [TestCase("com.kydldev.chestdecor")]      // ChestDecor itself
        [TestCase("com.kydldev.ChestDecor")]
        [TestCase("com.kydldev.")]                // prefix only
        [TestCase("com.someoneelse.Mod")]
        [TestCase("kydldev.Mod")]
        [TestCase("")]
        [TestCase(null)]
        public void OtherGuids_DoNotCount(string guid) { Assert.That(IsUser(guid), Is.False); }

        [Test]
        public void IsUserMod_UsesThePluginsGuid_NullIsNot()
        {
            Assert.That((bool)ModAssembly.Call("ChestDecor.KyItems", "IsUserMod", new BepInEx.BepInPlugin("com.kydldev.DrainTarPit", "DrainTarPit", "1.0.0")), Is.True);
            Assert.That((bool)ModAssembly.Call("ChestDecor.KyItems", "IsUserMod", new BepInEx.BepInPlugin("com.other.Mod", "Mod", "1.0.0")), Is.False);
            Assert.That((bool)ModAssembly.Call("ChestDecor.KyItems", "IsUserMod", new object[] { null }), Is.False);
        }

        [Test]
        public void KyItemsCategoryFlag_IsItsOwnBit()
        {
            var t = ModAssembly.Type("ChestDecor.AttachCategory");
            var ky = (int)System.Enum.Parse(t, "KyItems");
            Assert.That(ky, Is.EqualTo(1024));
            foreach (var name in System.Enum.GetNames(t))
                if (name != "KyItems" && name != "None") Assert.That((int)System.Enum.Parse(t, name) & ky, Is.Zero, name + " overlaps KyItems");
        }

        // ---- properties ----------------------------------------------------------------------------------------------------------

        /// Any capitalisation of the prefix + any non-empty name counts, except ChestDecor's own GUID.
        [Test]
        public void Property_PrefixInAnyCaseWithAName_Counts_ExceptChestDecor()
        {
            var gen = from flips in Gen.ArrayOf(Prefix.Length, Arb.Generate<bool>())
                      from name in Props.TextFrom("abcXYZ09._-ChestDecorchestdecor", 14)
                      select new string(Prefix.Select((c, i) => flips[i] ? char.ToUpperInvariant(c) : c).ToArray()) + name;
            Props.Check(Prop.ForAll(gen.ToArbitrary(), guid =>
                IsUser(guid) == !string.Equals(guid, "com.kydldev.chestdecor", System.StringComparison.OrdinalIgnoreCase)));
        }

        /// Anything that does not start with the prefix never counts (other authors' GUIDs, junk, unicode).
        [Test]
        public void Property_WithoutThePrefix_NeverCounts()
        {
            var gen = Arb.Generate<string>().Where(s => s == null || !s.StartsWith(Prefix, System.StringComparison.OrdinalIgnoreCase));
            Props.Check(Prop.ForAll(gen.ToArbitrary(), guid => !IsUser(guid)));
        }
    }
}
