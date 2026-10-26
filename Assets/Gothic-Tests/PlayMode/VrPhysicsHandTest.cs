using System;
using System.Collections;
using System.Collections.Generic;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gothic.Tests.PlayMode
{
    /// <summary>
    /// Physical hands (HVR replacement phase 5): VrPhysicsHand against static boxes and rig teleports.
    /// Runs without a Gothic installation and without XRI: a test rig (IVrRig) with one controller and a block hand
    /// built from cubes, moved from the physics loop.
    /// </summary>
    public class VrPhysicsHandTest
    {
        private const float HandDepth = 0.09f;
        private const float WallFront = 0.2f;

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
            Time.captureDeltaTime = 0f;
        }

        [UnityTest]
        public IEnumerator HandStopsAtStaticBoxAndReconnects()
        {
            var rig = CreateRig(Vector3.zero);
            CreateBox(new Vector3(0, 1, WallFront + 0.1f), new Vector3(1, 1, 0.2f));
            yield return WaitFixed(5);
            AssertFollows(rig, 0.01f, "The free hand must follow the controller.");

            // Push the controller 15 cm into the box.
            yield return MoveController(rig, new Vector3(0, 1, WallFront + 0.15f), 30);

            var bodyFront = rig.Hand.Body.position.z + HandDepth / 2;
            Assert.That(bodyFront, Is.LessThan(WallFront + 0.01f), "The hand must stop at the box surface.");
            Assert.That(bodyFront, Is.GreaterThan(WallFront - 0.03f), "The hand must reach the box surface.");
            Assert.That(rig.Hand.Separation, Is.GreaterThan(0.1f), "The controller is inside the box.");
            Assert.That(rig.Hand.transform.position.z + HandDepth / 2, Is.LessThan(WallFront + 0.02f),
                "The visual must stay where the physics stopped the hand.");

            // Pull back: the hand reconnects.
            yield return MoveController(rig, new Vector3(0, 1, 0), 30);
            yield return WaitFixed(10);
            AssertFollows(rig, 0.01f, "The hand must reconnect when the controller moves back.");
            Assert.That(rig.Hand.Separation, Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator HandAndHeldItemArriveAfterRigTeleport()
        {
            var rig = CreateRig(Vector3.zero);
            var item = CreateItem(rig);
            yield return WaitFixed(5);

            var itemLocal = rig.Root.InverseTransformPoint(item.position);
            var target = new Vector3(50, 0, 20);
            var rotation = Quaternion.Euler(0, 90, 0);
            rig.Teleport(target, rotation);
            yield return null;
            yield return WaitFixed(3);

            AssertFollows(rig, 0.01f, "The hand must arrive with the rig.");
            var itemOffset = rig.Root.InverseTransformPoint(item.position) - itemLocal;
            Assert.That(itemOffset.magnitude, Is.LessThan(0.01f),
                "The held item must be moved with the rig by the same offset.");
            Assert.That(Quaternion.Angle(rig.Root.rotation * Quaternion.identity, item.rotation), Is.LessThan(1f),
                "The held item must turn with the rig.");
        }

        [UnityTest]
        public IEnumerator HandPassesThroughBoxItOverlapsAfterTeleport()
        {
            var rig = CreateRig(Vector3.zero);
            // At the destination, the controller is inside a box.
            var box = CreateBox(new Vector3(30, 1, 0.3f), new Vector3(1, 1, 0.6f));
            yield return WaitFixed(5);

            rig.Teleport(new Vector3(30, 0, 0), Quaternion.identity);
            yield return null;
            yield return WaitFixed(10);

            AssertFollows(rig, 0.01f, "A hand teleported into a box must not be pushed out of it.");
            Assert.IsTrue(IsIgnoredByHand(rig, box), "Collisions with the overlapped box must be off meanwhile.");

            // Out of the box: collisions come back.
            yield return MoveController(rig, new Vector3(30, 1, -0.3f), 20);
            yield return WaitFixed(5);
            Assert.IsFalse(IsIgnoredByHand(rig, box), "Collisions must come back once the hand is clear.");
            AssertFollows(rig, 0.01f, "The hand must follow the controller.");
        }

        [UnityTest]
        public IEnumerator StuckHandIsPulledBackThroughWallWithoutSecondSnap()
        {
            var rig = CreateRig(Vector3.zero);
            var wall = CreateBox(new Vector3(0, 1, WallFront + 0.05f), new Vector3(2, 2, 0.1f));
            yield return WaitFixed(5);

            // Through the wall, more than the max. distance (0.5 m) for longer than 0.2 s: the hand snaps.
            rig.Controller.position = new Vector3(0, 1, 0.9f);
            yield return WaitFixed(25);
            AssertFollows(rig, 0.01f, "The stuck hand must have snapped to the controller behind the wall.");

            // Pulled back through the wall: the hand comes along instead of getting stuck on the back side.
            yield return MoveController(rig, new Vector3(0, 1, 0), 40);
            yield return WaitFixed(5);
            AssertFollows(rig, 0.01f, "The hand must come back through the wall with the controller.");
            Assert.IsFalse(IsIgnoredByHand(rig, wall), "Collisions with the wall must be back.");
        }

        [UnityTest]
        public IEnumerator ClimbStartAndEndDontPop()
        {
            var rig = CreateRig(Vector3.zero);
            CreateBox(new Vector3(0, 1, WallFront + 0.1f), new Vector3(1, 1, 0.2f));
            yield return WaitFixed(5);

            // Hand resting on the "rung", controller 10 cm inside it.
            yield return MoveController(rig, new Vector3(0, 1, WallFront + 0.05f), 20);
            var blocked = rig.Hand.transform.position;
            Assert.That(Vector3.Distance(blocked, rig.Controller.position), Is.GreaterThan(0.05f));

            // Climbing starts: the body turns off, the visual blends to the controller instead of jumping.
            // Fixed 100 fps, so that two frames are less than the blend time (0.15 s) on a slow machine as well.
            Time.captureDeltaTime = 0.01f;
            rig.IsClimbing = true;
            yield return null;
            yield return null;
            Assert.IsFalse(rig.Hand.Body.gameObject.activeSelf, "The body must be off while climbing.");
            Assert.That(Vector3.Distance(rig.Hand.transform.position, blocked), Is.LessThan(0.05f),
                "The visual must not pop to the controller when climbing starts.");
            yield return new WaitForSeconds(0.3f);
            Assert.That(Vector3.Distance(rig.Hand.transform.position, rig.Controller.position), Is.LessThan(0.001f),
                "The visual must reach the controller while climbing.");

            // Climbing ends with the hand moved out of the rung: the body starts at the visual, no pop.
            rig.Controller.position = new Vector3(0, 1, 0);
            yield return null;
            var visual = rig.Hand.transform.position;
            rig.IsClimbing = false;
            yield return WaitFixed(1);
            Assert.IsTrue(rig.Hand.Body.gameObject.activeSelf);
            Assert.That(Vector3.Distance(rig.Hand.Body.position, visual), Is.LessThan(0.01f),
                "The body must start where the visual is.");
            yield return WaitFixed(5);
            AssertFollows(rig, 0.01f, "The hand must follow the controller after climbing.");
        }

        [UnityTest]
        public IEnumerator HandKeepsUpWithFallingRig()
        {
            var rig = CreateRig(new Vector3(0, 500, 0));
            yield return WaitFixed(5);

            // Falling with gravity up to 20 m/s (XriRig's terminal speed), faster than the hand's max. speed
            // (15 m/s). Moved per frame like the CharacterController, at a fixed 100 fps (headset-like): the
            // editor's irregular frame times would move the rig in bigger jumps than one physics step can follow.
            Time.captureDeltaTime = 0.01f;
            var speed = 0f;
            for (var frame = 0; frame < 260; frame++)
            {
                speed = Mathf.Min(speed + 9.81f * Time.deltaTime, 20f);
                rig.transform.position += Vector3.down * (speed * Time.deltaTime);
                yield return null;
                Assert.That(rig.Hand.Separation, Is.LessThan(0.5f),
                    $"The hand must not fall behind the rig (frame {frame}, {speed:0.0} m/s).");
            }

            Assert.That(speed, Is.EqualTo(20f).Within(0.01f));
        }

        private static bool IsIgnoredByHand(TestRig rig, Collider other)
        {
            foreach (var handCollider in rig.Hand.Body.GetComponentsInChildren<Collider>())
            {
                if (Physics.GetIgnoreCollision(handCollider, other))
                    return true;
            }

            return false;
        }

        private static void AssertFollows(TestRig rig, float tolerance, string message)
        {
            var target = rig.Controller.position;
            Assert.That(Vector3.Distance(rig.Hand.Body.position, target), Is.LessThan(tolerance),
                $"{message} Body {rig.Hand.Body.position}, controller {target}.");
            Assert.That(Vector3.Distance(rig.Hand.transform.position, target), Is.LessThan(tolerance),
                $"{message} Visual {rig.Hand.transform.position}, controller {target}.");
        }

        /// <summary>
        /// Moves the controller (world space) in equal steps, one per physics step.
        /// </summary>
        private static IEnumerator MoveController(TestRig rig, Vector3 position, int steps)
        {
            var start = rig.Controller.position;
            for (var i = 1; i <= steps; i++)
            {
                yield return new WaitForFixedUpdate();
                rig.Controller.position = Vector3.Lerp(start, position, i / (float)steps);
            }

            yield return null;
        }

        private static IEnumerator WaitFixed(int steps)
        {
            for (var i = 0; i < steps; i++)
                yield return new WaitForFixedUpdate();

            // LateUpdate places the visual.
            yield return null;
        }

        private TestRig CreateRig(Vector3 position)
        {
            var rigGo = Create("Test Rig");
            rigGo.transform.position = position;
            var rig = rigGo.AddComponent<TestRig>();

            var head = new GameObject("Head").transform;
            head.SetParent(rigGo.transform, false);
            head.localPosition = new Vector3(0, 1.6f, -0.3f);

            var controller = new GameObject("Controller").transform;
            controller.SetParent(rigGo.transform, false);
            controller.localPosition = new Vector3(0, 1, 0);

            // Block hand: palm only, a unit cube scaled to the block (the body copies it as a box collider).
            var handGo = new GameObject("Block Hand")
            {
                layer = LayerMask.NameToLayer("Hand")
            };
            handGo.SetActive(false);
            handGo.transform.SetParent(controller, false);
            var palm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(palm.GetComponent<Collider>());
            palm.name = "Palm";
            palm.layer = handGo.layer;
            palm.transform.SetParent(handGo.transform, false);
            palm.transform.localScale = new Vector3(0.08f, 0.03f, HandDepth);

            var hand = handGo.AddComponent<VrPhysicsHand>();
            rig.Init(head, controller, hand);
            handGo.SetActive(true);
            return rig;
        }

        private Rigidbody CreateItem(TestRig rig)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _created.Add(go);
            go.name = "Held Item";
            go.transform.localScale = new Vector3(0.05f, 0.05f, 0.8f);
            go.transform.position = rig.Controller.position + new Vector3(0, 0, 0.45f);
            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;

            var grip = new GameObject("Grip").transform;
            grip.SetParent(go.transform, false);
            grip.position = rig.Controller.position;

            // Held items don't collide with the hands (VRPlayerService does that in game).
            rig.Hand.IgnoreCollisions(new Collider[] { go.GetComponent<Collider>() }, true);
            rig.Hand.SetHeldItem(body, grip);
            return body;
        }

        private BoxCollider CreateBox(Vector3 center, Vector3 size)
        {
            var go = Create("Static Box");
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }

        private GameObject Create(string goName)
        {
            var go = new GameObject(goName);
            _created.Add(go);
            return go;
        }

        /// <summary>
        /// Minimal IVrRig: root, head and one controller. Teleport moves the root and raises Teleported at once.
        /// </summary>
        private class TestRig : MonoBehaviour, IVrRig
        {
            public Transform Controller { get; private set; }
            public VrPhysicsHand Hand { get; private set; }

            public event Action<Vector3> Teleported;

            public Transform Root => transform;
            public Transform TrackingSpace => transform;
            public Transform Head { get; private set; }
            public Transform LeftHand => Controller;
            public Transform RightHand => Controller;
            public Camera Camera => null;
            public CharacterController CharacterController => null;
            public float CameraHeight => 1.6f;
            public bool IsGrounded => true;
            public bool IsClimbing { get; set; }
            public bool IsCrouching => false;
            public float CameraYOffset { get; set; }
            public bool MovementEnabled { get; set; }
            public bool TurnEnabled { get; set; }
            public bool TeleportEnabled { get; set; }
            public bool CanJump { get; set; }
            public bool CanCrouch { get; set; }
            public bool CanSprint { get; set; }
            public float Gravity { get; set; }
            public float MaxFallSpeed { get; set; }
            public float MoveSpeed { get; set; }
            public float RunSpeed { get; set; }
            public float StepOffset { get; set; }
            public float FadeAlpha => 0f;
            public IReadOnlyList<IVrGrabber> HandGrabbers => Array.Empty<IVrGrabber>();

            public void Init(Transform head, Transform controller, VrPhysicsHand hand)
            {
                Head = head;
                Controller = controller;
                Hand = hand;
            }

            public void ApplyComfortSettings(VrSitStand sitStand, VrMoveDirection direction, VrTurnType turn,
                float snapDegrees, float smoothSpeed)
            {
            }

            public void Teleport(Vector3 position, Quaternion rotation)
            {
                transform.SetPositionAndRotation(position, rotation);
                Teleported?.Invoke(position);
            }

            public void SetGrabbingEnabled(bool isEnabled)
            {
            }

            public Transform GetHandVisual(VrHandSide side)
            {
                return Hand != null ? Hand.transform : Controller;
            }

            public IReadOnlyList<Collider> GetHandColliders(VrHandSide side)
            {
                return Array.Empty<Collider>();
            }

            public float GetDistanceGrabRange(VrHandSide side)
            {
                return 0f;
            }

            public void SetDistanceGrabRange(VrHandSide side, float range)
            {
            }

            public void ResetDistanceGrabRange(VrHandSide side)
            {
            }

            public VrInteractable GetDistanceHovered(VrHandSide side)
            {
                return null;
            }

            public bool TryDistanceGrabHovered(VrHandSide side)
            {
                return false;
            }

            public void Fade(float alpha, float speed)
            {
            }
        }
    }
}
