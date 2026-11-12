using Gothic.Core.Domain.Vobs;
using NUnit.Framework;
using UnityEngine;

namespace Gothic.Tests.EditMode.Vobs
{
    /// <summary>
    /// A mover's keyframes count from where its VOB stands, unless the VOB already stands on them.
    /// </summary>
    public class MoverKeyframeDomainTest
    {
        private const float _tolerance = 0.0001f;

        private static readonly Quaternion _key1Rotation = Quaternion.Euler(0f, 90f, 0f);

        private static (Vector3 pos, Quaternion rot)[] CreateKeyframes()
        {
            return new[]
            {
                (new Vector3(10f, 0f, 20f), Quaternion.identity),
                (new Vector3(11f, 5f, 20f), _key1Rotation)
            };
        }

        [Test]
        public void VobOnKey0_KeepsKeyframes()
        {
            var keyframes = CreateKeyframes();

            var changed = MoverKeyframeDomain.RelativeToVob(keyframes, new Vector3(10f, 0f, 20f), Quaternion.identity,
                0, out _, out _);

            Assert.IsFalse(changed);
            AssertKeyframes(CreateKeyframes(), keyframes);
        }

        [Test]
        public void VobOnReachedKey_KeepsKeyframes()
        {
            var keyframes = CreateKeyframes();

            var changed = MoverKeyframeDomain.RelativeToVob(keyframes, new Vector3(11f, 5f, 20f), _key1Rotation, 1,
                out _, out _);

            Assert.IsFalse(changed);
            AssertKeyframes(CreateKeyframes(), keyframes);
        }

        [Test]
        public void VobAwayFromKey0_ShiftsAndRotatesKeyframesAroundVob()
        {
            // Like G2 Irdorath's gates: the VOB stands a few meters and a few degrees away from its keyframes.
            var keyframes = CreateKeyframes();
            var vobPosition = new Vector3(13f, 1f, 18f);
            var vobRotation = Quaternion.Euler(0f, 10f, 0f);

            var changed = MoverKeyframeDomain.RelativeToVob(keyframes, vobPosition, vobRotation, 0,
                out var positionOffset, out var rotationAngle);

            Assert.IsTrue(changed);
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(3f, 1f, -2f), positionOffset), _tolerance);
            Assert.AreEqual(10f, rotationAngle, 0.01f);
            AssertKeyframes(new[]
            {
                (vobPosition, vobRotation),
                (vobPosition + vobRotation * new Vector3(1f, 5f, 0f), vobRotation * _key1Rotation)
            }, keyframes);
        }

        [Test]
        public void TinyRotationDifference_OnlyShiftsKeyframes()
        {
            // Keyframe and VOB rotations are converted differently - half a degree is no real difference.
            var keyframes = CreateKeyframes();
            var vobPosition = new Vector3(13f, 1f, 18f);

            var changed = MoverKeyframeDomain.RelativeToVob(keyframes, vobPosition, Quaternion.Euler(0f, 0.5f, 0f), 0,
                out _, out _);

            Assert.IsTrue(changed);
            AssertKeyframes(new[]
            {
                (vobPosition, Quaternion.identity),
                (vobPosition + new Vector3(1f, 5f, 0f), _key1Rotation)
            }, keyframes);
        }

        private static void AssertKeyframes((Vector3 pos, Quaternion rot)[] expected,
            (Vector3 pos, Quaternion rot)[] actual)
        {
            Assert.AreEqual(expected.Length, actual.Length);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(0f, Vector3.Distance(expected[i].pos, actual[i].pos), _tolerance, $"position of key {i}");
                Assert.AreEqual(0f, Quaternion.Angle(expected[i].rot, actual[i].rot), 0.01f, $"rotation of key {i}");
            }
        }
    }
}
