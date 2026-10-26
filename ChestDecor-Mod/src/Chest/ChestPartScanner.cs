using System;
using System.Collections.Generic;
using System.Text;
using Kydl.Common;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// The part scan of a chest (split from ChestDecorator, class-size rule): which materials can be coloured, and the optional LogPartScan report.
    /// </summary>
    internal static class ChestPartScanner
    {
        internal static readonly HashSet<string> ScanLogged = new();

        /// <summary>Finds the colourable parts of a chest: every renderer material with a _Color, split into base / trim when its texture allows.</summary>
        internal static void Scan(GameObject root, Func<Transform, bool> isAttachment, List<ChestDecorator.Slot> _slots,
            List<ChestDecoratorRendererInfo> _renderers, Dictionary<string, ZoneMap> _zones)
        {
            var slotByKey = new Dictionary<string, ChestDecorator.Slot>();

            // Optional one-time (per chest type) report of what the scan found, to help diagnose "why only 1 part?".
            var prefabName = root.name.Replace("(Clone)", "").Trim();
            StringBuilder log = null;
            if (ModSettings.LogPartScan.Value && ScanLogged.Add(prefabName))
                log = new StringBuilder();
            var rootTransform = root.transform;
            var rendererCount = 0;

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                // attached items are not part of the chest: they must never become colourable parts
                if (isAttachment(r.transform)) continue;

                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer))
                {
                    log?.AppendLine($"  ignored {r.GetType().Name} '{PathOf(r.transform, rootTransform)}'");
                    continue;
                }

                var mats = r.sharedMaterials;
                var keys = new string[mats.Length];
                bool any = false;
                rendererCount++;

                log?.AppendLine($"  {r.GetType().Name} '{PathOf(r.transform, rootTransform)}' mesh '{MeshNameOf(r)}', {mats.Length} material slot(s)");

                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null)
                    {
                        log?.AppendLine($"    [{i}] <no material>");
                        continue;
                    }

                    if (!m.HasProperty(ChestDecorator.ColorProperty))
                    {
                        log?.AppendLine($"    [{i}] '{m.name}' shader '{ShaderNameOf(m)}' -> SKIPPED (no {ChestDecorator.ColorProperty} property)");
                        continue;
                    }

                    var key = KeyOf(m);
                    keys[i] = key;
                    any = true;

                    var isNew = !slotByKey.ContainsKey(key);
                    var zoneNote = "";
                    if (isNew)
                    {
                        // One material + one texture often holds both wood and metal: try to split it in two parts.
                        ZoneMap zones = null;
                        if (ModSettings.SplitParts.Value)
                            zones = ZoneMap.TryGetSplit(m.HasProperty(ChestDecorator.MainTexProperty) ? m.GetTexture(ChestDecorator.MainTexProperty) : null);

                        if (zones != null)
                        {
                            _zones[key] = zones;
                            zoneNote = " (texture split into 2 parts)";
                            for (int z = 0; z < 2; z++)
                            {
                                var zoneSlot = new ChestDecorator.Slot
                                {
                                    Key = ZoneKey(key, z),
                                    DisplayName = PrettyName(m.name) + (z == 0 ? " (base)" : " (trim)"),
                                    OriginalColor = zones.Average[z]
                                };
                                zoneSlot.OriginalColor.a = 1f;
                                _slots.Add(zoneSlot);
                                if (z == 0) slotByKey[key] = zoneSlot;
                            }
                        }
                        else
                        {
                            var slot = new ChestDecorator.Slot
                            {
                                Key = key,
                                DisplayName = PrettyName(m.name),
                                OriginalColor = m.GetColor(ChestDecorator.ColorProperty)
                            };
                            slot.OriginalColor.a = 1f;
                            slotByKey[key] = slot;
                            _slots.Add(slot);
                        }
                    }

                    log?.AppendLine($"    [{i}] '{m.name}' shader '{ShaderNameOf(m)}' {DescribeMainTex(m)} -> part '{key}'{(isNew ? zoneNote : " (already listed, shares this part)")}");
                }

                if (any)
                    _renderers.Add(new ChestDecoratorRendererInfo { Renderer = r, Original = mats, Keys = keys });
            }

            if (log != null)
            {
                ModLog.Info(
                    $"Part scan of '{prefabName}': {rendererCount} renderer(s), {_slots.Count} part(s)\n{log.ToString().TrimEnd()}");
            }
        }

        internal static string PathOf(Transform t, Transform root)
        {
            if (t == root) return t.name;
            var path = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }

        internal static string MeshNameOf(Renderer r)
        {
            Mesh mesh = null;
            var skinned = r as SkinnedMeshRenderer;
            if (skinned != null) mesh = skinned.sharedMesh;
            else
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter != null) mesh = filter.sharedMesh;
            }
            return mesh != null ? mesh.name : "?";
        }

        internal static string ShaderNameOf(Material m) => m.shader != null ? m.shader.name : "?";

        internal static string DescribeMainTex(Material m)
        {
            if (!m.HasProperty(ChestDecorator.MainTexProperty)) return "(no _MainTex)";
            var tex = m.GetTexture(ChestDecorator.MainTexProperty);
            return tex != null ? $"_MainTex '{tex.name}' {tex.width}x{tex.height}" : "(_MainTex empty)";
        }

        internal static string ZoneKey(string materialKey, int zone) => materialKey + "#" + zone;

        internal static string StripInstance(string name)
        {
            const string suffix = " (Instance)";
            while (name.EndsWith(suffix, StringComparison.Ordinal))
                name = name.Substring(0, name.Length - suffix.Length);
            return name.Trim();
        }

        internal static string KeyOf(Material m)
        {
            var n = StripInstance(m.name).ToLowerInvariant();
            // keep the separators used by the serialised format out of keys
            return n.Replace(';', '_').Replace('=', '_');
        }

        internal static string PrettyName(string materialName)
        {
            var n = StripInstance(materialName).Replace('_', ' ').Trim();
            if (n.Length == 0) return "Part";
            return char.ToUpperInvariant(n[0]) + n.Substring(1);
        }

    }
}
