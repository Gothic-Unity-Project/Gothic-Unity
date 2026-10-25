using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Gothic.VR.Interaction.Backends.Xri;
using Gothic.VR.Interaction.Components;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Gothic.Tests.PlayMode
{
    /// <summary>
    /// Lock picking on VrHinge (doors, chest lids of the Xri backend): a locked leaf stays shut, an unlocked one
    /// swings open on its own. Runs without a Gothic installation and without a hand.
    /// </summary>
    public class VrHingeTest
    {
        private const float Tolerance = 0.5f;

        private readonly List<GameObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
            {
                if (go != null)
                    Object.Destroy(go);
            }

            _created.Clear();
        }

        [UnityTest]
        public IEnumerator UnlockSwingsLeafOpen()
        {
            var hinge = CreateHinge();
            var openedCount = 0;
            hinge.Opened += () => openedCount++;
            yield return null;

            hinge.Lock();
            yield return new WaitForSeconds(0.3f);
            Assert.That(hinge.Angle, Is.EqualTo(0f).Within(Tolerance), "A locked leaf must stay shut.");
            Assert.IsTrue(hinge.IsClosed);

            hinge.Unlock();
            yield return new WaitForSeconds(2f);

            // Default UnlockOpenAngle (80) is below the default MaxAngle (90).
            Assert.That(hinge.Angle, Is.EqualTo(80f).Within(Tolerance), "An unlocked leaf must swing open.");
            Assert.IsTrue(hinge.IsOpen);
            Assert.AreEqual(1, openedCount);

            // And rest there.
            yield return new WaitForSeconds(0.5f);
            Assert.That(hinge.Angle, Is.EqualTo(80f).Within(Tolerance));
        }

        private VrHinge CreateHinge()
        {
            var go = new GameObject("Leaf");
            _created.Add(go);
            go.SetActive(false);
            go.AddComponent<Rigidbody>();
            var hinge = go.AddComponent<VrHinge>();

            var field = typeof(VrHinge).GetField("_vrBackend", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "VrHinge has no _vrBackend field.");
            field.SetValue(hinge, new XriBackend());

            go.SetActive(true);
            return hinge;
        }
    }
}
