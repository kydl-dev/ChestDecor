using Kydl.Common;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// Most Valheim chests use ONE material and ONE small texture atlas for everything (wood, hinges, bands),
    /// so there is no material boundary to colour separately. This class looks at the texture itself and splits
    /// it into two colour regions ("zones"): zone 0 = the larger one (base), zone 1 = the smaller one (trim).
    ///
    /// Each pixel gets a soft membership weight, so a chest can be re-baked with a different colour per zone.
    /// The textures are tiny (128-256px) so baking is cheap enough to do live while dragging the colour wheel.
    ///
    /// The split is a 2-means clustering on brightness and colourfulness (wood is brown and colourful, iron is
    /// grey), and is only accepted if the two clusters are clearly separate; otherwise the texture stays one part.
    /// </summary>
    internal class ZoneMap
    {
        private const int MaxSize = 512;
        private const float ChromaWeight = 2.5f;      // colourfulness counts this much more than brightness
        private const float MinSeparation = 0.25f;    // min distance between the two cluster centres
        private const float MinSeparationRatio = 3f;  // centre distance / typical spread inside a cluster
        private const float MinShare = 0.03f;         // smaller cluster must cover at least this much

        public string TextureName;
        public int Width;
        public int Height;
        public Color32[] Pixels;

        /// <summary>1 = could not be split, 2 = split into base (0) and trim (1).</summary>
        public int Zones = 1;

        /// <summary>Average original colour of each zone (a good starting colour for the picker).</summary>
        public readonly Color[] Average = new Color[2];

        private byte[] _luma;                                  // 8 bit brightness per pixel
        private float[] _w1;                                   // membership of zone 1 (zone 0 = 1 - w1)
        private readonly float[] _gain = new float[2];         // Full colour: colour multiplier per zone
        private readonly float[][] _curve = new float[2][];    // Full colour: brightness response per zone

        private static readonly Dictionary<int, ZoneMap> Cache = new();

        /// <summary>Forgets every analysed texture (the world was left, or the plugin is being unloaded).</summary>
        public static void Clear() => Cache.Clear();

        /// <summary>Returns the 2-zone split of this texture, or null if it is a single part / can't be read.</summary>
        public static ZoneMap TryGetSplit(Texture source)
        {
            var tex = source as Texture2D;
            if (tex == null) return null;

            var id = tex.GetInstanceID();
            if (!Cache.TryGetValue(id, out var map))
            {
                try
                {
                    map = Build(tex);
                }
                catch (Exception e)
                {
                    ModLog.Warn($"Could not analyse texture '{tex.name}' for parts: {e.Message}");
                    map = null;
                }
                Cache[id] = map; // also remembers failures so we don't retry every time
            }

            return map != null && map.Zones == 2 ? map : null;
        }

        // ------------------------------------------------------------------------------------------------
        // Analysis
        // ------------------------------------------------------------------------------------------------

        private static ZoneMap Build(Texture2D tex)
        {
            var pixels = NeutralTextures.ReadPixels(tex, MaxSize, out var w, out var h);
            var map = new ZoneMap { TextureName = tex.name, Width = w, Height = h, Pixels = pixels };

            var n = pixels.Length;
            var luma = new byte[n];
            var fx = new float[n]; // feature 1: brightness
            var fy = new float[n]; // feature 2: colourfulness (weighted)
            var solid = new bool[n];
            var solidCount = 0;
            double meanX = 0, meanY = 0;

            for (int i = 0; i < n; i++)
            {
                var p = pixels[i];
                var l = (54 * p.r + 183 * p.g + 19 * p.b) >> 8;
                luma[i] = (byte)l;
                fx[i] = l / 255f;
                var hi = Mathf.Max((int)p.r, Mathf.Max((int)p.g, (int)p.b));
                var lo = Mathf.Min((int)p.r, Mathf.Min((int)p.g, (int)p.b));
                fy[i] = (hi - lo) / 255f * ChromaWeight;

                if (p.a < 26) continue; // ignore (nearly) transparent texels
                solid[i] = true;
                solidCount++;
                meanX += fx[i];
                meanY += fy[i];
            }
            map._luma = luma;

            if (solidCount < 64)
            {
                Finish(map, "texture too small / transparent", null);
                return map;
            }

            meanX /= solidCount;
            meanY /= solidCount;

            // farthest-first initial cluster centres
            int ia = -1, ib = -1;
            float best = -1f;
            for (int i = 0; i < n; i++)
            {
                if (!solid[i]) continue;
                var d = Sq(fx[i] - (float)meanX) + Sq(fy[i] - (float)meanY);
                if (d > best) { best = d; ia = i; }
            }
            float ax = fx[ia], ay = fy[ia];

            best = -1f;
            for (int i = 0; i < n; i++)
            {
                if (!solid[i]) continue;
                var d = Sq(fx[i] - ax) + Sq(fy[i] - ay);
                if (d > best) { best = d; ib = i; }
            }
            float bx = fx[ib], by = fy[ib];

            // Lloyd iterations
            for (int iter = 0; iter < 20; iter++)
            {
                double sax = 0, say = 0, sbx = 0, sby = 0;
                int na = 0, nb = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!solid[i]) continue;
                    var da = Sq(fx[i] - ax) + Sq(fy[i] - ay);
                    var db = Sq(fx[i] - bx) + Sq(fy[i] - by);
                    if (da <= db) { sax += fx[i]; say += fy[i]; na++; }
                    else { sbx += fx[i]; sby += fy[i]; nb++; }
                }
                if (na == 0 || nb == 0) break;

                float nax = (float)(sax / na), nay = (float)(say / na);
                float nbx = (float)(sbx / nb), nby = (float)(sby / nb);
                var moved = Mathf.Abs(nax - ax) + Mathf.Abs(nay - ay) + Mathf.Abs(nbx - bx) + Mathf.Abs(nby - by);
                ax = nax; ay = nay; bx = nbx; by = nby;
                if (moved < 1e-4f) break;
            }

            // quality of the split
            double within = 0;
            int countA = 0, countB = 0;
            for (int i = 0; i < n; i++)
            {
                if (!solid[i]) continue;
                var da = Sq(fx[i] - ax) + Sq(fy[i] - ay);
                var db = Sq(fx[i] - bx) + Sq(fy[i] - by);
                if (da <= db) { countA++; within += da; }
                else { countB++; within += db; }
            }
            within = Math.Sqrt(within / solidCount);

            var dx = bx - ax;
            var dy = by - ay;
            var separation = Mathf.Sqrt(dx * dx + dy * dy);
            var ratio = (float)(separation / Math.Max(within, 1e-4));
            var minShare = Mathf.Min(countA, countB) / (float)solidCount;
            var stats = $"separation {separation:F2}, ratio {ratio:F1}, smaller share {minShare:P0}";

            if (separation < MinSeparation || ratio < MinSeparationRatio || minShare < MinShare)
            {
                Finish(map, "no clear second region (" + stats + ")", null);
                return map;
            }

            // zone 0 = larger cluster
            if (countA < countB)
            {
                (ax, bx) = (bx, ax);
                (ay, by) = (by, ay);
                dx = bx - ax;
                dy = by - ay;
            }

            // soft membership along the axis between the two centres
            var d2 = dx * dx + dy * dy;
            var w1 = new float[n];
            for (int i = 0; i < n; i++)
            {
                var t = ((fx[i] - ax) * dx + (fy[i] - ay) * dy) / d2;
                w1[i] = Smooth(0.35f, 0.65f, t);
            }
            map._w1 = w1;

            // per zone: average colour + Full colour brightness response
            var hist = new[] { new double[256], new double[256] };
            var sumR = new double[2];
            var sumG = new double[2];
            var sumB = new double[2];
            var weight = new double[2];
            for (int i = 0; i < n; i++)
            {
                if (!solid[i]) continue;
                var p = pixels[i];
                for (int z = 0; z < 2; z++)
                {
                    var wz = z == 0 ? 1.0 - w1[i] : w1[i];
                    hist[z][luma[i]] += wz;
                    sumR[z] += wz * p.r;
                    sumG[z] += wz * p.g;
                    sumB[z] += wz * p.b;
                    weight[z] += wz;
                }
            }

            for (int z = 0; z < 2; z++)
            {
                if (weight[z] < 1e-3)
                {
                    Finish(map, "a region ended up empty (" + stats + ")", null);
                    return map;
                }

                map.Average[z] = new Color(
                    (float)(sumR[z] / weight[z] / 255.0),
                    (float)(sumG[z] / weight[z] / 255.0),
                    (float)(sumB[z] / weight[z] / 255.0), 1f);

                double lumaSum = 0;
                for (int b = 0; b < 256; b++) lumaSum += hist[z][b] * (b / 255.0);
                var meanLuma = lumaSum / weight[z];
                var flat = meanLuma < 0.004;

                var curve = new float[256];
                double gSum = 0;
                for (int b = 0; b < 256; b++)
                {
                    float g;
                    if (flat) g = 1f / (1f + NeutralTextures.Knee);
                    else
                    {
                        var x = (float)((b / 255.0) / meanLuma);
                        g = x / (x + NeutralTextures.Knee);
                    }
                    curve[b] = g;
                    gSum += hist[z][b] * g;
                }
                var gMean = gSum / weight[z];
                map._curve[z] = curve;
                map._gain[z] = gMean > 0.01 ? Mathf.Clamp((float)(1.0 / gMean), 1f, NeutralTextures.MaxGain) : 1f;
            }

            map.Zones = 2;
            Finish(map,
                $"split into 2 parts ({stats}; base {weight[0] / solidCount:P0} avg #{ColorUtility.ToHtmlStringRGB(map.Average[0])}, " +
                $"trim {weight[1] / solidCount:P0} avg #{ColorUtility.ToHtmlStringRGB(map.Average[1])})", w1);
            return map;
        }

        private static void Finish(ZoneMap map, string message, float[] w1)
        {
            ModLog.Info($"Texture '{map.TextureName}' {map.Width}x{map.Height}: {message}");
            if (ModSettings.DumpTextures.Value) Dump(map, w1);
        }

        private static float Sq(float v) => v * v;

        private static float Smooth(float a, float b, float x)
        {
            var t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // ------------------------------------------------------------------------------------------------
        // Baking
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Writes the recoloured texture into <paramref name="output"/> (same size as <see cref="Pixels"/>).
        /// A null zone colour keeps that zone's original look.
        /// <paramref name="baseMul"/> is the material's original _Color, applied to untouched zones so they look
        /// exactly as before.
        /// </summary>
        public void Bake(PartColor? zone0, PartColor? zone1, Color baseMul, Color32[] output)
        {
            var has0 = zone0.HasValue;
            var has1 = zone1.HasValue;
            var c0 = has0 ? zone0.Value.Rgb : Color.white;
            var c1 = has1 ? zone1.Value.Rgb : Color.white;
            var m0 = has0 ? zone0.Value.Mode : ColorMode.Tint;
            var m1 = has1 ? zone1.Value.Mode : ColorMode.Tint;

            for (int i = 0; i < Pixels.Length; i++)
            {
                var p = Pixels[i];
                float r = p.r / 255f, g = p.g / 255f, b = p.b / 255f;
                var w1 = _w1[i];
                var w0 = 1f - w1;

                float r0 = 0f, g0 = 0f, b0 = 0f, r1 = 0f, g1 = 0f, b1 = 0f;
                if (w0 > 0.001f) ZoneColor(0, has0, m0, c0, r, g, b, _luma[i], baseMul, out r0, out g0, out b0);
                if (w1 > 0.001f) ZoneColor(1, has1, m1, c1, r, g, b, _luma[i], baseMul, out r1, out g1, out b1);

                output[i] = new Color32(
                    ToByte(r0 * w0 + r1 * w1),
                    ToByte(g0 * w0 + g1 * w1),
                    ToByte(b0 * w0 + b1 * w1),
                    p.a);
            }
        }

        private void ZoneColor(int zone, bool has, ColorMode mode, Color rgb, float r, float g, float b, int luma,
            Color baseMul, out float outR, out float outG, out float outB)
        {
            if (!has)
            {
                outR = r * baseMul.r;
                outG = g * baseMul.g;
                outB = b * baseMul.b;
            }
            else if (mode == ColorMode.Full)
            {
                // brightness detail only, scaled so the zone's average comes out as the picked colour
                var k = _gain[zone] * _curve[zone][luma];
                outR = rgb.r * k;
                outG = rgb.g * k;
                outB = rgb.b * k;
            }
            else
            {
                outR = r * rgb.r;
                outG = g * rgb.g;
                outB = b * rgb.b;
            }
        }

        private static byte ToByte(float v) => (byte)(Mathf.Clamp01(v) * 255f + 0.5f);

        // ------------------------------------------------------------------------------------------------
        // Debug dump
        // ------------------------------------------------------------------------------------------------

        private static void Dump(ZoneMap map, float[] w1)
        {
            try
            {
                // <profile>/BepInEx/cache/ChestDecorDump  (BepInEx's own cache folder, so it follows the active profile)
                var dir = Path.Combine(BepInEx.Paths.CachePath, "ChestDecorDump");
                Directory.CreateDirectory(dir);

                var name = map.TextureName;
                foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');

                WritePng(Path.Combine(dir, name + "_src.png"), map.Pixels, map.Width, map.Height);

                if (w1 != null)
                {
                    var mask = new Color32[w1.Length];
                    for (int i = 0; i < w1.Length; i++)
                    {
                        var v = ToByte(w1[i]);
                        mask[i] = new Color32(v, v, v, 255);
                    }
                    WritePng(Path.Combine(dir, name + "_mask.png"), mask, map.Width, map.Height);
                }
            }
            catch (Exception e)
            {
                ModLog.Verbose($"Texture dump failed: {e.Message}");
            }
        }

        private static void WritePng(string path, Color32[] pixels, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(pixels);
                tex.Apply();
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }
    }
}
