/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_StateMachine.cs
수정일 : 2026-11-12

# 설명
StateMachine<TOwner> 핵심 동작 테스트.
TestOwner / StateA / StateB 콘크리트를 파일 내부에 정의해 추상 동작을 검증한다.
Unity Test Runner (Edit Mode) 에서 실행한다.

# 테스트 구성
 E: 기본 기능 (생성/AddState/GetState)
 T: 전이 (MoveTo 타입/인스턴스/Exit-Enter 순서/같은 상태 무시)
 U: 갱신 (Update / FixedUpdate / LateUpdate 옵션 인터페이스)
 V: 이벤트 (OnStateChanged)
========================================================================= BLOCK_HEADER_END */

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._State
{

    using inonego.Xeri.Game;

    // ============================================================
    /// <summary>
    /// StateMachine 핵심 기능 테스트.
    /// </summary>
    // ============================================================
    public class TEST_StateMachine
    {

    #region 헬퍼

        // ------------------------------------------------------------
        /// <summary>
        /// Update / FixedUpdate / LateUpdate 카운터를 가진 소유자.
        /// </summary>
        // ------------------------------------------------------------
        private class TestOwner
        {
            public int Counter;
            public int FixedCounter;
            public int LateCounter;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// OnEnter / OnExit / OnUpdate 만 구현한 단순 상태.
        /// </summary>
        // ------------------------------------------------------------
        private class StateA : StateBase<TestOwner>
        {
            public override void OnEnter()  => Owner.Counter += 1;
            public override void OnExit()   => Owner.Counter += 10;
            public override void OnUpdate() => Owner.Counter += 100;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// IFixedUpdatable / ILateUpdatable 도 구현한 상태.
        /// </summary>
        // ------------------------------------------------------------
        private class StateB : StateBase<TestOwner>, IFixedUpdatable, ILateUpdatable
        {
            public void OnFixedUpdate() => Owner.FixedCounter += 1000;
            public void OnLateUpdate()  => Owner.LateCounter  += 10000;
        }

    #endregion

    #region E-1: 초기 생성

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

    #endregion

    #region T-1: MoveTo 타입 전이

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

    #endregion

    #region T-3: Exit → Enter 순서

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

    #endregion

    #region U-1: Update

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

    }

}
