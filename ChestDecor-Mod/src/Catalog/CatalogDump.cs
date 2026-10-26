using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Kydl.Common;
using UnityEngine;
using static ChestDecor.AttachCatalog;
using static ChestDecor.CatalogSources;
using static ChestDecor.CatalogEntries;

namespace ChestDecor
{
    /// <summary>
    /// Part of the attachment catalog (split from AttachCatalog, class-size rule): the DumpCatalog file for checking the list.
    /// </summary>
    internal static class CatalogDump
    {
        /// <summary>
        /// Writes every listed entry (with its categories, cost and where it comes from) and every candidate that was
        /// left out (with the reason) to BepInEx/cache/ChestDecorDump/AttachCatalog.txt, so the list can be checked
        /// against what was asked for. Does nothing when the "Dump Catalog" setting is off.
        /// </summary>
        internal static void WriteDump(List<AttachEntry> entries)
        {
            if (ModSettings.DumpCatalog == null || !ModSettings.DumpCatalog.Value) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Chest Decor attachment list, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"{entries.Count} listed entries, {Excluded.Count} candidates left out.");
                sb.AppendLine();
                sb.AppendLine("== Listed ==   prefab | shown name | categories | cost | where it comes from");
                foreach (var e in entries)
                {
                    sb.Append(e.Prefab).Append(" | ").Append(e.DisplayName).Append(" | ").Append(e.Categories).Append(" | ");
                    switch (e.Cost)
                    {
                        case CostKind.Coins: sb.Append(e.CoinPrice).Append(" coins"); break;
                        case CostKind.Item: sb.Append("the item itself"); break;
                        case CostKind.Material:
                            sb.Append(e.CostAmount).Append("x ").Append(e.CostItem != null ? e.CostItem.name : "?");
                            break;
                        case CostKind.Piece: sb.Append("build cost"); break;
                        default: sb.Append("recipe"); break;
                    }
                    if (!string.IsNullOrEmpty(e.Sources)) sb.Append(" | ").Append(e.Sources);
                    sb.AppendLine();
                }

                sb.AppendLine();
                sb.AppendLine("== Left out ==");
                foreach (var line in Excluded) sb.AppendLine(line);

                var dir = Path.Combine(BepInEx.Paths.CachePath, "ChestDecorDump");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "AttachCatalog.txt");
                File.WriteAllText(file, sb.ToString());
                ModLog.Info("Attachment list written to " + file);
            }
            catch (Exception e)
            {
                ModLog.Warn($"Could not write the attachment list dump: {e.Message}");
            }
        }
    }
}
