/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityChargeCost.cs
수정일 : 2026-11-19

# 설명
Charge 비용의 무변경 검사, 소비, 재검사의 사후 상태를 검증한다.
Charge 비용의 소비와 충전 시계 관리를 각각 검증한다.
지불 영수증의 환급은 TEST_AbilityChargeState.cs에서 검증한다.

# 테스트 구성
 C: Charge 검사와 실제 소비
 E: 외부 변경과 소비 예외
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

    // ============================================================
    /// <summary>
    /// 단일 Charge 비용을 검사하고 소비하는 최소 계약의 테스트.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityChargeCost
    {

    #region 검증용 Context

        private sealed class TestSpec
        {
            // NONE
        }

        private static AbilityInstance<TestSpec> CreateAbility(int capacity = 2)
        {
            var ability = new AbilityInstance<TestSpec>
            (
                new TestSpec(),
                new AbilityState(new AbilityKey("Burst"))
            );
            ability.State.Control.ConfigureCharge(capacity, 1f);
            return ability;
        }

        private static AbilityActivationContext<TestSpec> CreateContext
        (
            AbilityInstance<TestSpec> ability
        )
        {
            return new AbilityActivationContext<TestSpec>
            (
                null, null,
                new AbilityBinding<TestSpec>(new AbilityEntryKey("Q"), ability),
                default
            );
        }

    #endregion

    #region C-1: Charge 검사와 소비

        // ------------------------------------------------------------
        /// <summary>
        /// 비용 검사 단계에서 Charge를 감소시키지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeCost_검사는Charge수량을변경하지않음()
        {
            var ability = CreateAbility();
            var context = CreateContext(ability);
            var cost = new AbilityChargeCost<TestSpec>();

            Assert.IsTrue(cost.Evaluate(context).IsAllowed);
            Assert.IsTrue(cost.Evaluate(context).IsAllowed);
            Assert.AreEqual(2, ability.State.Charge.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Charge가 부족하면 비용을 소비하지 않고 거부하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeCost_부족한Charge는소비하지않음()
        {
            var ability = CreateAbility(1);
            var context = CreateContext(ability);
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new AbilityChargeCost<TestSpec>(2),
                }
            );

            Assert.IsFalse(policy.TryActivate(context, null, new List<AbilityCostReceipt>(), out var rejected));
            Assert.AreEqual(AbilityActivationFailure.CostRejected, rejected.Failure);
            Assert.AreEqual(1, ability.State.Charge.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 요청 수량만큼 Charge를 한 번 소비하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeCost_승인된수량을소비()
        {
            var ability = CreateAbility(3);
            var context = CreateContext(ability);
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new AbilityChargeCost<TestSpec>(2),
                }
            );

            Assert.IsTrue(policy.TryActivate(context, null, new List<AbilityCostReceipt>(), out var result));
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(1, ability.State.Charge.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 Charge의 두 비용도 먼저 확인하고 순차 소비하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeCost_동일Charge두비용을연속소비()
        {
            var ability = CreateAbility(2);
            var context = CreateContext(ability);
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new IAbilityActivationCost<TestSpec>[]
                {
                    new AbilityChargeCost<TestSpec>(),
                    new AbilityChargeCost<TestSpec>(),
                }
            );

            Assert.IsTrue(policy.TryActivate(context, null, new List<AbilityCostReceipt>(), out _));
            Assert.AreEqual(0, ability.State.Charge.Current);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 동일 Charge의 개별 검사는 합산 검사가 아니므로 소비 도중 실패할 수 있고,
        /// <br/> 그때 정책이 앞서 소비한 Charge를 환급하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeCost_동일Charge비용의합계부족은후속소비에서실패하고앞선소비를환급()
        {
            var ability = CreateAbility(1);
            var context = CreateContext(ability);
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new IAbilityActivationCost<TestSpec>[]
                {
                    new AbilityChargeCost<TestSpec>(),
                    new AbilityChargeCost<TestSpec>(),
                }
            );

            Assert.Throws<InvalidOperationException>(() => policy.TryActivate(context, null, new List<AbilityCostReceipt>(), out _));
            Assert.AreEqual(1, ability.State.Charge.Current);
        }

    #endregion

    #region E-1: 외부 변경

        // ------------------------------------------------------------
        /// <summary>
        /// 검사를 마친 후 잔량이 부족해지면 소비 명령이 실패하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeCost_검사후외부소비로부족하면Pay에서예외()
        {
            var ability = CreateAbility(1);
            var context = CreateContext(ability);
            var cost = new AbilityChargeCost<TestSpec>();
            Assert.IsTrue(cost.Evaluate(context).IsAllowed);
            Assert.IsTrue(ability.State.Control.TryConsumeCharge(1));

            Assert.Throws<InvalidOperationException>(() => cost.Pay(context));
            Assert.AreEqual(0, ability.State.Charge.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 비용 생성에 유효하지 않은 소비량을 허용하지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeCost_0이하소비량을거부()
        {
            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => new AbilityChargeCost<TestSpec>(0)
            );
        }

    #endregion

    }
}
