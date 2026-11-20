/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ExecutionTrigger.cs
수정일 : 2026-11-19

# 설명
Trigger의 시점·구간·Signal 판정, 공유 Spec과 Behavior 조합, 실행별 수명과 오류 복구 계약을 검증한다.

# 테스트 구성
 T: Trigger 발동·Spec·Behavior 연동·종료
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Execution
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// Trigger 판정과 수명 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_ExecutionTrigger : ExecutionTestBase
    {

    #region 검증용 Trigger와 Behavior

        // ============================================================
        /// <summary>
        /// 공통 Trigger를 서로 다른 Behavior에 결합하는 실행 테스트 어댑터.
        /// </summary>
        // ============================================================
        private sealed class TriggerTraceBehaviorRuntime : ExecutionBehaviorRuntime
        {

        #region 상태

            private readonly string name;
            private readonly ExecutionTrigger trigger;
            private readonly List<string> trace;
            private readonly List<float> times;

        #endregion

        #region 생성자

            // ------------------------------------------------------------
            /// <summary>
            /// Trigger 호출을 기록하는 테스트 Behavior를 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public TriggerTraceBehaviorRuntime
            (
                string name, ExecutionTrigger trigger,
                List<string> trace, List<float> times
            )
            {
                this.name = name;
                this.trigger = trigger;
                this.trace = trace;
                this.times = times;
            }

        #endregion

        #region 실행 수명과 Signal

            // ------------------------------------------------------------
            /// <summary>
            /// Trigger의 Begin 호출을 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Begin(ExecutionSession session)
            {
                trigger.Begin();
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Trigger의 Advance 호출을 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Advance(ExecutionSession session, float deltaTime)
            {
                if (trigger.TryEvaluate(session.PreviousTime, session.Time, out var effectiveTime))
                {
                    trace.Add(name);
                    times.Add(effectiveTime);
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Trigger가 수신한 Signal을 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Signal(ExecutionSession session, in ExecutionSignal signal)
            {
                trigger.Signal(signal, session.Time);
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Trigger의 종료 사유를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void End(ExecutionSession session, ExecutionEndReason reason)
            {
                trigger.End();
            }

        #endregion

        }

        // ============================================================
        /// <summary>
        /// 실행별 Trigger 수명 호출과 실패 정리를 관찰하는 테스트 구현.
        /// </summary>
        // ============================================================
        private sealed class LifecycleTrigger : ExecutionTrigger
        {

        #region 상태

            private readonly bool failOnBegin;
            private readonly bool failOnEnd;

            // ------------------------------------------------------------
            /// <summary>
            /// Begin 호출 횟수를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int BeginCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// End 호출 횟수를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int EndCount { get; private set; }

        #endregion

        #region 생성자

            // ------------------------------------------------------------
            /// <summary>
            /// 실패 여부를 지정하여 수명주기 테스트 Trigger를 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public LifecycleTrigger(bool failOnBegin = false, bool failOnEnd = false)
            {
                this.failOnBegin = failOnBegin;
                this.failOnEnd = failOnEnd;
            }

        #endregion

        #region 실행 수명과 평가

            // ------------------------------------------------------------
            /// <summary>
            /// Begin 호출을 기록하고 설정에 따라 실패시킨다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Begin()
            {
                BeginCount++;
                if (failOnBegin)
                {
                    throw new InvalidOperationException("Trigger begin failure");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// End 호출을 기록하고 설정에 따라 실패시킨다.
            /// </summary>
            // ------------------------------------------------------------
            public override void End()
            {
                EndCount++;
                if (failOnEnd)
                {
                    throw new InvalidOperationException("Trigger end failure");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 조건 평가 결과를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public override bool TryEvaluate
            (
                float previousTime,
                float currentTime,
                out float effectiveTime
            )
            {
                effectiveTime = 0f;
                return false;
            }

        #endregion

        }

    #endregion

    #region T-1: Trigger 시점·Signal 판정

        // ------------------------------------------------------------
        /// <summary>
        /// 정지 Tick에서는 발동하지 않고 지정 시각 최초 통과만 감지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimePointTrigger_시간점을_처음통과할때만실행()
        {
            var runtime = new ExecutionTimePointTriggerSpec(0f).CreateTrigger();
            runtime.Begin();

            Assert.IsFalse(runtime.TryEvaluate(0f, 0f, out _));
            Assert.IsTrue(runtime.TryEvaluate(0f, 0.1f, out var effectiveTime));
            Assert.AreEqual(0f, effectiveTime, 0.0001f);
            Assert.IsFalse(runtime.TryEvaluate(0.1f, 0.2f, out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시간창과 Tick의 겹침 및 교차 시작 시각이 정확해야 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimeWindowTrigger_Tick과Window가겹치면활성()
        {
            var spec = new ExecutionTimeWindowTriggerSpec(0.2f, 0.5f);
            var runtime = spec.CreateTrigger();

            Assert.IsFalse(runtime.TryEvaluate(0f, 0.1f, out _));
            Assert.IsTrue(runtime.TryEvaluate(0.1f, 0.3f, out var enteredTime));
            Assert.AreEqual(0.2f, enteredTime, 0.0001f);
            Assert.IsTrue(runtime.TryEvaluate(0.3f, 0.6f, out var overlapTime));
            Assert.AreEqual(0.3f, overlapTime, 0.0001f);
            Assert.IsFalse(runtime.TryEvaluate(0.6f, 0.7f, out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Signal 수신과 Offset이 충족된 뒤 한 번만 발동해야 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSignalTrigger_Signal시각과Offset으로_한번만실행()
        {
            var spec = new ExecutionSignalTriggerSpec("Detonate", 0.2f);
            var runtime = spec.CreateTrigger();
            runtime.Begin();
            runtime.Signal(new ExecutionSignal("Detonate"), 0.3f);

            Assert.IsFalse(runtime.TryEvaluate(0.3f, 0.49f, out _));
            Assert.IsTrue(runtime.TryEvaluate(0.49f, 0.5f, out var effectiveTime));
            Assert.AreEqual(0.5f, effectiveTime, 0.0001f);
            Assert.IsFalse(runtime.TryEvaluate(0.5f, 0.8f, out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 빈 창·역방향 구간에서는 발동하지 않고 정상 경계만 인정한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimeWindowTrigger_빈Window와정지역행Tick에서는발동하지않음()
        {
            var empty = new ExecutionTimeWindowTriggerSpec(0.4f, 0.4f).CreateTrigger();
            Assert.IsFalse(empty.TryEvaluate(0.2f, 0.5f, out _));

            var window = new ExecutionTimeWindowTriggerSpec(0.2f, 0.5f).CreateTrigger();
            Assert.IsFalse(window.TryEvaluate(0.3f, 0.3f, out _));
            Assert.IsFalse(window.TryEvaluate(0.4f, 0.2f, out _));
            Assert.IsFalse(window.TryEvaluate(float.NaN, 0.3f, out _));
            Assert.IsFalse(window.TryEvaluate(0.3f, float.NaN, out _));
            Assert.IsFalse(window.TryEvaluate(0.5f, 0.6f, out _));

            Assert.IsTrue(window.TryEvaluate(0.1f, 0.2f, out var atBegin));
            Assert.AreEqual(0.2f, atBegin, 0.0001f);
            Assert.IsTrue(window.TryEvaluate(0.4f, 0.5f, out var atEnd));
            Assert.AreEqual(0.4f, atEnd, 0.0001f);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 공유 Spec의 Trigger마다 발동 상태가 분리되고 재시작된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimePointTrigger_각인스턴스의일회성및Begin재시작()
        {
            var spec = new ExecutionTimePointTriggerSpec(0.25f);
            var first = spec.CreateTrigger();
            var second = spec.CreateTrigger();
            first.Begin();
            second.Begin();

            Assert.IsTrue(first.TryEvaluate(0f, 0.3f, out var firstTime));
            Assert.AreEqual(0.25f, firstTime, 0.0001f);
            Assert.IsFalse(first.TryEvaluate(0.3f, 0.6f, out _));

            Assert.IsFalse(second.TryEvaluate(0f, 0.2f, out _));
            Assert.IsTrue(second.TryEvaluate(0.2f, 0.3f, out _));
            first.Begin();
            Assert.IsTrue(first.TryEvaluate(0f, 0.3f, out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 첫 일치 Signal만 소비하고 재시작 시 수신 상태를 비운다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSignalTrigger_첫매칭Signal만받고재시작시초기화()
        {
            var trigger = new ExecutionSignalTriggerSpec("Launch", 0.2f).CreateTrigger();
            trigger.Begin();
            trigger.Signal(new ExecutionSignal("Other"), 0.1f);
            Assert.IsFalse(trigger.TryEvaluate(0.1f, 0.2f, out _));

            trigger.Signal(new ExecutionSignal("Launch"), 0.3f);
            trigger.Signal(new ExecutionSignal("Launch"), 0.4f);
            Assert.IsFalse(trigger.TryEvaluate(0.3f, 0.49f, out _));
            Assert.IsTrue(trigger.TryEvaluate(0.49f, 0.5f, out var firedTime));
            Assert.AreEqual(0.5f, firedTime, 0.0001f);
            Assert.IsFalse(trigger.TryEvaluate(0.5f, 0.8f, out _));

            trigger.Begin();
            Assert.IsFalse(trigger.TryEvaluate(0.8f, 0.9f, out _));
            trigger.Signal(new ExecutionSignal("Launch"), 0.9f);
            Assert.IsTrue(trigger.TryEvaluate(0.9f, 1.1f, out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 역방향·NaN 구간은 무시하고 정상 동일 시각에서 발동한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSignalTrigger_잘못된시간구간은소비하지않고_Offset0에서즉시발동()
        {
            var trigger = new ExecutionSignalTriggerSpec("Go", 0f).CreateTrigger();
            trigger.Begin();
            trigger.Signal(new ExecutionSignal("Go"), 0.25f);

            Assert.IsFalse(trigger.TryEvaluate(0.9f, 0.3f, out _));
            Assert.IsFalse(trigger.TryEvaluate(float.NaN, 0.3f, out _));

            Assert.IsTrue(trigger.TryEvaluate(0.25f, 0.25f, out var effectiveTime));
            Assert.AreEqual(0.25f, effectiveTime, 0.0001f);
            Assert.IsFalse(trigger.TryEvaluate(0.25f, 0.3f, out _));
        }

    #endregion

    #region T-2: Trigger Spec·Behavior 조합

        // ------------------------------------------------------------
        /// <summary>
        /// Spec 정규화는 이미 생성된 Trigger의 시각 설정을 변경하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTrigger_Spec공유와생성후정규화시실행별설정유지()
        {
            var windowSpec = new ExecutionTimeWindowTriggerSpec(0.2f, 0.8f);
            var original = windowSpec.CreateTrigger();
            windowSpec.Normalize(0.4f);
            var normalized = windowSpec.CreateTrigger();

            Assert.IsTrue(original.TryEvaluate(0.5f, 0.6f, out var originalTime));
            Assert.AreEqual(0.5f, originalTime, 0.0001f);
            Assert.IsFalse(normalized.TryEvaluate(0.5f, 0.6f, out _));

            var pointSpec = new ExecutionTimePointTriggerSpec(0.7f);
            var first = pointSpec.CreateTrigger();
            pointSpec.Normalize(0.3f);
            var second = pointSpec.CreateTrigger();

            Assert.IsTrue(first.TryEvaluate(0f, 0.8f, out var firstTime));
            Assert.IsTrue(second.TryEvaluate(0f, 0.8f, out var secondTime));
            Assert.AreEqual(0.7f, firstTime, 0.0001f);
            Assert.AreEqual(0.3f, secondTime, 0.0001f);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Behavior 형식에서 서로 다른 Trigger가 독립 발동한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTrigger_같은Behavior형식에서다른Trigger를조합()
        {
            var trace = new List<string>();
            var times = new List<float>();
            var point = new TriggerTraceBehaviorRuntime
            (
                "Point", new ExecutionTimePointTriggerSpec(0.2f).CreateTrigger(),
                trace, times
            );
            var signal = new TriggerTraceBehaviorRuntime
            (
                "Signal", new ExecutionSignalTriggerSpec("Launch", 0.1f).CreateTrigger(),
                trace, times
            );
            var session = CreateTimelineSession
            (
                duration: 1f,
                behavior: new ExecutionBehaviorRuntime[]
                {
                    point,
                    signal
                }
            );

            session.Begin();
            session.Advance(0.25f);
            session.PublishSignal(new ExecutionSignal("Launch"));
            session.Advance(0.1f);
            session.Advance(0.1f);

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Point",
                    "Signal"
                },
                trace
            );
            Assert.AreEqual(0.2f, times[0], 0.0001f);
            Assert.AreEqual(0.35f, times[1], 0.0001f);
            session.End(ExecutionEndReason.Completed);
        }

    #endregion

    #region T-3: Trigger 수명·오류 복구

        // ------------------------------------------------------------
        /// <summary>
        /// 소유 Behavior의 종료는 Trigger를 한 번만 종료해야 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTrigger_Session종료시소유Behavior가Trigger를종료()
        {
            var trigger = new LifecycleTrigger();
            var trace = new List<string>();
            var times = new List<float>();
            var behavior = new TriggerTraceBehaviorRuntime("Probe", trigger, trace, times);
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    behavior
                }
            );

            session.Begin();
            Assert.AreEqual(1, trigger.BeginCount);
            Assert.AreEqual(0, trigger.EndCount);

            Assert.IsTrue(session.End(ExecutionEndReason.Cancelled));
            Assert.AreEqual(1, trigger.EndCount);
            Assert.IsFalse(session.End(ExecutionEndReason.Cancelled));
            Assert.AreEqual(1, trigger.EndCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Trigger 시작 예외가 발생해도 자신의 종료 처리가 보장된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTrigger_Begin실패시해당Trigger도종료()
        {
            var trigger = new LifecycleTrigger(failOnBegin: true);
            var trace = new List<string>();
            var times = new List<float>();
            var behavior = new TriggerTraceBehaviorRuntime("Probe", trigger, trace, times);
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    behavior
                }
            );

            Assert.Throws<InvalidOperationException>(() => session.Begin());
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.AreEqual(1, trigger.BeginCount);
            Assert.AreEqual(1, trigger.EndCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 일부 Trigger 종료 실패가 다른 Trigger 정리를 막지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTrigger_한Trigger의종료오류에도다른Trigger를종료()
        {
            var first = new LifecycleTrigger(failOnEnd: true);
            var second = new LifecycleTrigger();
            var trace = new List<string>();
            var times = new List<float>();
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    new TriggerTraceBehaviorRuntime("First", first, trace, times),
                    new TriggerTraceBehaviorRuntime("Second", second, trace, times)
                }
            );

            session.Begin();

            Assert.Throws<InvalidOperationException>
            (
                () => session.End(ExecutionEndReason.Cancelled)
            );
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(1, first.EndCount);
            Assert.AreEqual(1, second.EndCount);
        }

    #endregion

    }
}
