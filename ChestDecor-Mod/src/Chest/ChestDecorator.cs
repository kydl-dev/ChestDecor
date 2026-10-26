using Kydl.Common;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// Lives on every player-built container. Discovers the colourable "parts" (distinct materials) of the
    /// chest, applies colour tints locally and stores/reads the saved colours in the chest's ZDO so they
    /// persist with the world and sync to other players that have the mod.
    ///
    /// It also owns the chest's attachments (see <see cref="ChestAttachment"/>): purely visual items shown on
    /// the chest, stored in the ZDO under their own key, independent of the colours.
    /// </summary>
    public class ChestDecorator : MonoBehaviour
    {
        /// <summary>ZDO key the colours are stored under.</summary>
        public const string ZdoKey = "ChestDecor_Colors";

        internal const string ColorProperty = "_Color";
        internal const string MainTexProperty = "_MainTex";

        /// <summary>One colourable part of the chest (all renderers sharing a material name).</summary>
        public class Slot
        {
            public string Key;
            public string DisplayName;
            public Color OriginalColor;
        }

        private Container _container;
        private ZNetView _nview;

        private readonly List<Slot> _slots = new();
        private readonly List<ChestDecoratorRendererInfo> _renderers = new();

        // materials whose single texture was split into two parts: material key -> analysis
        private readonly Dictionary<string, ZoneMap> _zones = new();
        private ChestPainter _painter;          // the colours on the materials (ChestPainter.cs)
        private ChestAttachmentView _view;     // the attached items shown on the chest (ChestAttachmentView.cs)
        private ChestEditLock _lock;           // one editor at a time (ChestEditLock.cs)

        private bool _scanned;
        private bool _editing;
        private string _lastSeenZdo;

        // attachments
        /// <summary>ZDO key the attachments are stored under.</summary>
        public const string AttachZdoKey = "ChestDecor_Attach";
        /// <summary>Upper limit of the "Max Attachments" setting (the editor has room for this many).</summary>
        public const int HardMaxAttachments = 8;

        private string _lastSeenAttach;
        private string _costCheckedFor;   // the attachment data already checked for unrecorded costs

        public Container Container => _container;
        public IReadOnlyList<Slot> Slots
        {
            get { EnsureScanned(); return _slots; }
        }

        // ------------------------------------------------------------------------------------------------
        // Attach / lifecycle
        // ------------------------------------------------------------------------------------------------

        /// <summary>Called from the Container.Awake patch.</summary>
        public static void Attach(Container container)
        {
            if (container == null || container.GetComponent<ChestDecorator>() != null) return;

            // Only chests: carts, ships (cargo hold), incinerators and cargo crates have a Container too, but they
            // are vehicles / machines / loose cargo, not chests.
            if (!IsChest(container)) return;

            var nview = container.GetComponentInParent<ZNetView>();
            if (nview == null || !nview.IsValid()) return; // placement ghosts etc.

            // Whether a chest was built by a player is checked when it is used (CanBeDecorated), because the
            // game only stamps the creator on a freshly placed piece after Awake has already run.
            if (container.GetComponentInParent<Piece>() == null) return;

            container.gameObject.AddComponent<ChestDecorator>();
        }

        /// <summary>False for containers that belong to something that is not a chest (see <see cref="Attach"/>).</summary>
        private static bool IsChest(Container container)
        {
            if (container.m_wagon != null) return false;                      // carts
            if (container.GetComponentInParent<Vagon>() != null) return false;
            if (container.GetComponentInParent<Ship>() != null) return false;  // cargo hold of a boat / raft
            if (container.GetComponentInParent<Incinerator>() != null) return false;

            var root = container.GetComponentInParent<Piece>();
            var name = root != null ? root.gameObject.name : container.gameObject.name;
            if (name.IndexOf("cargocrate", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }

        private void Awake()
        {
            _container = GetComponent<Container>();
            _nview = GetComponentInParent<ZNetView>();
            _painter = new ChestPainter(_renderers, _zones);
            _view = new ChestAttachmentView(this);
            _lock = new ChestEditLock(this);
        }

        private void Start()
        {
            ApplySaved();
            // Poll for changes made by other players. Randomised so chests don't all tick on the same frame.
            InvokeRepeating(nameof(PollRemote), UnityEngine.Random.Range(0.5f, 1.5f), 1f);
        }

        private void OnDestroy()
        {
            CancelInvoke();
            Unlock();
            _painter?.DestroyClones();
            ModLog.Verbose("ChestDecorator: '" + name + "' destroyed (lock released, material clones freed)");
        }

        private bool Valid => _nview != null && _nview.IsValid() && _nview.GetZDO() != null;
        /// <summary>The chest's network view and whether its data can be read (for ChestEditLock).</summary>
        internal ZNetView NView => _nview;
        internal bool ValidZdo => Valid;


        /// <summary>False for chests the config says are off limits (e.g. not built by a player).</summary>
        public bool CanBeDecorated
        {
            get
            {
                if (!Valid) return false;
                if (!ModSettings.OnlyPlayerBuilt.Value) return true;
                // "creator" is the ZDO key Piece.SetCreator writes; 0 means world-generated.
                return _nview.GetZDO().GetLong("creator", 0L) != 0L;
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Scanning
        // ------------------------------------------------------------------------------------------------

        private void EnsureScanned()
        {
            if (_scanned) return;
            _scanned = true;
            ChestPartScanner.Scan(_nview != null ? _nview.gameObject : gameObject, IsAttachment, _slots, _renderers, _zones);
            ModLog.Verbose("ChestDecorator: '" + name + "' scanned - " + _slots.Count + " colourable part(s), " + _renderers.Count + " renderer(s), " + _zones.Count + " split texture(s)");
        }

        internal static string ZoneKey(string materialKey, int zone) => ChestPartScanner.ZoneKey(materialKey, zone);

        // ------------------------------------------------------------------------------------------------
        // Applying colours (local visual only)
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Shows the given colours on the chest. Slots missing from the dictionary use their original look.
        /// Nothing is saved.
        /// </summary>
        public void Apply(IDictionary<string, PartColor> colors)
        {
            // Nothing to show and nothing ever applied: skip scanning the chest's renderers entirely.
            if (!_scanned && (colors == null || colors.Count == 0)) return;

            EnsureScanned();
            _painter.Paint(colors);
        }

        // ------------------------------------------------------------------------------------------------
        // Persistence
        // ------------------------------------------------------------------------------------------------

        /// <summary>Reads the colours currently saved on the chest.</summary>
        public Dictionary<string, PartColor> LoadSaved()
        {
            if (!Valid) return new Dictionary<string, PartColor>();
            return ChestColorData.Parse(_nview.GetZDO().GetString(ZdoKey, ""));
        }

        /// <summary>Shows whatever is currently saved on the chest.</summary>
        public void ApplySaved()
        {
            if (!Valid) return;
            _lastSeenZdo = _nview.GetZDO().GetString(ZdoKey, "");
            Apply(ChestColorData.Parse(_lastSeenZdo));
            ApplySavedAttachments();
        }

        /// <summary>Writes the colours into the chest's ZDO (synced to everyone) and shows them.</summary>
        public bool Save(IDictionary<string, PartColor> colors)
        {
            if (!Valid) return false;

            _nview.ClaimOwnership();
            var data = ChestColorData.Serialize(colors);
            _nview.GetZDO().Set(ZdoKey, data);
            _lastSeenZdo = data;
            Apply(colors);
            return true;
        }

        /// <summary>While the editor is open, changes from other players must not overwrite the local preview.</summary>
        public void BeginEdit() => _editing = true;

        public void EndEdit()
        {
            _editing = false;
            ApplySaved();
        }

        /// <summary>
        /// A chest further from the local player than this (metres) is not polled: nobody can be looking at it, and
        /// the next poll after the player comes back within range shows whatever changed in the meantime.
        /// </summary>
        private const float PollRange = 64f;

        private void PollRemote()
        {
            if (_editing || !Valid) return;

            // nothing to refresh for a chest the player is nowhere near (no data is read at all)
            var local = Player.m_localPlayer;
            if (local != null)
            {
                var range = PollRange;
                if ((transform.position - local.transform.position).sqrMagnitude > range * range) return;
            }

            var zdo = _nview.GetZDO();

            var colors = zdo.GetString(ZdoKey, "");
            if (colors != _lastSeenZdo)
            {
                _lastSeenZdo = colors;
                Apply(ChestColorData.Parse(colors));
            }

            var attach = zdo.GetString(AttachZdoKey, "");
            if (attach != _lastSeenAttach)
                ApplySavedAttachments();

            RecordUnrecordedCosts(zdo, attach);
        }

        /// <summary>
        /// An attachment saved without its cost can only be refunded by working it out from the catalog, which is
        /// impossible once the item has left it (removed or changed mod
        /// content). While the item is still in the catalog, its cost is recorded on the attachment here, so the
        /// refund no longer depends on the catalog from then on. It records what a refund would have given anyway
        /// (the original price can't be known), so nothing changes in the amount, it just can't be lost later.
        ///
        /// Only the machine that owns the chest does this, and only while nobody has the editor open (their window
        /// would otherwise see the chest "changed by someone else"). It also never builds the catalog itself: it
        /// waits until something else has, so it can't cause a build while the world is still loading.
        /// </summary>
        private void RecordUnrecordedCosts(ZDO zdo, string data)
        {
            if (string.IsNullOrEmpty(data) || data == _costCheckedFor) return;
            if (!AttachCatalog.IsBuilt) return;   // try again on a later poll
            if (!_nview.IsOwner() || LockHolderName() != null) return;

            var list = ChestAttachment.Parse(data, HardMaxAttachments);
            var recorded = 0;
            foreach (var attachment in list)
            {
                if (attachment == null || !attachment.Paid || attachment.Cost != null) continue;

                var entry = AttachCatalog.FindIfBuilt(attachment.Prefab);
                if (entry == null) continue;   // already gone from the catalog: nothing left to record

                attachment.Cost = AttachPayment.CostOf(entry);
                recorded++;
            }

            if (recorded > 0)
            {
                data = ChestAttachment.Serialize(list);
                zdo.Set(AttachZdoKey, data);
                _lastSeenAttach = data;   // what is shown does not change
                ModLog.Verbose($"Recorded the paid cost of {recorded} older attachment(s) on a chest.");
            }
            _costCheckedFor = data;
        }

        // ------------------------------------------------------------------------------------------------
        // Attachments
        // ------------------------------------------------------------------------------------------------

        /// <summary>How many items this chest can hold (config, 1 - 8).</summary>
        public int MaxAttachments => Mathf.Clamp(ModSettings.MaxAttachments.Value, 1, HardMaxAttachments);

        /// <summary>The object whose local space attachment positions are expressed in (the chest piece's root).</summary>
        internal Transform ModelRoot => _nview != null ? _nview.transform : transform;

        private bool IsAttachment(Transform t) => _view != null && _view.IsAttachment(t);

        /// <summary>Shows exactly these attachments on the chest (local visual only, nothing is saved). Cheap: models are reused.</summary>
        public void ShowAttachments(IList<ChestAttachment> list) => _view.ShowAttachments(list);
        /// <summary>Bounding box of the chest itself (attachments excluded), in the chest's local space.</summary>
        public Bounds LocalBounds => _view.LocalBounds;
        /// <summary>Bounds of an attachment's geometry around its own position, with its rotation and size applied.</summary>
        public Bounds ExtentOf(ChestAttachment a) => _view.ExtentOf(a);
        /// <summary>A starting placement for a new attachment: resting on top of the chest, spread along its width by index.</summary>
        public ChestAttachment CreateDefault(string prefab, int index) => _view.CreateDefault(prefab, index);

        // --- editor lock (ChestEditLock) -------------------------------------------------------------------------------------
        public enum LockResult { Acquired, Busy, Unavailable }
        /// <summary>How often (seconds) the editor refreshes its heartbeat.</summary>
        public const float LockHeartbeatSeconds = ChestEditLock.LockHeartbeatSeconds;
        public LockResult TryLock(Player player, out string holder) => _lock.TryLock(player, out holder);
        public bool HoldsLock() => _lock != null && _lock.HoldsLock();
        public string LockHolderName() => _lock != null ? _lock.LockHolderName() : null;
        public bool Heartbeat() => _lock != null && _lock.Heartbeat();
        public void Unlock() { if (_lock != null) _lock.Unlock(); }

        /// <summary>Reads the attachments currently saved on the chest.</summary>
        public List<ChestAttachment> LoadSavedAttachments()
        {
            if (!Valid) return new List<ChestAttachment>();
            return ChestAttachment.Parse(_nview.GetZDO().GetString(AttachZdoKey, ""), HardMaxAttachments);
        }

        /// <summary>Shows whatever is currently saved on the chest.</summary>
        private void ApplySavedAttachments()
        {
            if (!Valid) return;
            _lastSeenAttach = _nview.GetZDO().GetString(AttachZdoKey, "");
            ShowAttachments(ChestAttachment.Parse(_lastSeenAttach, HardMaxAttachments));
        }

        /// <summary>
        /// Refunds the paid attachments when the chest itself is destroyed.
        ///
        /// This must run before WearNTear destroys the GameObject because the attachment list lives in the chest ZDO.
        /// It works one attachment at a time and the saved list is the only state:
        ///  * An attachment is taken off the saved list only after its materials were really handed out, so a second
        ///    destruction callback (or another mod wrapping WearNTear) finds exactly what is still owed: nothing is
        ///    paid twice and nothing that failed is forgotten.
        ///  * If handing the materials to <paramref name="recipient"/> fails part-way, whatever is left is dropped on
        ///    the ground at the chest instead, because the chest (and with it the saved list) is about to disappear.
        ///  * If even that fails, the attachment stays on the chest with only the outstanding materials as its cost,
        ///    for another attempt (a later callback, or removing it by hand if the chest survives).
        ///
        /// <paramref name="recipient"/> comes from <see cref="RefundRecipient"/>; null means no player is responsible
        /// and everything is dropped at the chest. <paramref name="cause"/> is only used for the log.
        /// </summary>
        internal void RefundAttachmentsOnDestroy(Player recipient, string cause)
        {
            if (!ModSettings.RefundOnRemove.Value || !Valid) return;

            // only the machine that owns the chest handles it: it is the one that writes the chest's data
            if (!_nview.IsOwner()) return;

            var zdo = _nview.GetZDO();
            if (zdo == null) return;

            var data = zdo.GetString(AttachZdoKey, "");
            if (string.IsNullOrEmpty(data)) return;

            var attachments = ChestAttachment.Parse(data, HardMaxAttachments);
            if (attachments.Count == 0) return;

            var position = ModelRoot.position;
            ModLog.Verbose(
                $"Chest {cause}: refunding {attachments.Count} attachment(s) " +
                (recipient != null ? "to " + recipient.GetPlayerName() : "on the ground at the chest") + ".");

            var remaining = new List<ChestAttachment>(attachments);
            foreach (var attachment in attachments)
            {
                List<PaidMaterial> outstanding = null;

                if (attachment != null && attachment.Paid && !string.IsNullOrEmpty(attachment.Prefab))
                {
                    var owed = AttachPayment.OwedMaterials(attachment);

                    outstanding = AttachPayment.Give(attachment.Prefab, owed, recipient, position);
                    if (outstanding.Count > 0 && recipient != null)
                    {
                        // the recipient couldn't be given everything: the ground at the chest is the second route
                        outstanding = AttachPayment.Give(attachment.Prefab, outstanding, null, position);
                    }
                }

                if (outstanding != null && outstanding.Count > 0)
                {
                    // keep it on the chest, owing only what was not handed out
                    var kept = attachment.Clone();
                    kept.Cost = outstanding;
                    remaining[remaining.IndexOf(attachment)] = kept;
                    ModLog.Warn(
                        $"Refund of '{attachment.Prefab}' is incomplete; {outstanding.Count} material(s) are kept on the chest for another attempt.");
                }
                else
                {
                    remaining.Remove(attachment);   // fully handled (refunded, or free, so nothing is owed)
                }

                // saved after every attachment, so what is already handed out is never handed out again
                var left = ChestAttachment.Serialize(remaining);
                zdo.Set(AttachZdoKey, left);
                _lastSeenAttach = left;
            }
        }

        /// <summary>Writes the attachments into the chest's ZDO (synced to everyone). Does not change what is shown.</summary>
        public bool SaveAttachments(IList<ChestAttachment> list)
        {
            if (!Valid) return false;

            _nview.ClaimOwnership();
            var data = ChestAttachment.Serialize(list);
            _nview.GetZDO().Set(AttachZdoKey, data);
            _lastSeenAttach = data;
            return true;
        }

    }
}
