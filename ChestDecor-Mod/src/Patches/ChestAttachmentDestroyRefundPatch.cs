using Kydl.Common;
using System;
using HarmonyLib;

namespace ChestDecor
{
    /// <summary>
    /// Returns the materials paid for Chest Decor attachments when the chest itself is destroyed.
    ///
    /// Vanilla dismantling reaches WearNTear.Destroy before the chest GameObject/ZDO is removed, which is the last
    /// reliable point at which the saved attachment list can be read. A hammer removal is handled earlier, in
    /// <see cref="ChestAttachmentRemoveRefundPatch"/>, so it is attributed to the player who asked for it.
    ///
    /// Who receives the refund is decided in <see cref="RefundRecipient"/>: the remover or the attacking player, and
    /// otherwise nobody (the materials are dropped at the chest). The owning machine's local player is never used as
    /// a stand-in. Whatever one callback has already returned is removed from the chest's saved list, so a second
    /// callback for the same chest can neither pay it twice nor skip what is still owed.
    /// </summary>
    [HarmonyPatch]
    internal static class ChestAttachmentDestroyRefundPatch
    {
        [HarmonyPatch(typeof(WearNTear), "Destroy", new Type[] { typeof(HitData), typeof(bool) })]
        [HarmonyPrefix]
        private static void DestroyPrefix(WearNTear __instance, HitData hitData, bool blockDrop)
        {
            try
            {
                var container = __instance != null ? __instance.GetComponent<Container>() : null;
                if (container == null) return;

                var decorator = container.GetComponent<ChestDecorator>();
                if (decorator == null) return;

                // A hammer removal already refunded its remover in RPC_Remove (nothing is left to do here). What
                // reaches this point is damage or another cause, and only a player attacker counts as a recipient.
                var recipient = RefundRecipient.FromHit(hitData, out var cause);
                decorator.RefundAttachmentsOnDestroy(recipient, cause);
            }
            catch (Exception e)
            {
                ModLog.Warn($"Could not refund attachments on chest destruction: {e.Message}");
            }
        }
    }
}
