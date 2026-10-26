using Kydl.Common;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using static ChestDecor.AttachCost;

namespace ChestDecor
{
    /// <summary>
    /// Paying for an attachment and giving it back (split from AttachCost, class-size rule): Pay takes the cost through the game's own
    /// methods; CostOf records exactly what was taken; Refund / Give hand it out again (inventory first, the ground when it is full).
    /// </summary>
    internal static class AttachPayment
    {
        // ------------------------------------------------------------------------------------------------
        // Paying
        // ------------------------------------------------------------------------------------------------

        public static bool Pay(Player player, AttachEntry entry)
        {
            if (player == null || entry == null) return false;
            if (IsFree(player)) return true;

            try
            {
                switch (entry.Cost)
                {
                    case CostKind.Piece:
                        return Consume(player, IgnoredIngredients.Filter(entry.Piece != null ? entry.Piece.m_resources : null));
                    case CostKind.Recipe:
                        return Consume(player, IgnoredIngredients.Filter(entry.Recipe != null ? entry.Recipe.m_resources : null));
                    default:
                    {
                        var loose = LooseRecipe(entry);
                        if (loose != null) return Consume(player, loose.m_resources);

                        // fallback: inventory only
                        var name = LooseName(entry);
                        var amount = LooseAmount(entry);
                        if (string.IsNullOrEmpty(name) || amount <= 0) return true;

                        return TakeAndVerify(player, new Dictionary<string, int> { { name, amount } },
                            () => player.GetInventory().RemoveItem(name, amount));
                    }
                }
            }
            catch (Exception e)
            {
                ModLog.Warn($"Paying for '{entry.Prefab}' failed: {e.Message}");
                return false;
            }
        }

        internal static bool Consume(Player player, Piece.Requirement[] requirements)
        {
            if (requirements == null) return true;

            var needed = Needed(requirements);
            if (needed.Count == 0) return true;   // nothing to take

            return TakeAndVerify(player, needed, () =>
            {
                if (ConsumeM != null)
                {
                    ConsumeM.Invoke(player, BuildArgs(ConsumeM, requirements));
                    return;
                }

                // fallback: inventory only
                foreach (var pair in needed)
                    player.GetInventory().RemoveItem(pair.Key, pair.Value);
            });
        }

        /// <summary>The total amount of every item (by shared name) that a list of requirements asks for.</summary>
        internal static Dictionary<string, int> Needed(Piece.Requirement[] requirements)
        {
            var needed = new Dictionary<string, int>();
            if (requirements == null) return needed;

            foreach (var req in requirements)
            {
                if (req == null || req.m_resItem == null || req.m_amount <= 0) continue;

                var name = req.m_resItem.m_itemData.m_shared.m_name;
                needed.TryGetValue(name, out var have);
                needed[name] = have + req.m_amount;
            }
            return needed;
        }

        /// <summary>
        /// Runs <paramref name="take"/> (which takes the materials) and then checks that they really left the player:
        /// the item attaches only when the payment is confirmed, never just because the take call did not throw.
        /// </summary>
        internal static bool TakeAndVerify(Player player, Dictionary<string, int> needed, Action take)
        {
            var before = new Dictionary<string, int>();
            foreach (var name in needed.Keys) before[name] = Count(player, name);

            take();

            var anyShort = false;
            foreach (var pair in needed)
            {
                var taken = before[pair.Key] - Count(player, pair.Key);
                if (taken >= pair.Value) continue;

                anyShort = true;
                ModLog.Verbose($"Payment check: {pair.Key} needed {pair.Value}, {taken} left the inventory.");
            }
            if (!anyShort) return true;

            // A crafting-from-storage mod (AzuCraftyBoxes and similar) takes part of the cost from nearby chests, so
            // the inventory alone can legitimately fall short of the full amount. That can't be measured from here,
            // so with such a mod on the game's consume method the game's own result is trusted.
            if (ConsumeIsPatchedByAnotherMod())
            {
                ModLog.Verbose("Payment check: part of the cost did not come from the inventory; trusting the crafting-from-storage mod.");
                return true;
            }

            ModLog.Warn("The materials were not taken from the player, so nothing is attached.");
            return false;
        }

