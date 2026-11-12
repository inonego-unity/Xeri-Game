/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_Ability.cs
수정일 : 2026-11-12

# 설명
Ability의 Owner별 지속 상태, Binding, Cooldown과 Charge 충전·복원 계약을 검증한다.
사용 선택·Cost 평가·소비 테스트는 TEST_AbilityActivation.cs에서 별도 관리한다.

# 테스트 구성
 I: Ability Key·Entry 직렬화
 S: Owner 상태·제어·시간
 B: Binding 우선순위·해제
 O: 공유 Owner와 Instigator 독립성
 C: Charge 충전·복원
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
    using Xeri.Utility;

    public sealed class TEST_Ability
    {

    #region 검증용 정의와 요청 타입

        private sealed class TestDefinition
        {
            public string Name { get; }

            public TestDefinition(string name)
            {
                Name = name;
            }
        }

        private readonly struct TestProjectContext
        {
            public int Version { get; }

            public TestProjectContext(int version)
            {
                Version = version;
            }
        }

    #endregion

    #region 검증용 능력과 요청 맥락 생성

        private static AbilityInstance<TestDefinition> CreateInstance(string key, string name = null)
        {
            var state = new AbilityState(new AbilityKey(key));
            return new AbilityInstance<TestDefinition>
            (
                new TestDefinition(name ?? key),
                state
            );
        }

        private static AbilityActivationContext
        <
            TestDefinition,
            TestProjectContext
        > CreateContext
        (
            AbilityEntryKey entryKey,
            AbilityInstance<TestDefinition> ability,
            object owner = null,
            object instigator = null
        )
        {
            var binding = new AbilityBinding<TestDefinition>(entryKey, ability);
            return new AbilityActivationContext<TestDefinition, TestProjectContext>
            (
                owner,
                instigator,
                binding,
                new TestProjectContext(1)
            );
        }

    #endregion

    #region I-1: 식별자 직렬화와 진입점 구분

        [Test]
        public void TEST_AbilityKeys_UnitySerializedBackingField계약을보존()
        {
            AssertSerializedKeyField(typeof(AbilityKey));
            AssertSerializedKeyField(typeof(AbilityEntryKey));
        }

        private static void AssertSerializedKeyField(Type keyType)
        {
            var field = keyType.GetField
            (
                "value",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic
            );

            Assert.NotNull(field);
            Assert.AreEqual(typeof(string), field.FieldType);
            Assert.NotNull
            (
                Attribute.GetCustomAttribute
                (
                    field,
                    typeof(SerializeField)
                )
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 생성자를 거치지 않고 복원된 Key도 동등한 문자열로 조회되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityKeys_직렬화복원Key도정규화하여비교()
        {
            var flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic;

            var abilityField = typeof(AbilityKey).GetField("value", flags);
            object boxedAbility = default(AbilityKey);
            abilityField.SetValue(boxedAbility, " Skill ");
            var restoredAbility = (AbilityKey)boxedAbility;
            var expectedAbility = new AbilityKey("Skill");

            Assert.IsTrue(restoredAbility.IsValid);
            Assert.AreEqual("Skill", restoredAbility.Value);
            Assert.AreEqual(expectedAbility, restoredAbility);
            Assert.AreEqual(expectedAbility.GetHashCode(), restoredAbility.GetHashCode());

            var entryField = typeof(AbilityEntryKey).GetField("value", flags);
            object boxedEntry = default(AbilityEntryKey);
            entryField.SetValue(boxedEntry, " Skill ");
            var restoredEntry = (AbilityEntryKey)boxedEntry;
            var entries = new Dictionary<AbilityEntryKey, int>
            {
                [new AbilityEntryKey("Skill")] = 5
            };

            Assert.IsTrue(restoredEntry.IsValid);
            Assert.IsTrue(entries.TryGetValue(restoredEntry, out var amount));
            Assert.AreEqual(5, amount);

            entryField.SetValue(boxedEntry, "   ");
            Assert.IsFalse(((AbilityEntryKey)boxedEntry).IsValid);
        }

        [Test]
        public void TEST_AbilityBinding은_AbilityKey와EntryKey를독립보존()
        {
            var ability = CreateInstance("Fireball");
            var binding = new AbilityBinding<TestDefinition>
            (
                new AbilityEntryKey("Skill"),
                ability
            );
            var context = new AbilityActivationContext
            <
                TestDefinition,
                TestProjectContext
            >
            (
                null,
                null,
                binding,
                new TestProjectContext(1)
            );

            Assert.AreEqual("Fireball", ability.Key.Value);
            Assert.AreEqual("Skill", binding.EntryKey.Value);
            Assert.AreEqual("Fireball", context.Ability.Key.Value);
            Assert.AreEqual("Skill", context.EntryKey.Value);
        }

    #endregion

    #region S-1: 소유자별 상태와 시간 독립성

        [Test]
        public void TEST_AbilityInstance_같은Definition의OwnerState를독립보존()
        {
            var definition = new TestDefinition("Fireball");
            var key = new AbilityKey("Fireball");
            var first = new AbilityInstance<TestDefinition>
            (
                definition,
                new AbilityState(key)
            );
            var second = new AbilityInstance<TestDefinition>
            (
                definition,
                new AbilityState(key)
            );

            first.State.Values.SetI("Stack", 3);
            first.State.Control.TryBeginCooldown(2f);
            first.State.Control.ConfigureCharge(2, 1f);
            first.State.Control.TryConsumeCharge(1);
            first.State.Values.SetI("FollowupUses", 1);

            second.State.Control.ConfigureCharge(3, 2f);
            second.State.Values.SetI("Stack", 7);
            second.State.Values.SetI("FollowupUses", 4);

            Assert.AreSame(definition, first.Definition);
            Assert.AreSame(definition, second.Definition);
            Assert.AreNotSame(first.State, second.State);
            Assert.AreEqual(3, first.State.Values.GetI("Stack"));
            Assert.AreEqual(7, second.State.Values.GetI("Stack"));
            Assert.AreEqual(1, first.State.Values.GetI("FollowupUses"));
            Assert.AreEqual(4, second.State.Values.GetI("FollowupUses"));
            Assert.AreEqual(1, first.State.Charge.Current);
            Assert.AreEqual(3, second.State.Charge.Current);
            Assert.AreNotSame(first.State.Charge, second.State.Charge);
            Assert.IsFalse(first.State.IsCooldownReady);
            Assert.IsTrue(second.State.IsCooldownReady);

            first.State.Advance(0.5f);

            Assert.AreEqual(1, first.State.Charge.Current);
            Assert.AreEqual(3, second.State.Charge.Current);
            Assert.AreEqual(1, first.State.Values.GetI("FollowupUses"));
            Assert.AreEqual(4, second.State.Values.GetI("FollowupUses"));
        }

    #endregion

    #region S-2: 상태 제어·시간 입력과 콜백 전이

        [Test]
        public void TEST_AbilityStateControl_Cooldown시작감소Reset()
        {
            var state = new AbilityState(new AbilityKey("Dash"));

            Assert.IsTrue(state.Control.TryBeginCooldown(5f));
            Assert.IsFalse(state.IsCooldownReady);
            Assert.IsTrue(state.Control.ReduceCooldown(2f));
            Assert.AreEqual(3f, state.Cooldown.RemainingTime, 0.0001f);
            Assert.IsTrue(state.Control.ResetCooldown());
            Assert.IsTrue(state.IsCooldownReady);
            Assert.IsFalse(state.Control.ResetCooldown());
        }

        [Test]
        public void TEST_AbilityState_음수시간은무시하고비정상시간은거부()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.ConfigureCharge(2, 1f);
            Assert.AreEqual(1f, state.Charge.RechargeDuration, 0.0001f);
            state.Control.TryConsumeCharge(1);
            state.Control.TryBeginCooldown(2f);
            state.Values.SetI("FollowupStage", 2);

            state.Advance(-5f);

            Assert.AreEqual(2f, state.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(1f, state.Charge.Recharge.RemainingTime, 0.0001f);
            Assert.AreEqual(2, state.Values.GetI("FollowupStage"));
            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => state.Advance(float.NaN)
            );
            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => state.Advance(float.PositiveInfinity)
            );
            Assert.AreEqual(2f, state.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(1, state.Charge.Current);
            Assert.AreEqual(2, state.Values.GetI("FollowupStage"));
        }

        [Test]
        public void TEST_AbilityState_Cooldown종료콜백에서외부사건이후속단계를연다()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.TryBeginCooldown(1f);
            state.Cooldown.OnEnd += (_, _) => state.Values.SetI("FollowupStage", 2);

            state.Advance(1f);

            Assert.AreEqual(2, state.Values.GetI("FollowupStage"));
            state.Advance(100f);
            Assert.AreEqual(2, state.Values.GetI("FollowupStage"));
        }

        [Test]
        public void TEST_AbilityState_Cooldown종료값변경은Charge와독립적()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.ConfigureCharge(2, 1f, startFull: false);
            state.Control.TryBeginCooldown(1f);
            state.Cooldown.OnEnd += (_, _) => state.Values.SetI("FollowupUses", 1);

            state.Advance(1f);

            Assert.AreEqual(1, state.Values.GetI("FollowupUses"));
            Assert.AreEqual(1, state.Charge.Current);
            Assert.IsTrue(state.Charge.Recharge.IsRunning);
        }

    #endregion

    #region B-1: 바인딩 우선순위·해제와 수집

        [Test]
        public void TEST_AbilityBindings_Priority와Release로PartialOverride해석()
        {
            var attack = new AbilityEntryKey("Attack");
            var skill = new AbilityEntryKey("Skill");
            var baseAttack = CreateInstance("BaseAttack");
            var baseSkill = CreateInstance("BaseSkill");
            var formSkill = CreateInstance("FormSkill");
            var bindings = new AbilityBindings<TestDefinition>();
            var baseLayer = new AbilityBindingLayer<TestDefinition>(0);
            baseLayer.Set(attack, baseAttack);
            baseLayer.Set(skill, baseSkill);
            var formLayer = new AbilityBindingLayer<TestDefinition>(100);
            formLayer.Set(skill, formSkill);

            using var baseLease = bindings.AcquireLayer(baseLayer);

            using (bindings.AcquireLayer(formLayer))
            {
                Assert.IsTrue(bindings.TryResolve(attack, out var attackBinding));
                Assert.AreSame(baseAttack, attackBinding.Instance);
                Assert.IsTrue(bindings.TryResolve(skill, out var skillBinding));
                Assert.AreSame(formSkill, skillBinding.Instance);
            }

            Assert.IsTrue(bindings.TryResolve(skill, out var restored));
            Assert.AreSame(baseSkill, restored.Instance);
        }

        [Test]
        public void TEST_AbilityBindings_같은Priority에서는최근Layer가우선()
        {
            var entry = new AbilityEntryKey("Skill");
            var first = new AbilityBindingLayer<TestDefinition>(10);
            var second = new AbilityBindingLayer<TestDefinition>(10);
            var firstAbility = CreateInstance("First");
            var secondAbility = CreateInstance("Second");
            first.Set(entry, firstAbility);
            second.Set(entry, secondAbility);
            var bindings = new AbilityBindings<TestDefinition>();

            using var firstLease = bindings.AcquireLayer(first);
            Assert.IsTrue(bindings.TryResolve(entry, out var firstBinding));
            Assert.AreSame(firstAbility, firstBinding.Instance);

            using (bindings.AcquireLayer(second))
            {
                Assert.IsTrue(bindings.TryResolve(entry, out var secondBinding));
                Assert.AreSame(secondAbility, secondBinding.Instance);
            }

            Assert.IsTrue(bindings.TryResolve(entry, out var restored));
            Assert.AreSame(firstAbility, restored.Instance);
        }

        [Test]
        public void TEST_AbilityBindings_전체해석이Override와Grant를반영()
        {
            var attack = new AbilityEntryKey("Attack");
            var skill = new AbilityEntryKey("Skill");
            var utility = new AbilityEntryKey("Utility");
            var baseAttack = CreateInstance("BaseAttack");
            var baseSkill = CreateInstance("BaseSkill");
            var overrideSkill = CreateInstance("OverrideSkill");
            var grantedUtility = CreateInstance("GrantedUtility");
            var bindings = new AbilityBindings<TestDefinition>();
            var baseLayer = new AbilityBindingLayer<TestDefinition>(0);
            var overrideLayer = new AbilityBindingLayer<TestDefinition>(100);
            var resolved = new List<AbilityBinding<TestDefinition>>();

            baseLayer.Set(skill, baseSkill);
            baseLayer.Set(attack, baseAttack);
            overrideLayer.Set(skill, overrideSkill);
            overrideLayer.Set(utility, grantedUtility);

            using var baseLease = bindings.AcquireLayer(baseLayer);
            using var overrideLease = bindings.AcquireLayer(overrideLayer);

            resolved.Add(new AbilityBinding<TestDefinition>(new AbilityEntryKey("Stale"), CreateInstance("Stale")));
            bindings.ResolveAll(resolved);

            Assert.AreEqual(3, resolved.Count);
            Assert.AreEqual("Attack", resolved[0].EntryKey.Value);
            Assert.AreSame(baseAttack, resolved[0].Instance);
            Assert.AreEqual("Skill", resolved[1].EntryKey.Value);
            Assert.AreSame(overrideSkill, resolved[1].Instance);
            Assert.AreEqual("Utility", resolved[2].EntryKey.Value);
            Assert.AreSame(grantedUtility, resolved[2].Instance);

            overrideLayer.Remove(utility);
            bindings.ResolveAll(resolved);
            Assert.AreEqual(2, resolved.Count);
            Assert.AreSame(overrideSkill, resolved[1].Instance);
        }

    #endregion

    #region O-1: 공유 소유자와 실행 주체 격리

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
            var flow = new FixedTimelineExecutionFlowDefinition(1f);
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
            var policy = new AbilityActivationPolicy
            <
                TestDefinition,
                TestProjectContext
            >
            (
                requirement: new IAbilityActivationRequirement<TestDefinition, TestProjectContext>[]
                {
                    new AbilityCooldownRequirement<TestDefinition, TestProjectContext>()
                }
            );

            Assert.IsTrue(policy.TryActivate(firstContext, out var firstResult));
            Assert.IsTrue(firstResult.Succeeded);

            Assert.IsTrue(ability.State.Control.TryBeginCooldown(2f));

            Assert.IsFalse(policy.TryActivate(secondContext, out var secondResult));
            Assert.AreEqual
            (
                AbilityActivationFailure.RequirementRejected,
                secondResult.Failure
            );
        }

    #endregion

    #region C-1: 충전 시간과 완료 콜백

        [Test]
        public void TEST_AbilityChargeState_Cooldown과독립적으로순차Recharge()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.Control.ConfigureCharge(3, 1f);
            state.Control.TryBeginCooldown(5f);

            Assert.IsTrue(state.Control.TryConsumeCharge(2));
            Assert.AreEqual(1, state.Charge.Current);

            state.Advance(1f);

            Assert.AreEqual(2, state.Charge.Current);
            Assert.AreEqual(4f, state.Cooldown.RemainingTime, 0.0001f);

            state.Advance(1f);

            Assert.AreEqual(3, state.Charge.Current);
            Assert.IsTrue(state.Charge.IsFull);
            Assert.AreEqual(3f, state.Cooldown.RemainingTime, 0.0001f);
        }

        [Test]
        public void TEST_AbilityChargeState_충전완료알림예외에도시계가계속진행()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.Control.ConfigureCharge(2, 1f, startFull: false);
            state.Charge.Recharge.OnEnd += (_, _) =>
                throw new InvalidOperationException("end observer failed");

            Assert.Throws<InvalidOperationException>(() => state.Advance(1f));

            Assert.AreEqual(1, state.Charge.Current);
            Assert.IsTrue(state.Charge.Recharge.IsRunning);
            Assert.AreEqual(1f, state.Charge.Recharge.RemainingTime, 0.0001f);
            state.Advance(0.25f);
            Assert.AreEqual(0.75f, state.Charge.Recharge.RemainingTime, 0.0001f);
        }

        [Test]
        public void TEST_AbilityState_쿨다운알림실패에도Charge와Owner값유지()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.Control.ConfigureCharge(2, 1f, startFull: false);
            state.Values.SetI("FollowupUses", 2);
            state.Control.TryBeginCooldown(1f);
            state.Cooldown.OnEnd += (_, _) =>
                throw new InvalidOperationException("cooldown observer failed");

            Assert.Throws<InvalidOperationException>(() => state.Advance(1f));

            Assert.AreEqual(1, state.Charge.Current);
            Assert.AreEqual(2, state.Values.GetI("FollowupUses"));
            Assert.IsFalse(state.Cooldown.IsRunning);
        }

        [Test]
        public void TEST_AbilityChargeState_완료알림과다음주기시작오류를모두보존()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.Control.ConfigureCharge(2, 1f, startFull: false);
            state.Charge.Recharge.OnEnd += (_, _) =>
                throw new InvalidOperationException("end failed");
            state.Charge.Recharge.OnStateChange += (_, change) =>
            {
                if (change.Current == TimerState.Run)
                {
                    throw new InvalidOperationException("restart failed");
                }
            };

            var error = Assert.Throws<AggregateException>(() => state.Advance(1f));

            Assert.AreEqual(2, error.InnerExceptions.Count);
            Assert.AreEqual(1, state.Charge.Current);
            Assert.IsTrue(state.Charge.Recharge.IsRunning);
            Assert.AreEqual(1f, state.Charge.Recharge.RemainingTime, 0.0001f);
        }

        [Test]
        public void TEST_AbilityChargeState_완료이벤트에서즉시충전분을사용해도완료분이보존()
        {
            var ability = CreateInstance("Skill");
            ability.State.Control.ConfigureCharge(2, 1f, startFull: false);
            Assert.AreEqual(1, ability.State.Control.RestoreCharge(1));
            var consumed = false;
            ability.State.Charge.Recharge.OnEnd += (_, _) =>
                consumed = ability.State.Control.TryConsumeCharge(1);

            ability.State.Advance(1f);

            Assert.IsTrue(consumed);
            Assert.AreEqual(1, ability.State.Charge.Current);
            Assert.IsTrue(ability.State.Charge.Recharge.IsRunning);
        }

        [Test]
        public void TEST_AbilityChargeState_완료알림실패에도남은Tick시간을적용()
        {
            var ability = CreateInstance("Skill");
            ability.State.Control.ConfigureCharge(3, 1f, startFull: false);
            var failures = 0;
            ability.State.Charge.Recharge.OnStateChange += (_, change) =>
            {
                if (change.Current == TimerState.Ready && failures++ == 0)
                {
                    throw new InvalidOperationException("first recharge notification failed");
                }
            };

            var error = Assert.Throws<InvalidOperationException>(() => ability.State.Advance(2.5f));

            Assert.AreEqual("first recharge notification failed", error.Message);
            Assert.AreEqual(2, ability.State.Charge.Current);
            Assert.AreEqual(0.5f, ability.State.Charge.Recharge.RemainingTime, 0.0001f);
        }

        [Test]
        public void TEST_AbilityState_쿨다운콜백의Charge소비는기존충전Tick을막지않음()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.ConfigureCharge(2, 1f, startFull: false);
            Assert.AreEqual(1, state.Control.RestoreCharge(1));
            Assert.IsTrue(state.Control.TryBeginCooldown(1f));
            var consumed = false;
            state.Cooldown.OnEnd += (_, _) => consumed = state.Control.TryConsumeCharge(1);

            state.Advance(1f);

            Assert.IsTrue(consumed);
            Assert.AreEqual(1, state.Charge.Current);
            Assert.IsTrue(state.Charge.Recharge.IsRunning);
            Assert.AreEqual(1f, state.Charge.Recharge.RemainingTime, 0.0001f);
        }

        [Test]
        public void TEST_AbilityState_쿨다운콜백이새로시작한충전에기존Tick을적용하지않음()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.ConfigureCharge(2, 1f, startFull: true);
            Assert.IsTrue(state.Control.TryBeginCooldown(1f));
            var consumed = false;
            state.Cooldown.OnEnd += (_, _) => consumed = state.Control.TryConsumeCharge(1);

            state.Advance(1f);

            Assert.IsTrue(consumed);
            Assert.AreEqual(1, state.Charge.Current);
            Assert.IsTrue(state.Charge.Recharge.IsRunning);
            Assert.AreEqual(1f, state.Charge.Recharge.RemainingTime, 0.0001f);
        }

        [Test]
        public void TEST_AbilityChargeState_시작콜백의소비와예외후에도남은Tick시간을충전()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.ConfigureCharge(3, 1f, startFull: false);
            var consumed = false;
            state.Charge.Recharge.OnStateChange += (_, change) =>
            {
                if (change.Current == TimerState.Run && !consumed)
                {
                    consumed = state.Control.TryConsumeCharge(1);
                    throw new InvalidOperationException("run listener failed");
                }
            };

            var failure = Assert.Throws<InvalidOperationException>(() => state.Advance(2.5f));

            Assert.AreEqual("run listener failed", failure.Message);
            Assert.IsTrue(consumed);
            Assert.AreEqual(1, state.Charge.Current);
            Assert.AreEqual(0.5f, state.Charge.Recharge.RemainingTime, 0.0001f);
        }

        [Test]
        public void TEST_AbilityChargeState_완료콜백의소비후남은Tick시간을이어서충전()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.ConfigureCharge(3, 1f, startFull: false);
            var consumed = false;
            state.Charge.Recharge.OnEnd += (_, _) =>
            {
                if (!consumed)
                {
                    consumed = state.Control.TryConsumeCharge(1);
                }
            };

            state.Advance(2.5f);

            Assert.IsTrue(consumed);
            Assert.AreEqual(1, state.Charge.Current);
            Assert.AreEqual(0.5f, state.Charge.Recharge.RemainingTime, 0.0001f);
        }

        [Test]
        public void TEST_AbilityChargeState_재시작알림실패에도남은Tick시간을적용()
        {
            var ability = CreateInstance("Skill");
            ability.State.Control.ConfigureCharge(3, 1f, startFull: false);
            var failures = 0;
            ability.State.Charge.Recharge.OnStateChange += (_, change) =>
            {
                if (change.Current == TimerState.Run && failures++ == 0)
                {
                    throw new InvalidOperationException("next recharge start failed");
                }
            };

            var error = Assert.Throws<InvalidOperationException>(() => ability.State.Advance(2.5f));

            Assert.AreEqual("next recharge start failed", error.Message);
            Assert.AreEqual(2, ability.State.Charge.Current);
            Assert.AreEqual(0.5f, ability.State.Charge.Recharge.RemainingTime, 0.0001f);
        }

        [Test]
        public void TEST_AbilityChargeState_큰Delta에서여러Charge를연속복구()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.Control.ConfigureCharge(3, 1f);

            Assert.IsTrue(state.Control.TryConsumeCharge(3));
            Assert.AreEqual(0, state.Charge.Current);

            var ended = 0;
            state.Charge.Recharge.OnEnd += (sender, args) => ended++;
            state.Advance(2.5f);

            Assert.AreEqual(2, state.Charge.Current);
            Assert.AreEqual(2, ended);
            Assert.AreEqual(0.5f, state.Charge.Recharge.RemainingTime, 0.0001f);

            var large = new AbilityState(new AbilityKey("Large"));
            large.Control.ConfigureCharge(1_000_001, 1f, startFull: false);
            large.Advance(1_000_000.5f);
            Assert.AreEqual(1_000_000, large.Charge.Current);
            Assert.AreEqual(0.5f, large.Charge.Recharge.RemainingTime, 0.0001f);
        }

        [Test]
        public void TEST_AbilityChargeState_구독유무가충전결과를변경하지않음()
        {
            var plain = new AbilityState(new AbilityKey("Plain"));
            var watched = new AbilityState(new AbilityKey("Watched"));
            plain.Control.ConfigureCharge(10, 0.1f, startFull: false);
            watched.Control.ConfigureCharge(10, 0.1f, startFull: false);

            var ended = 0;
            watched.Charge.Recharge.OnEnd += (sender, args) => ended++;

            plain.Advance(0.5f);
            watched.Advance(0.5f);

            Assert.AreEqual(4, plain.Charge.Current);
            Assert.AreEqual(plain.Charge.Current, watched.Charge.Current);
            Assert.AreEqual(ended, watched.Charge.Current);
            Assert.AreEqual
            (
                plain.Charge.Recharge.RemainingTime,
                watched.Charge.Recharge.RemainingTime,
                0.000001f
            );
        }

        [Test]
        public void TEST_AbilityChargeState_완료콜백의재구성에이전충전이섞이지않음()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.Control.ConfigureCharge(3, 1f, startFull: false);
            var oldRecharge = state.Charge.Recharge;
            oldRecharge.OnEnd += (sender, args) =>
            {
                state.Control.ConfigureCharge(5, 10f, startFull: false);
            };

            state.Advance(1f);

            Assert.AreEqual(5, state.Charge.Capacity);
            Assert.AreEqual(0, state.Charge.Current);
            Assert.AreNotSame(oldRecharge, state.Charge.Recharge);
            Assert.AreEqual(10f, state.Charge.Recharge.RemainingTime, 0.0001f);

            state.Advance(10f);
            Assert.AreEqual(1, state.Charge.Current);
        }

    #endregion

    #region C-2: ChargeState 복원 및 경계

        [Test]
        public void TEST_AbilityChargeState_진행중Restore는충전경과시간을유지()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.Control.ConfigureCharge(3, 1f);
            Assert.IsTrue(state.Control.TryConsumeCharge(3));

            state.Advance(0.4f);
            Assert.AreEqual(1, state.Control.RestoreCharge(1));
            Assert.AreEqual(1, state.Charge.Current);
            Assert.AreEqual(0.6f, state.Charge.Recharge.RemainingTime, 0.0001f);

            state.Advance(0.6f);
            Assert.AreEqual(2, state.Charge.Current);
            Assert.AreEqual(1f, state.Charge.Recharge.RemainingTime, 0.0001f);

            Assert.AreEqual(1, state.Control.RestoreCharge(2));
            Assert.IsTrue(state.Charge.IsFull);
            Assert.IsFalse(state.Charge.Recharge.IsRunning);
        }

        [Test]
        public void TEST_AbilityChargeState_최대정수용량에서도Restore가넘치지않음()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.Control.ConfigureCharge(int.MaxValue, 1f, startFull: false);

            Assert.AreEqual(int.MaxValue, state.Control.RestoreCharge(int.MaxValue));
            Assert.AreEqual(int.MaxValue, state.Charge.Current);
            Assert.IsTrue(state.Control.TryConsumeCharge(1));
            Assert.AreEqual(1, state.Control.RestoreCharge(int.MaxValue));
            Assert.AreEqual(int.MaxValue, state.Charge.Current);
        }

        [Test]
        public void TEST_AbilityChargeState_실패한초기구성은상태를생성하지않음()
        {
            var state = new AbilityState(new AbilityKey("Burst"));

            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => state.Control.ConfigureCharge(0, 1f)
            );
            Assert.IsNull(state.Charge);

            state.Control.ConfigureCharge(2, 1f);
            state.Control.TryConsumeCharge(1);

            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => state.Control.ConfigureCharge(3, float.NaN)
            );
            Assert.AreEqual(2, state.Charge.Capacity);
            Assert.AreEqual(1, state.Charge.Current);
        }

    #endregion

    }
}
