using Gothic.Core.Extensions;
using NUnit.Framework;

namespace Gothic.Tests.EditMode.SaveGames
{
    /// <summary>
    /// Worlds inside a save are named upper case without extension, ZenKit's lookup is case-sensitive.
    /// </summary>
    public class GothicWorldNameTest
    {
        [TestCase("World.zen", "WORLD")]
        [TestCase("WORLD", "WORLD")]
        [TestCase("newworld.ZEN", "NEWWORLD")]
        [TestCase(@"AddonWorld\NewWorld.zen", "NEWWORLD")]
        [TestCase("AddonWorld/AddonWorld.zen", "ADDONWORLD")]
        public void ToGothicWorldName(string worldFileName, string expected)
        {
            Assert.AreEqual(expected, worldFileName.ToGothicWorldName());
        }
    }
}
