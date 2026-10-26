using Kydl.Common;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using static ChestDecor.AttachPayment;

namespace ChestDecor
{
    /// <summary>
    /// Checks and takes the cost of an attachment.
    ///
    /// Material checks and consumption go through the game's own Player methods (looked up by reflection, because
    /// their signatures changed between game versions). That way "craft from nearby containers" mods such as
    /// AzuCraftyBoxes, which patch those very methods, apply to attachments too.
    /// </summary>
    internal static class AttachCost
    {
        internal const string CoinsName = "$item_coins";

        internal static readonly MethodInfo HaveRecipeItemsM = Find(typeof(Player), "HaveRequirementItems", typeof(Recipe));
        internal static readonly MethodInfo HavePieceM = Find(typeof(Player), "HaveRequirements", typeof(Piece));
        internal static readonly MethodInfo ConsumeM = Find(typeof(Player), "ConsumeResources", typeof(Piece.Requirement[]));
        internal static readonly MethodInfo NoCostM = Find(typeof(Player), "NoCostCheat", null);
        internal static readonly MethodInfo RecipeKnownM = Find(typeof(Player), "IsRecipeKnown", typeof(string));

        // One-ingredient recipes for costs that are not a real recipe (coins, drop-only items such as trophies), so
        // they are checked and taken through the same game methods as everything else.
        internal static readonly Dictionary<string, Recipe> Loose = new(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache()
        {
            foreach (var recipe in Loose.Values)
                if (recipe != null) UnityEngine.Object.Destroy(recipe);
            Loose.Clear();
        }

        // ------------------------------------------------------------------------------------------------
        // Can the player pay?
        // ------------------------------------------------------------------------------------------------

        public static bool Evaluate(Player player, AttachEntry entry, out string reason)
        {
            reason = null;
            if (player == null || entry == null)
            {
                reason = "Not available";
                return false;
            }

            if (IsFree(player)) return true;

            switch (entry.Cost)
            {
                case CostKind.Piece: return CheckPiece(player, entry, out reason);
                case CostKind.Recipe: return CheckRecipe(player, entry, out reason);
                default: return CheckLoose(player, entry, out reason);
            }
        }

        /// <summary>
        /// Coins (trader items), the item itself (trophies and other drop-only items), or a set amount of one other
        /// item (the seed or fruit a plant is planted with, or Wood).
        /// </summary>
        internal static bool CheckLoose(Player player, AttachEntry entry, out string reason)
        {
            reason = null;
            var coins = entry.Cost == CostKind.Coins;
            var material = entry.Cost == CostKind.Material;

            // the game's own check: inventory, plus nearby chests when a crafting-from-storage mod is installed.
            // When it says no, that is the answer: payment goes through the game too, so a plain inventory count
            // must never overrule it.
            var recipe = LooseRecipe(entry);
            if (recipe != null)
            {
                // the one ingredient IS the cost (the item or the coins), so the ignored-ingredients list does not apply:
                // Pay takes it and the refund returns it, and the check must ask for exactly the same
                if (HaveRecipeItems(player, recipe, skipIgnored: false)) return true;
            }
            else if (Count(player, LooseName(entry)) >= LooseAmount(entry))
            {
                return true;   // no recipe could be made for this item: the inventory count is all there is
            }

            reason = coins ? "Not enough coins" : material ? "Missing materials" : "You need this item";
            return false;
        }

        /// <summary>The shared name of the one item a coins / item / material cost takes.</summary>
        internal static string LooseName(AttachEntry entry)
        {
            switch (entry.Cost)
            {
                case CostKind.Coins: return CoinsName;
                case CostKind.Material:
                    var drop = entry.CostItem != null ? entry.CostItem.GetComponent<ItemDrop>() : null;
                    return drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                        ? drop.m_itemData.m_shared.m_name
                        : null;
                default: return entry.SharedName;
            }
        }

        /// <summary>How many of that item a coins / item / material cost takes.</summary>
        internal static int LooseAmount(AttachEntry entry)
        {
            switch (entry.Cost)
            {
                case CostKind.Coins: return entry.CoinPrice;
                case CostKind.Material: return entry.CostAmount;
                default: return 1;
            }
        }

        internal static Recipe LooseRecipe(AttachEntry entry)
        {
            if (Loose.TryGetValue(entry.Prefab, out var cached) && cached != null) return cached;

            GameObject itemObject;
            int amount;
            if (entry.Cost == CostKind.Coins)
            {
                itemObject = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Coins") : null;
                amount = entry.CoinPrice;
            }
            else if (entry.Cost == CostKind.Material)
            {
                itemObject = entry.CostItem;
                amount = entry.CostAmount;
            }
            else
            {
                itemObject = entry.Object;
                amount = 1;
            }

            var drop = itemObject != null ? itemObject.GetComponent<ItemDrop>() : null;
            if (drop == null || amount <= 0) return null;

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "ChestDecor_" + entry.Prefab;
            recipe.hideFlags = HideFlags.HideAndDontSave;
            recipe.m_item = drop;
            recipe.m_enabled = true;
            recipe.m_resources = new[] { new Piece.Requirement { m_resItem = drop, m_amount = amount } };

            Loose[entry.Prefab] = recipe;
            return recipe;
        }

        internal static bool CheckPiece(Player player, AttachEntry entry, out string reason)
        {
            reason = null;
            var piece = entry.Piece;
            if (piece == null) { reason = "Not available"; return false; }

            if (ModSettings.RequireKnown.Value && !HavePiece(player, piece, "IsKnown"))
            {
                reason = "Not discovered yet";
                return false;
            }

            // No crafting station is needed: the piece is only an attachment that can't be taken off the chest again.
            if (!HavePiece(player, piece, "CanBuild"))
            {
                reason = "Missing materials";
                return false;
            }
            return true;
        }

        internal static bool CheckRecipe(Player player, AttachEntry entry, out string reason)
        {
            reason = null;
            var recipe = entry.Recipe;
            if (recipe == null) { reason = "Not available"; return false; }

            if (ModSettings.RequireKnown.Value && !IsRecipeKnown(player, entry.SharedName))
            {
                reason = "Recipe not known yet";
                return false;
            }

            // No crafting station is needed: the item is only an attachment that can't be taken off the chest again.
            if (!HaveRecipeItems(player, recipe))
            {
                reason = "Missing materials";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Whether the player knows this recipe. When the game can't be asked (method missing, or it throws) the
        /// answer is "not known": a requirement the player switched on must never be skipped by accident.
        /// </summary>
        internal static bool IsRecipeKnown(Player player, string sharedName)
        {
            if (RecipeKnownM == null)
            {
                WarnOnce("Player.IsRecipeKnown was not found, so 'Require Known Recipe' treats every recipe as unknown.");
                return false;
            }

            try { return (bool)RecipeKnownM.Invoke(player, new object[] { sharedName }); }
            catch (Exception e)
            {
                WarnOnce($"Recipe known check failed, treating the recipe as unknown: {e.Message}");
                return false;
            }
        }

        internal static readonly HashSet<string> Warned = new();

        internal static void WarnOnce(string message)
        {
            if (Warned.Add(message)) ModLog.Warn(message);
        }

        internal static bool HaveRecipeItems(Player player, Recipe recipe, bool skipIgnored = true)
        {
            if (HaveRecipeItemsM != null)
            {
                var original = recipe.m_resources;
                if (skipIgnored) recipe.m_resources = IgnoredIngredients.Filter(original);   // the game's check must not see the ignored ones
                try { return (bool)HaveRecipeItemsM.Invoke(player, BuildArgs(HaveRecipeItemsM, recipe)); }
                catch (Exception e) { ModLog.Verbose($"HaveRequirementItems failed, counting by hand: {e.Message}"); }
                finally { recipe.m_resources = original; }
            }
            return HaveByHand(player, recipe.m_resources, skipIgnored);
        }

        /// <summary>
        /// Asks the game whether the player can build / knows a piece. The game's "can build" check also wants the
        /// piece's crafting station within range, which attachments don't need. So for that check only, the piece's
        /// station reference is cleared (the game then skips the station test) and put back afterwards. Everything
        /// else the game checks (materials, including from nearby chests when a crafting-from-storage mod is
        /// installed) is unchanged.
        ///
        /// This replaces a Harmony patch on CraftingStation.HaveBuildStationInRange: that method returns a
        /// CraftingStation in current game versions (a bool in older ones), so a patch against it breaks whenever
        /// the signature changes. The m_craftingStation field is stable.
        /// </summary>
        internal static bool HavePiece(Player player, Piece piece, string mode)
        {
            var canBuildMode = mode == "CanBuild";

            // The game can't be asked: materials fall back to an inventory count, but "is it known" must not pass
            // by default, so that check answers no.
            if (HavePieceM == null)
            {
                if (!canBuildMode) WarnOnce("Player.HaveRequirements was not found, so 'Require Known Recipe' treats every piece as unknown.");
                return canBuildMode && HaveByHand(player, piece.m_resources);
            }

            var station = piece.m_craftingStation;
            var resources = piece.m_resources;
            piece.m_resources = IgnoredIngredients.Filter(resources);   // the game's check must not see the ignored ones
            if (canBuildMode) piece.m_craftingStation = null;
            try { return (bool)HavePieceM.Invoke(player, BuildArgs(HavePieceM, piece, mode)); }
            catch (Exception e)
            {
                ModLog.Verbose($"HaveRequirements failed: {e.Message}");
                if (!canBuildMode) WarnOnce($"Known-piece check failed, treating the piece as unknown: {e.Message}");
                return canBuildMode && HaveByHand(player, piece.m_resources);
            }
            finally
            {
                if (canBuildMode) piece.m_craftingStation = station;
                piece.m_resources = resources;
            }
        }

        /// <summary>Fallback when the game's own check can't be called: look in the player's inventory only.</summary>
        internal static bool HaveByHand(Player player, Piece.Requirement[] requirements, bool skipIgnored = true)
        {
            if (requirements == null) return true;
            foreach (var req in requirements)
            {
                if (req == null || req.m_resItem == null || req.m_amount <= 0) continue;
                if (skipIgnored && IgnoredIngredients.IsIgnored(req)) continue;
                if (Count(player, req.m_resItem.m_itemData.m_shared.m_name) < req.m_amount) return false;
            }
            return true;
        }


        // ------------------------------------------------------------------------------------------------
        // Text
        // ------------------------------------------------------------------------------------------------

        public static string Describe(AttachEntry entry)
        {
            if (entry == null) return "";
            if (IsFree(Player.m_localPlayer)) return "Free";   // the setting or the no-cost cheat, like Evaluate / Pay

            switch (entry.Cost)
            {
                case CostKind.Coins:
                    return entry.CoinPrice + " " + Loc(CoinsName);
                case CostKind.Item:
                    return "1x " + entry.DisplayName;
                case CostKind.Material:
                    var name = LooseName(entry);
                    return entry.CostAmount + "x " + (string.IsNullOrEmpty(name) ? "?" : Loc(name));
                default:
                    var reqs = entry.Requirements;
                    if (reqs == null || reqs.Length == 0) return "Free";

                    var sb = new StringBuilder();
                    foreach (var req in reqs)
                    {
                        if (req == null || req.m_resItem == null || req.m_amount <= 0) continue;
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(req.m_amount).Append("x ").Append(Loc(req.m_resItem.m_itemData.m_shared.m_name));
                    }
                    return sb.Length > 0 ? sb.ToString() : "Free";
            }
        }

        internal static string Loc(string key)
        {
            try { return Localization.instance.Localize(key); }
            catch { return key; }
        }

        // ------------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------------

        /// <summary>True when attaching costs nothing for this player (Free Attachments setting or the no-cost cheat).</summary>
        public static bool IsFree(Player player)
        {
            if (ModSettings.FreeAttachments.Value) return true;
            if (NoCostM == null) return false;
            try { return (bool)NoCostM.Invoke(player, null); }
            catch { return false; }
        }

        internal static int Count(Player player, string sharedName)
        {
            if (string.IsNullOrEmpty(sharedName)) return 0;
            var inventory = player.GetInventory();
            return inventory != null ? inventory.CountItems(sharedName) : 0;
        }

        internal static MethodInfo Find(Type type, string name, Type firstParam)
        {
            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != name) continue;

                var ps = m.GetParameters();
                if (firstParam == null)
                {
                    if (ps.Length == 0) return m;
                    continue;
                }
                if (ps.Length > 0 && ps[0].ParameterType == firstParam) return m;
            }
            return null;
        }

        /// <summary>
        /// Arguments for a game method whose exact signature differs between versions: the first one is given,
        /// the rest are filled in by type / name (quality 1, amount 1, no flags, the requested enum mode).
        /// </summary>
        internal static object[] BuildArgs(MethodInfo method, object first, string enumMode = "CanBuild")
        {
            var ps = method.GetParameters();
            var args = new object[ps.Length];
            args[0] = first;

            for (int i = 1; i < ps.Length; i++)
            {
                var p = ps[i];
                var t = p.ParameterType;

                if (t.IsEnum) args[i] = Enum.Parse(t, enumMode);
                else if (t == typeof(bool)) args[i] = false;
                else if (t == typeof(int))
                {
                    if (p.Name != null && p.Name.IndexOf("itemQuality", StringComparison.OrdinalIgnoreCase) >= 0)
                        args[i] = p.HasDefaultValue ? p.DefaultValue : -1;
                    else
                        args[i] = 1; // qualityLevel, amount, multiplier
                }
                else if (p.HasDefaultValue) args[i] = p.DefaultValue;
                else args[i] = t.IsValueType ? Activator.CreateInstance(t) : null;
            }
            return args;
        }
    }
}
