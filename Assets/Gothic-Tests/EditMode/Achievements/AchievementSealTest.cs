using System.Collections.Generic;
using System.Text;
using Gothic.Core.Domain.Achievements;
using Gothic.Core.Models.Achievements;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gothic.Tests.EditMode.Achievements
{
    /// <summary>
    /// Tamper-evident files: edits, copies and other devices break the seal, an untouched file stays sealed.
    /// </summary>
    public class AchievementSealTest
    {
        private const string _saveIdentity = "20.11.2026 - 18:00|3600";

        private static readonly byte[] _key = AchievementSealDomain.CreateKey(Encoding.UTF8.GetBytes("build"),
            Encoding.UTF8.GetBytes("device-a"));
        private static readonly byte[] _otherDeviceKey = AchievementSealDomain.CreateKey(
            Encoding.UTF8.GetBytes("build"), Encoding.UTF8.GetBytes("device-b"));

        private AchievementSealDomain _sealDomain;
        private AchievementProfileDomain _profileDomain;


        [SetUp]
        public void SetUp()
        {
            _sealDomain = new AchievementSealDomain();
            _profileDomain = new AchievementProfileDomain();
        }

        private static AchievementProfileData CreateProfile()
        {
            var profile = new AchievementProfileData();
            profile.Runs["run1"] = new AchievementRunRecord { NewestSaveNo = 3 };
            profile.Unlocks.Add(CreateUnlock("g1.a", AchievementTier.Innos, 2));
            profile.Unlocks.Add(CreateUnlock("g1.b", AchievementTier.Adanos, 3));
            profile.Unlocks.Add(CreateUnlock("g1.c", AchievementTier.Innos, 3));
            return profile;
        }

        private static AchievementUnlock CreateUnlock(string id, AchievementTier tier, int saveNo)
        {
            return new AchievementUnlock
            {
                Id = id, Tier = tier, DateUtc = "2026-11-20T18:00:00Z", RunId = "run1", SaveNo = saveNo,
                IsSealed = true
            };
        }

        private static AchievementRunState CreateRun()
        {
            var run = new AchievementRunState { RunId = "run1", SaveNo = 4, Tier = AchievementTier.Innos };
            run.Counters["lockpick.success"] = 7;
            run.AddDistinct("document.shown", "ItWr_Book_01");
            return run;
        }

        /// <summary>
        /// Rewrites the profile's data like a text editor would (decode, change, encode), keeping the old seal.
        /// </summary>
        private static string EditProfileData(string text, System.Action<JObject> edit)
        {
            var envelope = JObject.Parse(text);
            var data = JObject.Parse(AchievementSealDomain.Decode(envelope.Value<string>("Data")));
            edit(data);
            envelope["Data"] = AchievementSealDomain.Encode(data.ToString());
            return envelope.ToString();
        }

        [Test]
        public void ProfileRoundtripStaysSealed()
        {
            var text = _profileDomain.Seal(CreateProfile(), _key);

            var profile = _profileDomain.Open(text, _key);

            Assert.AreEqual(3, profile.Unlocks.Count);
            Assert.IsTrue(profile.Unlocks.TrueForAll(i => i.IsSealed));
            Assert.AreEqual(3, profile.Runs["run1"].NewestSaveNo);
        }

        [Test]
        public void ProfileIsNotReadableAtAGlance()
        {
            var text = _profileDomain.Seal(CreateProfile(), _key);

            StringAssert.DoesNotContain("g1.a", text);
        }

        [Test]
        public void EditedProfileIsUnsealedButKept()
        {
            var text = EditProfileData(_profileDomain.Seal(CreateProfile(), _key),
                data => data["Unlocks"][1]["Tier"] = (int)AchievementTier.Innos);

            var profile = _profileDomain.Open(text, _key);

            Assert.AreEqual(3, profile.Unlocks.Count, "Nothing is deleted.");
            Assert.IsTrue(profile.Unlocks.TrueForAll(i => !i.IsSealed));
            Assert.IsEmpty(profile.Runs, "Untrusted runs are forgotten (they play as Adanos).");
        }

        [Test]
        public void ProfileFromAnotherDeviceIsUnsealed()
        {
            var text = _profileDomain.Seal(CreateProfile(), _key);

            var profile = _profileDomain.Open(text, _otherDeviceKey);

            Assert.IsTrue(profile.Unlocks.TrueForAll(i => !i.IsSealed));
        }

        [Test]
        public void InsertedUnlockBreaksTheChainOnlyThere()
        {
            var unlocks = CreateProfile().Unlocks;
            _sealDomain.ChainUnlocks(_key, unlocks);
            var forged = CreateUnlock("g1.forged", AchievementTier.Innos, 3);
            forged.Chain = unlocks[0].Chain;
            unlocks.Insert(1, forged);

            _sealDomain.VerifyChain(_key, unlocks);

            CollectionAssert.AreEqual(new[] { true, false, true, true }, unlocks.ConvertAll(i => i.IsSealed));
        }

        [Test]
        public void UnsealedUnlockStaysOutOfTheChain()
        {
            var unlocks = CreateProfile().Unlocks;
            unlocks[1].IsSealed = false;
            _sealDomain.ChainUnlocks(_key, unlocks);

            _sealDomain.VerifyChain(_key, unlocks);

            Assert.IsNull(unlocks[1].Chain);
            CollectionAssert.AreEqual(new[] { true, false, true }, unlocks.ConvertAll(i => i.IsSealed));
        }

        [Test]
        public void ImplausibleInnosUnlockIsUnsealed()
        {
            var profile = CreateProfile();
            profile.Unlocks.Add(CreateUnlock("g1.future", AchievementTier.Innos, 9));
            var unknownRun = CreateUnlock("g1.unknownRun", AchievementTier.Innos, 1);
            unknownRun.RunId = "run2";
            profile.Unlocks.Add(unknownRun);

            var opened = _profileDomain.Open(_profileDomain.Seal(profile, _key), _key);

            CollectionAssert.AreEqual(new[] { true, true, true, false, false },
                opened.Unlocks.ConvertAll(i => i.IsSealed));
        }

        [Test]
        public void SectionRoundtrip()
        {
            var section = _sealDomain.SealSection(_key, CreateRun(), _saveIdentity);

            Assert.IsTrue(_sealDomain.TryOpenSection(_key, section, _saveIdentity, out var run));
            Assert.AreEqual("run1", run.RunId);
            Assert.AreEqual(4, run.SaveNo);
            Assert.AreEqual(AchievementTier.Innos, run.Tier);
            Assert.AreEqual(7, run.GetCounter("lockpick.success"));
            Assert.AreEqual(1, run.GetDistinctCount("document.shown"));
        }

        [Test]
        public void EditedSectionIsUnsealed()
        {
            var tainted = CreateRun();
            tainted.Tier = AchievementTier.Beliar;
            var section = _sealDomain.SealSection(_key, tainted, _saveIdentity);
            var clean = _sealDomain.SealSection(_key, CreateRun(), _saveIdentity);
            section["Data"] = clean["Data"];

            Assert.IsFalse(_sealDomain.TryOpenSection(_key, section, _saveIdentity, out var run));
            Assert.IsNull(run);
        }

        [Test]
        public void SectionCopiedIntoAnotherSaveIsUnsealed()
        {
            var section = _sealDomain.SealSection(_key, CreateRun(), _saveIdentity);

            Assert.IsFalse(_sealDomain.TryOpenSection(_key, section, "21.11.2026 - 10:00|7200", out _));
        }

        [Test]
        public void SectionWithOtherRunOrSaveNumberIsUnsealed()
        {
            var otherRun = _sealDomain.SealSection(_key, CreateRun(), _saveIdentity);
            otherRun["RunId"] = "run2";
            var otherSave = _sealDomain.SealSection(_key, CreateRun(), _saveIdentity);
            otherSave["SaveNo"] = 5;

            Assert.IsFalse(_sealDomain.TryOpenSection(_key, otherRun, _saveIdentity, out _));
            Assert.IsFalse(_sealDomain.TryOpenSection(_key, otherSave, _saveIdentity, out _));
        }

        [Test]
        public void SectionFromAnotherDeviceIsUnsealed()
        {
            var section = _sealDomain.SealSection(_key, CreateRun(), _saveIdentity);

            Assert.IsFalse(_sealDomain.TryOpenSection(_otherDeviceKey, section, _saveIdentity, out _));
        }

        [Test]
        public void EncodingRoundtrip()
        {
            const string text = "Jünger des Schläfers";

            Assert.AreEqual(text, AchievementSealDomain.Decode(AchievementSealDomain.Encode(text)));
        }
    }
}
