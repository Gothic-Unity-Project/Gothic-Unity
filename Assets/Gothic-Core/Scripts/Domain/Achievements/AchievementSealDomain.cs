using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Gothic.Core.Models.Achievements;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gothic.Core.Domain.Achievements
{
    /// <summary>
    /// Tamper-evident, not tamper-proof (Docs/achievements.md, "Seal"): HMAC-SHA256 with key = build key + device id,
    /// stored as gzip + base64. The profile carries a seal over its content and a hash chain over its unlocks, a save
    /// section a seal that also covers its run id, save number and the save it was written into.
    /// Pure: the key bytes are passed in, no file access (EditMode-testable).
    /// </summary>
    public class AchievementSealDomain
    {
        private const int _formatVersion = 1;
        private const string _dataKey = "Data";
        private const string _sealKey = "Seal";
        private const string _versionKey = "Version";
        private const string _runIdKey = "RunId";
        private const string _saveNoKey = "SaveNo";


        public static byte[] CreateKey(byte[] buildKey, byte[] deviceId)
        {
            var key = new byte[buildKey.Length + deviceId.Length];
            Buffer.BlockCopy(buildKey, 0, key, 0, buildKey.Length);
            Buffer.BlockCopy(deviceId, 0, key, buildKey.Length, deviceId.Length);
            return key;
        }

        public string Sign(byte[] key, string content)
        {
            using var hmac = new HMACSHA256(key);
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(content)));
        }

        public bool Verify(byte[] key, string content, string seal)
        {
            if (string.IsNullOrEmpty(seal))
                return false;

            var expected = Encoding.ASCII.GetBytes(Sign(key, content));
            var actual = Encoding.ASCII.GetBytes(seal);
            if (expected.Length != actual.Length)
                return false;

            // Constant time.
            var difference = 0;
            for (var i = 0; i < expected.Length; i++)
                difference |= expected[i] ^ actual[i];
            return difference == 0;
        }

        /// <summary>
        /// Not readable at a glance. Decode() is all a bug report needs.
        /// </summary>
        public static string Encode(string text)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                gzip.Write(bytes, 0, bytes.Length);
            }

            return Convert.ToBase64String(output.ToArray());
        }

        public static string Decode(string encoded)
        {
            using var input = new MemoryStream(Convert.FromBase64String(encoded));
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Writes the chain over the sealed unlocks (each entry's hash covers the previous one). Unsealed entries stay
        /// outside the chain without a hash until they're earned again.
        /// </summary>
        public void ChainUnlocks(byte[] key, List<AchievementUnlock> unlocks)
        {
            var previous = string.Empty;
            foreach (var unlock in unlocks)
            {
                if (!unlock.IsSealed)
                {
                    unlock.Chain = null;
                    continue;
                }

                unlock.Chain = Sign(key, GetChainContent(previous, unlock));
                previous = unlock.Chain;
            }
        }

        /// <summary>
        /// Sets IsSealed of every unlock: inserted, edited or reordered entries break the chain at that entry.
        /// </summary>
        public void VerifyChain(byte[] key, List<AchievementUnlock> unlocks)
        {
            var previous = string.Empty;
            foreach (var unlock in unlocks)
            {
                unlock.IsSealed = Verify(key, GetChainContent(previous, unlock), unlock.Chain);
                if (unlock.IsSealed)
                    previous = unlock.Chain;
            }
        }

        private static string GetChainContent(string previous, AchievementUnlock unlock)
        {
            return $"{previous}|{unlock.Id}|{(int)unlock.Tier}|{unlock.DateUtc}|{unlock.RunId}|{unlock.SaveNo}";
        }

        /// <summary>
        /// The profile file's text. The unlocks must be chained (ChainUnlocks) before.
        /// </summary>
        public string SealProfile(byte[] key, AchievementProfileData profile)
        {
            var data = Encode(JsonConvert.SerializeObject(profile));
            var envelope = new JObject
            {
                [_versionKey] = _formatVersion,
                [_dataKey] = data,
                [_sealKey] = Sign(key, data)
            };
            return envelope.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Reads a profile file. isFileSealed is false if its content was edited or comes from another device or
        /// build. The unlocks' own seals are checked by VerifyChain(). Throws if the text isn't a profile at all.
        /// </summary>
        public AchievementProfileData OpenProfile(byte[] key, string text, out bool isFileSealed)
        {
            var envelope = JObject.Parse(text);
            var data = envelope.Value<string>(_dataKey) ?? throw new FormatException("The profile has no data.");
            isFileSealed = Verify(key, data, envelope.Value<string>(_sealKey));

            var profile = JsonConvert.DeserializeObject<AchievementProfileData>(Decode(data)) ??
                          new AchievementProfileData();
            profile.Unlocks ??= new List<AchievementUnlock>();
            profile.Runs ??= new Dictionary<string, AchievementRunRecord>();
            return profile;
        }

        /// <summary>
        /// The save section core.achievements. saveIdentity names the save it's written into (date + play time), so
        /// the section can't be copied into another save.
        /// </summary>
        public JObject SealSection(byte[] key, AchievementRunState run, string saveIdentity)
        {
            var data = Encode(JsonConvert.SerializeObject(run));
            return new JObject
            {
                [_runIdKey] = run.RunId,
                [_saveNoKey] = run.SaveNo,
                [_dataKey] = data,
                [_sealKey] = Sign(key, GetSectionContent(run.RunId, run.SaveNo, saveIdentity, data))
            };
        }

        /// <summary>
        /// False (run = null) if the section isn't readable, was edited, copied into another save or written on another
        /// device/build.
        /// </summary>
        public bool TryOpenSection(byte[] key, JToken section, string saveIdentity, out AchievementRunState run)
        {
            run = null;
            if (section is not JObject json)
                return false;

            try
            {
                var runId = json.Value<string>(_runIdKey);
                var saveNo = json.Value<int?>(_saveNoKey) ?? -1;
                var data = json.Value<string>(_dataKey);
                if (data == null ||
                    !Verify(key, GetSectionContent(runId, saveNo, saveIdentity, data), json.Value<string>(_sealKey)))
                    return false;

                var state = JsonConvert.DeserializeObject<AchievementRunState>(Decode(data));
                if (state == null || state.RunId != runId || state.SaveNo != saveNo)
                    return false;

                state.Counters ??= new Dictionary<string, int>();
                state.Distinct ??= new Dictionary<string, HashSet<string>>();
                run = state;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetSectionContent(string runId, int saveNo, string saveIdentity, string data)
        {
            return $"{runId}|{saveNo}|{saveIdentity}|{data}";
        }
    }
}
