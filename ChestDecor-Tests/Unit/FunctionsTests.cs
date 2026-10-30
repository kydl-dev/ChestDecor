namespace ChestDecor.Tests.Unit
{
    /// <summary>PROJECT_RULES 11 (user, 2026-10-30): a functionality works only when EnableAll AND its own Enable are on; a switched-off one has no window page.</summary>
    [TestFixture]
    public class FunctionsTests
    {
        const string Type = "ChestDecor.Functions";

        [TestCase(true, true, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, false)]
        public void IsOn_NeedsEnableAllAndOwnEnable(bool all, bool own, bool expected)
        {
            Assert.That((bool)ModAssembly.Call(Type, "IsOn", all, own), Is.EqualTo(expected));
        }

        [TestCase(true, true, new[] { true, true, true })]
        [TestCase(true, false, new[] { true, false, false })]
        [TestCase(false, true, new[] { false, true, true })]
        [TestCase(false, false, new[] { false, false, false })]
        public void Pages_AttachmentsOwnTwoPages(bool colours, bool attachments, bool[] expected)
        {
            Assert.That((bool[])ModAssembly.Call(Type, "Pages", colours, attachments), Is.EqualTo(expected));
        }

        [TestCase(new[] { true, true, true }, 0)]
        [TestCase(new[] { false, true, true }, 1)]
        [TestCase(new[] { false, false, true }, 2)]
        [TestCase(new[] { false, false, false }, -1)]
        public void FirstPage_IsTheFirstShownOrMinusOne(bool[] pages, int expected)
        {
            Assert.That((int)ModAssembly.Call(Type, "FirstPage", new object[] { pages }), Is.EqualTo(expected));
        }

        [Test]
        public void FirstPage_NullIsNoPage()
        {
            Assert.That((int)ModAssembly.Call(Type, "FirstPage", new object[] { null }), Is.EqualTo(-1));
        }

        [TestCase(new[] { true, false, true }, 0, true)]
        [TestCase(new[] { true, false, true }, 1, false)]
        [TestCase(new[] { true, false, true }, 3, false)]
        [TestCase(new[] { true, false, true }, -1, false)]
        public void PageShown_OnlyForAnExistingShownPage(bool[] pages, int page, bool expected)
        {
            Assert.That((bool)ModAssembly.Call(Type, "PageShown", new object[] { pages, page }), Is.EqualTo(expected));
        }
    }
}
