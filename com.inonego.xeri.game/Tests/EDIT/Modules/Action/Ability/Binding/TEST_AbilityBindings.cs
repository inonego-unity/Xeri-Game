/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityBindings.cs
수정일 : 2026-11-19

# 설명
Binding 계층의 우선순위, 해제, 수집과 Revision 계약, Override 중 원래 Ability의 Identity·State 보존을 검증한다.

# 테스트 구성
 B: Binding 우선순위·해제·Revision·Override 중 State 보존
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

    // ======================================================================
    /// <summary>
    /// Binding 계층의 우선순위와 해제, Revision 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ======================================================================
    public sealed class TEST_AbilityBindings : AbilityTestBase
    {

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
            var bindings = new AbilityBindings<TestSpec>();
            var baseLayer = new AbilityBindingLayer<TestSpec>(0);
            baseLayer.Set(attack, baseAttack);
            baseLayer.Set(skill, baseSkill);
            var formLayer = new AbilityBindingLayer<TestSpec>(100);
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
            var first = new AbilityBindingLayer<TestSpec>(10);
            var second = new AbilityBindingLayer<TestSpec>(10);
            var firstAbility = CreateInstance("First");
            var secondAbility = CreateInstance("Second");
            first.Set(entry, firstAbility);
            second.Set(entry, secondAbility);
            var bindings = new AbilityBindings<TestSpec>();

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

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 전체 해석이 상위 Layer의 같은 Entry 교체와 새 Entry 추가를 반영하는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityBindings_전체해석이상위Layer의교체와추가Entry를반영()
        {
            var attack = new AbilityEntryKey("Attack");
            var skill = new AbilityEntryKey("Skill");
            var utility = new AbilityEntryKey("Utility");
            var baseAttack = CreateInstance("BaseAttack");
            var baseSkill = CreateInstance("BaseSkill");
            var overrideSkill = CreateInstance("OverrideSkill");
            var addedUtility = CreateInstance("AddedUtility");
            var bindings = new AbilityBindings<TestSpec>();
            var baseLayer = new AbilityBindingLayer<TestSpec>(0);
            var overrideLayer = new AbilityBindingLayer<TestSpec>(100);
            var resolved = new List<AbilityBinding<TestSpec>>();

            baseLayer.Set(skill, baseSkill);
            baseLayer.Set(attack, baseAttack);
            overrideLayer.Set(skill, overrideSkill);
            overrideLayer.Set(utility, addedUtility);

            using var baseLease = bindings.AcquireLayer(baseLayer);
            using var overrideLease = bindings.AcquireLayer(overrideLayer);

            resolved.Add(new AbilityBinding<TestSpec>(new AbilityEntryKey("Stale"), CreateInstance("Stale")));
            bindings.ResolveAll(resolved);

            Assert.AreEqual(3, resolved.Count);
            Assert.AreEqual("Attack", resolved[0].EntryKey.Value);
            Assert.AreSame(baseAttack, resolved[0].Instance);
            Assert.AreEqual("Skill", resolved[1].EntryKey.Value);
            Assert.AreSame(overrideSkill, resolved[1].Instance);
            Assert.AreEqual("Utility", resolved[2].EntryKey.Value);
            Assert.AreSame(addedUtility, resolved[2].Instance);

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
        [Test]
        public void TEST_AbilityBindings_Form재정의와해제는상태를독립보존하고Revision을올림()
        {
            var entry = new AbilityEntryKey("Skill");
            var attack = new AbilityEntryKey("Attack");
            var baseSkill = CreateInstance("BaseSkill");
            var formSkill = CreateInstance("FormSkill");
            baseSkill.State.TryBeginCooldown(5f);
            formSkill.State.ConfigureCharge(2, 1f);
            var bindings = new AbilityBindings<TestSpec>();
            var baseLayer = new AbilityBindingLayer<TestSpec>(0);
            baseLayer.Set(entry, baseSkill);
            Assert.AreEqual(0UL, bindings.Revision);

            using var baseLease = bindings.AcquireLayer(baseLayer);
            Assert.AreEqual(1UL, bindings.Revision);

            var formLayer = new AbilityBindingLayer<TestSpec>(100);
            formLayer.Set(entry, formSkill);
            var formLease = bindings.AcquireLayer(formLayer);
            Assert.AreEqual(2UL, bindings.Revision);
            Assert.IsTrue(bindings.TryResolve(entry, out var overridden));
            Assert.AreSame(formSkill, overridden.Instance);

            // Form 중에도 두 상태는 서로의 시간과 변경 번호에 영향을 주지 않는다.
            var baseRevision = baseSkill.State.Revision;
            formSkill.State.TryConsumeCharge(1);
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

    #region B-2: Override 중 독립 시간 진행

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
            original.State.ConfigureCharge(2, 1f, startFull: false);
            replacement.State.ConfigureCharge(3, 2f, startFull: false);
            Assert.IsTrue(original.State.TryBeginCooldown(5f));
            Assert.IsTrue(replacement.State.TryBeginCooldown(9f));

            var baseLayer = new AbilityBindingLayer<TestSpec>(0);
            baseLayer.Set(entry, original);
            var bindings = new AbilityBindings<TestSpec>();
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

    #endregion

    }
}
