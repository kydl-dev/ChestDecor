using Kydl.Common;
using System;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// Decides who receives the refund when a decorated chest is destroyed. One policy for every destruction path:
    ///
    ///  * Removed with the hammer  -> the player who sent the removal request (resolved from the RPC sender).
    ///  * Destroyed by damage      -> the player who delivered the hit, when the attacker is a player.
    ///  * Anything else            -> NO player: monster damage, structural collapse, decay, fire, another mod, or a
    ///                                sender that can't be identified. The materials are then dropped on the ground at
    ///                                the chest, like the pieces a destroyed building drops.
    ///
    /// The machine that happens to own the chest is never used as a fallback. Chest ownership follows whoever is
    /// nearest, so that machine's player can be a bystander (or, on a dedicated server, there is none).
    /// </summary>
    internal static class RefundRecipient
    {
        /// <summary>The player behind a hit, or null (with the reason in <paramref name="cause"/>) when no player did it.</summary>
        public static Player FromHit(HitData hit, out string cause)
        {
            if (hit == null)
            {
                cause = "destroyed without an attacker (collapse, decay or another mod)";
                return null;
            }

            var attacker = hit.GetAttacker();
            var player = attacker as Player;
            if (player != null)
            {
                cause = "destroyed by " + player.GetPlayerName();
                return player;
            }

            cause = attacker != null ? "destroyed by " + attacker.name : "destroyed by damage without an attacker";
            return null;
        }

        /// <summary>
        /// The player who sent a removal RPC, or null when that player can't be found on this machine.
        /// An RPC sender is a peer's session id, which is also the owner of that peer's player character, so the
        /// players in the world are matched by character owner. That works the same for the local player, for
        /// players on other machines, and on a dedicated server.
        /// </summary>
        public static Player FromRpcSender(long sender, out string cause)
        {
            cause = "removed by an unidentified player";
            if (sender == 0L) return null;

            Player player = null;

            // The request came from this very machine (single player, or the host / owner removing it themselves):
            // then the remover is, by definition, the player on this machine. This is not a guess about whoever
            // happens to own the chest: it only applies when the sender id is this machine's own id.
            if (IsThisMachine(sender)) player = Player.m_localPlayer;

            if (player == null) player = FindByCharacterOwner(sender);
            if (player == null) player = FindByPeer(sender);

            if (player != null) cause = "removed by " + player.GetPlayerName();
            return player;
        }

        private static bool IsThisMachine(long peerId)
        {
            try { return peerId == ZNet.GetUID(); }
            catch { return false; }
        }

        /// <summary>The player whose character is owned by this peer (every player's character is owned by their own machine).</summary>
        private static Player FindByCharacterOwner(long peerId)
        {
            try
            {
                var all = Player.GetAllPlayers();
                if (all == null) return null;

                foreach (var player in all)
                {
                    if (player == null) continue;

                    var nview = player.GetComponent<ZNetView>();
                    if (nview == null || !nview.IsValid()) continue;

                    var zdo = nview.GetZDO();
                    if (zdo != null && zdo.GetOwner() == peerId) return player;
                }
            }
            catch (Exception e)
            {
                ModLog.Verbose($"Looking up the removing player by character owner failed: {e.Message}");
            }
            return null;
        }

        /// <summary>Second way: the connected peer's character, through the network peer list.</summary>
        private static Player FindByPeer(long peerId)
        {
            try
            {
                var net = ZNet.instance;
                if (net == null || ZDOMan.instance == null) return null;

                var peer = net.GetPeer(peerId);
                if (peer == null) return null;

                var characterZdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (characterZdo == null) return null;

                var playerId = characterZdo.GetLong(ZDOVars.s_playerID, 0L);
                return playerId != 0L ? Player.GetPlayer(playerId) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
