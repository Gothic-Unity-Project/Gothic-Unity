using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Achievements;
using Reflex.Attributes;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Achievements
{
    /// <summary>
    /// The profile file &lt;persistentDataPath&gt;/Achievements/&lt;GameVersion&gt;&lt;ModCacheSuffix&gt;.json and the
    /// device id next to it. The one writer: a change is written a frame later (not in the hit or death frame),
    /// through a temp file and a rename. Flush() writes a pending change at once (quit, pause).
    /// </summary>
    public class AchievementProfileDomain
    {
        public const string FolderName = "Achievements";
        private const string _deviceIdFileName = "device.id";
        private const int _deviceIdBytes = 32;

        [Inject] private readonly UnityMonoService _unityMonoService;

        private readonly AchievementSealDomain _sealDomain = new();

        private string _path;
        private byte[] _key;
        private AchievementProfileData _pendingProfile;


        /// <summary>
        /// A random id generated once per installation. Not SystemInfo.deviceUniqueIdentifier: it can be
        /// "unsupportedIdentifier" or change after an OS update on Quest/Pico.
        /// </summary>
        public static byte[] ReadOrCreateDeviceId(string folder)
        {
            var path = Path.Combine(folder, _deviceIdFileName);
            if (File.Exists(path))
            {
                var stored = File.ReadAllText(path).Trim();
                if (stored.Length > 0)
                    return Encoding.UTF8.GetBytes(stored);
            }

            var bytes = new byte[_deviceIdBytes];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);

            var id = BitConverter.ToString(bytes).Replace("-", string.Empty);
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, id);
            return Encoding.UTF8.GetBytes(id);
        }

        /// <summary>
        /// Reads the profile. A missing file is an empty profile, a broken seal keeps every unlock (shown as
        /// unsealed) and forgets the runs (they play as Adanos). A pending change of the previous profile (game or
        /// mod switched) is written first.
        /// </summary>
        public AchievementProfileData Load(string path, byte[] key)
        {
            Flush();
            _path = path;
            _key = key;
            if (!File.Exists(path))
                return new AchievementProfileData();

            try
            {
                return Open(File.ReadAllText(path), key);
            }
            catch (Exception e)
            {
                // Nothing is deleted: the file is kept for a bug report, a new profile starts.
                var keptPath = path + ".unreadable";
                Logger.LogWarning($"[Achievements] {path} can't be read ({e.Message}). It's kept as {keptPath}.",
                    LogCat.Achievements);
                TryMove(path, keptPath);
                return new AchievementProfileData();
            }
        }

        /// <summary>
        /// The profile of a file's text with each unlock's seal state. Throws if it isn't a profile.
        /// </summary>
        public AchievementProfileData Open(string text, byte[] key)
        {
            var profile = _sealDomain.OpenProfile(key, text, out var isFileSealed);
            _sealDomain.VerifyChain(key, profile.Unlocks);

            if (!isFileSealed)
            {
                Logger.LogWarning("[Achievements] The profile's seal is broken (edited, or from another device or " +
                                  "build). Its unlocks are shown as unsealed until earned again.", LogCat.Achievements);
                profile.Runs.Clear();
            }

            foreach (var unlock in profile.Unlocks)
            {
                if (!isFileSealed || !IsPlausible(profile, unlock))
                    unlock.IsSealed = false;
            }

            return profile;
        }

        /// <summary>
        /// An Innos unlock needs a known run and a save number the run has reached.
        /// </summary>
        private static bool IsPlausible(AchievementProfileData profile, AchievementUnlock unlock)
        {
            if (unlock.Tier != AchievementTier.Innos)
                return true;

            return unlock.RunId != null && profile.Runs.TryGetValue(unlock.RunId, out var record) &&
                   unlock.SaveNo <= record.NewestSaveNo;
        }

        /// <summary>
        /// The text Write() stores: the chain renewed over the sealed unlocks, then sealed.
        /// </summary>
        public string Seal(AchievementProfileData profile, byte[] key)
        {
            _sealDomain.ChainUnlocks(key, profile.Unlocks);
            return _sealDomain.SealProfile(key, profile);
        }

        /// <summary>
        /// Written a frame later. Several changes in one frame are written once.
        /// </summary>
        public void ScheduleWrite(AchievementProfileData profile)
        {
            var isScheduled = _pendingProfile != null;
            _pendingProfile = profile;
            if (!isScheduled)
                _unityMonoService.StartCoroutine(WriteNextFrame());
        }

        private static void TryMove(string from, string to)
        {
            try
            {
                File.Copy(from, to, true);
                File.Delete(from);
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[Achievements] Keeping {from} as {to} failed: {e.Message}", LogCat.Achievements);
            }
        }

        private IEnumerator WriteNextFrame()
        {
            yield return null;
            Flush();
        }

        /// <summary>
        /// Writes a pending change now (quit, pause/headset off). Nothing pending: nothing to do.
        /// </summary>
        public void Flush()
        {
            var profile = _pendingProfile;
            _pendingProfile = null;
            if (profile == null || _path == null)
                return;

            var tmpPath = _path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(tmpPath, Seal(profile, _key));
                if (File.Exists(_path))
                    File.Replace(tmpPath, _path, null);
                else
                    File.Move(tmpPath, _path);
            }
            catch (PlatformNotSupportedException)
            {
                ReplaceByMoving(tmpPath);
            }
            catch (Exception e)
            {
                Logger.LogError($"[Achievements] Writing {_path} failed: {e.Message}", LogCat.Achievements);
            }
        }

        /// <summary>
        /// File.Replace isn't supported (some Android file systems): the old profile is moved aside, the new one moved
        /// in, and only then the old one deleted. A failed move puts the old profile back.
        /// </summary>
        private void ReplaceByMoving(string tmpPath)
        {
            var oldPath = _path + ".old";
            try
            {
                if (File.Exists(oldPath))
                    File.Delete(oldPath);
                File.Move(_path, oldPath);
                try
                {
                    File.Move(tmpPath, _path);
                }
                catch (Exception)
                {
                    File.Move(oldPath, _path);
                    throw;
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"[Achievements] Writing {_path} failed: {e.Message}", LogCat.Achievements);
                return;
            }

            try
            {
                File.Delete(oldPath);
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[Achievements] Deleting {oldPath} failed: {e.Message}", LogCat.Achievements);
            }
        }
    }
}
