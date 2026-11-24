/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ExecutionFlow.cs
수정일 : 2026-11-19

# 설명
HoldRelease Flow의 진행과 해제 시간 경계, Command 판정과 Behavior로의 Signal 전달 계약을 검증한다.

# 테스트 구성
 H: HoldRelease 진행·해제 시간·큰 Tick의 시간 창
 C: Command 판정과 Signal 전달
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
    /// Flow 진행과 Command·Signal 처리 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_ExecutionFlow : ExecutionTestBase
    {

    #region H-1: HoldRelease 진행·시간 경계

        // ------------------------------------------------------------
        /// <summary>
        /// 최소 유지 전 요청은 지연되고 해제 후 회복 시간이 적용된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_HoldRelease_MinHold뒤_Command로Release하고_Recovery후완료()
        {
            var spec = new HoldReleaseExecutionFlowSpec
            (
                "Confirm",
                0.2f,
                1f,
                0.1f
            );
            var session = new ExecutionSession
            (
                2UL,
                default,
                spec.CreateRuntime(),
                maximumTime: spec.MaximumDuration
            );

            session.Begin();
            session.Advance(0.1f);
            Assert.AreEqual(ExecutionCommandResult.Deferred, session.Command(new ExecutionCommand("Confirm")));
            Assert.IsFalse(session.Flow.HasReleased);

            session.Advance(0.1f);
            Assert.IsTrue(session.Flow.HasReleased);
            Assert.AreEqual(0.2f, session.Flow.ReleaseTime, 0.0001f);

            session.Advance(0.1f);
            Assert.IsTrue(session.IsComplete);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Command 없이 최대 유지 시간에 도달하면 자동 해제되어야 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_HoldRelease_MaxHold에서도Command없이자동Release()
        {
            var spec = new HoldReleaseExecutionFlowSpec
            (
                "Release",
                0.1f,
                0.5f,
                0.2f
            );
            var session = new ExecutionSession
            (
                3UL,
                default,
                spec.CreateRuntime(),
                maximumTime: spec.MaximumDuration
            );

            session.Begin();
            session.Advance(0.5f);

            Assert.IsTrue(session.Flow.HasReleased);
            Assert.AreEqual(0.5f, session.Flow.ReleaseTime, 0.0001f);
            Assert.AreEqual(1f, session.Flow.ReleaseProgress, 0.0001f);
            Assert.IsFalse(session.IsComplete);

            session.Advance(0.2f);

            Assert.IsTrue(session.IsComplete);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 한 번에 시간이 초과돼도 자동 해제는 원래 경계에서 일어난다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_HoldRelease_큰Tick에서도자동해제시각을보존()
        {
            var spec = new HoldReleaseExecutionFlowSpec("Release", 0.2f, 0.7f, 0.3f);
            var session = new ExecutionSession
            (
                51UL, default, spec.CreateRuntime(),
                maximumTime: spec.MaximumDuration
            );

            session.Begin();
            session.Advance(8f);

            Assert.AreEqual(0.7f, session.Flow.ReleaseTime, 0.0001f);
            Assert.AreEqual(1f, session.Time, 0.0001f);
            Assert.IsTrue(session.IsComplete);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지연된 Command는 큰 Tick에서도 최소 유지 시각에 해제된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_HoldRelease_지연Release_큰Tick에서도최소유지시각을보존()
        {
            var spec = new HoldReleaseExecutionFlowSpec("Release", 0.25f, 0.8f, 0.2f);
            var session = new ExecutionSession
            (
                52UL, default, spec.CreateRuntime(),
                maximumTime: spec.MaximumDuration
            );

            session.Begin();
            Assert.AreEqual
            (
                ExecutionCommandResult.Deferred,
                session.Command(new ExecutionCommand("Release"))
            );
            session.Advance(9f);

            Assert.AreEqual(0.25f, session.Flow.ReleaseTime, 0.0001f);
            Assert.AreEqual(1f, session.Time, 0.0001f);
            Assert.IsTrue(session.IsComplete);
        }

    #endregion

    #region H-2: 큰 Tick의 해제와 시간 창

        private sealed class WindowBehavior : ExecutionBehaviorRuntime
        {
            private readonly ExecutionTrigger window = new ExecutionTimeWindowTriggerSpec(0.28f, 0.30f).CreateTrigger();
            private readonly ExecutionTrigger enter = new ExecutionTimePointTriggerSpec(0.28f).CreateTrigger();
            private readonly ExecutionTrigger exit = new ExecutionTimePointTriggerSpec(0.30f).CreateTrigger();
            internal int WindowCount { get; private set; }
            internal int EnterCount { get; private set; }
            internal int ExitCount { get; private set; }
            internal float WindowTime { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 시간 창 Trigger를 시작한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Begin(ExecutionSession session)
            {
                window.Begin();
                enter.Begin();
                exit.Begin();
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 시간 창과 시점 Trigger를 평가한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Advance(ExecutionSession session, float deltaTime)
            {
                if (window.TryEvaluate(session.PreviousTime, session.Time, out var time))
                {
                    WindowCount++;
                    WindowTime = time;
                }

                if (enter.TryEvaluate(session.PreviousTime, session.Time, out _))
                {
                    EnterCount++;
                }

                if (exit.TryEvaluate(session.PreviousTime, session.Time, out _))
                {
                    ExitCount++;
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 시간 창 Trigger를 종료한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void End(ExecutionSession session, ExecutionEndReason reason)
            {
                window.End();
                enter.End();
                exit.End();
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 큰 진행 간격에서도 유지 해제와 시간 창 경계를 놓치지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_HoldRelease_큰Tick에서도해제와시간창경계를유지(bool largeTick)
        {
            var flow = new HoldReleaseExecutionFlowSpec("Release", 0.25f, 0.8f, 0.3f);
            var behavior = new WindowBehavior();
            var session = new ExecutionSession
            (
                3, default, flow.CreateRuntime(),
                new ExecutionBehaviorRuntime[]
                {
                    behavior,
                },
                maximumTime: flow.MaximumDuration
            );
            session.Begin();
            session.Advance(0.2f);
            Assert.AreEqual(ExecutionCommandResult.Deferred, session.Command(new ExecutionCommand("Release")));
            Assert.AreEqual(0, behavior.WindowCount);
            if (largeTick)
            {
                session.Advance(0.3f);
            }
            else
            {
                session.Advance(0.1f);
                session.Advance(0.1f);
                session.Advance(0.1f);
            }

            Assert.AreEqual(0.25f, session.Flow.ReleaseTime, 0.0001f);
            Assert.AreEqual(1, behavior.WindowCount);
            Assert.AreEqual(0.28f, behavior.WindowTime, 0.0001f);
            Assert.AreEqual(1, behavior.EnterCount);
            Assert.AreEqual(1, behavior.ExitCount);
            session.Advance(0.1f);
            Assert.AreEqual(1, behavior.WindowCount);
            Assert.AreEqual(1, behavior.EnterCount);
            Assert.AreEqual(1, behavior.ExitCount);
        }

    #endregion

    #region C-1: Command·Signal 처리

        // ------------------------------------------------------------
        /// <summary>
        /// Signal 전달은 해제 Command가 아니며 Flow 상태를 바꾸지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_HoldRelease_Signal알림은Flow를변경하지않음()
        {
            var spec = new HoldReleaseExecutionFlowSpec("Release", 0.2f, 0.8f, 0.1f);
            var probe = new TraceBehaviorRuntime();
            var session = new ExecutionSession
            (
                55UL, default, spec.CreateRuntime(),
                new ExecutionBehaviorRuntime[]
                {
                    probe
                },
                maximumTime: spec.MaximumDuration
            );

            session.Begin();
            session.PublishSignal(new ExecutionSignal("Release"));
            session.Advance(0.3f);

            Assert.AreEqual(1, probe.SignalCount);
            Assert.IsTrue(session.Flow.IsHolding);
            Assert.IsFalse(session.Flow.HasReleased);

            Assert.AreEqual
            (
                ExecutionCommandResult.Accepted,
                session.Command(new ExecutionCommand("Release"))
            );
            Assert.AreEqual(0.3f, session.Flow.ReleaseTime, 0.0001f);
            Assert.AreEqual(1, probe.SignalCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 지연 Command와 잘못된 Command의 결과를 구별한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_HoldRelease_Command수락지연과거부상태를구별()
        {
            var spec = new HoldReleaseExecutionFlowSpec("Release", 0.3f, 0.8f, 0.2f);
            var probe = new TraceBehaviorRuntime();
            var session = new ExecutionSession
            (
                53UL, default, spec.CreateRuntime(),
                new ExecutionBehaviorRuntime[]
                {
                    probe
                },
                maximumTime: spec.MaximumDuration
            );

            session.Begin();
            Assert.AreEqual
            (
                ExecutionCommandResult.Rejected,
                session.Command(new ExecutionCommand("Unknown"))
            );
            Assert.AreEqual
            (
                ExecutionCommandResult.Deferred,
                session.Command(new ExecutionCommand("Release"))
            );
            Assert.AreEqual(0, probe.SignalCount);
            session.PublishSignal(new ExecutionSignal("Release"));
            Assert.AreEqual(1, probe.SignalCount);

            session.Advance(0.7f);
            Assert.AreEqual(0.3f, session.Flow.ReleaseTime, 0.0001f);
            Assert.AreEqual
            (
                ExecutionCommandResult.Rejected,
                session.Command(new ExecutionCommand("Release"))
            );
            Assert.AreEqual(1, probe.SignalCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 최소 유지가 지난 Command는 현재 시각에 즉시 해제된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_HoldRelease_최소유지후Command는즉시수락()
        {
            var spec = new HoldReleaseExecutionFlowSpec("Release", 0.2f, 0.8f, 0.2f);
            var session = new ExecutionSession
            (
                54UL, default, spec.CreateRuntime(),
                maximumTime: spec.MaximumDuration
            );

            session.Begin();
            session.Advance(0.4f);
            Assert.AreEqual
            (
                ExecutionCommandResult.Accepted,
                session.Command(new ExecutionCommand("Release"))
            );
            Assert.AreEqual(0.4f, session.Flow.ReleaseTime, 0.0001f);
        }

    #endregion

    }
}
