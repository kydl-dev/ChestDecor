using System.Collections.Generic;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// The colours on a chest's materials (split from ChestDecorator, class-size rule): material clones, Tint / Full colour and the baked
    /// textures of split (base / trim) materials. Local visual only.
    /// </summary>
    internal sealed class ChestPainter
    {
        readonly List<ChestDecoratorRendererInfo> _renderers;
        readonly Dictionary<string, ZoneMap> _zones;
        readonly Dictionary<Material, Material> _clones = new();
        // recolored texture of a split material (original material -> baked texture)
        readonly Dictionary<Material, Texture2D> _baked = new();
        static Color32[] _bakeBuffer;

        internal ChestPainter(List<ChestDecoratorRendererInfo> renderers, Dictionary<string, ZoneMap> zones) { _renderers = renderers; _zones = zones; }

        /// <summary>Puts the colours on the scanned renderers (clones / baked textures are made and freed as needed). Nothing is saved.</summary>
        internal void Paint(IDictionary<string, PartColor> colors)
        {
            var usedOriginals = new HashSet<Material>();
            // every original material is configured only once per call, however many renderers share it
            var replacements = new Dictionary<Material, Material>();

            foreach (var info in _renderers)
            {
                if (info.Renderer == null) continue;

                Material[] result = null;
                for (int i = 0; i < info.Keys.Length; i++)
                {
                    var key = info.Keys[i];
                    if (key == null || colors == null) continue;

                    var original = info.Original[i];
                    if (!replacements.TryGetValue(original, out var replacement))
                    {
                        replacement = BuildReplacement(key, original, colors);
                        replacements[original] = replacement;
                    }
                    if (replacement == null) continue;

                    usedOriginals.Add(original);
                    if (result == null) result = (Material[])info.Original.Clone();
                    result[i] = replacement;
                }

                info.Renderer.sharedMaterials = result ?? info.Original;
            }

            // free clones (and baked textures) that are no longer in use
            List<Material> unused = null;
            foreach (var kv in _clones)
            {
                if (usedOriginals.Contains(kv.Key)) continue;
                (unused ?? (unused = new List<Material>())).Add(kv.Key);
            }
            if (unused != null)
            {
                foreach (var key in unused)
                {
                    if (_clones[key] != null) UnityEngine.Object.Destroy(_clones[key]);
                    _clones.Remove(key);

                    if (_baked.TryGetValue(key, out var baked))
                    {
                        if (baked != null) UnityEngine.Object.Destroy(baked);
                        _baked.Remove(key);
                    }
                }
            }
        }

        /// <summary>The recoloured material to show instead of <paramref name="original"/>, or null to keep it.</summary>
        private Material BuildReplacement(string key, Material original, IDictionary<string, PartColor> colors)
        {
            if (_zones.TryGetValue(key, out var map))
            {
                PartColor? zone0 = null, zone1 = null;
                if (colors.TryGetValue(ChestDecorator.ZoneKey(key, 0), out var base0)) zone0 = base0;
                if (colors.TryGetValue(ChestDecorator.ZoneKey(key, 1), out var trim1)) zone1 = trim1;

                // a colour stored under the material's own key (not a base / trim key) applies to the whole material
                if (!zone0.HasValue && !zone1.HasValue && colors.TryGetValue(key, out var whole))
                {
                    zone0 = whole;
                    zone1 = whole;
                }

                if (!zone0.HasValue && !zone1.HasValue) return null;

                var zoneClone = GetClone(original);
                ConfigureBaked(zoneClone, original, map, zone0, zone1);
                return zoneClone;
            }

            if (!colors.TryGetValue(key, out var part)) return null;

            var clone = GetClone(original);
            ConfigureClone(clone, original, part);
            return clone;
        }

        /// <summary>Bakes the zone colours into a texture of its own and puts it on the clone.</summary>
        private void ConfigureBaked(Material clone, Material original, ZoneMap map, PartColor? zone0, PartColor? zone1)
        {
            if (!_baked.TryGetValue(original, out var texture) || texture == null)
            {
                texture = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, true)
                {
                    name = map.TextureName + "_ChestDecorBaked",
                    hideFlags = HideFlags.HideAndDontSave
                };

                var source = original.GetTexture(ChestDecorator.MainTexProperty);
                if (source != null)
                {
                    texture.wrapMode = source.wrapMode;
                    texture.filterMode = source.filterMode;
                    texture.anisoLevel = source.anisoLevel;
                }
                _baked[original] = texture;
            }

            if (_bakeBuffer == null || _bakeBuffer.Length != map.Pixels.Length)
                _bakeBuffer = new Color32[map.Pixels.Length];

            var originalColor = original.GetColor(ChestDecorator.ColorProperty);
            map.Bake(zone0, zone1, new Color(originalColor.r, originalColor.g, originalColor.b, 1f), _bakeBuffer);

            texture.SetPixels32(_bakeBuffer);
            texture.Apply(true, false);

            clone.SetTexture(ChestDecorator.MainTexProperty, texture);
            clone.SetColor(ChestDecorator.ColorProperty, new Color(1f, 1f, 1f, originalColor.a)); // the colours are in the texture
        }

        /// <summary>
        /// Tint: the material keeps its texture and only gets a new _Color (texture x colour).
        /// Full: the material gets the neutral (brightness-only) copy of its texture, so the picked colour is
        /// what you actually see, even on dark textures. Falls back to Tint if the texture can't be processed.
        /// </summary>
        private static void ConfigureClone(Material clone, Material original, PartColor part)
        {
            var alpha = original.GetColor(ChestDecorator.ColorProperty).a;
            var rgb = part.Rgb;

            var hasTexSlot = original.HasProperty(ChestDecorator.MainTexProperty);
            var originalTex = hasTexSlot ? original.GetTexture(ChestDecorator.MainTexProperty) : null;

            if (part.Mode == ColorMode.Full && originalTex != null && NeutralTextures.TryGet(originalTex, out var neutral))
            {
                clone.SetTexture(ChestDecorator.MainTexProperty, neutral.Texture);
                rgb = new Color(rgb.r * neutral.Gain, rgb.g * neutral.Gain, rgb.b * neutral.Gain, 1f);
            }
            else if (hasTexSlot)
            {
                // (re)store the real texture, the clone may have been in Full mode before
                clone.SetTexture(ChestDecorator.MainTexProperty, originalTex);
            }

            clone.SetColor(ChestDecorator.ColorProperty, new Color(rgb.r, rgb.g, rgb.b, alpha));
        }

        private Material GetClone(Material original)
        {
            if (!_clones.TryGetValue(original, out var clone) || clone == null)
            {
                clone = new Material(original);
                clone.name = original.name + " (ChestDecor)";
                _clones[original] = clone;
            }
            return clone;
        }

        internal void DestroyClones()
        {
            foreach (var clone in _clones.Values)
                if (clone != null) UnityEngine.Object.Destroy(clone);
            _clones.Clear();

            foreach (var baked in _baked.Values)
                if (baked != null) UnityEngine.Object.Destroy(baked);
            _baked.Clear();
        }

    }
}
