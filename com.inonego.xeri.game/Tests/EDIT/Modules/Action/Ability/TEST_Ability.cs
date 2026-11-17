/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_Ability.cs
수정일 : 2026-11-17

# 설명
Ability의 Owner별 지속 상태, Binding, Cooldown과 Charge 충전·복원 계약을 검증한다.
사용 선택·Cost 평가·소비 테스트는 TEST_AbilityActivation.cs에서 별도 관리한다.
Revision·Cooldown 일시정지·직렬화 복원과 Binding 변경 번호 계약을 함께 검증한다.

# 테스트 구성
 I: Ability Key·Entry 직렬화
 S: Owner 상태·제어·시간·Revision·직렬화 복원
 B: Binding 우선순위·해제·Revision
 O: 공유 Owner와 Instigator 독립성·벤치 Owner
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

    // ================================================================================
    /// <summary>
    /// Ability의 Owner별 지속 상태와 Binding, Cooldown, Charge 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ================================================================================
    public sealed class TEST_Ability
    {

    #region 검증용 정의와 요청 타입

        private sealed class TestDefinition
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 테스트용 Ability 정의의 이름을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public string Name { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// 이름으로 테스트용 Ability 정의를 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public TestDefinition(string name)
            {
                Name = name;
            }
        }

        private readonly struct TestPayload
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 테스트용 요청 맥락의 버전을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int Version { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// 버전으로 테스트용 요청 맥락을 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public TestPayload(int version)
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

        private static AbilityActivationContext<TestDefinition> CreateContext
        (
            AbilityEntryKey entryKey,
            AbilityInstance<TestDefinition> ability,
            object owner = null,
            object instigator = null
        )
        {
            var binding = new AbilityBinding<TestDefinition>(entryKey, ability);
            return new AbilityActivationContext<TestDefinition>
            (
                owner,
                instigator,
                binding,
                new TestPayload(1)
            );
        }

    #endregion

    #region I-1: 식별자 직렬화와 진입점 구분

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> AbilityKey와 AbilityEntryKey의 Unity 직렬화 backing field 계약을
        /// <br/> 보존하는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
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

        // ----------------------------------------------------------------------
        /// <summary>
        /// Binding이 AbilityKey와 EntryKey를 서로 독립적으로 보존하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityBinding은_AbilityKey와EntryKey를독립보존()
        {
            var ability = CreateInstance("Fireball");
            var binding = new AbilityBinding<TestDefinition>
            (
                new AbilityEntryKey("Skill"),
                ability
            );
            var context = new AbilityActivationContext<TestDefinition>
            (
                null,
                null,
                binding,
                new TestPayload(1)
            );

            Assert.AreEqual("Fireball", ability.Key.Value);
            Assert.AreEqual("Skill", binding.EntryKey.Value);
            Assert.AreEqual("Fireball", context.Ability.Key.Value);
            Assert.AreEqual("Skill", context.EntryKey.Value);
        }

    #endregion

    #region S-1: 소유자별 상태와 시간 독립성

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 같은 Definition을 가진 Instance가 Owner별 State를 독립적으로 보존하는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// Cooldown의 시작, 감소, 초기화 제어를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 음수 시간은 무시하고 비정상 시간은 거부하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// Cooldown 종료 콜백에서 외부 사건이 후속 단계를 여는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// Cooldown 종료 시 Values 변경이 Charge와 독립적인지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

    #region S-3: Revision과 Cooldown 일시정지

        // ------------------------------------------------------------
        /// <summary>
        /// 실제 변경마다 Revision이 한 번씩 증가하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateControl_실제변경마다Revision이한번씩증가()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            Assert.AreEqual(0UL, state.Revision);

            Assert.IsTrue(state.Control.TryBeginCooldown(5f));
            Assert.AreEqual(1UL, state.Revision);
            Assert.IsFalse(state.Control.TryBeginCooldown(5f));
            Assert.AreEqual(1UL, state.Revision);

            Assert.IsTrue(state.Control.ReduceCooldown(1f));
            Assert.AreEqual(2UL, state.Revision);
            Assert.IsFalse(state.Control.ReduceCooldown(0f));
            Assert.AreEqual(2UL, state.Revision);

            Assert.IsTrue(state.Control.PauseCooldown());
            Assert.AreEqual(3UL, state.Revision);
            Assert.IsFalse(state.Control.PauseCooldown());
            Assert.IsTrue(state.Control.ResumeCooldown());
            Assert.AreEqual(4UL, state.Revision);
            Assert.IsFalse(state.Control.ResumeCooldown());

            Assert.IsTrue(state.Control.ResetCooldown());
            Assert.AreEqual(5UL, state.Revision);
            Assert.IsFalse(state.Control.ResetCooldown());
            Assert.AreEqual(5UL, state.Revision);

            // 지속시간 0은 시작할 상태가 없으므로 성공해도 변경이 아니다.
            Assert.IsTrue(state.Control.TryBeginCooldown(0f));
            Assert.AreEqual(5UL, state.Revision);

            state.Control.ConfigureCharge(2, 1f);
            Assert.AreEqual(6UL, state.Revision);
            Assert.IsTrue(state.Control.TryConsumeCharge(1));
            Assert.AreEqual(7UL, state.Revision);
            Assert.IsFalse(state.Control.TryConsumeCharge(2));
            Assert.AreEqual(7UL, state.Revision);
            Assert.AreEqual(1, state.Control.RestoreCharge(1));
            Assert.AreEqual(8UL, state.Revision);
            Assert.AreEqual(0, state.Control.RestoreCharge(1));
            Assert.AreEqual(8UL, state.Revision);

            state.Control.ConfigureCharge(3, 1f);
            Assert.AreEqual(9UL, state.Revision);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Values의 실제 변경만 Revision에 한 번씩 반영되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_Values의실제변경만Revision에한번씩반영()
        {
            var state = new AbilityState(new AbilityKey("Skill"));

            // Values를 여러 번 읽어도 구독이 중복되지 않는다.
            var first = state.Values;
            var second = state.Values;
            Assert.AreSame(first, second);

            first.SetI("Stack", 1);
            Assert.AreEqual(1UL, state.Revision);
            second.SetI("Stack", 1);
            Assert.AreEqual(1UL, state.Revision);
            state.Values.SetS("Label", "saved");
            Assert.AreEqual(2UL, state.Revision);
            Assert.IsTrue(state.Values.Remove("Stack"));
            Assert.AreEqual(3UL, state.Revision);
            Assert.IsFalse(state.Values.Remove("Stack"));
            Assert.AreEqual(3UL, state.Revision);
            state.Values.Clear();
            Assert.AreEqual(4UL, state.Revision);
            state.Values.Clear();
            Assert.AreEqual(4UL, state.Revision);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Advance는 Revision을 바꾸지 않고 소비와 복원만 이산 변경으로 세는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test, Category("N15")]
        public void TEST_AbilityState_N15_Advance는Revision을바꾸지않고소비복원만이산변경으로센다()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.ConfigureCharge(3, 0.5f, startFull: false);
            Assert.IsTrue(state.Control.TryBeginCooldown(5f));
            var completed = 0;
            state.Charge.Recharge.OnEnd += (_, _) => completed++;
            var revision = state.Revision;

            for (var tick = 0; tick < 100; tick++)
            {
                state.Advance(0.1f);
            }

            // 충전 완료와 Cooldown 종료를 포함해도 시간 진행은 변경 번호를 올리지 않는다.
            Assert.AreEqual(3, completed);
            Assert.AreEqual(3, state.Charge.Current);
            Assert.IsTrue(state.IsCooldownReady);
            Assert.AreEqual(revision, state.Revision);

            Assert.IsTrue(state.Control.TryConsumeCharge(1));
            Assert.AreEqual(revision + 1, state.Revision);

            // 같은 Tick의 소비와 복원은 결과 수량이 같아도 각각 이산 변경이다.
            state.Advance(0.2f);
            var remaining = state.Charge.Recharge.RemainingTime;
            var current = state.Charge.Current;
            revision = state.Revision;

            Assert.IsTrue(state.Control.TryConsumeCharge(1));
            Assert.AreEqual(1, state.Control.RestoreCharge(1));

            Assert.AreEqual(revision + 2, state.Revision);
            Assert.AreEqual(current, state.Charge.Current);
            Assert.AreEqual(remaining, state.Charge.Recharge.RemainingTime, 0.0001f);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Pause로 진행을 멈추고 Resume으로 재개하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateControl_Pause는진행을멈추고Resume으로재개()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            Assert.IsFalse(state.Control.PauseCooldown());
            Assert.IsFalse(state.Control.ResumeCooldown());
            Assert.AreEqual(0UL, state.Revision);

            Assert.IsTrue(state.Control.TryBeginCooldown(5f));
            state.Advance(1f);
            Assert.IsTrue(state.Control.PauseCooldown());
            Assert.IsTrue(state.Cooldown.IsPaused);
            Assert.IsFalse(state.IsCooldownReady);

            state.Advance(10f);
            Assert.AreEqual(4f, state.Cooldown.RemainingTime, 0.0001f);

            Assert.IsTrue(state.Control.ResumeCooldown());
            state.Advance(1f);
            Assert.AreEqual(3f, state.Cooldown.RemainingTime, 0.0001f);
        }

    #endregion

    #region S-4: 직렬화 복원과 사용 구간

        private static AbilityState CreateRestoreSource(string key = "Skill")
        {
            var source = new AbilityState(new AbilityKey(key));
            source.Control.TryBeginCooldown(5f);
            source.Control.ConfigureCharge(3, 1f, startFull: false);
            source.Values.SetI("Stack", 7);
            source.Values.SetS("Label", "saved");
            source.Advance(0.4f);
            return source;
        }

        private static void Restore(AbilityState source, AbilityState target)
        {
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), target);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// FromJsonOverwrite 복원이 내용을 덮어쓰고 Revision을 정확히 한 번 올리는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_직렬화복원은내용을덮어쓰고Revision을한번올림()
        {
            var target = new AbilityState(new AbilityKey("Skill"));
            target.Values.SetI("Old", 1);
            target.Control.ConfigureCharge(2, 1f);
            var revision = target.Revision;
            var source = CreateRestoreSource();
            var sourceRevision = source.Revision;

            Restore(source, target);

            Assert.AreEqual(revision + 1, target.Revision);
            Assert.AreEqual(4.6f, target.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(0, target.Charge.Current);
            Assert.AreEqual(7, target.Values.GetI("Stack"));
            Assert.AreEqual("saved", target.Values.GetS("Label"));
            Assert.AreEqual(sourceRevision, source.Revision);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 복원 뒤에도 Values 변경이 Revision에 계속 반영되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_직렬화복원뒤에도Values변경이Revision을올림()
        {
            var target = new AbilityState(new AbilityKey("Skill"));
            target.Values.SetI("Old", 1);
            var source = CreateRestoreSource();

            Restore(source, target);
            var restored = target.Revision;

            target.Values.SetI("Stack", 9);
            Assert.AreEqual(restored + 1, target.Revision);
            Assert.AreEqual(7, source.Values.GetI("Stack"));

            // 두 번째 복원도 구독을 중복하지 않고 한 번만 센다.
            Restore(source, target);
            restored = target.Revision;
            target.Values.SetI("Stack", 10);
            Assert.AreEqual(restored + 1, target.Revision);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 진행 중인 Cooldown과 Charge 충전 진행이 복원 뒤에도 남은 시간으로 이어지는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_직렬화복원은진행중Cooldown과Charge충전을이어감()
        {
            var source = CreateRestoreSource();
            var target = new AbilityState(new AbilityKey("Skill"));

            Restore(source, target);

            Assert.IsTrue(target.Cooldown.IsRunning);
            Assert.AreEqual(4.6f, target.Cooldown.RemainingTime, 0.0001f);
            Assert.IsTrue(target.Charge.Recharge.IsRunning);
            Assert.AreEqual(0.6f, target.Charge.Recharge.RemainingTime, 0.0001f);

            // 지연 보정은 복원 뒤 Advance로 처리한다.
            target.Advance(0.6f);
            Assert.AreEqual(4f, target.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(1, target.Charge.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 사용 구간은 중첩을 거부하고 Advance를 막지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_사용구간은중첩을거부하고Advance를막지않음()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.TryBeginCooldown(2f);

            state.EnterUse();
            Assert.Throws<InvalidOperationException>(() => state.EnterUse());
            state.Advance(1f);
            Assert.AreEqual(1f, state.Cooldown.RemainingTime, 0.0001f);

            state.ExitUse();
            state.EnterUse();
            state.ExitUse();
        }

    #endregion

    #region B-1: 바인딩 우선순위·해제와 수집

        // ----------------------------------------------------------------------
        /// <summary>
        /// Priority와 Release에 따라 PartialOverride가 해석되는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 Priority에서는 최근 Layer가 우선하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 전체 해석이 Override와 Grant를 반영하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ----------------------------------------------------------------------
        /// <summary>
        /// Form 재정의와 해제가 상태를 독립 보존하고 Revision을 올리는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test, Category("X02")]
        public void TEST_AbilityBindings_X02_Form재정의와해제는상태를독립보존하고Revision을올림()
        {
            var entry = new AbilityEntryKey("Skill");
            var attack = new AbilityEntryKey("Attack");
            var baseSkill = CreateInstance("BaseSkill");
            var formSkill = CreateInstance("FormSkill");
            baseSkill.State.Control.TryBeginCooldown(5f);
            formSkill.State.Control.ConfigureCharge(2, 1f);
            var bindings = new AbilityBindings<TestDefinition>();
            var baseLayer = new AbilityBindingLayer<TestDefinition>(0);
            baseLayer.Set(entry, baseSkill);
            Assert.AreEqual(0UL, bindings.Revision);

            using var baseLease = bindings.AcquireLayer(baseLayer);
            Assert.AreEqual(1UL, bindings.Revision);

            var formLayer = new AbilityBindingLayer<TestDefinition>(100);
            formLayer.Set(entry, formSkill);
            var formLease = bindings.AcquireLayer(formLayer);
            Assert.AreEqual(2UL, bindings.Revision);
            Assert.IsTrue(bindings.TryResolve(entry, out var overridden));
            Assert.AreSame(formSkill, overridden.Instance);

            // Form 중에도 두 상태는 서로의 시간과 변경 번호에 영향을 주지 않는다.
            var baseRevision = baseSkill.State.Revision;
            formSkill.State.Control.TryConsumeCharge(1);
            baseSkill.State.Advance(1f);
            Assert.AreEqual(baseRevision, baseSkill.State.Revision);
            Assert.AreEqual(4f, baseSkill.State.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(1, formSkill.State.Charge.Current);

            // 등록된 Layer의 실제 Set·Remove만 Binding 변경 번호를 올린다.
            formLayer.Set(attack, formSkill);
            Assert.AreEqual(3UL, bindings.Revision);
            formLayer.Set(attack, formSkill);
            Assert.AreEqual(3UL, bindings.Revision);
            Assert.IsTrue(formLayer.Remove(attack));
            Assert.AreEqual(4UL, bindings.Revision);
            Assert.IsFalse(formLayer.Remove(attack));
            Assert.AreEqual(4UL, bindings.Revision);

            formLease.Release();
            Assert.AreEqual(5UL, bindings.Revision);
            formLease.Release();
            Assert.AreEqual(5UL, bindings.Revision);
            Assert.IsTrue(bindings.TryResolve(entry, out var restored));
            Assert.AreSame(baseSkill, restored.Instance);
            Assert.AreEqual(4f, restored.Instance.State.Cooldown.RemainingTime, 0.0001f);

            // 해제된 Layer의 변경은 더 이상 반영하지 않는다.
            formLayer.Set(attack, formSkill);
            Assert.AreEqual(5UL, bindings.Revision);
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                requirement: new IAbilityActivationRequirement<TestDefinition>[]
                {
                    new AbilityCooldownRequirement<TestDefinition>()
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

        // ----------------------------------------------------------------------
        /// <summary>
        /// 벤치 Owner가 자기 상태만 진행하고 다른 Owner 사용의 영향을 받지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test, Category("X11")]
        public void TEST_AbilityInstance_X11_벤치Owner는자기상태만진행하고다른Owner의사용에영향받지않음()
        {
            var entry = new AbilityEntryKey("Skill");
            var bench = CreateInstance("Skill");
            var active = CreateInstance("Skill");
            bench.State.Control.ConfigureCharge(2, 1f);
            active.State.Control.ConfigureCharge(2, 1f);
            Assert.IsTrue(bench.State.Control.TryBeginCooldown(4f));

            var bindings = new AbilityBindings<TestDefinition>();
            var baseLayer = new AbilityBindingLayer<TestDefinition>(0);
            var formLayer = new AbilityBindingLayer<TestDefinition>(100);
            baseLayer.Set(entry, bench);
            formLayer.Set(entry, active);
            using var baseLease = bindings.AcquireLayer(baseLayer);
            using var formLease = bindings.AcquireLayer(formLayer);
            Assert.IsTrue(bindings.TryResolve(entry, out var resolved));
            Assert.AreSame(active, resolved.Instance);

            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                requirement: new IAbilityActivationRequirement<TestDefinition>[]
                {
                    new AbilityCooldownRequirement<TestDefinition>()
                },
                cost: new IAbilityActivationCost<TestDefinition>[]
                {
                    new AbilityChargeCost<TestDefinition>()
                }
            );
            var benchRevision = bench.State.Revision;

            Assert.IsTrue(policy.TryActivate(CreateContext(entry, resolved.Instance), out _));
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

    #region C-1: 충전 시간과 완료 콜백

        // ------------------------------------------------------------
        /// <summary>
        /// Charge가 Cooldown과 독립적으로 순차 충전되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 충전 완료 알림에서 예외가 나도 충전 시계가 계속 진행되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 쿨다운 알림이 실패해도 Charge와 Owner 값이 유지되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 완료 알림과 다음 주기 시작 오류를 모두 보존하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 완료 이벤트에서 즉시 충전분을 사용해도 완료분이 보존되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 완료 알림이 실패해도 남은 Tick 시간을 적용하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 쿨다운 콜백의 Charge 소비가 기존 충전 Tick을 막지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ----------------------------------------------------------------------
        /// <summary>
        /// 쿨다운 콜백이 새로 시작한 충전에 기존 Tick을 적용하지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
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

        // ----------------------------------------------------------------------
        /// <summary>
        /// 시작 콜백의 소비와 예외 후에도 남은 Tick 시간으로 충전하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 완료 콜백의 소비 후 남은 Tick 시간을 이어서 충전하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 재시작 알림이 실패해도 남은 Tick 시간을 적용하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 큰 Delta에서 여러 Charge를 연속으로 복구하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 알림 구독 유무가 충전 결과를 바꾸지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 완료 콜백의 재구성에서 이전 충전이 섞이지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 중 Restore가 충전 경과 시간을 유지하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 최대 정수 용량에서도 Restore가 넘치지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 실패한 초기 구성이 상태를 생성하지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
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
