namespace ChestDecor.Tests.Unit
{
    /// <summary>KY-Items (user, 2026-10-26): which mods count as the user's own. Pure rule in KyItems.IsUserGuid, reached in the built DLL.</summary>
    [TestFixture]
    public class KyItemsTests
    {
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
        public void KyItemsCategoryFlag_IsItsOwnBit()
        {
            var t = ModAssembly.Type("ChestDecor.AttachCategory");
            var ky = (int)System.Enum.Parse(t, "KyItems");
            Assert.That(ky, Is.EqualTo(1024));
            foreach (var name in System.Enum.GetNames(t))
                if (name != "KyItems" && name != "None") Assert.That((int)System.Enum.Parse(t, name) & ky, Is.Zero, name + " overlaps KyItems");
        }
    }
}
