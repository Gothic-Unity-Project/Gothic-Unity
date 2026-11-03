using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Gothic.Core.Domain.SaveGames;
using Gothic.Core.Models.SaveGames;
using Gothic.Core.Services.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Gothic.Tests.EditMode.SaveGames
{
    /// <summary>
    /// UNITYSAVE.json: sections of other owners survive a save, every registered section gets its data back.
    /// </summary>
    public class UnitySaveDomainTest
    {
        private string _folder;


        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), $"GothicUnity-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }

        [Test]
        public void CreateDataKeepsSectionsWithoutOwner()
        {
            var previous = new UnitySaveData();
            previous.Sections["vr.carriedItems"] = new UnitySaveSection { Version = 3, Data = new JValue("holster") };
            previous.Sections["core.test"] = new UnitySaveSection { Version = 1, Data = new JValue("old") };

            var sections = Register(new TestSection("core.test", 2) { Value = "new" });
            var data = UnitySaveDomain.CreateData(new UnitySaveHeader(), previous, sections);

            Assert.AreEqual("holster", data.Sections["vr.carriedItems"].Data.Value<string>());
            Assert.AreEqual(3, data.Sections["vr.carriedItems"].Version);
            Assert.AreEqual("new", data.Sections["core.test"].Data.Value<string>());
            Assert.AreEqual(2, data.Sections["core.test"].Version);
        }

        [Test]
        public void CreateDataKeepsPreviousDataOfFailingSection()
        {
            var previous = new UnitySaveData();
            previous.Sections["core.test"] = new UnitySaveSection { Version = 1, Data = new JValue("old") };

            LogAssert.Expect(LogType.Error, new Regex("core.test couldn't be saved"));
            var sections = Register(new TestSection("core.test", 1) { IsCaptureFailing = true });
            var data = UnitySaveDomain.CreateData(new UnitySaveHeader(), previous, sections);

            Assert.AreEqual("old", data.Sections["core.test"].Data.Value<string>());
        }

        [Test]
        public void ReadReturnsWrittenData()
        {
            var data = UnitySaveDomain.CreateData(new UnitySaveHeader { Game = "G1" }, null,
                Register(new TestSection("core.test", 4) { Value = "value" }));
            File.WriteAllText(Path.Combine(_folder, UnitySaveDomain.FileName), JsonConvert.SerializeObject(data));

            var read = new UnitySaveDomain().Read(_folder);

            Assert.AreEqual("G1", read.Header.Game);
            Assert.AreEqual(4, read.Sections["core.test"].Version);
            Assert.AreEqual("value", read.Sections["core.test"].Data.Value<string>());
        }

        [Test]
        public void ReadWithoutFileReturnsNull()
        {
            Assert.IsNull(new UnitySaveDomain().Read(_folder));
        }

        [Test]
        public void RestoreGivesSavedVersionAndSkipsMissingSections()
        {
            var save = new UnitySaveData { Header = null };
            save.Sections["core.saved"] = new UnitySaveSection { Version = 7, Data = new JValue("value") };
            var saved = new TestSection("core.saved", 8);
            var missing = new TestSection("core.missing", 1);

            new UnitySaveDomain().Restore(save, Register(saved, missing));

            Assert.AreEqual("value", saved.Value);
            Assert.AreEqual(7, saved.RestoredVersion);
            Assert.IsFalse(missing.IsRestored);
        }

        [Test]
        public void RestoreContinuesAfterFailingSection()
        {
            var save = new UnitySaveData { Header = null };
            save.Sections["core.failing"] = new UnitySaveSection { Version = 1, Data = new JValue("value") };
            save.Sections["core.working"] = new UnitySaveSection { Version = 1, Data = new JValue("value") };
            var working = new TestSection("core.working", 1);

            LogAssert.Expect(LogType.Error, new Regex("core.failing couldn't be restored"));
            new UnitySaveDomain().Restore(save,
                Register(new TestSection("core.failing", 1) { IsRestoreFailing = true }, working));

            Assert.IsTrue(working.IsRestored);
        }

        private static Dictionary<string, ISaveStateSection> Register(params TestSection[] sections)
        {
            var result = new Dictionary<string, ISaveStateSection>();
            foreach (var section in sections)
                result[section.Key] = section;
            return result;
        }

        private class TestSection : ISaveStateSection
        {
            public string Key { get; }
            public int Version { get; }

            public string Value;
            public bool IsCaptureFailing;
            public bool IsRestoreFailing;
            public bool IsRestored;
            public int RestoredVersion;


            public TestSection(string key, int version)
            {
                Key = key;
                Version = version;
            }

            public JToken Capture()
            {
                if (IsCaptureFailing)
                    throw new InvalidOperationException("capture");
                return new JValue(Value);
            }

            public void Reset() => Value = null;

            public void Restore(JToken data, int savedVersion)
            {
                if (IsRestoreFailing)
                    throw new InvalidOperationException("restore");

                Value = data.Value<string>();
                RestoredVersion = savedVersion;
                IsRestored = true;
            }
        }
    }
}
