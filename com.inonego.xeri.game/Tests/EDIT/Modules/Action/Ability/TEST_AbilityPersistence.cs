/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityPersistence.cs
수정일 : 2026-11-12

# 설명
AbilityState의 Unity JSON 복원과 Binding 전환 중 Identity·독립 시간 진행 계약을 검증한다.
========================================================================= BLOCK_HEADER_END */

using UnityEngine;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Ability
{
    using Xeri;
    using Xeri.Game;

    public sealed class TEST_AbilityPersistence
    {

    #region 검증용 Definition

        private sealed class TestDefinition
        {
            public string Name { get; }

            public TestDefinition(string name)
            {
                Name = name;
            }
        }

    #endregion

    #region 지속 상태 직렬화와 복원

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

    #region Binding identity 전환

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
