using Kydl.Common;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChestDecor
{
    /// <summary>
    /// Creates the visible model of an attached item.
    ///
    /// The prefab itself is never instantiated (that would spawn a networked object with scripts, physics and a
    /// ZDO). Instead the meshes and materials of its renderers are copied onto plain GameObjects, so an attachment
    /// is purely visual, costs nothing on the network and can't be picked up, hit or collided with.
    /// </summary>
    internal static class AttachVisuals
    {
        private static readonly Dictionary<string, AttachVisualModel> Cache = new();

        public static void ClearCache() => Cache.Clear();

        public static AttachVisualModel GetModel(GameObject prefab)
        {
            if (prefab == null) return new AttachVisualModel();

            var key = prefab.name;
            if (Cache.TryGetValue(key, out var cached) && IsStillLoaded(cached)) return cached;

            var model = Create(prefab);
            Cache[key] = model;
            return model;
        }

        /// <summary>A cached model whose meshes were unloaded (world change) must be read again.</summary>
        private static bool IsStillLoaded(AttachVisualModel model)
        {
            foreach (var part in model.Parts)
                if (part.Mesh == null) return false;
            return true;
        }

        // ------------------------------------------------------------------------------------------------
        // Reading a prefab
        // ------------------------------------------------------------------------------------------------

        private static AttachVisualModel Create(GameObject prefab)
        {
            var model = new AttachVisualModel();
            try
            {
                var root = prefab.transform;

                // Prefabs with several levels of detail: use the best one only, otherwise all levels would overlap.
                var skip = new HashSet<Renderer>();
                var lodGroup = prefab.GetComponentInChildren<LODGroup>(true);
                var lodInfo = new Dictionary<Renderer, string>();
                if (lodGroup != null)
                {
                    var lods = lodGroup.GetLODs();
                    for (int i = 1; i < lods.Length; i++)
                    {
                        if (lods[i].renderers == null) continue;
                        foreach (var r in lods[i].renderers)
                        {
                            if (r == null) continue;
                            skip.Add(r);
                            lodInfo[r] = $"LOD{i} (skipped)";
                        }
                    }
                    if (lods.Length > 0 && lods[0].renderers != null)
                    {
                        foreach (var r in lods[0].renderers)
                        {
                            if (r == null) continue;
                            skip.Remove(r);
                            lodInfo[r] = "LOD0 (kept)";
                        }
                    }
                }

                // Plants and bushes (anything pickable or plantable) keep switched-off child objects for their other
                // states (picked, growth stages). Only what is switched on shows the plant as it stands in the world,
                // so that comes first; if that finds nothing, the normal passes below decide.
                var plantLike = prefab.GetComponentInChildren<Pickable>(true) != null || prefab.GetComponent<Plant>() != null;

                // 1st pass ignores "attach*" children (the model shown while the item is equipped, which would
                // double the dropped model); 2nd pass takes everything; 3rd pass uses skinned meshes as a last resort.
                if (!(plantLike && Collect(model, root, skip, allowAttach: false, allowSkinned: false, activeOnly: true)))
                    if (!Collect(model, root, skip, allowAttach: false, allowSkinned: false))
                        if (!Collect(model, root, skip, allowAttach: true, allowSkinned: false))
                            Collect(model, root, skip, allowAttach: true, allowSkinned: true);

                if (model.Valid)
                {
                    var any = false;
                    foreach (var part in model.Parts)
                    {
                        var mb = part.Mesh.bounds;
                        for (int i = 0; i < 8; i++)
                        {
                            var corner = mb.center + Vector3.Scale(mb.extents,
                                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                            var p = part.Local.MultiplyPoint3x4(corner);
                            if (!any) { model.Bounds = new Bounds(p, Vector3.zero); any = true; }
                            else model.Bounds.Encapsulate(p);
                        }
                    }
                }
                else if (ModSettings.VerboseModelDiagnostics != null && ModSettings.VerboseModelDiagnostics.Value)
                {
                    LogModelDiagnostics(prefab, root, skip, lodGroup, lodInfo);
                }
            }
            catch (Exception e)
            {
                ModLog.Warn($"Could not read the model of '{prefab.name}': {e.Message}");
                if (ModSettings.VerboseModelDiagnostics != null && ModSettings.VerboseModelDiagnostics.Value)
                    ModLog.Warn($"[ModelDiag] {prefab.name}: exception while reading model: {e}");
                model.Parts.Clear();
            }
            return model;
        }

        private static void LogModelDiagnostics(GameObject prefab, Transform root, HashSet<Renderer> skip, LODGroup lodGroup, Dictionary<Renderer, string> lodInfo)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            ModLog.Warn($"[ModelDiag] {prefab.name}: INVALID physical model; renderer count={renderers.Length}, LODGroup={(lodGroup != null ? "yes" : "no")}, skippedByLOD={skip.Count}");

            foreach (var r in renderers)
            {
                if (r == null) continue;
                var path = GetTransformPath(r.transform, root);
                var reasons = new List<string>();

                if (skip.Contains(r)) reasons.Add(lodInfo.TryGetValue(r, out var lod) ? lod : "LOD-skipped");
                if (!r.enabled) reasons.Add("renderer-disabled");
                if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) reasons.Add("shadows-only");
                if (UnderAttach(r.transform, root)) reasons.Add("under-attach");

                Mesh mesh = null;
                if (r is MeshRenderer)
                {
                    var filter = r.GetComponent<MeshFilter>();
                    if (filter == null) reasons.Add("MeshRenderer-without-MeshFilter");
                    else if (filter.sharedMesh == null) reasons.Add("MeshFilter.sharedMesh-null");
                    else mesh = filter.sharedMesh;
                }
                else if (r is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh == null) reasons.Add("SkinnedMeshRenderer.sharedMesh-null");
                    else mesh = skinned.sharedMesh;
                }
                else
                {
                    reasons.Add("renderer-type-not-supported-by-collector");
                }

                var materials = r.sharedMaterials;
                if (materials == null || materials.Length == 0) reasons.Add("no-materials");

                var state = reasons.Count == 0 ? "WOULD-BE-ACCEPTED" : string.Join(", ", reasons);
                ModLog.Warn($"[ModelDiag] {prefab.name}: {path} [{r.GetType().Name}] enabled={r.enabled} active={r.gameObject.activeSelf} mesh={(mesh != null ? mesh.name : "<null>")} materials={(materials == null ? "<null>" : materials.Length.ToString())} => {state}");
            }

            ModLog.Warn($"[ModelDiag] {prefab.name}: If no renderer is marked WOULD-BE-ACCEPTED, the reasons above explain the exclusion. Skinned meshes are only accepted in the final collector pass.");
        }

        private static string GetTransformPath(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var p = t; p != null; p = p.parent)
            {
                parts.Add(p.name);
                if (p == root) break;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static bool Collect(AttachVisualModel model, Transform root, HashSet<Renderer> skip, bool allowAttach, bool allowSkinned,
            bool activeOnly = false)
        {
            model.Parts.Clear();

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (skip.Contains(r) || !r.enabled) continue;
                if (activeOnly && !SwitchedOn(r.transform, root)) continue;
                if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                if (!allowAttach && UnderAttach(r.transform, root)) continue;

                Mesh mesh = null;
                if (r is MeshRenderer)
                {
                    var filter = r.GetComponent<MeshFilter>();
                    if (filter != null) mesh = filter.sharedMesh;
                }
                else if (allowSkinned && r is SkinnedMeshRenderer)
                {
                    mesh = ((SkinnedMeshRenderer)r).sharedMesh;
                }

                if (mesh == null) continue;

                var materials = r.sharedMaterials;
                if (materials == null || materials.Length == 0) continue;

                model.Parts.Add(new AttachVisualPart
                {
                    Mesh = mesh,
                    Materials = materials,
                    Local = RelativeMatrix(r.transform, root)
                });
            }
            return model.Parts.Count > 0;
        }

        /// <summary>True when the object and every parent up to the prefab root are switched on (as stored in the prefab).</summary>
        private static bool SwitchedOn(Transform t, Transform root)
        {
            for (var p = t; p != null; p = p.parent)
            {
                if (!p.gameObject.activeSelf) return false;
                if (p == root) break;
            }
            return true;
        }

        private static bool UnderAttach(Transform t, Transform root)
        {
            for (var p = t; p != null && p != root; p = p.parent)
                if (p.name.StartsWith("attach", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static Matrix4x4 RelativeMatrix(Transform t, Transform root)
        {
            var m = Matrix4x4.identity;
            for (var p = t; p != null && p != root; p = p.parent)
                m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
            return m;
        }

        /// <summary>
        /// Puts a transform's local position, rotation and scale to those of a matrix. Unlike Matrix4x4.lossyScale
        /// (never negative) this keeps a mirrored part mirrored: when the matrix flips handedness, one axis gets a
        /// negative scale and the rotation is taken from the remaining right-handed axes.
        /// </summary>
        private static void ApplyMatrix(Transform t, Matrix4x4 m)
        {
            t.localPosition = new Vector3(m.m03, m.m13, m.m23);

            var x = (Vector3)m.GetColumn(0);
            var y = (Vector3)m.GetColumn(1);
            var z = (Vector3)m.GetColumn(2);
            var sx = x.magnitude;
            var sy = y.magnitude;
            var sz = z.magnitude;

            if (sx < 1e-6f || sy < 1e-6f || sz < 1e-6f)
            {
                // a flattened part has no usable axes: keep what the matrix can still say
                t.localRotation = m.rotation;
                t.localScale = m.lossyScale;
                return;
            }

            if (Vector3.Dot(Vector3.Cross(x, y), z) < 0f) sx = -sx;   // negative determinant: the part is mirrored

            // with the sign folded into x, the three axes are a right-handed set again
            t.localRotation = Quaternion.LookRotation(z / sz, y / sy);
            t.localScale = new Vector3(sx, sy, sz);
        }

        // ------------------------------------------------------------------------------------------------
        // Building the visible copy
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Creates "holder" (placed / rotated / scaled by the caller) with the model inside, centred on the
        /// holder's origin so rotating and scaling happen around the middle of the item.
        /// </summary>
        public static Transform Build(AttachVisualModel model, string name, Transform parent, int layer)
        {
            var holder = new GameObject("Attach_" + name);
            holder.layer = layer;
            holder.transform.SetParent(parent, false);

            var visual = new GameObject("Visual");
            visual.layer = layer;
            visual.transform.SetParent(holder.transform, false);
            visual.transform.localPosition = -model.Bounds.center;

            foreach (var part in model.Parts)
            {
                var go = new GameObject("AttachVisualPart");
                go.layer = layer;
                go.transform.SetParent(visual.transform, false);

                ApplyMatrix(go.transform, part.Local);

                go.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = part.Materials;
            }
            return holder.transform;
        }
    }
}
