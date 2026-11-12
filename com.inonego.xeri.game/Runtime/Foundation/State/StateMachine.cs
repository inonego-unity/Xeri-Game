/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : StateMachine.cs
수정일 : 2026-05-08

# 설명
유한 상태 머신 (FSM). Owner 와 현재 상태를 보유하며 인스턴스/타입 기반 전이를 모두 지원한다.
FixedTick / LateTick 은 Current 가 IFixedUpdatable / ILateUpdatable 을 구현할 때만 호출된다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 유한 상태 머신.
    /// </summary>
    // ============================================================
    [Serializable]
    public class StateMachine<TOwner>
    where TOwner : class
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 상태가 공유하는 소유자.
        /// </summary>
        // ------------------------------------------------------------
        public TOwner Owner => owner;
        private readonly TOwner owner;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태.
        /// </summary>
        // ------------------------------------------------------------
        public IState Current => current;
        private IState current = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 상태(타입을 키로 사용).
        /// </summary>
        // ------------------------------------------------------------
        private readonly Dictionary<Type, IState> states = new();

    #endregion

    #region 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// 상태가 변경될 때 호출된다(prev, next).
        /// </summary>
        // ------------------------------------------------------------
        public event Action<IState, IState> OnStateChanged = null;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Owner 를 받아 상태 머신을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public StateMachine(TOwner owner)
        {
            if (owner == null)
            {
                throw new ArgumentNullException("StateMachine 의 owner 가 null입니다.");
            }

            this.owner = owner;
        }

    #endregion

    #region 등록 / 조회

        // ----------------------------------------------------------------------
        /// <summary>
        /// 상태를 타입을 키로 등록한다. StateBase<TOwner> 면 Owner 가 자동 주입된다.
        /// </summary>
        // ----------------------------------------------------------------------
        public T AddState<T>(T state) where T : class, IState
        {
            if (state == null)
            {
                throw new ArgumentNullException("StateMachine.AddState()의 state 가 null입니다.");
            }

            if (state is StateBase<TOwner> sb)
            {
                sb.SetOwner(owner);
            }

            states[typeof(T)] = state;

            return state;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 상태를 타입으로 조회한다. 없으면 null.
        /// </summary>
        // ------------------------------------------------------------
        public T GetState<T>() where T : class, IState
        {
            return states.TryGetValue(typeof(T), out var s) ? (T)s : null;
        }

    #endregion

    #region 전이

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 타입으로 상태를 전이한다. 미등록 시 무시.
        /// </summary>
        // ------------------------------------------------------------
        public void MoveTo<T>() where T : class, IState
        {
            var next = GetState<T>();

            if (next != null)
            {
                MoveTo(next);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 인스턴스로 상태를 전이한다. 같은 인스턴스면 무시.
        /// </summary>
        // ------------------------------------------------------------
        public void MoveTo(IState next)
        {
            var prev = current;

            if (prev == next) return;

            prev?.OnExit();

            current = next;

            next?.OnEnter();

            OnStateChanged?.Invoke(prev, next);
        }

    #endregion

    #region 갱신

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태의 OnUpdate 를 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Tick()
        {
            current?.OnUpdate();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태가 IFixedUpdatable 이면 OnFixedUpdate 를 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        public void FixedTick()
        {
            if (current is IFixedUpdatable f)
            {
                f.OnFixedUpdate();
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태가 ILateUpdatable 이면 OnLateUpdate 를 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        public void LateTick()
        {
            if (current is ILateUpdatable l)
            {
                l.OnLateUpdate();
            }
        }

    #endregion

    }
}
