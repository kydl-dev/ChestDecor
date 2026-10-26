using Kydl.Common;
using System;
using HarmonyLib;

namespace ChestDecor
{
    /// <summary>
    /// Refunds the attachments of a chest that a player removes with the hammer, to that player, on the machine
    /// that owns the chest. Kept apart from <see cref="ChestAttachmentDestroyRefundPatch"/> so that a game update
    /// that changes one of the two methods only switches off its own half (see Plugin.ApplyPatches).
    /// </summary>
    [HarmonyPatch]
    internal static class ChestAttachmentRemoveRefundPatch
    {
        [HarmonyPatch(typeof(WearNTear), "RPC_Remove")]
        [HarmonyPrefix]
        private static void RpcRemovePrefix(WearNTear __instance, long sender, bool blockDrop)
        {
            try
            {
                var container = __instance != null ? __instance.GetComponent<Container>() : null;
                if (container == null) return;

                var decorator = container.GetComponent<ChestDecorator>();
                if (decorator == null) return;

                var recipient = RefundRecipient.FromRpcSender(sender, out var cause);
                decorator.RefundAttachmentsOnDestroy(recipient, cause);
            }
            catch (Exception e)
            {
                ModLog.Warn($"Could not refund attachments on chest removal: {e.Message}");
            }
        }
    }
}
