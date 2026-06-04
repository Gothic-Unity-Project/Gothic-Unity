using System.Collections.Generic;
using Gothic.Core.Models.Container;
using UnityEngine;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// Pure state machine for melee attack window timing.
    /// Shared by VR player (velocity-driven) and NPCs/Monsters (animation-frame-driven).
    ///
    /// Assumptions:
    /// 1. Attack window (collider hit check) always ends before the combo window starts.
    /// --attack---|--
    /// --------------|--combo---|
    ///
    /// 2. If we fail the combo window by doing e.g., "left-right" within the attack window, the combo is failed, and we need to wait.
    /// --attack---|--
    /// ------|fail--------------|
    /// </summary>
    public class AttackWindowStateMachine
    {
        public enum State
        {
            Initial,
            ComboFailed,
            Attack,
            WaitingForCombo,
            Combo
        }

        public State CurrentState { get; private set; } = State.Initial;

        public NpcContainer Owner { get; }

        private readonly float _attackWindowTime;
        private readonly float _comboWindowStart;
        private readonly float _comboWindowTime;

        private float _elapsedTime;
        private List<Collider> _alreadyHitColliders = new();

        public AttackWindowStateMachine(NpcContainer owner, float attackWindowTime, float comboWindowStart, float comboWindowTime)
        {
            Owner = owner;
            _attackWindowTime = attackWindowTime;
            _comboWindowStart = comboWindowStart;
            _comboWindowTime = comboWindowTime;
        }

        public bool IsInAttackWindow => CurrentState == State.Attack;
        public bool IsInComboWindow => CurrentState == State.Combo;
        public bool IsComboFailed => CurrentState == State.ComboFailed;
        public float ElapsedTime => _elapsedTime;

        /// <summary>
        /// Returns true if this collider was not yet hit during the current attack.
        /// If new, registers it so subsequent calls return false.
        /// </summary>
        public bool TryRegisterHit(Collider collider)
        {
            if (_alreadyHitColliders.Contains(collider))
                return false;

            _alreadyHitColliders.Add(collider);
            return true;
        }

        /// <summary>
        /// Called every physics tick by the owner (VR or NPC).
        /// Returns the new state if a transition happened, null otherwise.
        /// </summary>
        public State? Tick(float deltaTime)
        {
            _elapsedTime += deltaTime;

            switch (CurrentState)
            {
                case State.Attack:
                    return TickAttack();
                case State.ComboFailed:
                    return TickComboFailed();
                case State.WaitingForCombo:
                    return TickWaitingForCombo();
                case State.Combo:
                    return TickCombo();
                default:
                    return null;
            }
        }

        /// <summary>
        /// Called externally when an attack begins (VR: velocity threshold reached, NPC: animation started).
        /// </summary>
        public void StartAttack()
        {
            CurrentState = State.Attack;
            _elapsedTime = 0f;
            _alreadyHitColliders.Clear();

            GlobalEventDispatcher.FightWindowAttack.Invoke(Owner);
        }

        /// <summary>
        /// Called externally when a combo is triggered (VR: velocity returned after drop, NPC: combo animation started).
        /// Resets into a new attack window.
        /// </summary>
        public void StartCombo()
        {
            StartAttack();
        }

        /// <summary>
        /// Called externally when combo conditions failed during Attack or WaitingForCombo.
        /// </summary>
        public void FailCombo()
        {
            CurrentState = State.ComboFailed;
            GlobalEventDispatcher.FightWindowComboFailed.Invoke(Owner);
        }

        /// <summary>
        /// Called when the attack hit connected and we should advance past the attack window.
        /// </summary>
        public void AdvanceToWaitingForCombo()
        {
            CurrentState = State.WaitingForCombo;
            GlobalEventDispatcher.FightWindowWaitingForCombo.Invoke(Owner);
        }

        /// <summary>
        /// Full reset to initial state (e.g., weapon dropped, combat ended).
        /// </summary>
        public void Reset()
        {
            CurrentState = State.Initial;
            _elapsedTime = 0f;
            _alreadyHitColliders.Clear();

            GlobalEventDispatcher.FightWindowInitial.Invoke(Owner);
        }

        private State? TickAttack()
        {
            if (_elapsedTime >= _attackWindowTime)
            {
                CurrentState = State.WaitingForCombo;
                GlobalEventDispatcher.FightWindowWaitingForCombo.Invoke(Owner);
                return CurrentState;
            }

            return null;
        }

        private State? TickComboFailed()
        {
            if (_elapsedTime >= _comboWindowTime)
            {
                CurrentState = State.Initial;
                GlobalEventDispatcher.FightWindowInitial.Invoke(Owner);
                return CurrentState;
            }

            return null;
        }

        private State? TickWaitingForCombo()
        {
            if (_elapsedTime >= _comboWindowStart)
            {
                CurrentState = State.Combo;
                GlobalEventDispatcher.FightWindowCombo.Invoke(Owner);
                return CurrentState;
            }

            return null;
        }

        private State? TickCombo()
        {
            if (_elapsedTime >= _comboWindowTime)
            {
                CurrentState = State.Initial;
                GlobalEventDispatcher.FightWindowInitial.Invoke(Owner);
                return CurrentState;
            }

            return null;
        }
    }
}
