using Kydl.Common;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// Builds "neutral" copies of a material's main texture for <see cref="ColorMode.Full"/>.
    ///
    /// A normal tint multiplies the texture by a colour, so a dark texture stays dark whatever colour is picked.
    /// The neutral copy keeps only the texture's light/dark detail (grain, scratches, shading) and rescales it so
    /// its average brightness is the same for every texture. Multiplying THAT by the chosen colour makes the
    /// part's average colour match the pick, even if the original was nearly black.
    ///
    /// Copies are generated once per source texture and shared by every chest. They are dropped when the world
    /// is left (see DecorOpener.WatchWorld) and when the plugin unloads.
    /// </summary>
    internal static class NeutralTextures
    {
        /// <summary>Larger source textures are scaled down to this before processing (memory / speed).</summary>
        private const int MaxSize = 1024;

        /// <summary>
        /// Brightness response: g = x / (x + Knee), where x is the pixel's brightness relative to the texture's
        /// average. Smooth, never exceeds 1, keeps detail in both dark and bright areas.
        /// </summary>
        internal const float Knee = 1f / 3f;

        internal const float MaxGain = 2.5f;

        private static readonly Dictionary<int, NeutralTextureEntry> Cache = new();
        private static readonly HashSet<int> Failed = new();

        /// <summary>Gets (building it on first use) the neutral copy of <paramref name="source"/>.</summary>
        public static bool TryGet(Texture source, out NeutralTextureEntry entry)
        {
            entry = null;

            var tex = source as Texture2D;
            if (tex == null) return false;

            var id = tex.GetInstanceID();
            if (Cache.TryGetValue(id, out entry))
            {
                if (entry != null && entry.Texture != null) return true;
                Cache.Remove(id); // destroyed behind our back, rebuild
                entry = null;
            }

            if (Failed.Contains(id)) return false;

            try
            {
                entry = Build(tex);
                Cache[id] = entry;
                return true;
            }
            catch (Exception e)
            {
                Failed.Add(id);
                ModLog.Warn(
                    $"Full colour mode: could not process texture '{tex.name}', using tint instead. {e.Message}");
                entry = null;
                return false;
            }
        }

        public static void Clear()
        {
            foreach (var entry in Cache.Values)
                if (entry != null && entry.Texture != null)
                    UnityEngine.Object.Destroy(entry.Texture);
            Cache.Clear();
            Failed.Clear();
        }

        // ------------------------------------------------------------------------------------------------

        private static NeutralTextureEntry Build(Texture2D source)
        {
            var pixels = ReadPixels(source, MaxSize, out var w, out var h);

            // brightness histogram of the visible pixels (8 bit luma, weights sum to 256)
            var hist = new long[256];
            long count = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                if (p.a < 26) continue; // ignore (nearly) transparent texels
                hist[(54 * p.r + 183 * p.g + 19 * p.b) >> 8]++;
                count++;
            }

            double lumaSum = 0;
            for (int i = 0; i < 256; i++) lumaSum += hist[i] * (i / 255.0);
            var mean = count > 0 ? lumaSum / count : 0.0;

            // brightness -> output lookup table
            var lut = new byte[256];
            var flat = mean < 0.004; // practically black: no usable detail, fall back to a flat mid grey
            for (int i = 0; i < 256; i++)
            {
                float g;
                if (flat)
                {
                    g = 1f / (1f + Knee); // same value a pixel of average brightness gets
                }
                else
                {
                    var x = (float)((i / 255.0) / mean);
                    g = x / (x + Knee);
                }
                lut[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255);
            }

            // average output brightness decides how much to scale the colour up afterwards
            double outSum = 0;
            for (int i = 0; i < 256; i++) outSum += hist[i] * (lut[i] / 255.0);
            var outMean = count > 0 ? outSum / count : 0.75;
            var gain = outMean > 0.01 ? (float)(1.0 / outMean) : 1f;
            gain = Mathf.Clamp(gain, 1f, MaxGain);

            var result = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                var v = lut[(54 * p.r + 183 * p.g + 19 * p.b) >> 8];
                result[i] = new Color32(v, v, v, p.a); // keep alpha (cut-outs etc.)
            }

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = source.name + "_ChestDecorNeutral",
                wrapMode = source.wrapMode,
                filterMode = source.filterMode,
                anisoLevel = source.anisoLevel,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(result);
            texture.Apply(true, true); // build mip maps, free the CPU copy

            ModLog.Verbose(
                $"Full colour mode: built neutral texture for '{source.name}' ({w}x{h}, avg {mean:F3}, gain {gain:F2})");

            return new NeutralTextureEntry { Texture = texture, Gain = gain };
        }

        /// <summary>
        /// Reads a texture's pixels through a render texture, which works even when the texture was not
        /// imported as readable (most game textures).
        /// </summary>
        internal static Color32[] ReadPixels(Texture2D source, int maxSize, out int w, out int h)
        {
            var scale = Mathf.Min(1f, (float)maxSize / Mathf.Max(source.width, source.height));
            w = Mathf.Max(4, Mathf.RoundToInt(source.width * scale));
            h = Mathf.Max(4, Mathf.RoundToInt(source.height * scale));

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;

                readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                return readable.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (readable != null) UnityEngine.Object.Destroy(readable);
            }
        }
    }
}
