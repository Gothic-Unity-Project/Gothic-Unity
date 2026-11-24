using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Achievements;
using Newtonsoft.Json;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Achievements
{
    /// <summary>
    /// The profile file &lt;persistentDataPath&gt;/Achievements/&lt;GameVersion&gt;&lt;ModCacheSuffix&gt;.json, plain
    /// readable JSON. The one writer: a change is written a frame later (not in the hit or death frame), through a
    /// temp file and a rename. Flush() writes a pending change at once (quit, pause).
    /// </summary>
    public class AchievementProfileDomain
    {
        [Inject] private readonly UnityMonoService _unityMonoService;

        private string _path;
        private AchievementProfileData _pendingProfile;


        private static string Folder => Path.Combine(Application.persistentDataPath,
            Constants.Achievements.FolderName);

        /// <summary>
        /// The profile file of a game (and mod): &lt;GameVersion&gt;&lt;ModCacheSuffix&gt;.
        /// </summary>
        public static string GetProfilePath(string profileKey)
        {
            return Path.Combine(Folder, $"{profileKey}.json");
        }

        /// <summary>
        /// Reads the profile. A missing file is an empty profile, an unreadable one is kept aside and a new one starts.
        /// A pending change of the previous profile (game or mod switched) is written first.
        /// </summary>
        public AchievementProfileData Load(string path)
        {
            Flush();
            _path = path;
            if (!File.Exists(path))
                return new AchievementProfileData();

            try
            {
                return Open(File.ReadAllText(path));
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
        /// The profile of a file's text. Throws if it isn't JSON.
        /// </summary>
        public static AchievementProfileData Open(string text)
        {
            var profile = JsonConvert.DeserializeObject<AchievementProfileData>(text) ?? new AchievementProfileData();
            profile.Unlocks ??= new List<AchievementUnlock>();
            profile.Unlocks.RemoveAll(unlock => unlock == null || string.IsNullOrEmpty(unlock.Id));
            return profile;
        }

        /// <summary>
        /// The text Flush() stores.
        /// </summary>
        public static string Serialize(AchievementProfileData profile)
        {
            return JsonConvert.SerializeObject(profile, Formatting.Indented);
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
                File.WriteAllText(tmpPath, Serialize(profile));
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
