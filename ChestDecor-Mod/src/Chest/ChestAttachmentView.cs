using System.Collections.Generic;
using Kydl.Common;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// The attached items as they are SHOWN on one chest (split from ChestDecorator, class-size rule): the holder objects, their placement,
    /// the chest's own bounds and the default spot of a new item. Local visuals only; ChestDecorator saves and loads the list.
    /// </summary>
    internal sealed class ChestAttachmentView
    {
        readonly ChestDecorator _owner;
        Transform _attachRoot;
        readonly List<Transform> _attachHolders = new();
        readonly List<string> _attachPrefabs = new();
        readonly List<ChestAttachment> _showBuffer = new();
        Bounds? _localBounds;
        static readonly HashSet<string> MissingLogged = new();

        internal ChestAttachmentView(ChestDecorator owner) { _owner = owner; }

        /// <summary>True for a transform that belongs to a shown attachment (never a colourable part, never in the chest's bounds).</summary>
        internal bool IsAttachment(Transform t) => _attachRoot != null && t != null && t.IsChildOf(_attachRoot);

        /// <summary>
        /// Shows exactly these attachments on the chest (local visual only, nothing is saved). Models are reused
        /// between calls, so this is cheap enough to call on every slider movement.
        /// </summary>
        internal void ShowAttachments(IList<ChestAttachment> list)
        {
            _showBuffer.Clear();
            if (list != null)
                foreach (var a in list)
                {
                    if (a == null || string.IsNullOrEmpty(a.Prefab)) continue;
                    if (_showBuffer.Count >= ChestDecorator.HardMaxAttachments) break;
                    _showBuffer.Add(a);
                }

            if (_showBuffer.Count == 0 && _attachHolders.Count == 0) return;
            EnsureAttachRoot();

            for (int i = 0; i < _showBuffer.Count; i++)
            {
                var a = _showBuffer[i];

                if (i >= _attachHolders.Count)
                {
                    _attachHolders.Add(BuildHolder(a));
                    _attachPrefabs.Add(a.Prefab);
                }
                else if (_attachPrefabs[i] != a.Prefab || _attachHolders[i] == null)
                {
                    if (_attachHolders[i] != null) UnityEngine.Object.Destroy(_attachHolders[i].gameObject);
                    _attachHolders[i] = BuildHolder(a);
                    _attachPrefabs[i] = a.Prefab;
                }

                PlaceHolder(_attachHolders[i], a);
            }

            while (_attachHolders.Count > _showBuffer.Count)
            {
                var last = _attachHolders.Count - 1;
                if (_attachHolders[last] != null) UnityEngine.Object.Destroy(_attachHolders[last].gameObject);
                _attachHolders.RemoveAt(last);
                _attachPrefabs.RemoveAt(last);
            }
        }

        private void EnsureAttachRoot()
        {
            if (_attachRoot != null) return;

            var go = new GameObject("ChestDecor_Attachments");
            go.layer = _owner.ModelRoot.gameObject.layer;
            go.transform.SetParent(_owner.ModelRoot, false);
            _attachRoot = go.transform;
        }

        private Transform BuildHolder(ChestAttachment a)
        {
            var prefab = AttachCatalog.ResolvePrefab(a.Prefab);
            if (prefab == null)
            {
                LogMissingOnce(a.Prefab, "was not found (removed mod or renamed prefab?)");
                return null;
            }

            var model = AttachVisuals.GetModel(prefab);
            if (!model.Valid)
            {
                LogMissingOnce(a.Prefab, "has no model that can be shown");
                return null;
            }

            return AttachVisuals.Build(model, a.Prefab, _attachRoot, _attachRoot.gameObject.layer);
        }

        private static void LogMissingOnce(string prefab, string problem)
        {
            if (MissingLogged.Add(prefab))
                ModLog.Warn($"Attached item '{prefab}' {problem}; it is skipped.");
        }

        private static void PlaceHolder(Transform holder, ChestAttachment a)
        {
            if (holder == null) return;

            holder.localPosition = a.Position;
            holder.localRotation = Quaternion.Euler(a.Euler);
            holder.localScale = Vector3.one * Mathf.Clamp(a.Scale, ChestAttachment.MinScale, ChestAttachment.MaxScale);
        }

        /// <summary>
        /// Bounding box of the chest itself (attachments excluded), in the chest's local space. Used to set the
        /// range of the position sliders and to pick a starting spot on top of the chest.
        /// </summary>
        internal Bounds LocalBounds
        {
            get
            {
                if (_localBounds.HasValue) return _localBounds.Value;

                var root = _owner.ModelRoot;
                var toLocal = root.worldToLocalMatrix;
                var any = false;
                var bounds = new Bounds(Vector3.zero, Vector3.zero);

                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (r == null || !r.enabled || IsAttachment(r.transform)) continue;

                    var filter = r.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;

                    var mb = filter.sharedMesh.bounds;
                    var m = toLocal * r.transform.localToWorldMatrix;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = mb.center + Vector3.Scale(mb.extents,
                            new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                        var p = m.MultiplyPoint3x4(corner);
                        if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                        else bounds.Encapsulate(p);
                    }
                }

                // nothing readable: assume a typical chest so the sliders still have a sensible range
                if (!any) bounds = new Bounds(new Vector3(0f, 0.4f, 0f), new Vector3(1f, 0.8f, 0.7f));

                _localBounds = bounds;
                return bounds;
            }
        }

        /// <summary>
        /// Bounds of an attachment's geometry around its own position, in the chest's axes, with its current
        /// rotation and size applied. <see cref="PlacementLimits"/> measures from these edges, so an item can
        /// get exactly 10 cm clear of the chest whatever its shape, turn or size.
        /// </summary>
        internal Bounds ExtentOf(ChestAttachment a)
        {
            if (a == null) return new Bounds(Vector3.zero, Vector3.zero);

            var prefab = AttachCatalog.ResolvePrefab(a.Prefab);
            if (prefab == null) return new Bounds(Vector3.zero, Vector3.zero);

            var model = AttachVisuals.GetModel(prefab);
            if (!model.Valid) return new Bounds(Vector3.zero, Vector3.zero);

            // the same rotation and size PlaceHolder gives the visible copy
            var scale = Mathf.Clamp(a.Scale, ChestAttachment.MinScale, ChestAttachment.MaxScale);
            return model.ExtentAround(Quaternion.Euler(a.Euler), scale);
        }

        /// <summary>
        /// A starting placement for a new attachment: resting on top of the chest, spread along its width by
        /// <paramref name="index"/>, and scaled down if the item is large compared to the chest.
        /// </summary>
        internal ChestAttachment CreateDefault(string prefab, int index)
        {
            var attachment = new ChestAttachment { Prefab = prefab };
            var chest = LocalBounds;

            var size = new Vector3(0.3f, 0.3f, 0.3f);
            var go = AttachCatalog.ResolvePrefab(prefab);
            if (go != null)
            {
                var model = AttachVisuals.GetModel(go);
                if (model.Valid) size = model.Bounds.size;
            }

            var longest = Mathf.Max(size.x, Mathf.Max(size.y, Mathf.Max(size.z, 0.01f)));
            var target = Mathf.Max(0.25f, Mathf.Min(chest.size.x, chest.size.z) * 0.55f);
            var scale = Mathf.Clamp(Mathf.Min(1f, target / longest), ChestAttachment.MinScale, 1f);
            attachment.Scale = scale;

            var slots = _owner.MaxAttachments;
            var spread = (index - (slots - 1) * 0.5f) * (chest.size.x / slots);
            attachment.Euler = Vector3.zero;

            // the item's lowest point rests on the chest's top
            var extent = ExtentOf(attachment);
            attachment.Position = PlacementLimits.Clamp(new Vector3(
                chest.center.x + spread,
                chest.max.y - extent.min.y,
                chest.center.z), chest, extent);
            return attachment;
        }

    }
}
