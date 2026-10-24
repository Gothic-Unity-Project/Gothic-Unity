using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Gothic.Core.Adapters.Vob;
using Gothic.VR.Interaction.Backends.Xri;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Gothic.Tests.PlayMode
{
    /// <summary>
    /// Checks the VobLoader hierarchy of items put into and taken out of a VrSocketSlot (HVR phase 3a).
    /// Runs without a Gothic installation: the items are plain GameObjects with VobLoader -> grabbable -> cube
    /// (no VobContainer, therefore no category rotation and no item filter).
    /// </summary>
    public class VrSocketSlotTest
    {
        private const float CubeSize = 0.4f;
        private const float Tolerance = 0.001f;

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
        public IEnumerator PlaceAndRemoveWithoutBackend()
        {
            var world = Create("World");
            var slot = Create("Slot").AddComponent<VrSocketSlot>();
            slot.transform.position = new Vector3(0, 1, 0);
            var item = CreateItem(world.transform, withXri: false);
            item.Grabbable.transform.localPosition = new Vector3(0.2f, 0, 0); // As if a hand moved it.

            var placed = 0;
            var removed = 0;
            slot.Grabbed += args => placed += args.Kind == VrGrabberKind.Socket ? 1 : 100;
            slot.Released += _ => removed++;
            yield return null;

            Assert.IsTrue(slot.TryPlace(item.Interactable, true));
            AssertInSlot(slot, item);
            Assert.AreEqual(1, placed);
            Assert.IsFalse(slot.TryPlace(item.Interactable, true), "An occupied slot must not take a second item.");

            slot.ForceRelease();
            AssertRemoved(slot, item, world.transform);
            Assert.AreEqual(1, removed);
        }

        [UnityTest]
        public IEnumerator PlaceAndRemoveOnXri()
        {
            Create("XR Interaction Manager").AddComponent<XRInteractionManager>();
            var world = Create("World");
            var slot = CreateXriSlot(new Vector3(0, 1, 0));
            var item = CreateItem(world.transform, withXri: true);
            yield return null;
            yield return null;

            Assert.IsTrue(slot.TryPlace(item.Interactable, true));
            for (var i = 0; i < 5; i++)
                yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsTrue(item.GrabInteractable.isSelected, "XRI didn't select the item in the socket.");
            Assert.IsTrue(item.GrabInteractable.IsSocketed);
            AssertInSlot(slot, item);

            // The item follows the slot, XRI doesn't move the grabbable child.
            slot.transform.position += new Vector3(1, 0, 0);
            yield return new WaitForFixedUpdate();
            yield return null;
            AssertInSlot(slot, item);

            slot.ForceRelease();
            yield return null;
            Assert.IsFalse(item.GrabInteractable.isSelected);
            AssertRemoved(slot, item, world.transform);
        }

        [UnityTest]
        public IEnumerator HandTakesItemOutOnXri()
        {
            var manager = Create("XR Interaction Manager").AddComponent<XRInteractionManager>();
            var world = Create("World");
            var slot = CreateXriSlot(new Vector3(0, 1, 0));
            var item = CreateItem(world.transform, withXri: true);
            var hand = Create("Hand").AddComponent<XRDirectInteractor>();
            yield return null;
            yield return null;

            slot.TryPlace(item.Interactable, true);
            yield return null;
            Assert.IsTrue(item.GrabInteractable.IsSocketed);

            var removedWhileHandHeld = true;
            slot.Released += _ => removedWhileHandHeld = item.GrabInteractable.isSelected;
            manager.SelectEnter((IXRSelectInteractor)hand, item.GrabInteractable);
            yield return null;

            Assert.IsFalse(removedWhileHandHeld, "The socket must release the item before the hand's grab starts.");
            Assert.IsFalse(slot.IsOccupied);
            Assert.IsTrue(hand.IsSelecting(item.GrabInteractable));
            Assert.AreSame(item.Loader.transform, item.Grabbable.transform.parent,
                "The grabbable must stay below its VobLoader.");
            Assert.AreSame(world.transform, item.Loader.transform.parent);
            Assert.That(item.Loader.transform.localScale.x, Is.EqualTo(1f).Within(Tolerance));
        }

        private void AssertInSlot(VrSocketSlot slot, TestItem item)
        {
            Assert.IsTrue(slot.IsOccupied);
            Assert.AreSame(item.Interactable, slot.Held);
            Assert.AreSame(slot.AttachPoint, item.Loader.transform.parent, "The VobLoader must be in the slot.");
            Assert.AreSame(item.Loader.transform, item.Grabbable.transform.parent,
                "The grabbable must stay below its VobLoader.");
            Assert.That(item.Grabbable.transform.localPosition.magnitude, Is.LessThan(Tolerance));
            Assert.IsTrue(item.Rigidbody.isKinematic);

            // Scale-to-fit: half of the largest side becomes Size.
            var expectedScale = slot.Size / (CubeSize / 2);
            Assert.That(item.Loader.transform.localScale.x, Is.EqualTo(expectedScale).Within(Tolerance));
            Assert.That(Vector3.Distance(item.Cube.bounds.center, slot.AttachPoint.position),
                Is.LessThan(Tolerance), "The item must be centered in the slot.");
        }

        private static void AssertRemoved(VrSocketSlot slot, TestItem item, Transform world)
        {
            Assert.IsFalse(slot.IsOccupied);
            Assert.IsNull(slot.Held);
            Assert.AreSame(world, item.Loader.transform.parent, "The VobLoader must be back at its previous parent.");
            Assert.AreSame(item.Loader.transform, item.Grabbable.transform.parent);
            Assert.That(item.Loader.transform.localScale.x, Is.EqualTo(1f).Within(Tolerance));
            Assert.IsFalse(item.Rigidbody.isKinematic, "The kinematic state before the slot must be restored.");
        }

        private VrSocketSlot CreateXriSlot(Vector3 position)
        {
            var go = Create("Slot");
            go.SetActive(false);
            go.transform.position = position;
            go.AddComponent<SphereCollider>().isTrigger = true;
            var slot = go.AddComponent<VrSocketSlot>();
            var bridge = go.AddComponent<XriSocketBridge>();
            bridge.enabled = false;
            InjectXriBackend(bridge);
            go.SetActive(true);
            return slot;
        }

        private TestItem CreateItem(Transform parent, bool withXri)
        {
            var loaderGo = Create("Item");
            loaderGo.transform.SetParent(parent, false);
            loaderGo.transform.position = new Vector3(2, 0, 0);
            var loader = loaderGo.AddComponent<VobLoader>();

            var grabbableGo = new GameObject("Grabbable");
            grabbableGo.SetActive(false);
            grabbableGo.transform.SetParent(loaderGo.transform, false);
            var body = grabbableGo.AddComponent<Rigidbody>();
            body.useGravity = false;
            var interactable = grabbableGo.AddComponent<VrInteractable>();

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(grabbableGo.transform, false);
            cube.transform.localScale = Vector3.one * CubeSize;
            cube.transform.localPosition = new Vector3(0, 0.3f, 0); // Pivot not in the center, like many items.

            GothicGrabInteractable grab = null;
            if (withXri)
            {
                InjectXriBackend(grabbableGo.AddComponent<XriInteractableBridge>());
                grab = grabbableGo.AddComponent<GothicGrabInteractable>();
                grab.enabled = false;
                grab.unparentTransformOnGrab = false;
            }

            grabbableGo.SetActive(true);
            return new TestItem(loader, grabbableGo, interactable, body, cube.GetComponent<Renderer>(), grab);
        }

        private static void InjectXriBackend(Component component)
        {
            var field = component.GetType().GetField("_vrBackend", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"{component.GetType().Name} has no _vrBackend field.");
            field.SetValue(component, new XriBackend());
        }

        private GameObject Create(string goName)
        {
            var go = new GameObject(goName);
            _created.Add(go);
            return go;
        }

        private readonly struct TestItem
        {
            public readonly VobLoader Loader;
            public readonly GameObject Grabbable;
            public readonly VrInteractable Interactable;
            public readonly Rigidbody Rigidbody;
            public readonly Renderer Cube;
            public readonly GothicGrabInteractable GrabInteractable;

            public TestItem(VobLoader loader, GameObject grabbable, VrInteractable interactable, Rigidbody rigidbody,
                Renderer cube, GothicGrabInteractable grabInteractable)
            {
                Loader = loader;
                Grabbable = grabbable;
                Interactable = interactable;
                Rigidbody = rigidbody;
                Cube = cube;
                GrabInteractable = grabInteractable;
            }
        }
    }
}
