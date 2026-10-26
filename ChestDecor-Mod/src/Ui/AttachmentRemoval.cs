using Kydl.Common;
using System.Collections.Generic;
using UnityEngine;

namespace ChestDecor;

/// <summary>
/// Removing an attachment from the chest right away, and refunding what it cost.
/// </summary>
internal static class AttachmentRemoval
{
    private static ChestDecorator Decor => ChestDecorUI.Decor;

    /// <summary>Takes an item off the chest right away (and refunds it if the config says so).</summary>
    internal static void Remove(int index)
    {
        if (Decor == null) return;
        if (index < 0 || index >= ChestDecorUI.SavedAtt.Count || index >= ChestDecorUI.WorkAtt.Count) return;
        if (!ChestDecorUI.CanChangeAttachments()) return; // checked again after the reload, so the index below is still right
        if (index >= ChestDecorUI.SavedAtt.Count || index >= ChestDecorUI.WorkAtt.Count) return;

        var removed = ChestDecorUI.SavedAtt[index];   // the saved state: its paid flag and stored cost decide the refund
        var removedWork = ChestDecorUI.WorkAtt[index];   // what the editor shows: may hold unsaved position edits
        var prefab = removed.Prefab;
        var selectionBefore = PositionPage.SelectedIndex;

        var next = ChestAttachment.CloneList(ChestDecorUI.SavedAtt);
        next.RemoveAt(index);
        if (!Decor.SaveAttachments(next))
        {
            ChestDecorUI.Toast("Could not remove the attachment.");
            return;
        }

        ChestDecorUI.SavedAtt = next;
        ChestDecorUI.WorkAtt.RemoveAt(index);
        Decor.ShowAttachments(ChestDecorUI.WorkAtt);

        if (index < PositionPage.SelectedIndex) PositionPage.SelectedIndex--;

        var entry = AttachCatalog.Find(prefab);
        var shownName = entry != null ? entry.DisplayName : prefab;

        if (ModSettings.RefundOnRemove.Value && removed.Paid)
        {
            var player = Player.m_localPlayer;
            var outstanding = player != null
                ? AttachPayment.Refund(player, player.transform.position, removed)
                : AttachPayment.OwedMaterials(removed);

            if (outstanding.Count > 0)
            {
                PutBackWithOutstanding(index, removed, removedWork, outstanding, shownName, selectionBefore);
                return;
            }
        }

        if (ModSettings.RefundOnRemove.Value && removed.Paid && removed.Cost == null && entry == null)
        {
            // an older attachment whose cost was never recorded, and its item is gone from the game
            ChestDecorUI.Toast("Removed " + shownName + ". Its cost was not recorded and the item no longer exists, so nothing could be returned.");
        }
        else
        {
            ChestDecorUI.Toast("Removed " + shownName);
        }
        PositionPage.Refresh();
    }

    /// <summary>
    ///     A removal whose refund could not be completed must not lose the rest: the attachment goes back on the
    ///     chest owing only the materials that were not handed out, so removing it again tries again without paying
    ///     anything twice.
    /// </summary>
    private static void PutBackWithOutstanding(int index, ChestAttachment removed, ChestAttachment removedWork,
        List<PaidMaterial> outstanding, string shownName, int selectionBefore)
    {
        var kept = removed.Clone();
        kept.Cost = outstanding;

        // the editor's copy keeps the position the player had set but not saved yet, and owes the same materials
        var shown = (removedWork ?? removed).Clone();
        shown.Paid = kept.Paid;
        shown.Cost = new List<PaidMaterial>(outstanding);

        var restored = ChestAttachment.CloneList(ChestDecorUI.SavedAtt);
        restored.Insert(Mathf.Min(index, restored.Count), kept);

        if (Decor.SaveAttachments(restored))
        {
            ChestDecorUI.SavedAtt = restored;
            ChestDecorUI.WorkAtt.Insert(Mathf.Min(index, ChestDecorUI.WorkAtt.Count), shown);
            PositionPage.SelectedIndex = selectionBefore;
            Decor.ShowAttachments(ChestDecorUI.WorkAtt);
            ChestDecorUI.Toast("Could not return all materials for " + shownName + ". It stays on the chest; remove it again to retry.");
        }
        else
        {
            ModLog.Warn(
                $"Refund of '{removed.Prefab}' was incomplete and the attachment could not be put back on the chest.");
            ChestDecorUI.Toast("Could not return all materials for " + shownName + ".");
        }
        PositionPage.Refresh();
    }
}
