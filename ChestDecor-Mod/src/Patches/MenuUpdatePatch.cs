using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// Esc closes the editor; without this the same key press would also open the game's pause menu.
    ///
    /// Only the one call in <c>Menu.Update</c> that opens the pause menu is swapped for a guarded version. The rest
    /// of <c>Menu.Update</c> keeps running while the window is open (its hidden / shown frame counters, the
    /// save-time text, the layout rebuild, the localisation refresh), so nothing in the game's menu is left
    /// frozen or stale, and no other mod's patch on <c>Menu.Update</c> is cut off by a skipping prefix.
    /// </summary>
    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class MenuUpdatePatch
    {
        private static readonly MethodInfo Show = AccessTools.Method(typeof(Menu), "Show", Type.EmptyTypes);
        private static readonly MethodInfo GuardedShow = AccessTools.Method(typeof(MenuUpdatePatch), nameof(ShowUnlessEditing));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (Show == null || GuardedShow == null)
                throw new InvalidOperationException("Menu.Show (or the guard) could not be found.");

            var replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(Show))
                {
                    // Same stack shape: the Menu instance is already on the stack and the guard takes it as its
                    // argument. The instruction is changed in place so any label / block on it stays attached.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = GuardedShow;
                    replaced++;
                }
                yield return instruction;
            }

            // If the game changed and the call is gone, fail loudly (the plugin reports it and carries on without
            // this patch) instead of silently leaving the pause menu opening on top of the editor.
            if (replaced == 0)
                throw new InvalidOperationException("Menu.Update no longer calls Menu.Show.");
        }

        /// <summary>The game's "open the pause menu" step, skipped while the editor is open or has just closed.</summary>
        internal static void ShowUnlessEditing(Menu menu)
        {
            if (ChestDecorUI.BlocksMenu) return;
            menu.Show();
        }
    }
}
