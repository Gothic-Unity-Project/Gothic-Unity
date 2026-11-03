using Gothic.Core.Const;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Gothic.Tests.EditMode.Physics
{
    /// <summary>
    /// Items inside a VR slot are on VobItemSocketed: nothing collides with them (hands, body, the head-down ground
    /// check), but the hands' near grab still finds them, as physics queries only use their own mask.
    /// </summary>
    public class SocketedItemLayerTest
    {
        private const string PlayerPrefabPath = "Assets/Gothic-VR/Resources/VR/Prefabs/VR - Player XRI.prefab";
        private const string NearCasterType = "SphereInteractionCaster";

        [Test]
        public void LayerExists()
        {
            Assert.AreNotEqual(-1, LayerMask.NameToLayer("VobItemSocketed"));
            Assert.AreEqual(LayerMask.NameToLayer("VobItemSocketed"), (int)Constants.VobItemSocketedLayer);
        }

        [Test]
        public void CollidesWithNothing()
        {
            int layer = Constants.VobItemSocketedLayer;
            for (var other = 0; other < 32; other++)
            {
                Assert.IsTrue(UnityEngine.Physics.GetIgnoreLayerCollision(layer, other),
                    $"VobItemSocketed must not collide with layer {other} ({LayerMask.LayerToName(other)}).");
            }
        }

        [Test]
        public void HandsNearGrabFindsIt()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(prefab, $"{PlayerPrefabPath} not found.");

            var casters = 0;
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || component.GetType().Name != NearCasterType)
                    continue;

                casters++;
                var mask = new SerializedObject(component).FindProperty("m_PhysicsLayerMask").intValue;
                Assert.AreNotEqual(0, mask & (1 << Constants.VobItemSocketedLayer),
                    $"The near grab of {component.gameObject.name} must find VobItemSocketed.");
            }

            Assert.AreEqual(2, casters, "Expected one near grab caster per hand.");
        }
    }
}
