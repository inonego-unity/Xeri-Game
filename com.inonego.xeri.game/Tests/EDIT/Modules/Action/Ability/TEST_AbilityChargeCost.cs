/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityChargeCost.cs
수정일 : 2026-11-17

# 설명
Charge 비용의 무변경 검사, 소비, 재검사 및 알림 예외의 사후 상태를 검증한다.
Charge 비용의 소비와 충전 시계 관리를 각각 검증한다.
지불 영수증의 환급이 수량과 충전 시계를 지불 전 상태로 정확히 되돌리는지 검증한다.

# 테스트 구성
 C: Charge 검사와 실제 소비
 R: 지불 영수증 환급
 E: 이벤트·외부 변경 및 소비 예외
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

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

        private static AbilityActivationContext<TestDefinition> CreateContext
        (
            AbilityInstance<TestDefinition> ability
        )
        {
            return new AbilityActivationContext<TestDefinition>
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
            var cost = new AbilityChargeCost<TestDefinition>();

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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new AbilityChargeCost<TestDefinition>(2),
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new AbilityChargeCost<TestDefinition>(2),
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new IAbilityActivationCost<TestDefinition>[]
                {
                    new AbilityChargeCost<TestDefinition>(),
                    new AbilityChargeCost<TestDefinition>(),
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new IAbilityActivationCost<TestDefinition>[]
                {
                    new AbilityChargeCost<TestDefinition>(),
                    new AbilityChargeCost<TestDefinition>(),
                }
            );

            Assert.Throws<InvalidOperationException>(() => policy.TryActivate(context, out _));
            Assert.AreEqual(0, ability.State.Charge.Current);
        }

    #endregion

    #region R-1: 지불 영수증 환급

        private static AbilityState CreateState(int capacity = 3)
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.Control.ConfigureCharge(capacity, 1f);
            return state;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 가득 찬 상태에서 지불한 Charge를 환급하면 수량과 충전 시계가 지불 전과 같은지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_PayCharge환급은가득찬상태를정확히복원()
        {
            var state = CreateState(3);
            var recharge = state.Charge.Recharge;
            var beforeJson = JsonUtility.ToJson(state.Charge);
            var revision = state.Revision;

            var receipt = state.PayCharge(1);

            Assert.AreEqual(2, state.Charge.Current);
            Assert.IsTrue(state.Charge.Recharge.IsRunning);
            Assert.AreEqual(revision + 1, state.Revision);

            receipt.Refund();

            Assert.AreEqual(beforeJson, JsonUtility.ToJson(state.Charge));
            Assert.AreEqual(3, state.Charge.Current);
            Assert.IsFalse(state.Charge.Recharge.IsRunning);
            Assert.AreSame(recharge, state.Charge.Recharge);
            Assert.AreEqual(revision + 2, state.Revision);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 충전 시계가 진행 중이던 상태의 환급이 경과 시간까지 지불 전으로 되돌리는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_PayCharge환급은충전시계진행도지불전으로복원()
        {
            var state = CreateState(3);
            Assert.IsTrue(state.Control.TryConsumeCharge(1));
            state.Advance(0.4f);
            var beforeJson = JsonUtility.ToJson(state.Charge);

            var receipt = state.PayCharge(2);
            Assert.AreEqual(0, state.Charge.Current);

            receipt.Refund();

            Assert.AreEqual(beforeJson, JsonUtility.ToJson(state.Charge));
            Assert.AreEqual(2, state.Charge.Current);
            Assert.IsTrue(state.Charge.Recharge.IsRunning);
            Assert.AreEqual(0.6f, state.Charge.Recharge.RemainingTime, 0.0001f);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 지불 뒤 Charge가 바뀌었으면 환급이 예외를 던지고 상태를 바꾸지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_PayCharge환급은지불후변경을감지하면거부()
        {
            var advanced = CreateState(3);
            var advancedReceipt = advanced.PayCharge(1);
            advanced.Advance(0.1f);
            var advancedRevision = advanced.Revision;

            Assert.Throws<InvalidOperationException>(() => advancedReceipt.Refund());
            Assert.AreEqual(2, advanced.Charge.Current);
            Assert.AreEqual(advancedRevision, advanced.Revision);

            var restored = CreateState(3);
            var restoredReceipt = restored.PayCharge(2);
            Assert.AreEqual(1, restored.Control.RestoreCharge(1));

            Assert.Throws<InvalidOperationException>(() => restoredReceipt.Refund());
            Assert.AreEqual(2, restored.Charge.Current);

            var configured = CreateState(3);
            var configuredReceipt = configured.PayCharge(1);
            configured.Control.ConfigureCharge(5, 1f);

            Assert.Throws<InvalidOperationException>(() => configuredReceipt.Refund());
            Assert.AreEqual(5, configured.Charge.Capacity);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 지불할 수 없으면 예외를 던지고 Charge와 Revision을 바꾸지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_PayCharge는지불불가시상태를바꾸지않음()
        {
            var state = CreateState(1);
            Assert.IsTrue(state.Control.TryConsumeCharge(1));
            var revision = state.Revision;

            Assert.Throws<InvalidOperationException>(() => state.PayCharge(1));
            Assert.AreEqual(0, state.Charge.Current);
            Assert.AreEqual(revision, state.Revision);

            var empty = new AbilityState(new AbilityKey("None"));
            Assert.Throws<InvalidOperationException>(() => empty.PayCharge(1));
            Assert.AreEqual(0UL, empty.Revision);
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
            var cost = new AbilityChargeCost<TestDefinition>();

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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new AbilityChargeCost<TestDefinition>(),
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
            var cost = new AbilityChargeCost<TestDefinition>();
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
                () => new AbilityChargeCost<TestDefinition>(0)
            );
        }

    #endregion

    }
}
