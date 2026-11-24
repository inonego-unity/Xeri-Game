/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_StateMachine.cs
수정일 : 2026-11-16

# 설명
StateMachine<TOwner> 핵심 동작 테스트.
TestOwner / StateA / StateB / CallbackState로 상태 수명과 실패 경계를 검증한다.
Unity Test Runner (Edit Mode) 에서 실행한다.

# 테스트 구성
 E: 기본 기능 (생성/AddState/GetState)
 T: 전이 (MoveTo 타입/인스턴스/Exit-Enter 순서/같은 상태 무시)
 U: 갱신 (Update / FixedUpdate / LateUpdate 옵션 인터페이스)
 V: 이벤트 (OnStateChanged)
 전이 중 재진입, 실패 후 정리와 복구
========================================================================= BLOCK_HEADER_END */

using System;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._State
{
    // ============================================================
    /// <summary>
    /// StateMachine 핵심 기능 테스트.
    /// </summary>
    // ============================================================
    public class TEST_StateMachine
    {

    #region 헬퍼

        // ============================================================
        /// <summary>
        /// Update / FixedUpdate / LateUpdate 카운터를 가진 소유자.
        /// </summary>
        // ============================================================
        private class TestOwner
        {
            // ------------------------------------------------------------
            /// <summary>
            /// Update 실행 횟수 검증 값.
            /// </summary>
            // ------------------------------------------------------------
            public int Counter;

            // ------------------------------------------------------------
            /// <summary>
            /// FixedUpdate 실행 횟수 검증 값.
            /// </summary>
            // ------------------------------------------------------------
            public int FixedCounter;

            // ------------------------------------------------------------
            /// <summary>
            /// LateUpdate 실행 횟수 검증 값.
            /// </summary>
            // ------------------------------------------------------------
            public int LateCounter;
        }

        // ============================================================
        /// <summary>
        /// OnEnter / OnExit / OnUpdate 만 구현한 단순 상태.
        /// </summary>
        // ============================================================
        private class StateA : StateBase<TestOwner>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 진입 횟수를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void OnEnter()
            {
                Owner.Counter += 1;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 종료 횟수를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void OnExit()
            {
                Owner.Counter += 10;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 갱신 횟수를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void OnUpdate()
            {
                Owner.Counter += 100;
            }
        }

        // ============================================================
        /// <summary>
        /// IFixedUpdatable / ILateUpdatable 도 구현한 상태.
        /// </summary>
        // ============================================================
        private class StateB : StateBase<TestOwner>, IFixedUpdatable, ILateUpdatable
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 물리 갱신 횟수를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnFixedUpdate()
            {
                Owner.FixedCounter += 1000;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 지연 갱신 횟수를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnLateUpdate()
            {
                Owner.LateCounter += 10000;
            }
        }

        // ============================================================
        /// <summary>
        /// 서로 다른 Owner 타입에 묶인 StateBase의 우회 등록을 검증한다.
        /// </summary>
        // ============================================================
        private sealed class ForeignOwnerState : StateBase<object>
        {
            // NONE
        }

        // ============================================================
        /// <summary>
        /// 전이 콜백의 재진입과 실패를 재현하는 상태.
        /// </summary>
        // ============================================================
        private sealed class CallbackState : IState, IFixedUpdatable, ILateUpdatable
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 상태 진입 동작.
            /// </summary>
            // ------------------------------------------------------------
            public Action Enter { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 상태 종료 동작.
            /// </summary>
            // ------------------------------------------------------------
            public Action Exit { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 상태 갱신 동작.
            /// </summary>
            // ------------------------------------------------------------
            public Action Update { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 물리 갱신 동작.
            /// </summary>
            // ------------------------------------------------------------
            public Action FixedUpdate { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 지연 갱신 동작.
            /// </summary>
            // ------------------------------------------------------------
            public Action LateUpdate { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 상태 진입 콜백을 호출한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnEnter() => Enter?.Invoke();

            // ------------------------------------------------------------
            /// <summary>
            /// 상태 종료 콜백을 호출한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnExit() => Exit?.Invoke();

            // ------------------------------------------------------------
            /// <summary>
            /// 갱신 콜백을 호출한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnUpdate() => Update?.Invoke();

            // ------------------------------------------------------------
            /// <summary>
            /// 물리 갱신 콜백을 호출한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnFixedUpdate() => FixedUpdate?.Invoke();

            // ------------------------------------------------------------
            /// <summary>
            /// 지연 갱신 콜백을 호출한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnLateUpdate() => LateUpdate?.Invoke();
        }

    #endregion

    #region E-1: 초기 생성

        // ------------------------------------------------------------
        /// <summary>
        /// 필수 Owner와 등록 State의 null 입력을 거절한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_null_Owner와_등록State를_거절()
        {
            Assert.Throws<ArgumentNullException>(() => new StateMachine<TestOwner>(null));

            var machine = new StateMachine<TestOwner>(new TestOwner());
            Assert.Throws<ArgumentNullException>(() => machine.AddState<StateA>(null));
            Assert.IsNull(machine.GetState<StateA>());
            Assert.IsNull(machine.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 생성 시 Owner를 보관하고 Current는 비어 있다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_초기_생성_Owner_보유_Current_null()
        {
            var owner = new TestOwner();
            var fsm   = new StateMachine<TestOwner>(owner);

            Assert.AreSame(owner, fsm.Owner);
            Assert.IsNull(fsm.Current);
        }

    #endregion

    #region E-2: AddState / GetState

        // ------------------------------------------------------------
        /// <summary>
        /// 상태 등록 후 동일 타입으로 조회하고 누락 타입은 null을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_AddState_GetState_등록_및_조회()
        {
            var owner = new TestOwner();
            var fsm   = new StateMachine<TestOwner>(owner);

            var added = fsm.AddState(new StateA());

            Assert.IsNotNull(added);
            Assert.AreSame(added, fsm.GetState<StateA>());
            Assert.IsNull(fsm.GetState<StateB>());
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 동일 타입 교체는 조회 결과만 바꾸고 활성 상태를 다음 전이까지 보존한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_동일_타입_교체는_Current를_유지하고_다음_전이에_반영()
        {
            var owner = new TestOwner();
            var machine = new StateMachine<TestOwner>(owner);
            var first = machine.AddState(new StateA());
            machine.MoveTo<StateA>();

            var second = machine.AddState(new StateA());
            Assert.AreSame(second, machine.GetState<StateA>());
            Assert.AreSame(first, machine.Current);
            Assert.AreSame(second, machine.AddState(second));

            machine.MoveTo<StateA>();
            Assert.AreSame(second, machine.Current);
            Assert.AreEqual(12, owner.Counter);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 객체를 여러 타입 키로 등록하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_동일_인스턴스_다중_타입_등록을_거절()
        {
            var machine = new StateMachine<TestOwner>(new TestOwner());
            var state = new CallbackState();
            machine.AddState<IState>(state);

            Assert.Throws<InvalidOperationException>(() => machine.AddState<CallbackState>(state));
            Assert.AreSame(state, machine.GetState<IState>());
            Assert.IsNull(machine.GetState<CallbackState>());
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 Owner라도 다른 FSM이 StateBase 인스턴스를 재사용하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_서로_다른_머신은_StateBase를_공유할_수_없음()
        {
            var owner = new TestOwner();
            var first = new StateMachine<TestOwner>(owner);
            var second = new StateMachine<TestOwner>(owner);
            var third = new StateMachine<TestOwner>(new TestOwner());
            var state = first.AddState(new StateA());
            var secondState = second.AddState(new StateA());

            Assert.Throws<InvalidOperationException>(() => second.AddState(state));
            Assert.Throws<InvalidOperationException>(() => third.AddState(state));
            Assert.AreSame(secondState, second.GetState<StateA>());
            Assert.IsNull(third.GetState<StateA>());

            first.MoveTo<StateA>();
            Assert.AreSame(state, first.Current);
            Assert.AreEqual(1, owner.Counter);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Owner 타입이 다른 StateBase는 IState 타입으로 전달해도 거절한다.
        /// <br/> 잘못된 등록으로 기존 상태가 교체되거나 Owner가 변경되지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_다른_Owner_타입은_인터페이스_등록으로_우회할_수_없음()
        {
            var foreign = new StateMachine<object>(new object());
            var target = new StateMachine<TestOwner>(new TestOwner());
            var state = foreign.AddState(new ForeignOwnerState());
            var original = target.AddState<IState>(new StateA());

            Assert.Throws<InvalidOperationException>(() => target.AddState<IState>(state));
            Assert.AreSame(original, target.GetState<IState>());
            Assert.Throws<InvalidOperationException>(() => target.MoveTo(state));
            Assert.Throws<InvalidOperationException>(() => target.MoveTo(new ForeignOwnerState()));
            Assert.IsNull(target.Current);

            foreign.MoveTo(state);
            target.MoveTo(original);
            Assert.AreSame(state, foreign.Current);
            Assert.AreSame(original, target.Current);
        }

    #endregion

    #region T-1: MoveTo 타입 전이

        // ------------------------------------------------------------
        /// <summary>
        /// 미등록 타입 전이는 활성 상태나 이벤트를 변경하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_미등록_타입으로_전이해도_기존_상태를_유지()
        {
            var owner = new TestOwner();
            var machine = new StateMachine<TestOwner>(owner);
            var changedCount = 0;
            machine.OnStateChanged += (previous, next) => changedCount++;

            machine.MoveTo<StateB>();
            Assert.IsNull(machine.Current);
            Assert.AreEqual(0, changedCount);

            var active = machine.AddState(new StateA());
            machine.MoveTo<StateA>();
            machine.MoveTo<StateB>();

            Assert.AreSame(active, machine.Current);
            Assert.AreEqual(1, owner.Counter);
            Assert.AreEqual(1, changedCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 타입으로 이동하면 진입 훅을 호출하고 Current를 갱신한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_MoveTo_타입_OnEnter_호출_Current_갱신()
        {
            var owner = new TestOwner();
            var fsm   = new StateMachine<TestOwner>(owner);

            fsm.AddState(new StateA());
            fsm.MoveTo<StateA>();

            Assert.IsNotNull(fsm.Current);
            Assert.AreEqual(1, owner.Counter);
        }

    #endregion

    #region T-2: MoveTo 인스턴스 전이

        // ------------------------------------------------------------
        /// <summary>
        /// 인스턴스로 이동하면 진입 훅을 호출하고 Current를 갱신한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_MoveTo_인스턴스_OnEnter_호출_Current_갱신()
        {
            var owner = new TestOwner();
            var fsm   = new StateMachine<TestOwner>(owner);

            var state = fsm.AddState(new StateA());
            fsm.MoveTo(state);

            Assert.AreSame(state, fsm.Current);
            Assert.AreEqual(1, owner.Counter);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 인스턴스 전이는 현재 머신에 등록되지 않은 StateBase를 거절한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_직접전이에서_외부_StateBase_소유권을_검증()
        {
            var owner = new TestOwner();
            var first = new StateMachine<TestOwner>(owner);
            var second = new StateMachine<TestOwner>(owner);
            var state = first.AddState(new StateA());

            Assert.Throws<InvalidOperationException>(() => second.MoveTo(state));
            Assert.Throws<InvalidOperationException>(() => second.MoveTo(new StateA()));
            Assert.IsNull(second.Current);

            first.MoveTo(state);
            Assert.AreSame(state, first.Current);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 타입 교체는 기존 Current를 유지하지만, 퇴장한 옛 StateBase를
        /// <br/> 직접 다시 활성화하는 것은 거절한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_교체된_과거_상태는_다시_활성화할_수_없음()
        {
            var owner = new TestOwner();
            var machine = new StateMachine<TestOwner>(owner);
            var previous = machine.AddState(new StateA());
            machine.MoveTo(previous);

            var replacement = machine.AddState(new StateA());
            machine.MoveTo(previous);
            Assert.AreSame(previous, machine.Current);
            Assert.AreEqual(1, owner.Counter);

            machine.MoveTo(replacement);
            Assert.Throws<InvalidOperationException>(() => machine.MoveTo(previous));
            Assert.AreSame(replacement, machine.Current);
            Assert.AreEqual(12, owner.Counter);

            machine.MoveTo(null);
            Assert.Throws<InvalidOperationException>(() => machine.MoveTo(previous));
            Assert.IsNull(machine.Current);
        }

    #endregion

    #region T-3: Exit → Enter 순서

        // ------------------------------------------------------------
        /// <summary>
        /// 전이 시 이전 상태를 종료한 뒤 새 상태를 진입시킨다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_A에서_B_전이_OnExit_OnEnter_순서()
        {
            var owner = new TestOwner();
            var fsm   = new StateMachine<TestOwner>(owner);

            fsm.AddState(new StateA());
            fsm.AddState(new StateB());

            fsm.MoveTo<StateA>();
            Assert.AreEqual(1, owner.Counter);

            fsm.MoveTo<StateB>();
            Assert.AreEqual(11, owner.Counter);
        }

    #endregion

    #region T-4: 같은 상태 재진입 무시

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 상태로 전이하면 진입·종료 훅을 다시 호출하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_같은_인스턴스_재진입_OnExit_OnEnter_미호출()
        {
            var owner = new TestOwner();
            var fsm   = new StateMachine<TestOwner>(owner);

            var state = fsm.AddState(new StateA());

            fsm.MoveTo(state);
            Assert.AreEqual(1, owner.Counter);

            fsm.MoveTo(state);
            Assert.AreEqual(1, owner.Counter);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// null 전이는 현재 상태를 정상 종료하고 다음 Tick을 비활성화한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_null_전이는_현재_상태를_종료()
        {
            var owner = new TestOwner();
            var machine = new StateMachine<TestOwner>(owner);
            var state = machine.AddState(new StateA());
            machine.MoveTo(state);
            machine.MoveTo(null);
            machine.Tick();

            Assert.IsNull(machine.Current);
            Assert.AreEqual(11, owner.Counter);
        }

    #endregion

    #region U-1: Update

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 Tick은 현재 상태의 OnUpdate만 실행한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_Update_Current_OnUpdate_호출()
        {
            var owner = new TestOwner();
            var fsm   = new StateMachine<TestOwner>(owner);

            fsm.AddState(new StateA());
            fsm.MoveTo<StateA>();

            owner.Counter = 0;
            fsm.Tick();

            Assert.AreEqual(100, owner.Counter);
        }

    #endregion

    #region U-2: FixedUpdate 옵션 인터페이스

        // ------------------------------------------------------------
        /// <summary>
        /// FixedTick은 선택 인터페이스를 구현한 상태만 갱신한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_FixedUpdate_IFixedUpdatable_구현_시에만_호출()
        {
            var owner = new TestOwner();
            var fsm   = new StateMachine<TestOwner>(owner);

            fsm.AddState(new StateA());
            fsm.AddState(new StateB());

            // A 는 IFixedUpdatable 미구현 → 무시
            fsm.MoveTo<StateA>();
            fsm.FixedTick();
            Assert.AreEqual(0, owner.FixedCounter);

            // B 는 IFixedUpdatable 구현 → 호출
            fsm.MoveTo<StateB>();
            fsm.FixedTick();
            Assert.AreEqual(1000, owner.FixedCounter);
        }

    #endregion

    #region U-3: LateUpdate 옵션 인터페이스

        // ------------------------------------------------------------
        /// <summary>
        /// LateTick은 선택 인터페이스를 구현한 상태만 갱신한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_LateUpdate_ILateUpdatable_구현_시에만_호출()
        {
            var owner = new TestOwner();
            var fsm   = new StateMachine<TestOwner>(owner);

            fsm.AddState(new StateA());
            fsm.AddState(new StateB());

            fsm.MoveTo<StateA>();
            fsm.LateTick();
            Assert.AreEqual(0, owner.LateCounter);

            fsm.MoveTo<StateB>();
            fsm.LateTick();
            Assert.AreEqual(10000, owner.LateCounter);
        }

    #endregion

    #region V-1: OnStateChanged 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// 성공한 전이에서 이전 상태와 새 상태를 알린다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_OnStateChanged_prev_next_발화()
        {
            var owner = new TestOwner();
            var fsm   = new StateMachine<TestOwner>(owner);

            var a = fsm.AddState(new StateA());
            var b = fsm.AddState(new StateB());

            IState capturedPrev = a;
            IState capturedNext = a;

            fsm.OnStateChanged += (prev, next) =>
            {
                capturedPrev = prev;
                capturedNext = next;
            };

            fsm.MoveTo<StateA>();
            Assert.IsNull(capturedPrev);
            Assert.AreSame(a, capturedNext);

            fsm.MoveTo<StateB>();
            Assert.AreSame(a, capturedPrev);
            Assert.AreSame(b, capturedNext);
        }

    #endregion

    #region 전이 재진입과 실패

        // ------------------------------------------------------------
        /// <summary>
        /// 종료 중 등록 교체를 막고 선택된 전이 대상을 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_종료중_등록교체를_거절하고_전이대상을_보존()
        {
            var owner = new TestOwner();
            var machine = new StateMachine<TestOwner>(owner);
            var original = machine.AddState(new StateA());
            var previous = new CallbackState
            {
                Exit = () => Assert.Throws<InvalidOperationException>(() => machine.AddState(new StateA()))
            };

            machine.MoveTo(previous);
            machine.MoveTo<StateA>();

            Assert.AreSame(original, machine.GetState<StateA>());
            Assert.AreSame(original, machine.Current);
            Assert.AreEqual(1, owner.Counter);

            var replacement = machine.AddState(new StateA());
            Assert.AreSame(original, machine.Current);
            machine.MoveTo<StateA>();
            Assert.AreSame(replacement, machine.Current);
            Assert.AreEqual(12, owner.Counter);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 진입과 완료 이벤트 중에도 상태 등록 변경을 거절한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_진입과_이벤트중_등록변경을_거절()
        {
            var machine = new StateMachine<TestOwner>(new TestOwner());
            var previous = new CallbackState();
            var next = machine.AddState(new CallbackState());
            var entered = 0;
            var notified = 0;

            next.Enter = () =>
            {
                entered++;
                Assert.Throws<InvalidOperationException>(() => machine.AddState(new CallbackState()));
            };
            machine.MoveTo(previous);
            machine.OnStateChanged += (oldState, newState) =>
            {
                if (ReferenceEquals(newState, next))
                {
                    notified++;
                    Assert.Throws<InvalidOperationException>(() => machine.AddState<IState>(new CallbackState()));
                }
            };

            machine.MoveTo<CallbackState>();

            Assert.AreSame(next, machine.Current);
            Assert.AreSame(next, machine.GetState<CallbackState>());
            Assert.IsNull(machine.GetState<IState>());
            Assert.AreEqual(1, entered);
            Assert.AreEqual(1, notified);

            var later = machine.AddState<IState>(new CallbackState());
            Assert.AreSame(later, machine.GetState<IState>());
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Exit, Enter, 이벤트 중첩 전이를 거절하고 바깥 전이를 한 번만 확정한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_콜백_중첩전이를_거절하고_단일전이를_유지()
        {
            var fsm = new StateMachine<TestOwner>(new TestOwner());
            var a = new CallbackState();
            var b = new CallbackState();
            var exitCount = 0;
            var enterCount = 0;
            var changeCount = 0;

            a.Exit = () =>
            {
                exitCount++;
                Assert.IsNull(fsm.Current);
                Assert.Throws<InvalidOperationException>(() => fsm.MoveTo(new CallbackState()));
            };
            b.Enter = () =>
            {
                enterCount++;
                Assert.AreSame(b, fsm.Current);
                Assert.Throws<InvalidOperationException>(() => fsm.MoveTo(a));
            };
            fsm.OnStateChanged += (prev, next) =>
            {
                if (ReferenceEquals(next, b))
                {
                    changeCount++;
                    Assert.Throws<InvalidOperationException>(() => fsm.MoveTo(a));
                }
            };

            fsm.MoveTo(a);
            fsm.MoveTo(b);
            fsm.MoveTo(b);

            Assert.AreSame(b, fsm.Current);
            Assert.AreEqual(1, exitCount);
            Assert.AreEqual(1, enterCount);
            Assert.AreEqual(1, changeCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 종료 실패는 다음 상태 진입을 막고 갱신할 상태를 남기지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_종료실패시_Current를_해제하고_재전이가능()
        {
            var fsm = new StateMachine<TestOwner>(new TestOwner());
            var a = new CallbackState
            {
                Exit = () => throw new InvalidOperationException("exit failed")
            };
            var entered = 0;
            var b = new CallbackState
            {
                Enter = () => entered++
            };

            fsm.MoveTo(a);

            Assert.Throws<InvalidOperationException>(() => fsm.MoveTo(b));
            Assert.IsNull(fsm.Current);
            Assert.AreEqual(0, entered);

            fsm.MoveTo(b);

            Assert.AreSame(b, fsm.Current);
            Assert.AreEqual(1, entered);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 진입 실패는 실패 상태를 해제하고 성공 이벤트를 발생시키지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_진입실패시_정리후_재전이가능()
        {
            var fsm = new StateMachine<TestOwner>(new TestOwner());
            var a = new CallbackState();
            var cleanupCount = 0;
            var changedCount = 0;
            var b = new CallbackState
            {
                Enter = () => throw new InvalidOperationException("enter failed"),
                Exit = () => cleanupCount++
            };
            fsm.OnStateChanged += (prev, next) => changedCount++;

            fsm.MoveTo(a);
            Assert.Throws<InvalidOperationException>(() => fsm.MoveTo(b));

            Assert.IsNull(fsm.Current);
            Assert.AreEqual(1, cleanupCount);
            Assert.AreEqual(1, changedCount);

            b.Enter = null;
            fsm.MoveTo(b);

            Assert.AreSame(b, fsm.Current);
            Assert.AreEqual(2, changedCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 진입 및 정리 오류를 모두 보존하면서 실패 상태를 비활성화한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_진입과_정리_모두_실패해도_복구가능()
        {
            var fsm = new StateMachine<TestOwner>(new TestOwner());
            var failing = new CallbackState
            {
                Enter = () => throw new InvalidOperationException("enter failed"),
                Exit = () => throw new InvalidOperationException("cleanup failed")
            };

            var exception = Assert.Throws<AggregateException>(() => fsm.MoveTo(failing));

            Assert.AreEqual(2, exception.InnerExceptions.Count);
            Assert.AreEqual("enter failed", exception.InnerExceptions[0].Message);
            Assert.AreEqual("cleanup failed", exception.InnerExceptions[1].Message);
            Assert.IsNull(fsm.Current);

            var recovered = new CallbackState();
            fsm.MoveTo(recovered);
            Assert.AreSame(recovered, fsm.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 각 Update 콜백의 전이는 다음 Tick부터 새 상태를 갱신한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_Update_중_상태변경은_다음_Tick에_적용()
        {
            var fsm = new StateMachine<TestOwner>(new TestOwner());
            var a = new CallbackState();
            var updated = 0;
            var fixedUpdated = 0;
            var lateUpdated = 0;
            var b = new CallbackState
            {
                Update = () => updated++,
                FixedUpdate = () => fixedUpdated++,
                LateUpdate = () => lateUpdated++
            };

            a.Update = () => fsm.MoveTo(b);
            a.FixedUpdate = () => fsm.MoveTo(b);
            a.LateUpdate = () => fsm.MoveTo(b);

            fsm.MoveTo(a);
            fsm.Tick();
            Assert.AreSame(b, fsm.Current);
            Assert.AreEqual(0, updated);
            fsm.Tick();
            Assert.AreEqual(1, updated);

            fsm.MoveTo(a);
            fsm.FixedTick();
            Assert.AreSame(b, fsm.Current);
            Assert.AreEqual(0, fixedUpdated);
            fsm.FixedTick();
            Assert.AreEqual(1, fixedUpdated);

            fsm.MoveTo(a);
            fsm.LateTick();
            Assert.AreSame(b, fsm.Current);
            Assert.AreEqual(0, lateUpdated);
            fsm.LateTick();
            Assert.AreEqual(1, lateUpdated);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 이벤트 예외는 이미 완료된 전이를 되돌리지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_이벤트_예외후에도_확정상태를_유지()
        {
            var fsm = new StateMachine<TestOwner>(new TestOwner());
            var a = new CallbackState();
            var b = new CallbackState();

            fsm.OnStateChanged += (prev, next) =>
            {
                if (ReferenceEquals(next, b))
                {
                    throw new InvalidOperationException("listener failed");
                }
            };

            fsm.MoveTo(a);
            Assert.Throws<InvalidOperationException>(() => fsm.MoveTo(b));
            Assert.AreSame(b, fsm.Current);

            fsm.MoveTo(a);
            Assert.AreSame(a, fsm.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 이벤트 구독자 일부가 실패해도 나머지를 호출하고 오류를 모두 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_일부_이벤트_구독자가_실패해도_나머지를_호출()
        {
            var machine = new StateMachine<TestOwner>(new TestOwner());
            var previous = new CallbackState();
            var next = new CallbackState();
            machine.MoveTo(previous);

            var notified = 0;
            machine.OnStateChanged += (oldState, newState) =>
                throw new InvalidOperationException("first failure");
            machine.OnStateChanged += (oldState, newState) => notified++;
            machine.OnStateChanged += (oldState, newState) =>
                throw new InvalidOperationException("third failure");

            var error = Assert.Throws<AggregateException>(() => machine.MoveTo(next));
            Assert.AreEqual(2, error.InnerExceptions.Count);
            Assert.AreEqual(1, notified);
            Assert.AreSame(next, machine.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 구독자 예외가 하나면 원래 예외를 스택 추적과 함께 다시 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_이벤트_구독자_예외하나는_원래스택추적유지()
        {
            var machine = new StateMachine<TestOwner>(new TestOwner());
            machine.OnStateChanged += ThrowFromStateObserver;

            var thrown = Assert.Throws<InvalidOperationException>(() => machine.MoveTo(new CallbackState()));

            Assert.That(thrown.StackTrace, Does.Contain(nameof(ThrowFromStateObserver)));
        }

        private static void ThrowFromStateObserver(IState previous, IState next)
        {
            throw new InvalidOperationException("listener failed");
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 타입 기반 전이도 콜백 재진입을 거절한다. 미등록 타입도 예외가 된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StateMachine_타입_전이도_재진입을_거절()
        {
            var machine = new StateMachine<TestOwner>(new TestOwner());
            var state = new CallbackState
            {
                Enter = () => Assert.Throws<InvalidOperationException>(() => machine.MoveTo<StateA>())
            };

            machine.MoveTo(state);
            Assert.AreSame(state, machine.Current);
        }

    #endregion

    }

}
