using System.IO;
using System.Linq;
using Gothic.Core.Domain.Config;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Gothic.Tests.PlayMode
{
    /// <summary>
    /// Guards saving menu settings into Gothic.ini. On case-sensitive file systems (Linux, Android) the file may be
    /// named GOTHIC.INI (Wine/Proton); the writer must update that file instead of failing on "Gothic.ini".
    /// Runs without a Gothic installation: each test writes into its own temp folder.
    /// </summary>
    public class GothicIniWriterTest
    {
        private string _systemDir;

        [SetUp]
        public void SetUp()
        {
            _systemDir = Path.Combine(Path.GetTempPath(), "GothicIniWriterTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_systemDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_systemDir))
                Directory.Delete(_systemDir, true);
        }

        [Test]
        public void WritesIntoFileWithOtherCasing()
        {
            var realIniPath = Path.Combine(_systemDir, "GOTHIC.INI");
            File.WriteAllLines(realIniPath, new[] { "[GAME]", "subTitles=1", "[SOUND]", "musicVolume=1" });

            new GothicIniWriter(Path.Combine(_systemDir, "Gothic.ini")).WriteSetting("GAME", "subTitles", "0");

            var lines = File.ReadAllLines(realIniPath);
            CollectionAssert.Contains(lines, "subTitles=0");
            CollectionAssert.Contains(lines, "musicVolume=1");
            CollectionAssert.DoesNotContain(lines, "subTitles=1");

            var fileNames = Directory.GetFiles(_systemDir).Select(Path.GetFileName).ToArray();
            CollectionAssert.AreEquivalent(new[] { "GOTHIC.INI", "GOTHIC.INI.Gothic.Unity.bak" }, fileNames,
                "Only the existing ini and its backup may exist - no second Gothic.ini.");
        }

        [Test]
        public void KeepsExistingBackupOfOtherCasing()
        {
            var realIniPath = Path.Combine(_systemDir, "GOTHIC.INI");
            var originalBackupPath = Path.Combine(_systemDir, "Gothic.ini.Gothic.Unity.bak");
            File.WriteAllLines(realIniPath, new[] { "[GAME]", "subTitles=1" });
            File.WriteAllLines(originalBackupPath, new[] { "[GAME]", "subTitles=original" });

            new GothicIniWriter(Path.Combine(_systemDir, "Gothic.ini")).WriteSetting("GAME", "subTitles", "0");

            CollectionAssert.Contains(File.ReadAllLines(originalBackupPath), "subTitles=original");
            Assert.AreEqual(2, Directory.GetFiles(_systemDir).Length, "The original backup must not get a twin.");
        }

        [Test]
        public void MissingIniDoesNotThrow()
        {
            // The writer logs an error instead - expected here.
            LogAssert.ignoreFailingMessages = true;

            Assert.DoesNotThrow(() =>
                new GothicIniWriter(Path.Combine(_systemDir, "Gothic.ini")).WriteSetting("GAME", "subTitles", "0"));
            Assert.IsEmpty(Directory.GetFiles(_systemDir));
        }
    }
}
