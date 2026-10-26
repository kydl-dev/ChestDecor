using Kydl.Common;
using System;
using HarmonyLib;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// A free camera that works while the decorate window is open, so the chest and its attachments can be
    /// looked at from any side. It needs no admin rights or cheats (it is not the game's own "freefly" command).
    ///
    ///   Hold right mouse button + move the mouse   look around (the drag must start outside the window)
    ///   W / A / S / D  (or the arrow keys)         fly forward / left / back / right
    ///   E or Space  /  Q or Left Ctrl              fly up / down
    ///   Mouse wheel                                zoom in / out (moves along the view direction)
    ///   Left / Right Shift                         move faster
    ///   F                                          back to the normal game camera
    ///
    /// The camera only takes over on the first camera input, starting exactly where the game camera is, so the
    /// normal view is untouched until you use it. The player never moves.
    ///
    /// The camera can never be further from the PLAYER than the "Camera Max Distance" setting (3 m by default), so it
    /// can't be used to scout the surroundings. It is held relative to the player, so it travels along with the
    /// player (on a ship, for instance), and it is kept above the terrain.
    /// </summary>
    internal static class FreeCamera
    {
        private const float FastMultiplier = 3f;
        private const float ZoomStep = 0.4f;          // metres per mouse wheel notch
        private const float MaxPitch = 89f;
        private const float LeashEaseSpeed = 6f;      // m/s: how fast the limit closes in when the camera starts outside it
        private const float GroundClearance = 0.3f;   // metres the camera keeps above the terrain
        private const float MinRadius = 0.5f;

        private static bool _armed;            // the window is open
        private static bool _engaged;          // we are driving the camera instead of the game
        private static bool _looking;          // right mouse button drag in progress
        private static bool _faulted;          // it kept failing (or its patch is missing): stay off for the rest of the session
        private static bool _windowFaulted;    // it threw in the window that is open, or was last open
        private static int _faultStreak;       // windows in a row in which it threw
        private const int MaxFaultStreak = 3;  // this many windows in a row switch it off for the session
        private static bool _hintShown;

        private static Vector3 _offset;        // camera position relative to the player's centre (world axes)
        private static Vector3 _lastMouse;
        private static float _yaw;
        private static float _pitch;
        private static float _leash;           // the radius in force right now (eases down to the configured one)

        internal static bool IsArmed => _armed;

        /// <summary>Called when the decorate window opens.</summary>
        internal static void Begin()
        {
            // one error costs the camera only for the window it happened in; a window that went by without one
            // starts the count again
            if (!_windowFaulted) _faultStreak = 0;
            _windowFaulted = false;

            _armed = !_faulted;
            _engaged = false;
            _looking = false;
            _lastMouse = Input.mousePosition;

            if (_armed && ModSettings.FreeCameraEnabled.Value && !_hintShown)
            {
                _hintShown = true;
                ChestDecorUI.Toast(
                    $"Free camera (stays within {ModSettings.FreeCameraMaxDistance.Value:0.#} m of you): " +
                    "hold right mouse to look, WASD to move, Q/E down/up, wheel to zoom, F to reset");
            }
        }

        /// <summary>Called when the window closes: the game camera takes over again on the next frame.</summary>
        internal static void End()
        {
            _armed = false;
            _engaged = false;
            _looking = false;
        }

        /// <summary>The camera patch could not be applied: never arm (no hint, no captured keys) for the whole session.</summary>
        internal static void Unavailable()
        {
            _faulted = true;
            End();
        }

        internal static void Fault(Exception e)
        {
            // Off for the rest of this window (the game's camera takes over and the error can't repeat every frame).
            End();
            _windowFaulted = true;
            _faultStreak++;

            if (_faultStreak >= MaxFaultStreak)
            {
                _faulted = true;
                ModLog.Warn(
                    $"Free camera disabled for this session after errors in {MaxFaultStreak} windows in a row: {e}");
            }
            else
            {
                ModLog.Warn(
                    $"Free camera turned off for this window after an error (it is tried again next time): {e}");
            }
        }

        /// <summary>Runs every frame after the game has positioned its camera.</summary>
        internal static void Apply(Transform cam)
        {
            if (!_armed || cam == null) return;

            var player = Player.m_localPlayer;
            if (player == null)
            {
                End();
                return;
            }

            // Off in the config, or an admin is using the game's own free fly: don't interfere.
            if (!ModSettings.FreeCameraEnabled.Value || GameCamera.InFreeFly())
            {
                _engaged = false;
                _looking = false;
                return;
            }

            // Mouse movement since last frame, in pixels.
            var mouse = Input.mousePosition;
            var mouseDelta = mouse - _lastMouse;
            _lastMouse = mouse;

            var overWindow = ChestDecorUI.PointerOverWindow();

            // Clicking out in the world takes the keyboard back from a hex / search box that still has focus,
            // otherwise W / A / S / D would be typed into it instead of flying the camera.
            if (!overWindow && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
                ChestDecorUI.ReleaseTextFocus();

            var typing = ChestDecorUI.IsTyping;

            if (!_looking && Input.GetMouseButtonDown(1) && !overWindow) _looking = true;
            if (_looking && !Input.GetMouseButton(1)) _looking = false;

            if (!typing && Input.GetKeyDown(KeyCode.F))
            {
                _engaged = false;   // hand the camera back to the game
                return;
            }

            var look = _looking ? new Vector2(mouseDelta.x, mouseDelta.y) : Vector2.zero;
            var move = typing ? Vector3.zero : ReadMove();
            var scroll = overWindow ? 0f : Input.mouseScrollDelta.y;

            var center = Anchor(player);

            if (!_engaged)
            {
                var anyInput = look.sqrMagnitude > 0f || move.sqrMagnitude > 0f || Mathf.Abs(scroll) > 0.001f;
                if (!anyInput) return;
                Engage(cam, center);
            }

            var fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            var dt = Time.unscaledDeltaTime;

            // --- look
            if (look.sqrMagnitude > 0f)
            {
                var sens = ModSettings.FreeCameraLookSpeed.Value;
                _yaw += look.x * sens;
                _pitch = Mathf.Clamp(_pitch - look.y * sens, -MaxPitch, MaxPitch);
            }

            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // --- fly (forward / sideways follow the view, up / down are world axes)
            var pos = center + _offset;
            var speed = ModSettings.FreeCameraSpeed.Value * (fast ? FastMultiplier : 1f);
            var planar = new Vector3(move.x, 0f, move.z);
            if (planar.sqrMagnitude > 1f) planar.Normalize();
            pos += (rotation * planar + Vector3.up * move.y) * (speed * dt);

            // --- zoom
            if (Mathf.Abs(scroll) > 0.001f)
                pos += rotation * Vector3.forward * (scroll * ZoomStep * (fast ? FastMultiplier : 1f));

            // --- keep the camera close to the player and above the ground
            var limit = Mathf.Max(MinRadius, ModSettings.FreeCameraMaxDistance.Value);
            _leash = Mathf.MoveTowards(_leash, limit, LeashEaseSpeed * dt);

            pos = Constrain(pos, center, _leash);
            _offset = pos - center;

            cam.SetPositionAndRotation(pos, rotation);
        }

        /// <summary>The point the distance limit is measured from: the middle of the player's body.</summary>
        private static Vector3 Anchor(Player player)
        {
            try { return player.GetCenterPoint(); }
            catch { return player.transform.position + Vector3.up; }
        }

        /// <summary>Start from exactly where the game camera is, so nothing jumps.</summary>
        private static void Engage(Transform cam, Vector3 center)
        {
            _offset = cam.position - center;
            var euler = cam.rotation.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;

            // The game's own camera usually sits a bit further out than the limit. Start from where it is and let the
            // limit close in smoothly, rather than snapping the view the moment the first key is pressed.
            _leash = Mathf.Max(ModSettings.FreeCameraMaxDistance.Value, _offset.magnitude);

            _engaged = true;
        }

        /// <summary>
        /// Keeps a position inside the sphere of the given radius around the player and above the terrain. The sphere
        /// has the last word, so the distance limit is never exceeded.
        /// </summary>
        private static Vector3 Constrain(Vector3 pos, Vector3 center, float radius)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                pos = KeepAboveGround(pos);

                var offset = pos - center;
                if (offset.sqrMagnitude > radius * radius)
                    pos = center + offset.normalized * radius;
            }
            return pos;
        }

        private static Vector3 KeepAboveGround(Vector3 pos)
        {
            var zones = ZoneSystem.instance;
            if (zones != null && zones.GetGroundHeight(pos, out var ground))
            {
                var min = ground + GroundClearance;
                if (pos.y < min) pos.y = min;
            }
            return pos;
        }

        private static Vector3 ReadMove()
        {
            var move = Vector3.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move.z += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move.z -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move.x += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move.x -= 1f;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) move.y += 1f;
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl)) move.y -= 1f;
            return move;
        }
    }
}
