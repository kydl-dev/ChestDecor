using System.Collections.Generic;
using System.Linq;
using Kydl.Common;
using UnityEngine;

namespace ChestDecor.Testing
{
    /// <summary>
    /// KydlCore D-084: hooks for DrakesTestLab (ChestDecor-Tests/TestLab/suites/ChestDecor). They call the REAL ChestDecorator code (Save, SaveAttachments, Load...), not a copy.
    /// A chest is passed as a GameObject tag (`"as": "chest"` on the `spawn` step) or found again with `chestdecor.nearestChest` after a relog.
    /// </summary>
    internal static class TestHooks
    {
        static ChestDecorator Of(GameObject chest)
        {
            if (chest == null) throw new System.Exception("no chest");
            var d = chest.GetComponentInChildren<ChestDecorator>();
            if (d == null) throw new System.Exception(chest.name + " has no ChestDecorator");
            return d;
        }

        [TestHook("chestdecor.selftest")]
        static string SelfTest() { return TestLabSupport.RunInGameTests(typeof(InGameTests)); }

        /// <summary>The ChestDecorator nearest to the player within 30 m (after a relog the old tag is gone).</summary>
        [TestHook("chestdecor.nearestChest")]
        static GameObject NearestChest()
        {
            var p = Player.m_localPlayer;
            if (p == null) throw new System.Exception("no player");
            ChestDecorator best = null; float bd = 30f;
            foreach (var d in Object.FindObjectsByType<ChestDecorator>(FindObjectsSortMode.None))
            {
                float dist = Vector3.Distance(d.transform.position, p.transform.position);
                if (dist < bd) { bd = dist; best = d; }
            }
            if (best == null) throw new System.Exception("no chest within 30 m");
            return best.gameObject;
        }

        /// <summary>Number of colour parts the chest has (0 = nothing to colour: the decorator did not scan it).</summary>
        [TestHook("chestdecor.slotCount")]
        static int SlotCount(GameObject chest) { return Of(chest).Slots.Count; }

        [TestHook("chestdecor.canBeDecorated")]
        static bool CanBeDecorated(GameObject chest) { return Of(chest).CanBeDecorated; }

        /// <summary>Colours every part with the hex colour (RRGGBB) through ChestDecorator.Save. Returns the number of parts saved.</summary>
        [TestHook("chestdecor.paint")]
        static int Paint(GameObject chest, string hex, string mode = "Full")
        {
            var d = Of(chest);
            Color c;
            if (!ColorUtility.TryParseHtmlString("#" + hex.TrimStart('#'), out c)) throw new System.Exception("bad colour " + hex);
            var colours = new Dictionary<string, PartColor>();
            foreach (var s in d.Slots) colours[s.Key] = new PartColor(c, mode == "Tint" ? ColorMode.Tint : ColorMode.Full);
            if (!d.Save(colours)) throw new System.Exception("Save returned false");
            return colours.Count;
        }

        /// <summary>Number of parts that have a saved colour (read back from the chest's ZDO).</summary>
        [TestHook("chestdecor.savedCount")]
        static int SavedCount(GameObject chest) { return Of(chest).LoadSaved().Count; }

        /// <summary>The saved colour of the first part as RRGGBB ("" = none saved).</summary>
        [TestHook("chestdecor.savedHex")]
        static string SavedHex(GameObject chest)
        {
            var saved = Of(chest).LoadSaved();
            return saved.Count == 0 ? "" : ColorUtility.ToHtmlStringRGB(saved.Values.First().Rgb);
        }

        /// <summary>Removes all saved colours (Save with an empty set). Returns the number of parts left.</summary>
        [TestHook("chestdecor.resetColours")]
        static int ResetColours(GameObject chest)
        {
            var d = Of(chest);
            d.Save(new Dictionary<string, PartColor>());
            return d.LoadSaved().Count;
        }

        /// <summary>Attaches one default item (prefab name) through SaveAttachments; returns the number of attachments saved afterwards.</summary>
        [TestHook("chestdecor.attach")]
        static int Attach(GameObject chest, string prefab)
        {
            var d = Of(chest);
            var list = d.LoadSavedAttachments();
            list.Add(d.CreateDefault(prefab, list.Count));
            if (!d.SaveAttachments(list)) throw new System.Exception("SaveAttachments returned false");
            return d.LoadSavedAttachments().Count;
        }

        [TestHook("chestdecor.attachCount")]
        static int AttachCount(GameObject chest) { return Of(chest).LoadSavedAttachments().Count; }

        /// <summary>Prefab of attachment `index` ("" = none).</summary>
        [TestHook("chestdecor.attachPrefab")]
        static string AttachPrefab(GameObject chest, int index)
        {
            var l = Of(chest).LoadSavedAttachments();
            return index >= 0 && index < l.Count ? l[index].Prefab : "";
        }

        /// <summary>Serialise then parse an attachment list: the round trip must keep the count and the prefabs.</summary>
        [TestHook("chestdecor.attachRoundTrip")]
        static bool AttachRoundTrip(GameObject chest)
        {
            var l = Of(chest).LoadSavedAttachments();
            var back = ChestAttachment.Parse(ChestAttachment.Serialize(l));
            return ChestAttachment.Same(l, back);
        }

        /// <summary>Edit lock: true when this player can take the chest's editor lock.</summary>
        [TestHook("chestdecor.lock")]
        static string Lock(GameObject chest)
        {
            var d = Of(chest);
            string holder;
            var r = d.TryLock(Player.m_localPlayer, out holder);
            return r.ToString() + (d.HoldsLock() ? " (held)" : "");
        }

        /// <summary>The editor window is open (KyUi window of the mod).</summary>
        [TestHook("chestdecor.editorOpen")]
        static bool EditorOpen() { return KydlCore.KyUi.IsOpen(Plugin.ModGuid, "editor"); }

        /// <summary>Opens the editor window for the chest through ChestDecorUI.Open (what the decorate key does after it has found the chest under the crosshair).</summary>
        [TestHook("chestdecor.openEditor")]
        static bool OpenEditor(GameObject chest)
        {
            var d = Of(chest);
            string holder;
            d.TryLock(Player.m_localPlayer, out holder);        // the decorate key takes the editor lock first, then opens the window
            ChestDecorUI.Open(d);
            return EditorOpen();
        }

        [TestHook("chestdecor.closeEditor")]
        static bool CloseEditor() { ChestDecorUI.Close(); return KydlCore.KyUi.AnyOpen; }

        [TestHook("chestdecor.unlock")]
        static bool Unlock(GameObject chest) { Of(chest).Unlock(); return !Of(chest).HoldsLock(); }
    }
}
