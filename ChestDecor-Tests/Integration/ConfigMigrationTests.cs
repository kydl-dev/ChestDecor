using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ChestDecor.Tests.Integration
{
    /// <summary>
    /// Settings were grouped by functionality (D-074): a player's value must survive the move to the new section / key. Uses a real BepInEx
    /// ConfigFile on a temp file (reached by reflection, the test project does not reference BepInEx) and the built mod's ConfigMigration.
    /// </summary>
    [TestFixture]
    public class ConfigMigrationTests
    {
        string _dir;

        [SetUp]
        public void MakeDir() { _dir = Path.Combine(Path.GetTempPath(), "chestdecor-cfg-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }

        [TearDown]
        public void DeleteDir() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

        object NewConfig(string text)
        {
            var path = Path.Combine(_dir, "test.cfg");
            File.WriteAllText(path, text);
            var type = Type.GetType("BepInEx.Configuration.ConfigFile, BepInEx", true);
            return Activator.CreateInstance(type, path, true);
        }

        static object Bind(object cfg, string section, string key, object def)
        {
            var m = cfg.GetType().GetMethods().First(x => x.Name == "Bind" && x.IsGenericMethod && x.GetParameters().Length == 4 && x.GetParameters()[3].ParameterType == typeof(string));
            return m.MakeGenericMethod(def.GetType()).Invoke(cfg, new[] { section, key, def, "test" });
        }

        static bool Move(object cfg, string oldSection, string oldKey, object entry)
        {
            var move = ModAssembly.Type("ChestDecor.ConfigMigration").GetMethod("Move", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var entryType = entry.GetType().GetGenericArguments()[0];
            return (bool)move.MakeGenericMethod(entryType).Invoke(null, new[] { cfg, oldSection, oldKey, entry });
        }

        static object Value(object entry) { return entry.GetType().GetProperty("Value").GetValue(entry, null); }

        [Test]
        public void OldValue_IsKeptInTheNewPlace()
        {
            var cfg = NewConfig("[General]\n\nLive Preview = true\n\n[Camera]\n\nFree Camera = false\n");
            var live = Bind(cfg, "Colours", "Live Preview", false);
            var cam = Bind(cfg, "Camera", "Enable", true);

            Assert.That(Move(cfg, "General", "Live Preview", live), Is.True);
            Assert.That(Move(cfg, "Camera", "Free Camera", cam), Is.True);

            Assert.That(Value(live), Is.EqualTo(true), "Live Preview was on, it stays on in [Colours]");
            Assert.That(Value(cam), Is.EqualTo(false), "Free Camera was off, [Camera] Enable is off");
        }

        [Test]
        public void NoOldValue_ChangesNothing()
        {
            var cfg = NewConfig("[General]\n\nShow Hover Hint = true\n");
            var live = Bind(cfg, "Colours", "Live Preview", false);

            Assert.That(Move(cfg, "General", "Live Preview", live), Is.False);
            Assert.That(Value(live), Is.EqualTo(false));
        }

        [Test]
        public void NewValueAlreadySet_WinsOverTheOldOne()
        {
            var cfg = NewConfig("[General]\n\nLive Preview = true\n\n[Colours]\n\nLive Preview = false\n\n[Colours2]\n\nX = 1\n");
            var live = Bind(cfg, "Colours", "Live Preview", true);      // default true, the file says false: the player already set the new entry

            Assert.That(Move(cfg, "General", "Live Preview", live), Is.False);
            Assert.That(Value(live), Is.EqualTo(false));
        }
    }
}
