/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityChargeCost.cs
수정일 : 2026-11-12

# 설명
Charge 비용의 무변경 검사, 소비, 재검사 및 알림 예외의 사후 상태를 검증한다.
Charge 비용의 소비와 충전 시계 관리를 각각 검증한다.

# 테스트 구성
 C: Charge 검사와 실제 소비
 E: 이벤트·외부 변경 및 소비 예외
========================================================================= BLOCK_HEADER_END */

using System;

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

        private sealed class TestDefinition
        {
            // NONE
        }

        private readonly struct TestProjectContext
        {
            // NONE
        }

        private static AbilityInstance<TestDefinition> CreateAbility(int capacity = 2)
        {
            var ability = new AbilityInstance<TestDefinition>
            (
                new TestDefinition(),
                new AbilityState(new AbilityKey("Burst"))
            );
            ability.State.Control.ConfigureCharge(capacity, 1f);
            return ability;
        }

        private static AbilityActivationContext<TestDefinition, TestProjectContext> CreateContext
        (
            AbilityInstance<TestDefinition> ability
        )
        {
            return new AbilityActivationContext<TestDefinition, TestProjectContext>
            (
                null, null,
                new AbilityBinding<TestDefinition>(new AbilityEntryKey("Q"), ability),
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
            var cost = new AbilityChargeCost<TestDefinition, TestProjectContext>();

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
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new[]
                {
                    new AbilityChargeCost<TestDefinition, TestProjectContext>(2),
                }
            );

            Assert.IsFalse(policy.TryActivate(context, out var rejected));
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
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new[]
                {
                    new AbilityChargeCost<TestDefinition, TestProjectContext>(2),
                }
            );

            Assert.IsTrue(policy.TryActivate(context, out var result));
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
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new IAbilityActivationCost<TestDefinition, TestProjectContext>[]
                {
                    new AbilityChargeCost<TestDefinition, TestProjectContext>(),
                    new AbilityChargeCost<TestDefinition, TestProjectContext>(),
                }
            );

            Assert.IsTrue(policy.TryActivate(context, out _));
            Assert.AreEqual(0, ability.State.Charge.Current);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 동일 Charge의 개별 검사는 합산 검사가 아니므로 소비 도중 실패할 수 있음을 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeCost_동일Charge비용의합계부족은후속소비에서실패()
        {
            var ability = CreateAbility(1);
            var context = CreateContext(ability);
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new IAbilityActivationCost<TestDefinition, TestProjectContext>[]
                {
                    new AbilityChargeCost<TestDefinition, TestProjectContext>(),
                    new AbilityChargeCost<TestDefinition, TestProjectContext>(),
                }
            );

            Assert.Throws<InvalidOperationException>(() => policy.TryActivate(context, out _));
            Assert.AreEqual(0, ability.State.Charge.Current);
        }

    #endregion

    #region E-1: 알림 예외와 외부 변경

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 소비 알림이 예외를 던져도 실제 Charge 소비는 되돌리지 않는다.
        /// <br/> 예외를 처리할 책임은 알림을 등록한 외부 소비자에게 있다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeCost_소비알림예외는이미소비한Charge를복구하지않음()
        {
            var ability = CreateAbility();
            ability.State.Charge.Recharge.OnStateChange += (_, change) =>
            {
                if (change.Current == TimerState.Run)
                {
                    throw new InvalidOperationException("notification");
                }
            };
            var context = CreateContext(ability);
            var cost = new AbilityChargeCost<TestDefinition, TestProjectContext>();

            Assert.That(() => cost.Pay(context), Throws.Exception);
            Assert.AreEqual(1, ability.State.Charge.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 소비 알림에서 별도 Charge를 소비해도 해당 변경을 복구하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeCost_소비알림의외부소비는그대로유지()
        {
            var ability = CreateAbility();
            ability.State.Charge.Recharge.OnStateChange += (_, change) =>
            {
                if (change.Current == TimerState.Run)
                {
                    Assert.IsTrue(ability.State.Control.TryConsumeCharge(1));
                }
            };
            var context = CreateContext(ability);
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new[]
                {
                    new AbilityChargeCost<TestDefinition, TestProjectContext>(),
                }
            );

            Assert.IsTrue(policy.TryActivate(context, out _));
            Assert.AreEqual(0, ability.State.Charge.Current);
        }

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
            var cost = new AbilityChargeCost<TestDefinition, TestProjectContext>();
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
                () => new AbilityChargeCost<TestDefinition, TestProjectContext>(0)
            );
        }

    #endregion

    }
}
