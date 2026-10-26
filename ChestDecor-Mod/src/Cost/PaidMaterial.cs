namespace ChestDecor
{
    /// <summary>
    /// One material that was really paid for an attachment: the item prefab and how many of it.
    /// A list of these is stored on every attachment when it is created (see <see cref="ChestAttachment.Cost"/>),
    /// so a refund returns exactly what was paid, whatever the recipe or the config says later.
    /// </summary>
    public readonly struct PaidMaterial
    {
        public readonly string Prefab;
        public readonly int Amount;

        public PaidMaterial(string prefab, int amount)
        {
            Prefab = prefab;
            Amount = amount;
        }
    }
}
