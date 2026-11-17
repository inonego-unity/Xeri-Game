/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityExecutionDrive.cs
수정일 : 2026-11-17

# 설명
Host의 지속 비용과 Execution 시간 창의 경계 동작을 검증한다.
========================================================================= BLOCK_HEADER_END */

using System;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Ability
{
    using Xeri;
    using Xeri.Game;
    using Xeri.Game.TEST;
    using Xeri.Game.TEST._Ability;
    using Xeri.Game.TEST._Ability.Fixture;

    // ============================================================
    /// <summary>
    /// 지속 사용과 시간 창을 실제 Session으로 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityExecutionDrive
    {

    #region 검증용 Host와 Behavior

        private sealed class SustainHost
        {
            private readonly ResourceFixture resource;
            private readonly object owner;
            internal ExecutionSession Session { get; }
            internal int EndCount { get; private set; }

            internal SustainHost(ResourceFixture resource, object owner)
            {
                this.resource = resource;
                this.owner = owner;
                Session = CreateSession(1);
                Session.OnEnded += (_, _) => EndCount++;
            }

            internal void Tick(bool endRequested = false)
            {
                if (!Session.IsActive) return;

                var paid = resource.TryDebit(owner, 1);
                if (!paid || endRequested)
                {
                    Session.End(ExecutionEndReason.Interrupted);
                }
            }
        }

        private sealed class WindowBehavior : ExecutionBehaviorRuntime
        {
            private readonly ExecutionTrigger window = new ExecutionTimeWindowTriggerDefinition(0.28f, 0.30f).CreateTrigger();
            private readonly ExecutionTrigger enter = new ExecutionTimePointTriggerDefinition(0.28f).CreateTrigger();
            private readonly ExecutionTrigger exit = new ExecutionTimePointTriggerDefinition(0.30f).CreateTrigger();
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

        private static ExecutionSession CreateSession(ulong id)
        {
            var flow = new FixedTimelineExecutionFlowDefinition(10f);
            return new ExecutionSession(id, default, flow.CreateRuntime(), maximumTime: flow.MaximumDuration);
        }

    #endregion

    #region S-1: 지속 비용 소모와 실행 종료

        // ------------------------------------------------------------
        /// <summary>
        /// 자원 부족은 지속 실행을 중단하고 후속 실행을 허용한다.
        /// </summary>
        // ------------------------------------------------------------
        [Category("X09")]
        [Test]
        public void TEST_AbilityExecutionDrive_지속비용부족후다른실행()
        {
            var resource = new ResourceFixture();
            var owner = new object();
            resource.Add(owner, 2);
            var host = new SustainHost(resource, owner);
            host.Session.Begin();
            host.Tick();
            host.Tick();
            Assert.AreEqual(0, resource.Get(owner));
            Assert.IsTrue(host.Session.IsActive);
            host.Tick();
            host.Tick();
            Assert.AreEqual(0, resource.Get(owner));
            Assert.IsTrue(host.Session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Interrupted, host.Session.EndReason);
            Assert.AreEqual(1, host.EndCount);

            var next = CreateSession(2);
            next.Begin();
            Assert.IsTrue(next.IsActive);
            Assert.IsTrue(next.End(ExecutionEndReason.Completed));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 소비 실패와 종료 요청이 겹쳐도 종료는 한 번만 확정된다.
        /// </summary>
        // ------------------------------------------------------------
        [Category("X09")]
        [Test]
        public void TEST_AbilityExecutionDrive_소비실패와종료가겹치고종료Observer가실패()
        {
            var resource = new ResourceFixture();
            var owner = new object();
            resource.Add(owner, 0);
            var host = new SustainHost(resource, owner);
            var failingCalls = 0;
            host.Session.OnEnded += (_, _) =>
            {
                failingCalls++;
                throw new InvalidOperationException("end observer");
            };
            host.Session.Begin();
            Assert.Throws<InvalidOperationException>(() => host.Tick(endRequested: true));
            Assert.IsTrue(host.Session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Interrupted, host.Session.EndReason);
            Assert.AreEqual(1, host.EndCount);
            Assert.AreEqual(1, failingCalls);
            Assert.AreEqual(0, resource.Get(owner));
            host.Tick(endRequested: true);
            Assert.AreEqual(1, host.EndCount);
            Assert.AreEqual(1, failingCalls);
            Assert.AreEqual(0, resource.Get(owner));
        }

    #endregion

    #region H-1: HoldRelease와 큰 Tick의 Window

        // ------------------------------------------------------------
        /// <summary>
        /// 큰 진행 간격에서도 유지 해제와 시간 창 경계를 놓치지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Category("X10")]
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityExecutionDrive_HoldRelease와큰Tick의Window(bool largeTick)
        {
            var flow = new HoldReleaseExecutionFlowDefinition("Release", 0.25f, 0.8f, 0.3f);
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

    }
}
