using Kydl.Common;
using System;
using HarmonyLib;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>Give every eligible container a ChestDecorator once it has been initialised.</summary>
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class ContainerAwakePatch
    {
        private static void Postfix(Container __instance)
        {
            try { ChestDecorator.Attach(__instance); }
            catch (Exception e) { ModLog.Warn($"Could not attach to chest: {e.Message}"); }
        }
    }
}