        internal static bool ConsumeIsPatchedByAnotherMod()
        {
            if (ConsumeM == null) return false;   // the by-hand removal takes exactly from the inventory

            try
            {
                var info = Harmony.GetPatchInfo(ConsumeM);
                return info != null &&
                       (info.Prefixes.Count > 0 || info.Postfixes.Count > 0 || info.Transpilers.Count > 0 || info.Finalizers.Count > 0);
            }
            catch (Exception e)
            {
                ModLog.Verbose($"Could not read the patches on ConsumeResources: {e.Message}");
                return true;   // can't tell: don't turn a payment the game made into a failure
            }
        }

        // ------------------------------------------------------------------------------------------------
        // What was paid, and the refund when an attachment is removed again
        // ------------------------------------------------------------------------------------------------

        internal const string CoinsPrefab = "Coins";

        /// <summary>
        /// The materials <see cref="Pay"/> takes for this entry right now, broken down to base materials (the
        /// ingredients of a recipe / piece, the coins of a trader item, or the item itself). The caller stores this
        /// list on the new attachment, so the refund later returns exactly these materials and never a recalculation
        /// from the then-current recipe, build cost or config.
        /// </summary>
        public static List<PaidMaterial> CostOf(AttachEntry entry)
        {
            var result = new List<PaidMaterial>();
            if (entry == null) return result;

            switch (entry.Cost)
            {
                case CostKind.Piece:
                    AddRequirements(result, entry.Piece != null ? entry.Piece.m_resources : null);
                    break;

                case CostKind.Recipe:
                    AddRequirements(result, entry.Recipe != null ? entry.Recipe.m_resources : null);
                    break;

                case CostKind.Coins:
                    AddMaterial(result, CoinsPrefab, entry.CoinPrice);
                    break;

                case CostKind.Material:
                    if (entry.CostItem != null) AddMaterial(result, entry.CostItem.name, entry.CostAmount);
                    break;

                default:
                    AddMaterial(result, entry.Prefab, 1);
                    break;
            }
            return result;
        }

        internal static void AddRequirements(List<PaidMaterial> list, Piece.Requirement[] requirements)
        {
            if (requirements == null) return;

            foreach (var req in IgnoredIngredients.Filter(requirements))   // the same ingredients Pay takes
            {
                if (req == null || req.m_resItem == null || req.m_amount <= 0) continue;
                AddMaterial(list, req.m_resItem.gameObject.name, req.m_amount);
            }
        }

        /// <summary>Adds a material, merging it into an earlier entry for the same prefab.</summary>
        internal static void AddMaterial(List<PaidMaterial> list, string prefab, int amount)
        {
            if (string.IsNullOrEmpty(prefab) || amount <= 0) return;

            for (int i = 0; i < list.Count; i++)
            {
                if (!string.Equals(list[i].Prefab, prefab, StringComparison.OrdinalIgnoreCase)) continue;
                list[i] = new PaidMaterial(list[i].Prefab, list[i].Amount + amount);
                return;
            }
            list.Add(new PaidMaterial(prefab, amount));
        }

        /// <summary>
        /// The materials this attachment is owed back: exactly what is stored on it, never the finished item.
        /// The attachment's own saved state is the only authority:
        ///  * <see cref="ChestAttachment.Paid"/> false (attached for free) -> nothing, whatever the settings are now.
        ///  * Paid true -> the stored materials, even if Free Attachments (or the no-cost cheat) is switched on later,
        ///    and even if the recipe, build cost, catalog or ignored-ingredients setting has changed since.
        ///  * Paid true but no stored cost (an attachment saved without its cost) -> the only thing left to go on is
        ///    the current catalog entry, so it is rebuilt from that.
        /// Mirrors <see cref="Pay"/>, so an attach / remove cycle can never gain anything.
        /// </summary>
        public static List<PaidMaterial> OwedMaterials(ChestAttachment attachment)
        {
            if (attachment == null || !attachment.Paid) return new List<PaidMaterial>();
            if (attachment.Cost != null) return new List<PaidMaterial>(attachment.Cost);

            var entry = AttachCatalog.Find(attachment.Prefab);
            if (entry == null)
            {
                ModLog.Warn(
                    $"Could not refund attachment '{attachment.Prefab}': its cost was not stored (older save) and it is no longer in the catalog.");
                return new List<PaidMaterial>();
            }
            return CostOf(entry);
        }

