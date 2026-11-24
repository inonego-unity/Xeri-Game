/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ExecutionChannel.cs
수정일 : 2026-11-20

# 설명
ExecutionChannel의 점유·선점 순서, 거절, 전환 중 재선점 거부, 종료 통지에 따른 해제와 실패 사후조건을 검증한다.

# 테스트 구성
 S: 시작과 선점
 G: 전환 중 재선점 거부와 해제
 F: 실패 사후조건과 인자 검사
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Execution
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// ExecutionChannel 점유·선점·실패 계약 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_ExecutionChannel : ExecutionTestBase
    {

    #region 테스트 데이터

        // End에서 정리 실패를 일으키는 Behavior.
        private sealed class ThrowOnEndBehaviorRuntime : ExecutionBehaviorRuntime
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 종료 정리 실패를 일으킨다.
            /// </summary>
            // ------------------------------------------------------------
            public override void End(ExecutionSession session, ExecutionEndReason reason)
            {
                throw new InvalidOperationException("end failure");
            }
        }

        private static ExecutionSession CreateSession(string name, List<string> trace, ulong id, bool failOnBegin = false)
        {
            var behaviors = new[]
            {
                new TraceBehaviorRuntime(name, trace, failOnBegin),
            };

            return CreateTimelineSession(behavior: behaviors, id: id);
        }

    #endregion

    #region S-1: 시작과 선점

        // ------------------------------------------------------------
        /// <summary>
        /// 빈 채널은 판정 없이 Session을 시작하고 점유하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionChannel_빈채널은판정없이시작하고점유()
        {
            var arbitrated = 0;
            var channel = new ExecutionChannel<int>
            (
                (current, next) =>
                {
                    arbitrated++;
                    return ExecutionChannelDecision.Reject;
                }
            );
            var session = CreateSession("A", null, 1);

            Assert.IsTrue(channel.CanStart(1));
            Assert.IsTrue(channel.TryStart(session, 1));

            Assert.AreEqual(0, arbitrated);
            Assert.AreSame(session, channel.Active);
            Assert.AreEqual(1, channel.ActiveClaim);
            Assert.IsTrue(channel.IsOccupied);
            Assert.IsTrue(session.IsActive);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 판정대로 이전 Session을 끝내고, 그 종료 통지가 새 Session 시작보다
        /// <br/> 먼저 한 번만 실행되는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [TestCase(ExecutionChannelDecision.Replace, ExecutionEndReason.Replaced)]
        [TestCase(ExecutionChannelDecision.Interrupt, ExecutionEndReason.Interrupted)]
        public void TEST_ExecutionChannel_선점은이전종료통지뒤새시작
        (
            ExecutionChannelDecision decision,
            ExecutionEndReason expected
        )
        {
            var trace = new List<string>();
            var received = new List<(int Current, int Next)>();
            var channel = new ExecutionChannel<int>
            (
                (current, next) =>
                {
                    received.Add((current, next));
                    return decision;
                }
            );
            var previous = CreateSession("A", trace, 1);
            var next = CreateSession("B", trace, 2);

            channel.TryStart(previous, 1);
            previous.OnEnded += (_, reason) => trace.Add($"A.OnEnded:{reason}");
            trace.Clear();

            Assert.IsTrue(channel.TryStart(next, 2));

            var expectedTrace = new List<string>
            {
                $"A.End:{expected}",
                $"A.OnEnded:{expected}",
                "B.Begin",
            };

            CollectionAssert.AreEqual(expectedTrace, trace);
            Assert.AreEqual(1, received.Count);
            Assert.AreEqual((1, 2), received[0]);
            Assert.AreEqual(expected, previous.EndReason);
            Assert.AreSame(next, channel.Active);
            Assert.AreEqual(2, channel.ActiveClaim);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 거절 판정은 이전 Session을 유지하고 새 Session을 시작하지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionChannel_거절은이전유지와새Session미시작()
        {
            var channel = new ExecutionChannel<int>((current, next) => ExecutionChannelDecision.Reject);
            var previous = CreateSession("A", null, 1);
            var next = CreateSession("B", null, 2);

            channel.TryStart(previous, 1);

            Assert.IsFalse(channel.CanStart(2));
            Assert.IsFalse(channel.TryStart(next, 2));

            Assert.AreSame(previous, channel.Active);
            Assert.IsTrue(previous.IsActive);
            Assert.IsFalse(next.HasBegun);
            Assert.IsFalse(channel.IsTransitioning);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// CanStart가 판정만 하고 점유 상태를 바꾸지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionChannel_CanStart는상태를바꾸지않음()
        {
            var decision = ExecutionChannelDecision.Replace;
            var channel = new ExecutionChannel<int>((current, next) => decision);
            var session = CreateSession("A", null, 1);

            channel.TryStart(session, 1);

            Assert.IsTrue(channel.CanStart(2));

            decision = ExecutionChannelDecision.Reject;

            Assert.IsFalse(channel.CanStart(2));
            Assert.AreSame(session, channel.Active);
            Assert.AreEqual(1, channel.ActiveClaim);
            Assert.IsTrue(session.IsActive);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 판정 함수 예외는 상태를 바꾸지 않고 전환 표시를 푸는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionChannel_판정예외는상태를바꾸지않음()
        {
            var channel = new ExecutionChannel<int>((current, next) => throw new InvalidOperationException("arbitrate"));
            var previous = CreateSession("A", null, 1);
            var next = CreateSession("B", null, 2);

            channel.TryStart(previous, 1);

            Assert.Throws<InvalidOperationException>(() => channel.TryStart(next, 2));

            Assert.AreSame(previous, channel.Active);
            Assert.IsTrue(previous.IsActive);
            Assert.IsFalse(next.HasBegun);
            Assert.IsFalse(channel.IsTransitioning);
        }

    #endregion

    #region G-1: 전환 중 재선점 거부와 해제

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 이전 Session 종료 통지와 새 Session 시작 안에서의 선점이 거부되고
        /// <br/> 전환이 끝나면 다시 판정하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionChannel_전환중재선점은거부()
        {
            var channel = new ExecutionChannel<int>((current, next) => ExecutionChannelDecision.Replace);
            var trace = new List<string>();
            var previous = CreateSession("A", trace, 1);
            var intruderInEnd = CreateSession("C", trace, 3);
            var intruderInBegin = CreateSession("D", trace, 4);
            var nextBehavior = new TraceBehaviorRuntime("B", trace);
            var nextBehaviors = new ExecutionBehaviorRuntime[]
            {
                nextBehavior,
            };
            var next = CreateTimelineSession(behavior: nextBehaviors, id: 2);
            var results = new List<bool>();

            channel.TryStart(previous, 1);

            previous.OnEnded += (_, _) =>
            {
                results.Add(channel.CanStart(9));
                results.Add(channel.TryStart(intruderInEnd, 9));
            };
            nextBehavior.OnBegin = _ => results.Add(channel.TryStart(intruderInBegin, 9));

            Assert.IsTrue(channel.TryStart(next, 2));

            Assert.AreEqual(3, results.Count);
            Assert.That(results, Is.All.False);
            Assert.IsFalse(intruderInEnd.HasBegun);
            Assert.IsFalse(intruderInBegin.HasBegun);
            Assert.AreSame(next, channel.Active);
            Assert.IsFalse(channel.IsTransitioning);
            Assert.IsTrue(channel.CanStart(9));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 점유 Session이 스스로 끝나면 채널이 비는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [TestCase(ExecutionEndReason.Completed)]
        [TestCase(ExecutionEndReason.Cancelled)]
        public void TEST_ExecutionChannel_점유Session이끝나면채널이빔(ExecutionEndReason reason)
        {
            var channel = new ExecutionChannel<int>((current, next) => ExecutionChannelDecision.Reject);
            var session = CreateSession("A", null, 1);
            var next = CreateSession("B", null, 2);

            channel.TryStart(session, 1);
            session.End(reason);

            Assert.IsNull(channel.Active);
            Assert.AreEqual(0, channel.ActiveClaim);
            Assert.IsFalse(channel.IsOccupied);

            // 비었으므로 거절 판정과 무관하게 시작한다.
            Assert.IsTrue(channel.TryStart(next, 2));
            Assert.AreSame(next, channel.Active);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시작 직후 OnBegun에서 끝난 Session은 채널에 남지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionChannel_시작직후끝난Session은채널에남지않음()
        {
            var channel = new ExecutionChannel<int>((current, next) => ExecutionChannelDecision.Reject);
            var session = CreateSession("A", null, 1);

            session.OnBegun += started => started.End(ExecutionEndReason.Completed);

            Assert.IsTrue(channel.TryStart(session, 1));
            Assert.IsTrue(session.HasEnded);
            Assert.IsFalse(channel.IsOccupied);
        }

    #endregion

    #region F-1: 실패 사후조건과 인자 검사

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 새 Session 시작이 실패하면 채널은 비고 예외를 전달하며
        /// <br/> 이미 끝난 이전 Session은 되살리지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionChannel_새시작실패는채널을비우고예외전달()
        {
            var channel = new ExecutionChannel<int>((current, next) => ExecutionChannelDecision.Replace);
            var previous = CreateSession("A", null, 1);
            var next = CreateSession("B", null, 2, failOnBegin: true);

            channel.TryStart(previous, 1);

            Assert.Throws<InvalidOperationException>(() => channel.TryStart(next, 2));

            Assert.IsFalse(channel.IsOccupied);
            Assert.IsFalse(channel.IsTransitioning);
            Assert.AreEqual(ExecutionEndReason.Replaced, previous.EndReason);
            Assert.AreEqual(ExecutionEndReason.Cancelled, next.EndReason);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 이전 Session 종료 정리가 실패하면 이전 Session은 끝난 채로 채널이 비고
        /// <br/> 새 Session은 시작하지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionChannel_이전종료실패는새Session을시작하지않음()
        {
            var channel = new ExecutionChannel<int>((current, next) => ExecutionChannelDecision.Replace);
            var failing = new ExecutionBehaviorRuntime[]
            {
                new ThrowOnEndBehaviorRuntime(),
            };
            var previous = CreateTimelineSession(behavior: failing, id: 1);
            var next = CreateSession("B", null, 2);

            channel.TryStart(previous, 1);

            Assert.Throws<InvalidOperationException>(() => channel.TryStart(next, 2));

            Assert.IsTrue(previous.HasEnded);
            Assert.IsFalse(next.HasBegun);
            Assert.IsFalse(channel.IsOccupied);
            Assert.IsFalse(channel.IsTransitioning);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 잘못된 인자를 상태 변경 전에 거절하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionChannel_잘못된인자는상태변경전예외()
        {
            Assert.Throws<ArgumentNullException>(() => new ExecutionChannel<int>(null));

            var channel = new ExecutionChannel<int>((current, next) => ExecutionChannelDecision.Replace);
            var session = CreateSession("A", null, 1);
            var begun = CreateSession("B", null, 2);

            channel.TryStart(session, 1);
            begun.Begin();

            Assert.Throws<ArgumentNullException>(() => channel.TryStart(null, 2));
            Assert.Throws<ArgumentException>(() => channel.TryStart(begun, 2));

            Assert.AreSame(session, channel.Active);
            Assert.IsTrue(session.IsActive);
        }

    #endregion

    }
}
