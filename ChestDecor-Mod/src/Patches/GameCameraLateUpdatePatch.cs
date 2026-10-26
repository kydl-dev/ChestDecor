using System;
using HarmonyLib;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// Runs after the game has placed its camera for the frame (it rebuilds the pose from the player every frame),
    /// and replaces the pose while the free camera is engaged.
    /// </summary>
    [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
    internal static class GameCameraLateUpdatePatch
    {
        private static void Postfix(GameCamera __instance)
        {
            if (!FreeCamera.IsArmed) return;

            try { FreeCamera.Apply(__instance.transform); }
            catch (Exception e) { FreeCamera.Fault(e); }
        }
    }
}
