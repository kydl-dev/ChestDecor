using System;
using Kydl.Common;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// The chest's editor lock (split from ChestDecorator, class-size rule): only one player at a time may have the window open for a chest.
    /// </summary>
    internal sealed class ChestEditLock
    {
        // ------------------------------------------------------------------------------------------------
        // Editor lock: only one player at a time may have this chest's Chest Decor window open
        //
        // The lock lives in the chest's ZDO (so every player sees it): who holds it, their name, and a heartbeat
        // time. The holder refreshes the heartbeat every couple of seconds while the window is open and clears the
        // lock when it closes. A lock whose heartbeat has stopped (crash, disconnect, game closed) counts as free
        // after <see cref="LockTimeoutSeconds"/>, so a chest can never stay locked for good.
        // ------------------------------------------------------------------------------------------------

        public const string LockIdKey = "ChestDecor_EditorId";
        public const string LockNameKey = "ChestDecor_EditorName";
        public const string LockBeatKey = "ChestDecor_EditorBeat";

        /// <summary>How often (seconds) the editor refreshes its heartbeat.</summary>
        public const float LockHeartbeatSeconds = 2f;

        /// <summary>A lock without a heartbeat for this long (seconds) is treated as abandoned.</summary>
        public const double LockTimeoutSeconds = 10.0;

        /// <summary>The player id this component holds the lock as (0 = not holding it).</summary>
        private long _lockId;
        readonly ChestDecorator _owner;

        internal ChestEditLock(ChestDecorator owner) { _owner = owner; }

        /// <summary>Time shared by all players (the server's), in milliseconds.</summary>
        private static long NowMs()
        {
            var seconds = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : (double)Time.realtimeSinceStartup;
            return (long)(seconds * 1000.0);
        }

        /// <summary>True when somebody other than <paramref name="me"/> holds a lock that is still alive.</summary>
        private static bool HeldByOther(ZDO zdo, long me, out string holder)
        {
            holder = null;
            var id = zdo.GetLong(LockIdKey, 0L);
            if (id == 0L || id == me) return false;

            // a heartbeat far from "now" in either direction is stale (also covers a time that was reset)
            var age = (NowMs() - zdo.GetLong(LockBeatKey, 0L)) / 1000.0;
            if (age > LockTimeoutSeconds || age < -LockTimeoutSeconds) return false;

            holder = zdo.GetString(LockNameKey, "");
            if (string.IsNullOrEmpty(holder)) holder = "another player";
            return true;
        }

        private void WriteLock(ZDO zdo, long id, string name)
        {
            zdo.Set(LockIdKey, id);
            zdo.Set(LockBeatKey, NowMs());
            if (!string.IsNullOrEmpty(name)) zdo.Set(LockNameKey, name);
        }

        /// <summary>
        /// Tries to become the one editor of this chest. The chest is only claimed when nobody else holds a live
        /// lock, so a player who is already editing is not disturbed. Two players asking in the same instant are
        /// sorted out by <see cref="HoldsLock"/>, which the caller checks again once the data has had time to sync.
        /// </summary>
        public ChestDecorator.LockResult TryLock(Player player, out string holder)
        {
            holder = null;
            if (player == null || !_owner.ValidZdo) return ChestDecorator.LockResult.Unavailable;

            var me = player.GetPlayerID();
            if (me == 0L) return ChestDecorator.LockResult.Unavailable;

            if (HeldByOther(_owner.NView.GetZDO(), me, out holder)) return ChestDecorator.LockResult.Busy;

            _owner.NView.ClaimOwnership();
            var zdo = _owner.NView.GetZDO();
            if (HeldByOther(zdo, me, out holder)) return ChestDecorator.LockResult.Busy;

            WriteLock(zdo, me, player.GetPlayerName());
            _lockId = me;
            return ChestDecorator.LockResult.Acquired;
        }

        /// <summary>True when this player is still the registered editor of the chest, as the chest's data says right now.</summary>
        public bool HoldsLock()
        {
            return _lockId != 0L && _owner.ValidZdo && _owner.NView.GetZDO().GetLong(LockIdKey, 0L) == _lockId;
        }

        /// <summary>Name of the player who holds a live lock other than ours, or null.</summary>
        public string LockHolderName()
        {
            if (!_owner.ValidZdo) return null;
            return HeldByOther(_owner.NView.GetZDO(), _lockId, out var holder) ? holder : null;
        }

        /// <summary>Keeps the lock alive. Returns false when somebody else has taken the chest over.</summary>
        public bool Heartbeat()
        {
            if (_lockId == 0L || !_owner.ValidZdo) return false;

            if (HeldByOther(_owner.NView.GetZDO(), _lockId, out _))
            {
                _lockId = 0L;
                return false;
            }

            _owner.NView.ClaimOwnership();   // only the owner's writes reach the other players
            WriteLock(_owner.NView.GetZDO(), _lockId, null);
            return true;
        }

        /// <summary>Gives the chest back (only clears the lock if it is still ours).</summary>
        public void Unlock()
        {
            var me = _lockId;
            _lockId = 0L;
            if (me == 0L) return;

            try
            {
                if (!_owner.ValidZdo) return;
                var zdo = _owner.NView.GetZDO();
                if (zdo.GetLong(LockIdKey, 0L) != me) return;

                _owner.NView.ClaimOwnership();
                zdo.Set(LockIdKey, 0L);
                zdo.Set(LockBeatKey, 0L);
                zdo.Set(LockNameKey, "");
            }
            catch (Exception e)
            {
                ModLog.Verbose($"Releasing the chest editor lock failed: {e.Message}");
            }
        }

    }
}
