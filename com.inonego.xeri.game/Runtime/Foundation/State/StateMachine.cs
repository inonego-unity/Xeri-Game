/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : StateMachine.cs
수정일 : 2026-11-17

# 설명
유한 상태 머신 (FSM). Owner 와 현재 상태를 보유하며 인스턴스/타입 기반 전이를 모두 지원한다.
FixedTick / LateTick 은 Current 가 IFixedUpdatable / ILateUpdatable 을 구현할 때만 호출된다.
외부에서 확정된 상태를 복원해야 하는 파생 머신은 ReplaceCurrent 로 전이 생명주기 없이 Current 만 교체한다.
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

        [NonSerialized]
        private bool isTransitioning = false;

    #endregion

    #region 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// 상태 전이가 성공적으로 완료된 후 호출된다(prev, next).
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
        /// 같은 타입은 교체 등록한다. StateBase 계열이면 Owner가 자동 주입된다.
        /// </summary>
        // ----------------------------------------------------------------------
        public T AddState<T>(T state) where T : class, IState
        {
            if (isTransitioning)
            {
                throw new InvalidOperationException("상태 전이 도중에는 상태 등록을 변경할 수 없습니다.");
            }

            if (state == null)
            {
                throw new ArgumentNullException("StateMachine.AddState()의 state 가 null입니다.");
            }

            foreach (var pair in states)
            {
                if (pair.Key != typeof(T) && ReferenceEquals(pair.Value, state))
                {
                    throw new InvalidOperationException("동일 State 인스턴스를 다른 타입으로 중복 등록할 수 없습니다.");
                }
            }

            if (state is IStateMachineOwnedState)
            {
                if (state is not StateBase<TOwner> stateBase)
                {
                    throw new InvalidOperationException("상태가 요구하는 Owner 타입이 StateMachine의 Owner 타입과 다릅니다.");
                }

                stateBase.SetOwner(owner, this);
            }

            // 등록 교체는 현재 활성 상태를 종료하지 않는다. 다음 명시적 전이에서 이동한다.
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
            if (isTransitioning)
            {
                throw new InvalidOperationException("상태 전이 도중에는 다른 전이를 시작할 수 없습니다.");
            }

            var next = GetState<T>();

            if (next != null)
            {
                MoveTo(next);
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 동일 인스턴스는 무시하고 진행 중인 전이의 재진입은 거절한다.
        /// <br/> 진입·종료 실패 시 Current를 비우고 실패한 진입은 종료를 시도한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void MoveTo(IState next)
        {
            if (isTransitioning)
            {
                throw new InvalidOperationException("상태 전이 도중에는 다른 전이를 시작할 수 없습니다.");
            }

            var prev = current;

            if (ReferenceEquals(prev, next)) return;

            ValidateRegistered(next);

            isTransitioning = true;

            try
            {
                // 이전 상태가 종료에 실패해도 종료되지 않은 상태를 갱신하지 않는다.
                current = null;
                prev?.OnExit();

                if (next != null)
                {
                    current = next;

                    try
                    {
                        next.OnEnter();
                    }
                    catch (Exception enterError)
                    {
                        // 진입 중 획득한 자원을 해제하고 실패한 상태를 비활성화한다.
                        current = null;

                        try
                        {
                            next.OnExit();
                        }
                        catch (Exception exitError)
                        {
                            throw new AggregateException("상태 진입과 진입 실패 정리가 모두 실패했습니다.", enterError, exitError);
                        }

                        throw;
                    }
                }

                NotifyStateChanged(prev, next);
            }
            finally
            {
                isTransitioning = false;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 확정된 전이를 모든 구독자에게 알린다.
        /// <br/> 한 구독자의 예외가 다른 구독자의 알림을 막지 않게 수집한다.
        /// </summary>
        // ------------------------------------------------------------
        private void NotifyStateChanged(IState previous, IState next)
        {
            var callbacks = OnStateChanged?.GetInvocationList();
            if (callbacks == null)
            {
                return;
            }

            List<Exception> failures = null;

            foreach (Action<IState, IState> callback in callbacks)
            {
                try
                {
                    callback(previous, next);
                }
                catch (Exception exception)
                {
                    failures ??= new List<Exception>();
                    failures.Add(exception);
                }
            }

            if (failures != null)
            {
                throw failures.Count == 1
                    ? failures[0]
                    : new AggregateException("StateMachine 상태 변경 알림 중 일부 구독자가 실패했습니다.", failures);
            }
        }

    #endregion

    #region 전이 없는 현재 상태 교체

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 전이 생명주기(OnExit/OnEnter/OnStateChanged) 없이
        /// <br/> 현재 상태만 교체한다. 외부에서 확정된 상태를 복원하는
        /// <br/> 파생 머신이 사용하며 null이면 활성 상태를 비운다.
        /// </summary>
        // ------------------------------------------------------------
        protected void ReplaceCurrent(IState state)
        {
            if (isTransitioning)
            {
                throw new InvalidOperationException("상태 전이 도중에는 현재 상태를 교체할 수 없습니다.");
            }

            ValidateRegistered(state);

            current = state;
        }

    #endregion

    #region 등록 검증

        // ------------------------------------------------------------
        /// <summary>
        /// StateBase는 이 머신의 등록표에 남아 있어야 현재 상태가 될 수 있다.
        /// </summary>
        // ------------------------------------------------------------
        private void ValidateRegistered(IState state)
        {
            if (state is not IStateMachineOwnedState ownedState)
            {
                return;
            }

            if (!ReferenceEquals(ownedState.RegisteredMachine, this))
            {
                throw new InvalidOperationException("StateBase는 등록된 StateMachine에서만 전이할 수 있습니다.");
            }

            foreach (var registeredState in states.Values)
            {
                if (ReferenceEquals(registeredState, state))
                {
                    return;
                }
            }

            throw new InvalidOperationException("교체되어 등록에서 제외된 StateBase로 전이할 수 없습니다.");
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
