/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityExecutionLease.cs
수정일 : 2026-11-19

# 설명
고정 Tick 실행 순서와 테스트 Motion Lease의 수명 및 실패 정리를 검증한다.

# 테스트 구성
 L: 시작과 종료, 시작 실패
 T: 고정 Tick 교체와 큰 Delta 순서
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

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
    /// 실행 순서와 Motion Lease 경계를 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityExecutionLease
    {

    #region 검증용 Spec과 Behavior

        private sealed class TestSpec
        {
            // NONE
        }

        private sealed class MotionBehavior : ExecutionBehaviorRuntime
        {
            private readonly List<string> trace;
            private readonly bool fail;
            internal int Acquired { get; private set; }
            internal int Released { get; private set; }
            private Lease motion;

            internal bool Held => motion != null && !motion.IsDisposed;

            internal MotionBehavior(List<string> trace, bool fail = false)
            {
                this.trace = trace;
                this.fail = fail;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 테스트 Motion 소유권을 확보한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Begin(ExecutionSession session)
            {
                motion = new Lease(ReleaseMotion);
                Acquired++;
                trace.Add("motion.acquire");
                if (fail)
                {
                    throw new InvalidOperationException("motion begin failure");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 실행 시간 진행을 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Advance(ExecutionSession session, float deltaTime)
            {
                trace.Add("advance");
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 소유권을 한 번 해제한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void End(ExecutionSession session, ExecutionEndReason reason)
            {
                motion?.Release();
            }

            private void ReleaseMotion()
            {
                Released++;
                trace.Add("motion.release");
            }
        }

        private static ExecutionSession Session(MotionBehavior behavior)
        {
            var flow = new FixedTimelineExecutionFlowSpec(10f);
            var behaviors = new ExecutionBehaviorRuntime[]
            {
                behavior
            };
            return new ExecutionSession(1, default, flow.CreateRuntime(), behaviors, flow.MaximumDuration);
        }

        private static AbilityUseSelection<TestSpec, string> Select
        (
            AbilityBindings<TestSpec> bindings, AbilityInstance<TestSpec> ability
        )
        {
            var binding = new AbilityBinding<TestSpec>(new AbilityEntryKey("Skill"), ability);
            var context = new AbilityActivationContext<TestSpec>(null, null, binding, null);
            var costs = new IAbilityActivationCost<TestSpec>[]
            {
                new AbilityChargeCost<TestSpec>(),
            };
            var policy = new AbilityActivationPolicy<TestSpec>(cost: costs);

            // Cooldown은 Host 시작이 성공한 뒤 전이에서 시작한다.
            void StartCooldown()
            {
                Assert.IsTrue(ability.State.TryBeginCooldown(2f));
            }

            var selector = new AbilityUseSelector<TestSpec, object, string>
            (
                new Func<AbilityActivationContext<TestSpec>, object, AbilityUseChoice<TestSpec, string>>[]
                {
                    (selected, payload) => new AbilityUseChoice<TestSpec, string>("execution", policy, transition: StartCooldown)
                }
            );
            Assert.IsTrue(selector.TrySelect(bindings, context, out var selection, out _));
            return selection;
        }

    #endregion

    #region L-1: 시작과 종료 및 시작 실패

        // ------------------------------------------------------------
        /// <summary>
        /// 시작 성공과 실패에서 Motion Lease를 정확히 한 번 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityExecutionLease_시작과종료및시작실패(bool fail)
        {
            var trace = new List<string>();
            var behavior = new MotionBehavior(trace, fail);
            var session = Session(behavior);
            var ability = new AbilityInstance<TestSpec>(new TestSpec(), new AbilityState(new AbilityKey("A")));
            ability.State.ConfigureCharge(2, 10f);
            var layer = new AbilityBindingLayer<TestSpec>(0);
            layer.Set(new AbilityEntryKey("Skill"), ability);
            var bindings = new AbilityBindings<TestSpec>();
            using var lease = bindings.AcquireLayer(layer);
            var selection = Select(bindings, ability);
            var baseline = ability.State.Revision;
            if (fail)
            {
                Assert.Throws<InvalidOperationException>(() => selection.TryActivate(Begin, out _, out _));
                Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);

                // 시작 실패는 소비한 Charge를 환급하고 전이(Cooldown 시작)를 실행하지 않는다.
                Assert.IsTrue(ability.State.IsCooldownReady);
                Assert.AreEqual(2, ability.State.Charge.Current);
                // Revision은 단조 증가하므로 소비와 환급이 각각 올린다.
                Assert.Greater(ability.State.Revision, baseline);
            }
            else
            {
                Assert.IsTrue(selection.TryActivate(Begin, out _, out _));
                Assert.IsTrue(behavior.Held);
                Assert.IsFalse(ability.State.IsCooldownReady);
                Assert.AreEqual(1, ability.State.Charge.Current);
                Assert.IsTrue(session.End(ExecutionEndReason.Completed));
            }

            Assert.IsFalse(session.IsActive);
            Assert.IsFalse(session.End(ExecutionEndReason.Cancelled));
            Assert.AreEqual(1, behavior.Acquired);
            Assert.AreEqual(1, behavior.Released);
            Assert.IsFalse(behavior.Held);

            bool Begin(in AbilityUse<string> use)
            {
                session.Begin();
                return true;
            }
        }

    #endregion

    #region T-1: 고정 Tick 교체와 큰 Delta 순서

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 Tick의 승인, 진행, 접촉, 상태 변경 및 정리 순서를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [TestCase(0.25f)]
        [TestCase(5f)]
        public void TEST_AbilityExecutionLease_고정Tick교체와큰Delta순서(float delta)
        {
            var trace = new List<string>();
            var clock = new FakeAbilityNetwork<int>(0.25);
            var roster = new AbilityFixtureRoster(1, 2);
            var ability = new AbilityInstance<TestSpec>(new TestSpec(), new AbilityState(new AbilityKey("A")));
            ability.State.ConfigureCharge(2, 10f);
            var layer = new AbilityBindingLayer<TestSpec>(0);
            layer.Set(new AbilityEntryKey("Skill"), ability);
            var bindings = new AbilityBindings<TestSpec>();
            using var lease = bindings.AcquireLayer(layer);
            var behavior = new MotionBehavior(trace);
            var session = Session(behavior);
            var target = new GameObject("delivery-target");
            try
            {
                var collider = target.AddComponent<BoxCollider>();
                var journal = new AbilityFixtureResults<AbilityUseOrigin>();
                var used = default(AbilityUse<string>);
                var afterUse = 0UL;
                clock.Send(clock.Connect(1), clock.Server, 1, delayTicks: 1);

                Assert.AreEqual(0, clock.Server.Received.Count);
                RunTick(clock, delta, HandleRequest, session.Advance, ApplyResult, Cleanup);

                Assert.AreEqual(1, clock.Tick);
                Assert.AreEqual(0.25d, clock.Now);
                Assert.AreEqual(0, clock.PendingCount);
                Assert.AreEqual(1, journal.Entries.Count);
                Assert.AreEqual(used.Origin, journal.Entries[0].Origin);
                Assert.AreEqual(1, ability.State.Values.GetI("Hits"));
                Assert.AreEqual(afterUse + 1, ability.State.Revision);
                Assert.AreEqual(1, ability.State.Charge.Current);
                Assert.AreEqual(1, behavior.Released);
                CollectionAssert.AreEqual
                (
                    new[]
                    {
                        "request", "begin", "motion.acquire", "advance", "switch",
                        "contact", "result", "state", "motion.release", "cleanup"
                    },
                    trace
                );

                void HandleRequest(int owner)
                {
                    Assert.AreEqual((ulong)owner, roster.ActiveOwner);
                    trace.Add("request");
                    var selection = Select(bindings, ability);
                    var before = ability.State.Revision;
                    Assert.IsTrue(selection.TryActivate(Begin, out _, out used));
                    Assert.Greater(ability.State.Revision, before);
                    afterUse = ability.State.Revision;
                    Assert.IsFalse(ability.State.IsCooldownReady);
                }

                bool Begin(in AbilityUse<string> use)
                {
                    trace.Add("begin");
                    session.Begin();
                    return true;
                }

                void ApplyResult()
                {
                    Assert.IsTrue(roster.TrySelect(2));
                    trace.Add("switch");
                    var contact = new DeliveryContact3D(collider, Vector3.zero, Vector3.up, Vector3.forward, 1);
                    var delivery = new DeliveryContactQueryResult3D(true, true, contact);
                    Assert.IsTrue(delivery.HasContact);
                    Assert.AreSame(collider, delivery.Contact.Recipient);
                    trace.Add("contact");
                    journal.Record(used.Origin, 9, 1);
                    trace.Add("result");
                    ability.State.Values.SetI("Hits", journal.Entries.Count);
                    trace.Add("state");
                }

                void Cleanup()
                {
                    Assert.IsTrue(session.End(ExecutionEndReason.Completed));
                    trace.Add("cleanup");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void RunTick
        (
            FakeAbilityNetwork<int> clock, float delta, Action<int> handleRequest,
            Action<float> advance, Action applyResult, Action cleanup
        )
        {
            var first = clock.Server.Received.Count;
            clock.AdvanceTick();
            clock.DeliverReady();
            var end = clock.Server.Received.Count;
            for (var index = first; index < end; index++)
            {
                handleRequest(clock.Server.Received[index].Payload);
            }

            advance(delta);
            applyResult();
            cleanup();
        }

    #endregion

    }
}
