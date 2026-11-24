/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityExecutionChannel.cs
수정일 : 2026-11-20

# 설명
Ability 사용의 begin 콜백에서 ExecutionChannel로 실행을 교체할 때의 비용 유지·환급과 실행 수명을 검증한다.
사용 조건 거절, 선점 거절, 새 시작 실패, 이전 종료 실패, 시작 뒤 Host 실패, 정상 교체와 완료를 다룬다.

# 테스트 구성
 X: 채널 선점과 비용 환급
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

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
    /// Ability 사용과 ExecutionChannel 선점의 환급 계약 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityExecutionChannel : AbilityActivationTestBase
    {

    #region 테스트 데이터

        // Begin 또는 End에서 실패를 일으키는 Behavior.
        private sealed class FailingBehaviorRuntime : ExecutionBehaviorRuntime
        {
            private readonly bool failOnBegin;
            private readonly bool failOnEnd;

            internal FailingBehaviorRuntime(bool failOnBegin, bool failOnEnd)
            {
                this.failOnBegin = failOnBegin;
                this.failOnEnd   = failOnEnd;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 설정되면 시작 실패를 일으킨다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Begin(ExecutionSession session)
            {
                if (failOnBegin)
                {
                    throw new InvalidOperationException("begin failure");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 설정되면 종료 정리 실패를 일으킨다.
            /// </summary>
            // ------------------------------------------------------------
            public override void End(ExecutionSession session, ExecutionEndReason reason)
            {
                if (failOnEnd)
                {
                    throw new InvalidOperationException("end failure");
                }
            }
        }

        // 모든 요청을 거절하는 사용 조건.
        private sealed class RejectRequirement : IAbilityActivationRequirement<TestSpec>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 이 요청의 승인을 거절한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityActivationCheck Evaluate(in AbilityActivationContext<TestSpec> context) =>
                AbilityActivationCheck.Reject("rejected");
        }

        private static ExecutionSession CreateSession(ulong id, bool failOnBegin = false, bool failOnEnd = false)
        {
            var flow = new FixedTimelineExecutionFlowSpec(1f);
            var behaviors = new ExecutionBehaviorRuntime[]
            {
                new FailingBehaviorRuntime(failOnBegin, failOnEnd),
            };

            return new ExecutionSession
            (
                id,
                default,
                flow.CreateRuntime(),
                behaviors,
                flow.MaximumDuration
            );
        }

        // 자원 2를 쓰는 사용 하나를 선택한다.
        private static AbilityUseSelection<TestSpec, string> SelectUse
        (
            TestResourceOwner owner,
            bool rejects = false,
            Action transition = null
        )
        {
            IAbilityActivationRequirement<TestSpec>[] requirement = null;

            if (rejects)
            {
                requirement = new IAbilityActivationRequirement<TestSpec>[]
                {
                    new RejectRequirement(),
                };
            }

            var policy = new AbilityActivationPolicy<TestSpec>
            (
                requirement: requirement,
                cost: new IAbilityActivationCost<TestSpec>[]
                {
                    new TestResourceCost(owner, 2),
                }
            );

            Assert.IsTrue(Select(CreateSelector(policy, null, transition), CreateContext(), out var selection, out _));
            return selection;
        }

    #endregion

    #region X-1: 채널 선점과 비용 환급

        // ----------------------------------------------------------------------
        /// <summary>
        /// 사용 조건이 거절되면 시작하지 않고 채널과 이전 실행을 유지하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityExecutionChannel_사용조건거절은시작없이이전유지()
        {
            var owner = new TestResourceOwner(5);
            var channel = new ExecutionChannel<int>((current, next) => ExecutionChannelDecision.Replace);
            var previous = CreateSession(1);
            var next = CreateSession(2);
            var began = 0;

            channel.TryStart(previous, 1);

            Assert.IsFalse
            (
                SelectUse(owner, rejects: true).TryActivate
                (
                    (in AbilityUse<string> use) =>
                    {
                        began++;
                        return channel.TryStart(next, 2);
                    },
                    out var result,
                    out _
                )
            );

            Assert.AreEqual(AbilityActivationFailure.RequirementRejected, result.Failure);
            Assert.AreEqual(0, began);
            Assert.AreEqual(5, owner.Current);
            Assert.AreSame(previous, channel.Active);
            Assert.IsTrue(previous.IsActive);
            Assert.IsFalse(next.HasBegun);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 채널 선점이 거절되면 비용을 환급하고 이전 실행을 유지하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityExecutionChannel_선점거절은비용환급과이전유지()
        {
            var owner = new TestResourceOwner(5);
            var channel = new ExecutionChannel<int>((current, next) => ExecutionChannelDecision.Reject);
            var previous = CreateSession(1);
            var next = CreateSession(2);

            channel.TryStart(previous, 1);

            Assert.IsFalse(SelectUse(owner).TryActivate((in AbilityUse<string> use) => channel.TryStart(next, 2), out var result, out _));

            Assert.AreEqual(AbilityActivationFailure.StartRejected, result.Failure);
            Assert.AreEqual(5, owner.Current);
            Assert.AreSame(previous, channel.Active);
            Assert.IsFalse(next.HasBegun);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 새 실행 시작이 실패하면 비용을 환급하고 채널이 비는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityExecutionChannel_새시작실패는비용환급과채널비움()
        {
            var owner = new TestResourceOwner(5);
            var channel = new ExecutionChannel<int>((current, next) => ExecutionChannelDecision.Replace);
            var previous = CreateSession(1);
            var next = CreateSession(2, failOnBegin: true);

            channel.TryStart(previous, 1);

            Assert.Throws<InvalidOperationException>
            (
                () => SelectUse(owner).TryActivate((in AbilityUse<string> use) => channel.TryStart(next, 2), out _, out _)
            );

            Assert.AreEqual(5, owner.Current);
            Assert.AreEqual(ExecutionEndReason.Replaced, previous.EndReason);
            Assert.AreEqual(ExecutionEndReason.Cancelled, next.EndReason);
            Assert.IsFalse(channel.IsOccupied);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 이전 실행 종료가 실패하면 비용을 환급하고 새 실행을 시작하지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityExecutionChannel_이전종료실패는비용환급과새실행미시작()
        {
            var owner = new TestResourceOwner(5);
            var channel = new ExecutionChannel<int>((current, next) => ExecutionChannelDecision.Interrupt);
            var previous = CreateSession(1, failOnEnd: true);
            var next = CreateSession(2);

            channel.TryStart(previous, 1);

            Assert.Throws<InvalidOperationException>
            (
                () => SelectUse(owner).TryActivate((in AbilityUse<string> use) => channel.TryStart(next, 2), out _, out _)
            );

            Assert.AreEqual(5, owner.Current);
            Assert.AreEqual(ExecutionEndReason.Interrupted, previous.EndReason);
            Assert.IsFalse(next.HasBegun);
            Assert.IsFalse(channel.IsOccupied);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 새 실행을 시작한 뒤 Host가 실패하면 Host가 그 실행을 끝내 채널이 비고
        /// <br/> 비용은 환급되는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityExecutionChannel_시작뒤Host실패는실행종료와비용환급()
        {
            var owner = new TestResourceOwner(5);
            var channel = new ExecutionChannel<int>((current, next) => ExecutionChannelDecision.Replace);
            var previous = CreateSession(1);
            var next = CreateSession(2);

            channel.TryStart(previous, 1);

            Assert.Throws<InvalidOperationException>
            (
                () => SelectUse(owner).TryActivate
                (
                    (in AbilityUse<string> use) =>
                    {
                        Assert.IsTrue(channel.TryStart(next, 2));

                        // 시작 뒤 실패한 Host는 시작한 실행을 스스로 끝낸 뒤 실패를 전달한다.
                        next.End(ExecutionEndReason.Cancelled);
                        throw new InvalidOperationException("host failure");
                    },
                    out _,
                    out _
                )
            );

            Assert.AreEqual(5, owner.Current);
            Assert.AreEqual(ExecutionEndReason.Replaced, previous.EndReason);
            Assert.AreEqual(ExecutionEndReason.Cancelled, next.EndReason);
            Assert.IsFalse(channel.IsOccupied);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 선점이 성공하면 비용을 유지하고 전이를 시작 뒤 한 번만 적용하며
        /// <br/> 새 실행이 끝나면 채널이 비는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityExecutionChannel_선점성공은비용유지와전이1회후완료시채널비움()
        {
            var owner = new TestResourceOwner(5);
            var channel = new ExecutionChannel<int>((current, next) => ExecutionChannelDecision.Replace);
            var previous = CreateSession(1);
            var next = CreateSession(2);
            var transitions = 0;
            var transitionsAtBegin = -1;

            channel.TryStart(previous, 1);

            var selection = SelectUse(owner, transition: () => transitions++);

            Assert.IsTrue
            (
                selection.TryActivate
                (
                    (in AbilityUse<string> use) =>
                    {
                        transitionsAtBegin = transitions;
                        return channel.TryStart(next, 2);
                    },
                    out var result,
                    out var started
                )
            );

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("action", started.Execution);
            Assert.AreEqual(0, transitionsAtBegin);
            Assert.AreEqual(1, transitions);
            Assert.AreEqual(3, owner.Current);
            Assert.AreEqual(ExecutionEndReason.Replaced, previous.EndReason);
            Assert.AreSame(next, channel.Active);

            // 같은 선택은 다시 시작하지 않는다.
            Assert.IsFalse(selection.TryActivate((in AbilityUse<string> use) => true, out var repeated, out _));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, repeated.Failure);

            next.Advance(1f);

            Assert.IsTrue(next.TryComplete());
            Assert.IsFalse(channel.IsOccupied);
            Assert.AreEqual(3, owner.Current);
        }

    #endregion

    }
}
