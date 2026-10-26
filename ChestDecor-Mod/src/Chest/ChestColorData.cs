using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// The saved colour format of a chest (split from ChestDecorator, class-size rule):  key=RRGGBB;key2=RRGGBB,f  (no suffix = Tint, ",f" = Full colour).
    /// </summary>
    internal static class ChestColorData
    {
        // ------------------------------------------------------------------------------------------------
        // Serialisation:  key=RRGGBB;key2=RRGGBB,f
        //   no suffix = Tint, ",f" = Full colour
        // ------------------------------------------------------------------------------------------------

        internal const string FullSuffix = ",f";

        public static string Serialize(IDictionary<string, PartColor> colors)
        {
            if (colors == null || colors.Count == 0) return "";

            var keys = new List<string>(colors.Keys);
            keys.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            foreach (var key in keys)
            {
                var part = colors[key];
                if (sb.Length > 0) sb.Append(';');
                sb.Append(key).Append('=').Append(ColorUtility.ToHtmlStringRGB(part.Rgb));
                if (part.Mode == ColorMode.Full) sb.Append(FullSuffix);
            }
            return sb.ToString();
        }

        public static Dictionary<string, PartColor> Parse(string data)
        {
            var result = new Dictionary<string, PartColor>();
            if (string.IsNullOrEmpty(data)) return result;

            foreach (var entry in data.Split(';'))
            {
                var idx = entry.IndexOf('=');
                if (idx <= 0) continue;

                var key = entry.Substring(0, idx);
                var value = entry.Substring(idx + 1);

                var mode = ColorMode.Tint;
                var comma = value.IndexOf(',');
                if (comma >= 0)
                {
                    if (string.Equals(value.Substring(comma), FullSuffix, StringComparison.OrdinalIgnoreCase))
                        mode = ColorMode.Full;
                    value = value.Substring(0, comma);
                }

                if (ColorUtility.TryParseHtmlString("#" + value, out var color))
                    result[key] = new PartColor(color, mode);
            }
            return result;
        }

        public static bool SameColors(IDictionary<string, PartColor> a, IDictionary<string, PartColor> b)
        {
            return string.Equals(Serialize(a), Serialize(b), StringComparison.Ordinal);
        }

        public static Dictionary<string, PartColor> Clone(IDictionary<string, PartColor> source)
        {
            return source == null ? new Dictionary<string, PartColor>() : new Dictionary<string, PartColor>(source);
        }
    }
}
