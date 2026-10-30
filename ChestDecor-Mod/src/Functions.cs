namespace ChestDecor
{
    /// <summary>
    /// Which functionalities of Chest Decor are on (PROJECT_RULES 11, user 2026-10-30). Pure: no game types, unit tested.
    /// A functionality works only when [General] EnableAll AND its own [Section] Enable are on. Off = does nothing and its UI is hidden.
    /// Functionalities: Colours (page 0), Attachments (pages 1 "Attach items" and 2 "Position items"), Camera (the orbit camera in the window).
    /// </summary>
    internal static class Functions
    {
        /// Expected outcome: true only when both switches are on.
        internal static bool IsOn(bool enableAll, bool own) { return enableAll && own; }

        /// Which of the three window pages are shown: Colours, Attach items, Position items (the last two belong to Attachments).
        internal static bool[] Pages(bool colours, bool attachments) { return new[] { colours, attachments, attachments }; }

        /// The first page that is shown, or -1 when none is (then the window has nothing to show and is not opened).
        internal static int FirstPage(bool[] pages)
        {
            if (pages == null) return -1;
            for (int i = 0; i < pages.Length; i++) if (pages[i]) return i;
            return -1;
        }

        /// True when `page` exists and is shown.
        internal static bool PageShown(bool[] pages, int page) { return pages != null && page >= 0 && page < pages.Length && pages[page]; }
    }
}
