using Kydl.Common;
using System;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// An orbit camera that works while the decorate window is open, so the chest and its attachments can be looked at from any side
    /// (user, 2026-10-26: it always circles the chest, aimed at its middle, at the distance set by the zoom; replaces the free-flying
    /// camera of 1.0.0, which was hard to keep pointed at the chest). It needs no admin rights or cheats.
    ///
    ///   A / D  (or Left / Right arrow)             circle left / right around the chest
    ///   W / S  (or Up / Down arrow)                circle up / down (look from above / from lower)
    ///   Hold right mouse button + move the mouse   the same, with the mouse (the drag must start outside the window)
    ///   Mouse wheel (outside the window), E / Q    zoom in / out: 1 m .. Camera Max Distance from the chest's box
    ///   Left / Right Shift                         3x faster
    ///   F                                          back to the normal game camera
    ///
    /// HARD LIMITS (user, after the 2nd test, 2026-10-26: "I can go into the flooring and into the chest"): a camera spot is only taken
    /// when it is at least 1 m from the chest's box, has a free line of sight to the chest's middle (checked both ways), has 30 cm of free
    /// space around it and is above the terrain. A move that would break one of these is simply not made: the camera stops there, like
    /// against a wall. (The first orbit version pulled the camera closer instead, which put it inside the chest and under the floor.)
    /// The camera only takes over on the first camera input, starting from where the game camera is. The player never moves.
    /// </summary>
    internal static class FreeCamera
    {
        private const float FastMultiplier = 3f;
        private const float ZoomStep = 0.4f;          // metres per mouse wheel notch
        private const float DegreesPerSpeed = 30f;    // keys circle at Camera Speed x 30 degrees per second (3 -> 90 deg/s)
        private const float SearchPitchStep = 5f;     // finding a free spot: degrees tried upwards
        private const float SearchZoomStep = 0.5f;    // finding a free spot: metres tried closer

        private static bool _armed;            // the window is open
        private static bool _engaged;          // we are driving the camera instead of the game
        private static bool _looking;          // right mouse button drag in progress
        private static bool _faulted;          // it kept failing (or its patch is missing): stay off for the rest of the session
        private static bool _windowFaulted;    // it threw in the window that is open, or was last open
        private static int _faultStreak;       // windows in a row in which it threw
        private const int MaxFaultStreak = 3;  // this many windows in a row switch it off for the session
        private static bool _hintShown;

        private static Vector3 _lastMouse;
        private static float _yaw;             // the last accepted (free) spot
        private static float _pitch;
        private static float _zoom;            // metres from the chest's box
        private static string _stoppedBy;      // for the log: what the last refused move ran into (null = moving freely)
        private static int _blockMask;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static readonly Collider[] Overlaps = new Collider[16];

        internal static bool IsArmed => _armed;

        /// <summary>Called when the decorate window opens.</summary>
        internal static void Begin()
        {
            // one error costs the camera only for the window it happened in; a window that went by without one starts the count again
            if (!_windowFaulted) _faultStreak = 0;
            _windowFaulted = false;

            _armed = !_faulted;
            _engaged = false;
            _looking = false;
            _lastMouse = Input.mousePosition;

            if (_armed && ModSettings.FreeCameraEnabled.Value && !_hintShown)
            {
                _hintShown = true;
                ChestDecorUI.Toast("Camera: WASD / arrows or right mouse to circle the chest, wheel or E / Q to zoom, F to reset");
            }
            ModLog.Verbose("FreeCamera: armed " + _armed + " (orbit around the chest, 1 .. " + ModSettings.FreeCameraMaxDistance.Value.ToString("0.#") + " m from it)");
        }

        /// <summary>Called when the window closes: the game camera takes over again on the next frame.</summary>
        internal static void End()
        {
            if (_engaged) ModLog.Verbose("FreeCamera: released to the game camera");
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
                ModLog.Warn($"Free camera disabled for this session after errors in {MaxFaultStreak} windows in a row: {e}");
            }
            else
            {
                ModLog.Warn($"Free camera turned off for this window after an error (it is tried again next time): {e}");
            }
        }

        /// <summary>Runs every frame after the game has positioned its camera.</summary>
        internal static void Apply(Transform cam)
        {
            if (!_armed || cam == null) return;

            var player = Player.m_localPlayer;
            var decor = ChestDecorUI.Decor;
            if (player == null || decor == null)
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
            // otherwise W / A / S / D would be typed into it instead of moving the camera.
            if (!overWindow && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
                ChestDecorUI.ReleaseTextFocus();

            var typing = ChestDecorUI.IsTyping;

            if (!_looking && Input.GetMouseButtonDown(1) && !overWindow) _looking = true;
            if (_looking && !Input.GetMouseButton(1)) _looking = false;

            if (!typing && Input.GetKeyDown(KeyCode.F))
            {
                if (_engaged) ModLog.Verbose("FreeCamera: F - back to the game camera");
                _engaged = false;   // hand the camera back to the game
                return;
            }

            var look = _looking ? new Vector2(mouseDelta.x, mouseDelta.y) : Vector2.zero;
            var keys = typing ? Vector3.zero : ReadKeys();
            var scroll = overWindow ? 0f : Input.mouseScrollDelta.y;

            var spot = new Spot(decor.ModelRoot, decor.LocalBounds, player.transform);
            var max = ModSettings.FreeCameraMaxDistance.Value;

            if (!_engaged)
            {
                var anyInput = look.sqrMagnitude > 0f || keys.sqrMagnitude > 0f || Mathf.Abs(scroll) > 0.001f;
                if (!anyInput || !Engage(cam, spot, max)) return;
            }

            var fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            var dt = Time.unscaledDeltaTime;
            var mult = fast ? FastMultiplier : 1f;

            // --- the move asked for: circle (mouse drag, keys) and zoom
            float yaw = _yaw, pitch = _pitch;
            if (look.sqrMagnitude > 0f)
            {
                var sens = ModSettings.FreeCameraLookSpeed.Value;
                yaw += look.x * sens;
                pitch -= look.y * sens;
            }
            var degrees = ModSettings.FreeCameraSpeed.Value * DegreesPerSpeed * mult * dt;
            yaw -= keys.x * degrees;          // A (x = -1) moves the camera to its left around the chest
            pitch += keys.y * degrees;        // W (y = +1) goes up and looks down more
            pitch = CameraOrbit.ClampPitch(pitch);
            var zoom = CameraOrbit.ClampDistance(_zoom - (scroll * ZoomStep * mult + keys.z * ModSettings.FreeCameraSpeed.Value * mult * dt), max);

            // --- take it only where the camera is free; otherwise the part that is possible, otherwise stay (a hard stop)
            Vector3 pos;
            string why;
            if (spot.TryPlace(yaw, pitch, zoom, out pos, out why)) { if (yaw != _yaw || pitch != _pitch || zoom != _zoom) Free(); Accept(yaw, pitch, zoom); }
            else if (zoom != _zoom && spot.TryPlace(_yaw, _pitch, zoom, out pos, out _)) { Stopped(why); Accept(_yaw, _pitch, zoom); }
            else if ((yaw != _yaw || pitch != _pitch) && spot.TryPlace(yaw, pitch, _zoom, out pos, out _)) { Stopped(why); Accept(yaw, pitch, _zoom); }
            else if (spot.TryPlace(_yaw, _pitch, CameraOrbit.ClampDistance(_zoom, max), out pos, out var stay)) { Stopped(why); _zoom = CameraOrbit.ClampDistance(_zoom, max); }
            else if (!FindFreeSpot(spot, _yaw, _pitch, CameraOrbit.ClampDistance(_zoom, max), out pos))
            {
                // something was built / moved into the camera's spot and there is no free spot left: hand back to the game camera
                ModLog.Info("FreeCamera: no free spot around the chest any more (" + stay + "), back to the game camera");
                _engaged = false;
                return;
            }

            cam.SetPositionAndRotation(pos, Quaternion.LookRotation(spot.Centre - pos, Vector3.up));
        }

        private static void Accept(float yaw, float pitch, float zoom)
        {
            if (Mathf.Floor(zoom * 2f) != Mathf.Floor(_zoom * 2f)) ModLog.Verbose("FreeCamera: zoom " + zoom.ToString("0.0") + " m from the chest");
            _yaw = yaw; _pitch = pitch; _zoom = zoom;
        }

        /// <summary>The whole asked move was possible: logs once that nothing stops the camera any more.</summary>
        private static void Free()
        {
            if (_stoppedBy == null) return;
            _stoppedBy = null;
            ModLog.Verbose("FreeCamera: moving freely again");
        }

        /// <summary>Logs (once per obstacle) that a move was refused.</summary>
        private static void Stopped(string why)
        {
            if (why == _stoppedBy) return;
            _stoppedBy = why;
            ModLog.Verbose("FreeCamera: stopped - " + why);
        }

        /// <summary>Start from the direction the game camera looks at the chest from. Expected outcome: true when a free spot was found.</summary>
        private static bool Engage(Transform cam, Spot spot, float max)
        {
            var offset = cam.position - spot.Centre;
            var yaw = CameraOrbit.YawOf(offset);
            var pitch = CameraOrbit.PitchOf(offset);
            var zoom = CameraOrbit.ClampDistance(CameraOrbit.BoxDistance(spot.Box, spot.Root.InverseTransformPoint(cam.position)), max);
            _stoppedBy = null;
            if (!FindFreeSpot(spot, yaw, pitch, zoom, out _))
            {
                ModLog.Info("FreeCamera: no free spot 1 m or more from the chest (walls / ceiling / floor too close) - the game camera stays");
                ChestDecorUI.Toast("No room for the camera around this chest.");
                _engaged = false;
                return false;
            }
            _engaged = true;
            ModLog.Verbose("FreeCamera: took over (yaw " + _yaw.ToString("0") + ", pitch " + _pitch.ToString("0") + ", " + _zoom.ToString("0.0") + " m from the chest)");
            return true;
        }

        /// <summary>
        /// The nearest free spot: the given one, else higher up (5 degree steps), else closer (0.5 m steps, never under 1 m).
        /// Expected outcome: true and the spot accepted when one is free.
        /// </summary>
        private static bool FindFreeSpot(Spot spot, float yaw, float pitch, float zoom, out Vector3 pos)
        {
            for (float z = zoom; z >= CameraOrbit.MinDistance - 0.001f; z -= SearchZoomStep)
                for (float p = pitch; p <= CameraOrbit.MaxPitch + 0.001f; p += SearchPitchStep)
                {
                    var tryPitch = Mathf.Min(p, CameraOrbit.MaxPitch);
                    if (!spot.TryPlace(yaw, tryPitch, z, out pos, out _)) continue;
                    if (tryPitch != pitch || z != zoom) ModLog.Verbose("FreeCamera: moved to a free spot (pitch " + tryPitch.ToString("0") + ", " + z.ToString("0.0") + " m)");
                    _yaw = yaw; _pitch = tryPitch; _zoom = z;
                    return true;
                }
            pos = Vector3.zero;
            return false;
        }

        /// <summary>One frame's chest and the checks for a camera spot around it.</summary>
        private readonly struct Spot
        {
            public readonly Transform Root;
            public readonly Bounds Box;          // the chest, in Root's space
            public readonly Transform Player;
            public readonly Vector3 Centre;      // the chest's middle, world

            public Spot(Transform root, Bounds box, Transform player)
            {
                Root = root; Box = box; Player = player;
                Centre = root.TransformPoint(box.center);
            }

            /// <summary>
            /// Where the camera would be for these angles and zoom, and whether it is free there (see the class comment).
            /// Expected outcome: true exactly when every hard limit holds; `why` names the first one broken.
            /// </summary>
            public bool TryPlace(float yaw, float pitch, float zoom, out Vector3 pos, out string why)
            {
                var dirLocal = Root.InverseTransformDirection(CameraOrbit.Offset(yaw, pitch)).normalized;
                pos = Root.TransformPoint(Box.center + dirLocal * CameraOrbit.ReachFor(Box, dirLocal, zoom));

                var zones = ZoneSystem.instance;
                if (zones != null && zones.GetGroundHeight(pos, out var ground) && pos.y < ground + CameraOrbit.Clearance)
                {
                    why = "the terrain (" + (pos.y - ground).ToString("0.00") + " m above it)";
                    return false;
                }
                why = Blocker(Centre, pos) ?? Blocker(pos, Centre) ?? Crowded(pos);
                return why == null;
            }

            /// <summary>The first solid thing between two points (not the chest, not the player), described for the log, or null.</summary>
            string Blocker(Vector3 from, Vector3 to)
            {
                var dir = to - from;
                var length = dir.magnitude;
                if (length < 1e-4f) return null;
                int n = Physics.RaycastNonAlloc(from, dir / length, Hits, length + CameraOrbit.Clearance, Mask(), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    if (Counts(Hits[i].collider)) return Describe(Hits[i].collider) + " between the camera and the chest";
                return null;
            }

            /// <summary>Something solid within the clearance radius of the camera spot, described for the log, or null.</summary>
            string Crowded(Vector3 pos)
            {
                int n = Physics.OverlapSphereNonAlloc(pos, CameraOrbit.Clearance, Overlaps, Mask(), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    if (Counts(Overlaps[i])) return Describe(Overlaps[i]) + " closer than " + (CameraOrbit.Clearance * 100f).ToString("0") + " cm to the camera";
                return null;
            }

            bool Counts(Collider c) { return c != null && !c.transform.IsChildOf(Root) && !c.transform.IsChildOf(Player); }
        }

        private static int Mask()
        {
            if (_blockMask == 0) _blockMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            return _blockMask;
        }

        /// <summary>"'wood_floor' (layer piece)": the building piece (or root object) a collider belongs to.</summary>
        private static string Describe(Collider c)
        {
            var piece = c.GetComponentInParent<Piece>();
            var name = piece != null ? piece.name : c.transform.root.name;
            return "'" + name.Replace("(Clone)", "") + "' (layer " + LayerMask.LayerToName(c.gameObject.layer) + ")";
        }

        /// <summary>x = circle right (+) / left (-), y = up (+) / down (-), z = zoom in (+) / out (-).</summary>
        private static Vector3 ReadKeys()
        {
            var move = Vector3.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move.y -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move.x += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move.x -= 1f;
            if (Input.GetKey(KeyCode.E)) move.z += 1f;
            if (Input.GetKey(KeyCode.Q)) move.z -= 1f;
            return move;
        }
    }
}
