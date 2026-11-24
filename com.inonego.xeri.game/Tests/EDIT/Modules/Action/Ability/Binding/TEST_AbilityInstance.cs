/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityInstance.cs
수정일 : 2026-11-19

# 설명
공유 Owner에서 Ability Instance의 상태 독립성과 Instigator 격리, 벤치 Owner 계약을 검증한다.

# 테스트 구성
 O: 공유 Owner와 Instigator 독립성·벤치 Owner
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
    using Xeri.Utility;
    using Xeri.Game.TEST;
    using Xeri.Game.TEST._Ability;
    using Xeri.Game.TEST._Ability.Fixture;

    // ============================================================
    /// <summary>
    /// 공유 Owner와 실행 주체 격리 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityInstance : AbilityTestBase
    {

    #region 검증용 요청 맥락 생성

        private static AbilityActivationContext<TestSpec> CreateContext
        (
            AbilityEntryKey entryKey,
            AbilityInstance<TestSpec> ability,
            object owner = null,
            object instigator = null
        )
        {
            var binding = new AbilityBinding<TestSpec>(entryKey, ability);
            return new AbilityActivationContext<TestSpec>
            (
                owner,
                instigator,
                binding,
                new TestPayload(1)
            );
        }

    #endregion

    #region O-1: 공유 소유자와 실행 주체 격리

        // ----------------------------------------------------------------------
        /// <summary>
        /// 같은 Owner State를 공유하고 Execution은 Instigator별로 격리되는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityInstance_같은OwnerState를공유하고Execution은Instigator별격리()
        {
            var owner = new object();
            var firstInstigator = new object();
            var secondInstigator = new object();
            var ability = CreateInstance("SharedSkill");
            var entry = new AbilityEntryKey("Skill");
            var firstContext = CreateContext
            (
                entry,
                ability,
                owner,
                firstInstigator
            );
            var secondContext = CreateContext
            (
                entry,
                ability,
                owner,
                secondInstigator
            );
            var flow = new FixedTimelineExecutionFlowSpec(1f);
            var firstExecution = new ExecutionSession
            (
                101UL,
                new ExecutionContext(owner, firstInstigator),
                flow.CreateRuntime(),
                maximumTime: flow.MaximumDuration
            );
            var secondExecution = new ExecutionSession
            (
                102UL,
                new ExecutionContext(owner, secondInstigator),
                flow.CreateRuntime(),
                maximumTime: flow.MaximumDuration
            );

            firstExecution.Begin();
            secondExecution.Begin();
            firstExecution.Variables.SetI("ActorLocal", 1);
            secondExecution.Variables.SetI("ActorLocal", 2);
            ability.State.Control.TryBeginCooldown(2f);

            Assert.AreSame(firstContext.Ability.State, secondContext.Ability.State);
            Assert.AreSame(owner, firstExecution.Context.Owner);
            Assert.AreSame(owner, secondExecution.Context.Owner);
            Assert.AreSame(firstInstigator, firstExecution.Context.Instigator);
            Assert.AreSame(secondInstigator, secondExecution.Context.Instigator);
            Assert.AreEqual(1, firstExecution.Variables.GetI("ActorLocal"));
            Assert.AreEqual(2, secondExecution.Variables.GetI("ActorLocal"));
            Assert.IsFalse(firstContext.Ability.State.IsCooldownReady);
            Assert.IsFalse(secondContext.Ability.State.IsCooldownReady);

            firstExecution.End(ExecutionEndReason.Cancelled);

            Assert.IsTrue(secondExecution.IsActive);
            Assert.IsFalse(ability.State.IsCooldownReady);

            secondExecution.End(ExecutionEndReason.Cancelled);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// SharedCooldown이 다른 Instigator의 다음 Activation을 거부하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityInstance_SharedCooldown은다른Instigator의다음Activation을거부()
        {
            var owner = new object();
            var firstInstigator = new object();
            var secondInstigator = new object();
            var ability = CreateInstance("SharedSkill");
            var entry = new AbilityEntryKey("Skill");
            var firstContext = CreateContext
            (
                entry,
                ability,
                owner,
                firstInstigator
            );
            var secondContext = CreateContext
            (
                entry,
                ability,
                owner,
                secondInstigator
            );
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                requirement: new IAbilityActivationRequirement<TestSpec>[]
                {
                    new AbilityCooldownRequirement<TestSpec>()
                }
            );

            Assert.IsTrue(policy.TryActivate(firstContext, null, new List<AbilityCostReceipt>(), out var firstResult));
            Assert.IsTrue(firstResult.Succeeded);

            Assert.IsTrue(ability.State.Control.TryBeginCooldown(2f));

            Assert.IsFalse(policy.TryActivate(secondContext, null, new List<AbilityCostReceipt>(), out var secondResult));
            Assert.AreEqual
            (
                AbilityActivationFailure.RequirementRejected,
                secondResult.Failure
            );
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 벤치 Owner가 자기 상태만 진행하고 다른 Owner 사용의 영향을 받지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityInstance_벤치Owner는자기상태만진행하고다른Owner의사용에영향받지않음()
        {
            var entry = new AbilityEntryKey("Skill");
            var bench = CreateInstance("Skill");
            var active = CreateInstance("Skill");
            bench.State.Control.ConfigureCharge(2, 1f);
            active.State.Control.ConfigureCharge(2, 1f);
            Assert.IsTrue(bench.State.Control.TryBeginCooldown(4f));

            var bindings = new AbilityBindings<TestSpec>();
            var baseLayer = new AbilityBindingLayer<TestSpec>(0);
            var formLayer = new AbilityBindingLayer<TestSpec>(100);
            baseLayer.Set(entry, bench);
            formLayer.Set(entry, active);
            using var baseLease = bindings.AcquireLayer(baseLayer);
            using var formLease = bindings.AcquireLayer(formLayer);
            Assert.IsTrue(bindings.TryResolve(entry, out var resolved));
            Assert.AreSame(active, resolved.Instance);

            var policy = new AbilityActivationPolicy<TestSpec>
            (
                requirement: new IAbilityActivationRequirement<TestSpec>[]
                {
                    new AbilityCooldownRequirement<TestSpec>()
                },
                cost: new IAbilityActivationCost<TestSpec>[]
                {
                    new AbilityChargeCost<TestSpec>()
                }
            );
            var benchRevision = bench.State.Revision;

            Assert.IsTrue(policy.TryActivate(CreateContext(entry, resolved.Instance), null, new List<AbilityCostReceipt>(), out _));
            Assert.IsTrue(active.State.Control.TryBeginCooldown(2f));

            Assert.AreEqual(1, active.State.Charge.Current);
            Assert.AreEqual(2, bench.State.Charge.Current);
            Assert.AreEqual(4f, bench.State.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(benchRevision, bench.State.Revision);

            // Host가 각 Owner를 따로 진행하며 한쪽 진행이 다른 쪽에 전달되지 않는다.
            active.State.Advance(1f);
            Assert.AreEqual(1f, active.State.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(2, active.State.Charge.Current);
            Assert.AreEqual(4f, bench.State.Cooldown.RemainingTime, 0.0001f);

            bench.State.Advance(3f);
            Assert.AreEqual(1f, bench.State.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(1f, active.State.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(benchRevision, bench.State.Revision);
        }

    #endregion

    }
}
