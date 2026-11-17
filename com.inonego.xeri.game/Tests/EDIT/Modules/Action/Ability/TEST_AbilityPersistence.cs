/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityPersistence.cs
수정일 : 2026-11-17

# 설명
AbilityState의 Unity JSON 복원(진행 중·일시정지 Cooldown, 부분 충전, Values)과 Binding 전환 중 Identity·독립 시간 진행 계약을 검증한다.
========================================================================= BLOCK_HEADER_END */

using UnityEngine;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Ability
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// Ability 상태의 저장과 복원 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityPersistence
    {

    #region 검증용 Definition

        private sealed class TestDefinition
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 테스트 정의의 이름을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public string Name { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// 이름으로 테스트 정의를 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public TestDefinition(string name)
            {
                Name = name;
            }
        }

    #endregion

    #region P-1: 지속 상태 직렬화와 복원

        // ------------------------------------------------------------
        /// <summary>
        /// Unity JSON 왕복 후 진행 중인 상태가 복원되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_UnityJson왕복은진행중인상태를복원()
        {
            var state = new AbilityState(new AbilityKey("PersistedSkill"));
            Assert.IsTrue(state.Control.TryBeginCooldown(5f));
            state.Control.ConfigureCharge(3, 1f, startFull: false);
            state.Values.SetI("FollowupStage", 2);
            state.Values.SetI("FollowupUses", 1);
            state.Values.SetI("Stack", 7);
            state.Values.SetS("Label", "saved");
            state.Values.SetF("Multiplier", 1.25f);
            state.Values.SetFlag("Empowered");
            state.Advance(0.4f);

            var json = JsonUtility.ToJson(state);
            var restored = JsonUtility.FromJson<AbilityState>(json);

            Assert.AreEqual("PersistedSkill", restored.Key.Value);
            Assert.AreEqual(7, restored.Values.GetI("Stack"));
            Assert.AreEqual("saved", restored.Values.GetS("Label"));
            Assert.AreEqual(1.25f, restored.Values.GetF("Multiplier"), 0.0001f);
            Assert.IsTrue(restored.Values.HasFlag("Empowered"));
            Assert.AreEqual(4.6f, restored.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(0, restored.Charge.Current);
            Assert.AreEqual(0.6f, restored.Charge.Recharge.RemainingTime, 0.0001f);
            Assert.AreEqual(2, restored.Values.GetI("FollowupStage"));
            Assert.AreEqual(1, restored.Values.GetI("FollowupUses"));

            restored.Advance(0.6f);

            Assert.AreEqual(1, restored.Charge.Current);
            Assert.AreEqual(1f, restored.Charge.Recharge.RemainingTime, 0.0001f);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 저장과 복원 후 진행 중인 Cooldown, 부분 충전, Values가 유지되는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test, Category("X12")]
        public void TEST_AbilityState_X12_저장복원은진행중Cooldown과부분충전과Values를유지()
        {
            var state = new AbilityState(new AbilityKey("SavedSkill"));
            Assert.IsTrue(state.Control.TryBeginCooldown(5f));
            state.Control.ConfigureCharge(3, 2f, startFull: false);
            state.Values.SetI("Stack", 3);
            state.Values.SetS("Label", "saved");
            state.Advance(1.5f);
            var revision = state.Revision;

            var restored = JsonUtility.FromJson<AbilityState>(JsonUtility.ToJson(state));

            // 저장은 Revision을 바꾸지 않고 변경 번호는 저장하지 않는다. 복원은 변경 1회로 센다.
            Assert.AreEqual(revision, state.Revision);
            Assert.AreEqual(1UL, restored.Revision);
            Assert.IsTrue(restored.Cooldown.IsRunning);
            Assert.AreEqual(3.5f, restored.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(0, restored.Charge.Current);
            Assert.IsTrue(restored.Charge.Recharge.IsRunning);
            Assert.AreEqual(0.5f, restored.Charge.Recharge.RemainingTime, 0.0001f);
            Assert.AreEqual(3, restored.Values.GetI("Stack"));
            Assert.AreEqual("saved", restored.Values.GetS("Label"));

            restored.Advance(0.5f);

            Assert.AreEqual(1, restored.Charge.Current);
            Assert.AreEqual(3f, restored.Cooldown.RemainingTime, 0.0001f);

            // 복원한 상태의 Values 변경도 Revision에 반영한다.
            restored.Values.SetI("Stack", 4);
            Assert.AreEqual(2UL, restored.Revision);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 일시정지한 Cooldown도 저장과 복원 후 남은 시간을 유지하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test, Category("X12")]
        public void TEST_AbilityState_X12_일시정지한Cooldown도저장복원후남은시간을유지()
        {
            var state = new AbilityState(new AbilityKey("SavedSkill"));
            Assert.IsTrue(state.Control.TryBeginCooldown(5f));
            state.Advance(2f);
            Assert.IsTrue(state.Control.PauseCooldown());

            var restored = JsonUtility.FromJson<AbilityState>(JsonUtility.ToJson(state));

            Assert.IsTrue(restored.Cooldown.IsPaused);
            Assert.IsFalse(restored.IsCooldownReady);
            Assert.AreEqual(3f, restored.Cooldown.RemainingTime, 0.0001f);

            restored.Advance(10f);
            Assert.AreEqual(3f, restored.Cooldown.RemainingTime, 0.0001f);

            Assert.IsTrue(restored.Control.ResumeCooldown());
            restored.Advance(1f);
            Assert.AreEqual(2f, restored.Cooldown.RemainingTime, 0.0001f);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 충전 완료 콜백에서 JSON 복원 후 다음 충전이 이어지는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_충전완료콜백에서JSON복원은다음충전을이어감()
        {
            var state = new AbilityState(new AbilityKey("ChargeSave"));
            state.Control.ConfigureCharge(3, 1f, startFull: false);
            AbilityState restored = null;
            state.Charge.Recharge.OnEnd += (_, _) =>
            {
                restored = JsonUtility.FromJson<AbilityState>(JsonUtility.ToJson(state));
            };

            state.Advance(1f);

            Assert.IsNotNull(restored);
            Assert.AreEqual(1, restored.Charge.Current);
            Assert.IsTrue(restored.Charge.Recharge.IsRunning);
            Assert.AreEqual(1f, restored.Charge.Recharge.RemainingTime, 0.0001f);
        }

    #endregion

    #region B-1: Binding identity 전환

        // ----------------------------------------------------------------------
        /// <summary>
        /// Override 중 독립 시간 진행 후 A의 Identity와 State가 복원되는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityBindings_Override중독립시간진행후A의Identity와State를복원()
        {
            var entry = new AbilityEntryKey("Skill");
            var original = CreateInstance("A");
            var replacement = CreateInstance("B");
            original.State.Values.SetI("Stack", 4);
            original.State.Control.ConfigureCharge(2, 1f, startFull: false);
            replacement.State.Control.ConfigureCharge(3, 2f, startFull: false);
            Assert.IsTrue(original.State.Control.TryBeginCooldown(5f));
            Assert.IsTrue(replacement.State.Control.TryBeginCooldown(9f));

            var baseLayer = new AbilityBindingLayer<TestDefinition>(0);
            baseLayer.Set(entry, original);
            var bindings = new AbilityBindings<TestDefinition>();
            using var baseLease = bindings.AcquireLayer(baseLayer);

            Assert.IsTrue(bindings.TryResolve(entry, out var first));
            Assert.AreSame(original, first.Instance);

            using (bindings.AcquireOverride(entry, replacement, 100))
            {
                Assert.IsTrue(bindings.TryResolve(entry, out var second));
                Assert.AreSame(replacement, second.Instance);

                // Binding 교체는 상태 시간을 이관하지 않는다. 각 Owner의 Tick만 진행한다.
                original.State.Advance(1f);
                replacement.State.Advance(0.5f);

                Assert.AreEqual(1, original.State.Charge.Current);
                Assert.AreEqual(0, replacement.State.Charge.Current);
            }

            Assert.IsTrue(bindings.TryResolve(entry, out var returned));
            Assert.AreSame(original, returned.Instance);
            Assert.AreEqual(4, returned.Instance.State.Values.GetI("Stack"));
            Assert.AreEqual(4f, returned.Instance.State.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(1, returned.Instance.State.Charge.Current);
            Assert.AreEqual(1f, returned.Instance.State.Charge.Recharge.RemainingTime, 0.0001f);
            Assert.AreEqual(8.5f, replacement.State.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(0, replacement.State.Charge.Current);
            Assert.AreEqual(1.5f, replacement.State.Charge.Recharge.RemainingTime, 0.0001f);
        }

        private static AbilityInstance<TestDefinition> CreateInstance(string key)
        {
            return new AbilityInstance<TestDefinition>
            (
                new TestDefinition(key),
                new AbilityState(new AbilityKey(key))
            );
        }

    #endregion

    }
}
