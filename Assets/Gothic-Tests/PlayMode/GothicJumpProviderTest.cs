using Gothic.VR.Interaction.Backends.Xri;
using NUnit.Framework;

namespace Gothic.Tests.PlayMode
{
    /// <summary>
    /// GothicJumpProvider's inverse of XRI's jump force curve (continuous). Frame steps lose a few percent more,
    /// which VrLedgeClimb's reach margin covers; the real rise is checked in game.
    /// </summary>
    public class GothicJumpProviderTest
    {
        private const float Gravity = 9.81f;
        private const float HoldTime = 0.5f;

        [Test]
        public void PrefabJumpHeightRisesAboutThirtyCentimetres()
        {
            Assert.AreEqual(0.3f, GothicJumpProvider.GetRise(0.46f, Gravity, HoldTime, false), 0.01f);
        }

        [TestCase(0.3f)]
        [TestCase(0.8f)]
        [TestCase(1.2f)]
        [TestCase(2f)]
        public void JumpHeightForRiseRoundTrips(float rise)
        {
            var jumpHeight = GothicJumpProvider.GetJumpHeightForRise(rise, Gravity, HoldTime, false);

            Assert.AreEqual(rise, GothicJumpProvider.GetRise(jumpHeight, Gravity, HoldTime, false), 0.005f);
        }
    }
}
