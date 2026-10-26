using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Kydl.Common;
using UnityEngine;

namespace ChestDecor
{
    /// <summary>
    /// One item attached to a chest: which prefab to show and where it sits, in the chest's own local space.
    /// Stored in the chest's ZDO so it persists with the world and syncs to other players that have the mod.
    /// </summary>
    public sealed class ChestAttachment
    {
        public string Prefab;
        public Vector3 Position;
        public Vector3 Euler;
        public float Scale = 1f;

        /// <summary>
        /// True when the player really paid for this attachment, false when it was attached for free (Free Attachments
        /// setting or the game's no-cost cheat). Only a paid attachment is refunded when it is removed, so attaching
        /// for free and removing later can't produce materials. Attachments saved before this existed count as paid.
        /// </summary>
        public bool Paid = true;

        /// <summary>
        /// The exact materials that were paid when this attachment was created (empty when it was free). A refund
        /// returns exactly these, so changing a recipe, a build cost or the config after crafting can never change
        /// what comes back. Null on an attachment saved without its cost: that one is refunded from the
        /// current catalog until the chest's owner records it (see ChestDecorator.RecordUnrecordedCosts), after which
        /// it no longer depends on the catalog.
        /// </summary>
        public List<PaidMaterial> Cost;

        public const float MinScale = 0.05f;
        public const float MaxScale = 2f;

        /// <summary>Safety limit for one stored amount, so a damaged save can't spawn an absurd number of items.</summary>
        public const int MaxPaidAmount = 100000;

        public ChestAttachment Clone()
        {
            var copy = (ChestAttachment)MemberwiseClone();
            if (Cost != null) copy.Cost = new List<PaidMaterial>(Cost);   // the list must not be shared
            return copy;
        }

        /// <summary>Takes over what was paid (and whether anything was) from another attachment, e.g. when a position is reset.</summary>
        public void CopyPaymentFrom(ChestAttachment other)
        {
            if (other == null) return;
            Paid = other.Paid;
            Cost = other.Cost != null ? new List<PaidMaterial>(other.Cost) : null;
        }

        public static List<ChestAttachment> CloneList(IList<ChestAttachment> source)
        {
            var result = new List<ChestAttachment>();
            if (source == null) return result;
            foreach (var item in source) result.Add(item.Clone());
            return result;
        }

        // ------------------------------------------------------------------------------------------------
        // Serialisation:  prefab|px|py|pz|rx|ry|rz|scale|paid|cost;prefab2|...
        //   paid = 1 / 0 (no paid field counts as paid)
        //   cost = name:amount,name:amount (names URL-escaped); an empty cost field means "paid nothing";
        //          no cost field at all means "not recorded"
        // ------------------------------------------------------------------------------------------------

        public static string Serialize(IList<ChestAttachment> list)
        {
            if (list == null || list.Count == 0) return "";

            var sb = new StringBuilder();
            foreach (var a in list)
            {
                if (a == null || string.IsNullOrEmpty(a.Prefab)) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(a.Prefab.Replace(';', '_').Replace('|', '_'));
                Append(sb, a.Position.x); Append(sb, a.Position.y); Append(sb, a.Position.z);
                Append(sb, a.Euler.x); Append(sb, a.Euler.y); Append(sb, a.Euler.z);
                Append(sb, a.Scale);
                sb.Append('|').Append(a.Paid ? '1' : '0');
                if (a.Cost != null) sb.Append('|').Append(SerializeCost(a.Cost));   // null = not recorded: no field at all
            }
            return sb.ToString();
        }

        private static string SerializeCost(IList<PaidMaterial> cost)
        {
            var sb = new StringBuilder();
            foreach (var m in cost)
            {
                if (string.IsNullOrEmpty(m.Prefab) || m.Amount <= 0) continue;
                if (sb.Length > 0) sb.Append(',');
                // escaped so the name can never contain the ; | , : separators of the save format
                sb.Append(Uri.EscapeDataString(m.Prefab)).Append(':')
                  .Append(Mathf.Min(m.Amount, MaxPaidAmount).ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static List<PaidMaterial> ParseCost(string text)
        {
            var result = new List<PaidMaterial>();
            if (string.IsNullOrEmpty(text)) return result;

            foreach (var part in text.Split(','))
            {
                var colon = part.LastIndexOf(':');
                // Stryker disable once Equality : a colon at 0 gives an empty name, which the next check drops as well
                if (colon <= 0) continue;

                var name = Uri.UnescapeDataString(part.Substring(0, colon));
                // Stryker disable once all : safety net - after the colon check the escaped name is never empty
                if (name.Length == 0) continue;

                if (!int.TryParse(part.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
                    continue;

                result.Add(new PaidMaterial(name, Mathf.Min(amount, MaxPaidAmount)));
            }
            return result;
        }

        private static void Append(StringBuilder sb, float value)
        {
            sb.Append('|').Append(value.ToString("0.####", CultureInfo.InvariantCulture));
        }

        public static List<ChestAttachment> Parse(string data, int max = 16)
        {
            var result = new List<ChestAttachment>();
            if (string.IsNullOrEmpty(data)) return result;

            foreach (var entry in data.Split(';'))
            {
                if (result.Count >= max) { ModLog.Verbose("ChestAttachment.Parse: more than " + max + " stored attachments, the rest is not loaded"); break; }

                var f = entry.Split('|');
                // Stryker disable once all : 'entry.Length > 0' only keeps an empty entry (trailing ';') out of the debug log
                if (f.Length < 8 || f[0].Length == 0) { if (entry.Length > 0) ModLog.Verbose("ChestAttachment.Parse: entry '" + entry + "' skipped (too few fields or no prefab)"); continue; }

                // px py pz rx ry rz scale. A loop instead of seven 'out var' in one condition: same result, and Stryker can mutate it
                // (with the chained 'out var' every mutation failed to compile, so this save format was not mutation-tested; 2026-10-26)
                var v = new float[7];
                var readable = true;
                for (int i = 0; i < v.Length && readable; i++) readable = Read(f[i + 1], out v[i]);
                if (!readable) { ModLog.Verbose("ChestAttachment.Parse: entry '" + entry + "' skipped (a position, angle or scale is not a finite number)"); continue; }

                result.Add(new ChestAttachment
                {
                    Prefab = f[0],
                    Position = new Vector3(v[0], v[1], v[2]),
                    Euler = new Vector3(v[3], v[4], v[5]),
                    Scale = Mathf.Clamp(v[6], MinScale, MaxScale),
                    Paid = f.Length < 9 || f[8] != "0",   // saves from before the paid flag count as paid
                    Cost = f.Length >= 10 ? ParseCost(f[9]) : null   // saves from before the cost was stored have none
                });
            }
            return result;
        }

        private static bool Read(string text, out float value)
        {
            // "NaN" and "Infinity" parse as valid floats, but an item at such a position / size can't be shown or
            // measured (every limit calculation would turn into NaN too), so a damaged entry is skipped like any
            // other unreadable one.
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool Same(IList<ChestAttachment> a, IList<ChestAttachment> b)
        {
            return string.Equals(Serialize(a), Serialize(b), StringComparison.Ordinal);
        }
    }
}
