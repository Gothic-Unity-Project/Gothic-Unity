using System.Collections.Generic;
using Gothic.Core.Domain.Movement;
using Gothic.Core.Models.Movement;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gothic.Tests.PlayMode
{
    /// <summary>
    /// Gothic's ledge rules (LedgeProbeDomain) against boxes: a body at the origin looks along +z at a block whose
    /// wall starts 40 cm in front of it. Runs without a Gothic installation, with G1's human values.
    /// </summary>
    public class LedgeProbeTest
    {
        private const float WallZ = 0.4f;

        private static readonly LedgeBody Body = new(0.25f, 1.8f, 1 << 0);
        private static readonly LedgeRules Rules = LedgeRules.GothicHumanDefaults;

        private readonly LedgeProbeDomain _domain = new();
        private readonly List<GameObject> _created = new();

        [SetUp]
        public void SetUp()
        {
            CreateBox(new Vector3(0f, -0.1f, 0f), new Vector3(20f, 0.2f, 20f));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                Object.DestroyImmediate(go);

            _created.Clear();
        }

        [TestCase(0.4f, LedgeBand.Free)]
        [TestCase(0.8f, LedgeBand.Low)]
        [TestCase(1.5f, LedgeBand.Mid)]
        [TestCase(2.5f, LedgeBand.High)]
        [TestCase(3.5f, LedgeBand.None)]
        public void ProbeAheadClassifiesByHeight(float height, LedgeBand expected)
        {
            CreateBlock(height);

            var info = _domain.ProbeAhead(Vector3.zero, Vector3.forward, Body, Rules);

            Assert.AreEqual(expected, info.Band);
            if (!info.IsClimbable)
                return;

            Assert.AreEqual(height, info.Height, 0.01f);
            Assert.AreEqual(WallZ, info.EdgePoint.z, 0.01f, "The lip is where the wall is.");
            Assert.AreEqual(height, info.StandPoint.y, 0.01f, "The body stands on the top.");
            Assert.That(Vector3.Dot(info.WallNormal, Vector3.back), Is.GreaterThan(0.99f));
        }

        [Test]
        public void ProbeAheadWithoutObstacleIsFree()
        {
            var info = _domain.ProbeAhead(Vector3.zero, Vector3.forward, Body, Rules);

            Assert.AreEqual(LedgeBand.Free, info.Band);
        }

        [Test]
        public void NoRoomOnTopIsNoLedge()
        {
            CreateBlock(1.5f);
            // A block 80 cm above the top, reaching higher than Gothic's probe starts: the body doesn't fit.
            CreateBox(new Vector3(0f, 2.9f, 1.4f), new Vector3(4f, 1.2f, 2f));

            Assert.AreEqual(LedgeBand.None, _domain.ProbeAhead(Vector3.zero, Vector3.forward, Body, Rules).Band);
            Assert.AreEqual(LedgeBand.None, ProbeEdgeAt(1.5f).Band);
        }

        [Test]
        public void NoHeadroomIsNoLedge()
        {
            CreateBlock(2.5f);
            // A roof over the body at 2.2 m, in front of the wall.
            CreateBox(new Vector3(0f, 2.3f, -0.5f), new Vector3(4f, 0.2f, 1.8f));

            Assert.AreEqual(LedgeBand.None, _domain.ProbeAhead(Vector3.zero, Vector3.forward, Body, Rules).Band);
        }

        [Test]
        public void SteepTopIsNoLedge()
        {
            CreateBlock(1.5f);
            // A plate rising at 60 degrees right behind the lip.
            var roof = CreateBox(new Vector3(0f, 1.9f, 0.75f), new Vector3(4f, 0.05f, 1f));
            roof.transform.rotation = Quaternion.Euler(-60f, 0f, 0f);
            Physics.SyncTransforms();

            Assert.AreEqual(LedgeBand.None, _domain.ProbeAhead(Vector3.zero, Vector3.forward, Body, Rules).Band);
        }

        [TestCase(0.8f, LedgeBand.Low)]
        [TestCase(1.5f, LedgeBand.Mid)]
        [TestCase(2.8f, LedgeBand.High)]
        [TestCase(3.2f, LedgeBand.None)]
        public void ProbeEdgeAtHand(float height, LedgeBand expected)
        {
            CreateBlock(height);

            var info = ProbeEdgeAt(height);

            Assert.AreEqual(expected, info.Band);
            if (!info.IsClimbable)
                return;

            Assert.AreEqual(WallZ, info.EdgePoint.z, 0.01f);
            Assert.AreEqual(height, info.EdgePoint.y, 0.01f);
            Assert.That(info.StandPoint.z, Is.GreaterThan(WallZ + Body.Radius), "The stand point is on the top.");
        }

        [Test]
        public void ProbeEdgeAtOpenFloorIsNoLedge()
        {
            var info = _domain.ProbeEdge(new Vector3(0f, 0.1f, 0.3f), Vector3.forward, 0f, Body, Rules);

            Assert.AreEqual(LedgeBand.None, info.Band);
        }

        [TestCase(2.5f, LedgeBand.High)]
        [TestCase(1.2f, LedgeBand.Mid)]
        public void ProbeTopFindsTheLipTowardsTheClimber(float height, LedgeBand expected)
        {
            CreateBlock(height);

            var info = _domain.ProbeTop(new Vector3(0f, height, 1.6f), new Vector3(0f, 0f, -3f), Body, Rules);

            Assert.AreEqual(expected, info.Band);
            Assert.AreEqual(WallZ, info.EdgePoint.z, 0.01f);
        }

        [Test]
        public void ProbeTopOfTooHighWallIsNoLedge()
        {
            CreateBlock(3.5f);

            var info = _domain.ProbeTop(new Vector3(0f, 3.5f, 1.6f), new Vector3(0f, 0f, -3f), Body, Rules);

            Assert.AreEqual(LedgeBand.None, info.Band);
        }

        /// <summary>
        /// A hand 5 cm in front of the lip and 5 cm above the top, reaching along +z from a body on the floor.
        /// </summary>
        private LedgeInfo ProbeEdgeAt(float height)
        {
            return _domain.ProbeEdge(new Vector3(0f, height + 0.05f, WallZ - 0.05f), Vector3.forward, 0f, Body, Rules);
        }

        /// <summary>
        /// A 4 m wide, 2 m deep block whose wall starts at WallZ.
        /// </summary>
        private void CreateBlock(float height)
        {
            CreateBox(new Vector3(0f, height / 2f, WallZ + 1f), new Vector3(4f, height, 2f));
        }

        private GameObject CreateBox(Vector3 center, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.position = center;
            box.transform.localScale = size;
            _created.Add(box);
            Physics.SyncTransforms();
            return box;
        }
    }
}
