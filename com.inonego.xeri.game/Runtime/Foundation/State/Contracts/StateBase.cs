/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : StateBase.cs
수정일 : 2026-11-16

# 설명
Owner를 자동 보관하는 IState 추상 베이스 클래스.
한 StateBase 인스턴스는 하나의 StateMachine에만 등록되며 다른 머신에 재등록할 수 없다.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Owner 타입과 무관하게 FSM 등록 소유자를 식별하는 내부 계약.
    /// </summary>
    // ============================================================
    internal interface IStateMachineOwnedState
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 상태를 등록한 머신. 아직 등록하지 않았다면 null.
        /// </summary>
        // ------------------------------------------------------------
        object RegisteredMachine { get; }
    }

    // ============================================================
    /// <summary>
    /// Owner 보관 + IState 구현 추상 베이스.
    /// </summary>
    // ============================================================
    public abstract class StateBase<TOwner> : IState, IStateMachineOwnedState
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 상태가 속한 소유자.
        /// </summary>
        // ------------------------------------------------------------
        protected TOwner Owner { get; private set; }

        object IStateMachineOwnedState.RegisteredMachine => registeredMachine;
        [NonSerialized]
        private object registeredMachine = null;

    #endregion

    #region Owner 주입

        // ------------------------------------------------------------
        /// <summary>
        /// 이 머신의 Owner를 주입하며 다른 머신의 재등록은 거절한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void SetOwner(TOwner owner, object machine)
        {
            if (machine == null)
            {
                throw new ArgumentNullException(nameof(machine));
            }

            if (registeredMachine != null && !ReferenceEquals(registeredMachine, machine))
            {
                throw new InvalidOperationException("동일 State 인스턴스를 다른 StateMachine에 등록할 수 없습니다.");
            }

            Owner = owner;
            registeredMachine = machine;
        }

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 상태 진입 시 호출된다. 자식이 override 한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void OnEnter()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 상태 종료 시 호출된다. 자식이 override 한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void OnExit()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 상태 갱신 시 호출된다. 자식이 override 한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void OnUpdate()
        {
            // NONE
        }

    #endregion

    }
}