        /// <summary>
        /// Gives back what was paid for this attachment (see <see cref="OwedMaterials"/>) to <paramref name="player"/>,
        /// at their feet, or on the ground at <paramref name="position"/> when there is no player. Returns the
        /// materials that could NOT be returned because something went wrong while handing them out (empty = all
        /// done), so the caller can keep them for another attempt instead of losing them.
        /// </summary>
        public static List<PaidMaterial> Refund(Player player, Vector3 position, ChestAttachment attachment)
        {
            if (attachment == null) return new List<PaidMaterial>();
            return Give(attachment.Prefab, OwedMaterials(attachment), player, position);
        }

        /// <summary>
        /// Hands out materials: at the player's feet, or on the ground at <paramref name="position"/> when
        /// <paramref name="player"/> is null. A material that failed part-way is tried once more right away.
        /// Returns what is still outstanding after that. A material whose item no longer exists in the game is
        /// logged and not reported as outstanding, because trying again can never succeed. Never throws.
        /// </summary>
        public static List<PaidMaterial> Give(string label, IList<PaidMaterial> owed, Player player, Vector3 position)
        {
            var outstanding = HandOut(label, owed, player, position);
            if (outstanding.Count > 0)
                outstanding = HandOut(label, outstanding, player, position);   // one more try for transient failures
            return outstanding;
        }

        internal static List<PaidMaterial> HandOut(string label, IList<PaidMaterial> materials, Player player, Vector3 position)
        {
            var outstanding = new List<PaidMaterial>();
            if (materials == null) return outstanding;

            foreach (var material in materials)
            {
                if (string.IsNullOrEmpty(material.Prefab) || material.Amount <= 0) continue;

                GameObject prefab = null;
                try { prefab = ResolveItemPrefab(material.Prefab); }
                catch (Exception e)
                {
                    // could not even look it up: that can be temporary, so keep it outstanding
                    ModLog.Warn($"Refunding '{label}': looking up '{material.Prefab}' failed: {e.Message}");
                    outstanding.Add(material);
                    continue;
                }

                if (prefab == null)
                {
                    ModLog.Warn(
                        $"Refunding '{label}': the paid material '{material.Prefab}' no longer exists, {material.Amount}x could not be returned.");
                    continue;
                }

                var given = SpawnDrop(label, player, position, prefab, material.Amount);
                if (given < material.Amount)
                    outstanding.Add(new PaidMaterial(material.Prefab, material.Amount - given));
            }
            return outstanding;
        }

        /// <summary>Finds the item prefab for a stored material name. Only real items are returned, never other prefabs.</summary>
        internal static GameObject ResolveItemPrefab(string name)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(name) : null;
            if (prefab == null) prefab = AttachCatalog.ResolvePrefab(name);
            return prefab != null && prefab.GetComponent<ItemDrop>() != null ? prefab : null;
        }

        /// <summary>
        /// Spawns up to <paramref name="amount"/> of an item, at the player's feet or (no player) on the ground around
        /// <paramref name="center"/>. Returns how many were really spawned, so a failure part-way is known exactly.
        /// </summary>
        internal static int SpawnDrop(string label, Player player, Vector3 center, GameObject prefab, int amount)
        {
            if (prefab == null || amount <= 0) return 0;

            var spawned = 0;
            try
            {
                var probe = prefab.GetComponent<ItemDrop>();
                var maxStack = probe != null && probe.m_itemData != null && probe.m_itemData.m_shared != null
                    ? Mathf.Max(1, probe.m_itemData.m_shared.m_maxStackSize)
                    : 1;

                var remaining = amount;
                while (remaining > 0)
                {
                    var stack = Mathf.Min(remaining, maxStack);

                    Vector3 pos;
                    if (player != null)
                        pos = player.transform.position + player.transform.forward * 0.8f + Vector3.up * 1.2f;
                    else
                        pos = center + Vector3.up + UnityEngine.Random.insideUnitSphere * 0.25f;   // a little spread so stacks don't overlap

                    var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
                    remaining -= stack;
                    spawned += stack;

                    var drop = go.GetComponent<ItemDrop>();
                    if (drop != null && stack > 1) drop.SetStack(stack);
                }
            }
            catch (Exception e)
            {
                ModLog.Warn($"Refunding '{label}': handing out '{prefab.name}' failed after {spawned} of {amount}: {e.Message}");
            }
            return spawned;
        }
    }
}
