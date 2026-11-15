using System;
using System.Collections.Generic;
using Gothic.VR.Interaction.Models;
using NUnit.Framework;

namespace Gothic.Tests.EditMode.VR
{
    /// <summary>
    /// The help panels label each stick and face button with what it does (VrControlLayout.GetRole). Checked for every
    /// combination of the settings controller hand, jump button and menu button.
    /// </summary>
    public class VrControlLayoutTest
    {
        private static readonly VrHandSide[] _sides = { VrHandSide.Left, VrHandSide.Right };
        private static readonly VrControlSlot[] _slots = (VrControlSlot[])Enum.GetValues(typeof(VrControlSlot));

        private static IEnumerable<TestCaseData> AllLayouts()
        {
            for (var hand = 0; hand < 2; hand++)
            {
                for (var jump = 0; jump < 3; jump++)
                {
                    for (var menu = 0; menu < 3; menu++)
                        yield return new TestCaseData(hand, jump, menu).SetName($"hand{hand}_jump{jump}_menu{menu}");
                }
            }
        }

        [TestCaseSource(nameof(AllLayouts))]
        public void EachRoleIsOnExactlyOneControl(int hand, int jump, int menu)
        {
            var layout = VrControlLayout.FromSettings(hand, jump, menu);
            var found = new Dictionary<VrControlRole, (VrHandSide, VrControlSlot)>();

            foreach (var side in _sides)
            {
                foreach (var slot in _slots)
                {
                    var role = layout.GetRole(side, slot);
                    if (role == VrControlRole.None)
                        continue;

                    Assert.IsFalse(found.ContainsKey(role), $"{role} is on {found.GetValueOrDefault(role)} and " +
                                                            $"{(side, slot)}.");
                    found[role] = (side, slot);
                }
            }

            foreach (VrControlRole role in Enum.GetValues(typeof(VrControlRole)))
            {
                if (role != VrControlRole.None)
                    Assert.IsTrue(found.ContainsKey(role), $"{role} is on no control.");
            }
        }

        [TestCaseSource(nameof(AllLayouts))]
        public void RolesSitWhereTheSettingsPutThem(int hand, int jump, int menu)
        {
            var layout = VrControlLayout.FromSettings(hand, jump, menu);
            var moveSide = hand == 1 ? VrHandSide.Right : VrHandSide.Left;
            var turnSide = hand == 1 ? VrHandSide.Left : VrHandSide.Right;
            var jumpSlot = new[] { VrControlSlot.Primary, VrControlSlot.Secondary, VrControlSlot.JoystickButton }[jump];
            var crouchSlot = jumpSlot == VrControlSlot.Secondary ? VrControlSlot.Primary : VrControlSlot.Secondary;
            var menuSlot = new[] { VrControlSlot.Secondary, VrControlSlot.Primary, VrControlSlot.Menu }[menu];
            // The right menu button is the system button: the menu stays on the left controller.
            var menuSide = menuSlot == VrControlSlot.Menu ? VrHandSide.Left : moveSide;

            Assert.AreEqual(VrControlRole.Move, layout.GetRole(moveSide, VrControlSlot.Stick));
            Assert.AreEqual(VrControlRole.Sprint, layout.GetRole(moveSide, VrControlSlot.JoystickButton));
            Assert.AreEqual(VrControlRole.Turn, layout.GetRole(turnSide, VrControlSlot.Stick));
            Assert.AreEqual(VrControlRole.Jump, layout.GetRole(turnSide, jumpSlot));
            Assert.AreEqual(VrControlRole.Crouch, layout.GetRole(turnSide, crouchSlot));
            Assert.AreEqual(VrControlRole.Menu, layout.GetRole(menuSide, menuSlot));
            Assert.AreEqual(menuSide, layout.MenuSide);
        }

        [TestCaseSource(nameof(AllLayouts))]
        public void GripAndTriggerHaveNoRole(int hand, int jump, int menu)
        {
            var layout = VrControlLayout.FromSettings(hand, jump, menu);
            foreach (var side in _sides)
            {
                Assert.AreEqual(VrControlRole.None, layout.GetRole(side, VrControlSlot.Grip));
                Assert.AreEqual(VrControlRole.None, layout.GetRole(side, VrControlSlot.Trigger));
            }
        }

        [Test]
        public void DefaultIsRightHandedJumpLowerMenuUpper()
        {
            var layout = VrControlLayout.Default;
            Assert.IsFalse(layout.IsLeftHanded);
            Assert.AreEqual(VrButton.Primary, layout.JumpButton);
            Assert.AreEqual(VrButton.Secondary, layout.MenuButton);
            Assert.AreEqual(VrControlRole.Menu, layout.GetRole(VrHandSide.Left, VrControlSlot.Secondary));
            Assert.AreEqual(VrControlRole.None, layout.GetRole(VrHandSide.Left, VrControlSlot.Primary));
            Assert.AreEqual(VrControlRole.None, layout.GetRole(VrHandSide.Right, VrControlSlot.JoystickButton));
        }
    }
}
