/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityKeys.cs
수정일 : 2026-11-19

# 설명
Ability Key·Entry Key의 Unity 직렬화 계약과 정규화, Binding과 Instance의 식별자 보존을 검증한다.

# 테스트 구성
 I: Ability Key·Entry 직렬화와 진입점 구분
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
    using Xeri.Game.TEST;
    using Xeri.Game.TEST._Ability;
    using Xeri.Game.TEST._Ability.Fixture;

    // ============================================================
    /// <summary>
    /// Ability 식별자의 직렬화와 진입점 구분 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityKeys : AbilityTestBase
    {

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
            var binding = new AbilityBinding<TestSpec>
            (
                new AbilityEntryKey("Skill"),
                ability
            );
            var context = new AbilityActivationContext<TestSpec>
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

        // ------------------------------------------------------------
        /// <summary>
        /// Key가 없는 State는 AbilityInstance에 연결할 수 없는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityInstance_Key가없는State는연결을거부()
        {
            var spec = new TestSpec("Skill");

            Assert.Throws<ArgumentException>(() => new AbilityInstance<TestSpec>(spec, new AbilityState()));
            Assert.DoesNotThrow(() => new AbilityInstance<TestSpec>(spec, new AbilityState(new AbilityKey("Skill"))));
        }

    #endregion

    }
}
