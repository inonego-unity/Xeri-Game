/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ExecutionSessionFailure.cs
수정일 : 2026-11-19

# 설명
ExecutionSession의 시작 실패 롤백과 진행·완료 판정·Observer 실패 뒤 정리 계약을 검증한다.

# 테스트 구성
 E: 시작·진행·Observer 실패 복구
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

    // ======================================================================
    /// <summary>
    /// ExecutionSession의 시작·진행 실패 복구 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ======================================================================
    public sealed class TEST_ExecutionSessionFailure : ExecutionTestBase
    {

    #region E-1: Session 시작 실패·롤백

        // ------------------------------------------------------------
        /// <summary>
        /// Behavior 시작 실패 시 시도한 대상까지 역순 정리된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_BehaviorBegin실패시_시작된Behavior를역순정리()
        {
            var trace = new List<string>();
            var first = new TraceBehaviorRuntime("First", trace);
            var failing = new TraceBehaviorRuntime("Failing", trace, failOnBegin: true);
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    first,
                    failing
                }
            );

            Assert.Throws<InvalidOperationException>(() => session.Begin());

            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.AreEqual(1, first.EndCount);
            Assert.AreEqual(1, failing.EndCount);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.Begin",
                    "Failing.Begin",
                    "Failing.End:Cancelled",
                    "First.End:Cancelled",
                },
                trace
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시작 중 재진입 종료는 거부되며 Session은 실패 종료된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_Begin중재진입End를막고정리()
        {
            var probe = new TraceBehaviorRuntime
            {
                OnBegin = session => session.End(ExecutionEndReason.Cancelled)
            };
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    probe
                }
            );

            Assert.Throws<InvalidOperationException>(() => session.Begin());
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.AreEqual(1, probe.EndCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시작 알림 예외에도 종료 통지와 자원 정리가 유지된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_OnBegun예외가Cancelled정리를보장()
        {
            var probe = new TraceBehaviorRuntime();
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    probe
                }
            );
            var endedCount = 0;
            session.OnEnded += (_, reason) =>
            {
                Assert.AreEqual(ExecutionEndReason.Cancelled, reason);
                endedCount++;
            };
            session.OnBegun += _ => throw new InvalidOperationException("start observer failure");

            Assert.Throws<InvalidOperationException>(() => session.Begin());
            Assert.AreEqual(1, endedCount);
            Assert.IsFalse(session.IsActive);
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.AreEqual(1, probe.EndCount);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 시작 알림 구독자가 Session을 종료하면 남은 구독자에게 시작을 알리지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_OnBegun구독자의종료뒤에는남은구독자에게알리지않음()
        {
            var session = CreateTimelineSession();
            var trace = new List<string>();
            session.OnBegun += target =>
            {
                trace.Add("Begun:first");
                target.End(ExecutionEndReason.Cancelled);
            };
            session.OnBegun += _ => trace.Add("Begun:second");
            session.OnEnded += (_, reason) => trace.Add($"Ended:{reason}");

            session.Begin();

            Assert.AreEqual
            (
                new[]
                {
                    "Begun:first",
                    "Ended:Cancelled",
                },
                trace.ToArray()
            );
            Assert.IsTrue(session.HasEnded);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 확정 알림 구독자가 Session을 종료하면 남은 구독자에게 확정을 알리지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_OnCommitted구독자의종료뒤에는남은구독자에게알리지않음()
        {
            var session = CreateTimelineSession();
            var trace = new List<string>();
            session.OnCommitted += target =>
            {
                trace.Add("Committed:first");
                target.End(ExecutionEndReason.Cancelled);
            };
            session.OnCommitted += _ => trace.Add("Committed:second");
            session.OnEnded += (_, reason) => trace.Add($"Ended:{reason}");
            session.Begin();

            Assert.IsTrue(session.TryCommit());

            Assert.AreEqual
            (
                new[]
                {
                    "Committed:first",
                    "Ended:Cancelled",
                },
                trace.ToArray()
            );
            Assert.IsTrue(session.HasEnded);
        }

    #endregion

    #region E-2: Session 진행·Observer 실패

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 도중 재진입을 차단하며 실패 후 시간과 종료 상태를 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_Advance재진입은실행시간을변경하지않고실패종료()
        {
            var attempted = false;
            var probe = new TraceBehaviorRuntime
            {
                OnAdvance = (running, _) =>
                {
                    if (attempted)
                    {
                        return;
                    }

                    attempted = true;
                    running.Advance(0.3f);
                }
            };
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    probe
                }
            );

            session.Begin();
            Assert.Throws<InvalidOperationException>(() => session.Advance(0.2f));

            Assert.IsTrue(attempted);
            Assert.AreEqual(0.2f, session.Time, 0.0001f);
            Assert.AreEqual(0f, session.PreviousTime, 0.0001f);
            Assert.AreEqual(0.2f, session.AppliedDelta, 0.0001f);
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Commit 알림 예외가 발생해도 실패 종료가 완료된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_OnCommitted예외가Cancelled정리를보장()
        {
            var probe = new TraceBehaviorRuntime();
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    probe
                }
            );
            session.OnCommitted += _ => throw new InvalidOperationException("commit observer failure");

            session.Begin();
            Assert.Throws<InvalidOperationException>(() => session.TryCommit());
            Assert.IsFalse(session.IsActive);
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.AreEqual(1, probe.EndCount);
        }

        // ============================================================
        /// <summary>
        /// 완료 판정에서 예외를 던지는 Flow 테스트 대역.
        /// </summary>
        // ============================================================
        private sealed class FailingCompletionFlowRuntime : ExecutionFlowRuntime
        {

            // ------------------------------------------------------------
            /// <summary>
            /// 조회하면 항상 예외를 던진다.
            /// </summary>
            // ------------------------------------------------------------
            public override bool IsComplete => throw new InvalidOperationException("completion failure");

            // ------------------------------------------------------------
            /// <summary>
            /// 진행은 아무 일도 하지 않는다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Advance(ExecutionSession session, float deltaTime)
            {
                // NONE
            }

        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Flow의 완료 판정이 예외를 던지면 TryComplete가 예외를 전달하고
        /// <br/> Session을 Cancelled로 종료한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_완료판정예외는Cancelled정리를보장()
        {
            var probe = new TraceBehaviorRuntime();
            var behaviors = new ExecutionBehaviorRuntime[1];
            behaviors[0] = probe;
            var session = new ExecutionSession
            (
                1UL, default, new FailingCompletionFlowRuntime(), behaviors, 1f
            );
            var ended = 0;
            session.OnEnded += (_, _) => ended++;
            session.Begin();

            Assert.Throws<InvalidOperationException>(() => session.TryComplete());

            Assert.IsFalse(session.IsActive);
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.AreEqual(1, ended);
            Assert.AreEqual(1, probe.EndCount);
        }

        // ============================================================
        /// <summary>
        /// 처음 받은 Signal만 다시 발행하는 Behavior 테스트 대역.
        /// </summary>
        // ============================================================
        private sealed class ReentrantSignalBehaviorRuntime : ExecutionBehaviorRuntime
        {

            // ------------------------------------------------------------
            /// <summary>
            /// Signal 수신 횟수를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int Calls { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 처음 받은 Signal을 같은 Session에 다시 발행한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Signal(ExecutionSession session, in ExecutionSignal signal)
            {
                Calls++;
                if (Calls == 1)
                {
                    session.PublishSignal(signal);
                }
            }

        }

        // ============================================================
        /// <summary>
        /// 처음 받은 Command만 다시 요청하는 Flow 테스트 대역.
        /// </summary>
        // ============================================================
        private sealed class ReentrantCommandFlowRuntime : ExecutionFlowRuntime
        {

            // ------------------------------------------------------------
            /// <summary>
            /// Command 수신 횟수를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int Calls { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 완료되지 않는다.
            /// </summary>
            // ------------------------------------------------------------
            public override bool IsComplete => false;

            // ------------------------------------------------------------
            /// <summary>
            /// 진행은 아무 일도 하지 않는다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Advance(ExecutionSession session, float deltaTime)
            {
                // NONE
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 처음 받은 Command를 같은 Session에 다시 요청한다.
            /// </summary>
            // ------------------------------------------------------------
            public override ExecutionCommandResult Command(ExecutionSession session, in ExecutionCommand command)
            {
                Calls++;
                return Calls == 1 ? session.Command(command) : ExecutionCommandResult.Accepted;
            }

        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Signal 전달 중의 Signal 재발행은 즉시 거부하고
        /// <br/> 실패한 Session은 Cancelled로 종료한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_Signal전달중재발행을거부()
        {
            var reentrant = new ReentrantSignalBehaviorRuntime();
            var behaviors = new ExecutionBehaviorRuntime[1];
            behaviors[0] = reentrant;
            var session = CreateTimelineSession(behavior: behaviors);
            session.Begin();

            Assert.Throws<InvalidOperationException>
            (
                () => session.PublishSignal(new ExecutionSignal("Release"))
            );

            Assert.AreEqual(1, reentrant.Calls);
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Command 처리 중의 Command 재요청은 즉시 거부하고
        /// <br/> 실패한 Session은 Cancelled로 종료한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_Command처리중재요청을거부()
        {
            var flow = new ReentrantCommandFlowRuntime();
            var session = new ExecutionSession(1UL, default, flow, maximumTime: 1f);
            session.Begin();

            Assert.Throws<InvalidOperationException>
            (
                () => session.Command(new ExecutionCommand("Release"))
            );

            Assert.AreEqual(1, flow.Calls);
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Behavior 진행 예외에도 Session과 Behavior가 취소 종료된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_Advance예외가Cancelled정리를보장()
        {
            var probe = new TraceBehaviorRuntime
            {
                OnAdvance = (_, _) => throw new InvalidOperationException("Advance probe failure")
            };
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    probe
                }
            );

            session.Begin();
            Assert.Throws<InvalidOperationException>(() => session.Advance(0.1f));
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.AreEqual(1, probe.EndCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 종료 Observer 일부가 실패해도 나머지 Observer를 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_OnEnded실패에도모든Observer에게통지()
        {
            var session = CreateTimelineSession();
            var observed = 0;
            session.OnEnded += (_, _) => throw new InvalidOperationException("end observer failure");
            session.OnEnded += (_, _) => observed++;

            session.Begin();
            Assert.Throws<InvalidOperationException>
            (
                () => session.End(ExecutionEndReason.Cancelled)
            );
            Assert.AreEqual(1, observed);
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.IsFalse(session.End(ExecutionEndReason.Cancelled));
        }

    #endregion

    }
}
