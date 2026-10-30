using System;
using BepInEx.Configuration;
using HarmonyLib;
using Kydl.Common;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>Adds "[K] Decorate chest" to the chest's hover text.</summary>
    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class ContainerHoverPatch
    {
        // The game asks for the hover text of the looked-at chest every frame. Whether the hint applies (ward and
        // access rules, who built the chest) does not change from one frame to the next, so the answer is kept
        // for a short moment instead of being worked out again every frame. Only the hint is cached: pressing the
        // hotkey still runs the full check (see DecorOpener.Tick).
        private const float CacheSeconds = 0.5f;

        private static int _containerId;
        private static float _validUntil;
        private static bool _show;

        // the hint text, rebuilt only when the hotkey setting changes
        private static KeyboardShortcut _hintKey;
        private static string _hint;

        private static void Postfix(Container __instance, ref string __result)
        {
            if (!ModSettings.AnyEditorPage || !ModSettings.ShowHoverHint.Value) return;     // AnyEditorPage includes EnableAll

            var player = Player.m_localPlayer;
            if (player == null) return;
            if (!ShowFor(__instance, player)) return;

            __result += Hint();
        }

        private static bool ShowFor(Container container, Player player)
        {
            var id = container.GetInstanceID();
            var now = Time.unscaledTime;
            if (id == _containerId && now < _validUntil) return _show;

            var decorator = container.GetComponent<ChestDecorator>();
            var show = decorator != null && decorator.CanBeDecorated && DecorOpener.CanCustomize(container, player);

            _containerId = id;
            _validUntil = now + CacheSeconds;
            _show = show;
            return show;
        }

        private static string Hint()
        {
            var key = ModSettings.Hotkey.Value;
            if (_hint == null || !key.Equals(_hintKey))
            {
                _hintKey = key;
                _hint = $"\n[<color=yellow><b>{key}</b></color>] Decorate chest";
            }
            return _hint;
        }
    }
}
