/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityActivationPolicy.cs
수정일 : 2026-11-26

# 설명
Requirement와 Cost를 전부 검사한 뒤 비용을 소비하는 Activation Policy 계약을 검증한다.
소비 도중 예외가 나면 정책이 앞서 소비한 비용을 역순으로 환급한다. 한 Cost 안의 원자성은 해당 Resource Owner가 책임진다.

# 테스트 구성
 A: 사용 조건·비용 검사·소비
 R: 독립 Resource Owner와 비용 소유권
 V: 결과·Context·영수증 값
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
    using Xeri.Game.TEST;
    using Xeri.Game.TEST._Ability;
    using Xeri.Game.TEST._Ability.Fixture;

    // ============================================================
    /// <summary>
    /// 사용 조건·비용 검사와 소비, 결과 값 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityActivationPolicy : AbilityActivationTestBase
    {

    #region 검증용 조건

        private sealed class CallbackRequirement :
            IAbilityActivationRequirement<TestSpec>
        {
            private readonly Func<AbilityActivationCheck> evaluate;

            // ------------------------------------------------------------
            /// <summary>
            /// 평가 콜백으로 Requirement를 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public CallbackRequirement(Func<AbilityActivationCheck> evaluate) => this.evaluate = evaluate;

            // ------------------------------------------------------------
            /// <summary>
            /// 콜백으로 사용 가능 여부를 평가한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityActivationCheck Evaluate
            (
                in AbilityActivationContext<TestSpec> context
            ) => evaluate();
        }

        private static AbilityActivationCheck Deny() => AbilityActivationCheck.Reject("사용 불가");

    #endregion

    #region A-1: 전체 조건과 비용 검사

        // ------------------------------------------------------------
        /// <summary>
        /// 요구 조건을 거부하면 비용 검사를 시작하지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityActivationPolicy_조건거부시비용을확인하지않음()
        {
            var checkedCost = false;
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                requirement: new[]
                {
                    new CallbackRequirement(Deny),
                },
                cost: new[]
                {
                    new CallbackCost(() => TraceThenAllow(() => checkedCost = true), () => Assert.Fail("소비 금지")),
                }
            );

            Assert.IsFalse(policy.TryActivate(CreateContext(), null, new List<AbilityCostReceipt>(), out var result));
            Assert.AreEqual(AbilityActivationFailure.RequirementRejected, result.Failure);
            Assert.AreEqual(0, result.PolicyIndex);
            Assert.IsFalse(checkedCost);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 비용을 먼저 검사한 뒤 등록 순서대로 소비하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityActivationPolicy_모든비용검사후순서대로소비()
        {
            var calls = new List<string>();
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(() => TraceThenAllow(() => calls.Add("A.Check")), () => calls.Add("A.Pay")),
                    new CallbackCost(() => TraceThenAllow(() => calls.Add("B.Check")), () => calls.Add("B.Pay")),
                }
            );

            Assert.IsTrue(policy.TryActivate(CreateContext(), null, new List<AbilityCostReceipt>(), out var result));
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("A.Check,B.Check,A.Pay,B.Pay", string.Join(",", calls));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 두 번째 비용이 거부되면 첫 번째 비용도 소비하지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityActivationPolicy_두번째비용검사거부시첫비용도소비하지않음()
        {
            var trace = new List<string>();
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(() => TraceThenAllow(() => trace.Add("A.Check")), () => trace.Add("A.Pay")),
                    new CallbackCost(() => { trace.Add("B.Check"); return Deny(); }, () => trace.Add("B.Pay")),
                }
            );

            Assert.IsFalse(policy.TryActivate(CreateContext(), null, new List<AbilityCostReceipt>(), out var result));
            Assert.AreEqual(AbilityActivationFailure.CostRejected, result.Failure);
            Assert.AreEqual(1, result.PolicyIndex);
            Assert.AreEqual("A.Check,B.Check", string.Join(",", trace));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 비용 검사에서 예외가 발생하면 소비하지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityActivationPolicy_검사예외시소비하지않음()
        {
            var paid = false;
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => paid = true),
                    new CallbackCost(() => throw new InvalidOperationException("check"), () => paid = true),
                }
            );

            Assert.Throws<InvalidOperationException>(() => policy.TryActivate(CreateContext(), null, new List<AbilityCostReceipt>(), out _));
            Assert.IsFalse(paid);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 요청할 때마다 현재 상태를 다시 평가하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityActivationPolicy_매요청현재상태를다시평가()
        {
            var current = true;
            var payCount = 0;
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                requirement: new[]
                {
                    new CallbackRequirement(() => current ? Allow() : Deny()),
                },
                cost: new[]
                {
                    new CallbackCost(() => current ? Allow() : Deny(), () => payCount++),
                }
            );
            var context = CreateContext();

            Assert.IsTrue(policy.TryActivate(context, null, new List<AbilityCostReceipt>(), out _));
            current = false;
            Assert.IsFalse(policy.TryActivate(context, null, new List<AbilityCostReceipt>(), out var rejected));
            Assert.AreEqual(AbilityActivationFailure.RequirementRejected, rejected.Failure);
            Assert.AreEqual(1, payCount);
        }

    #endregion

    #region A-2: 소비 예외와 사후 상태

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 정책을 직접 호출해도 후속 비용의 소비 예외가 나면 앞서 소비한 비용을
        /// <br/> 역순으로 환급하고 원래 예외를 다시 던지는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityActivationPolicy_소비예외시앞선비용을역순환급하고예외전달()
        {
            var owner = new TestResourceOwner(2);
            var refunds = new List<string>();
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new IAbilityActivationCost<TestSpec>[]
                {
                    new TestResourceCost(owner, 1),
                    new CallbackCost(Allow, () => { }, () => refunds.Add("A")),
                    new CallbackCost(Allow, () => { }, () => refunds.Add("B")),
                    new CallbackCost(Allow, () => throw new InvalidOperationException("pay")),
                }
            );

            var thrown = Assert.Throws<InvalidOperationException>(() => policy.TryActivate(CreateContext(), null, new List<AbilityCostReceipt>(), out _));

            Assert.AreEqual("pay", thrown.Message);
            Assert.AreEqual("B,A", string.Join(",", refunds));
            Assert.AreEqual(2, owner.Current);
        }

    #endregion

    #region R-1: 독립 Resource Owner

        // ----------------------------------------------------------------------
        /// <summary>
        /// 서로 다른 Resource Owner의 비용을 각각 검사하고 소비하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ResourceCost_서로다른Owner의비용을검사하고소비()
        {
            var ownerA = new TestResourceOwner(3);
            var ownerB = new TestResourceOwner(5);
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new IAbilityActivationCost<TestSpec>[]
                {
                    new TestResourceCost(ownerA, 2),
                    new TestResourceCost(ownerB, 4),
                }
            );

            Assert.IsTrue(policy.TryActivate(CreateContext(), null, new List<AbilityCostReceipt>(), out _));
            Assert.AreEqual(1, ownerA.Current);
            Assert.AreEqual(1, ownerB.Current);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 검사 후 외부 Resource 변경은 해당 Owner가 소비 시점에 판정하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ResourceCost_검사뒤외부변경은Owner가소비시판정()
        {
            var owner = new TestResourceOwner(1);
            var paid = 0;
            void ChangeOwner()
            {
                owner.SetCurrent(0);
                paid++;
            }

            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new IAbilityActivationCost<TestSpec>[]
                {
                    new CallbackCost(Allow, ChangeOwner),
                    new TestResourceCost(owner, 1),
                }
            );

            Assert.Throws<InvalidOperationException>(() => policy.TryActivate(CreateContext(), null, new List<AbilityCostReceipt>(), out _));
            Assert.AreEqual(1, paid);
            Assert.AreEqual(0, owner.Current);
        }

    #endregion

    #region V-1: 결과·Context·영수증 값

        // ----------------------------------------------------------------------
        /// <summary>
        /// 영수증은 환급 동작을 요구하고 NoChange는 환급할 내용이 없는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityCostReceipt_환급동작을요구하고NoChange는환급하지않음()
        {
            Assert.Throws<ArgumentNullException>(() => AbilityCostReceipt.From(null));

            // NoChange만 지불한 사용은 시작이 거부돼도 환급 예외 없이 거부 결과를 반환한다.
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => { }),
                }
            );
            Assert.IsTrue(Select(CreateSelector(policy, null), CreateContext(), out var selection, out _));
            Assert.IsFalse(selection.TryActivate((in AbilityUse<string> use) => false, out var result, out _));
            Assert.AreEqual(AbilityActivationFailure.StartRejected, result.Failure);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Owner·Instigator·Payload를 각각 보존하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityActivationContext_Owner와Instigator및Payload를보존()
        {
            var owner = new object();
            var instigator = new object();
            var context = CreateContext(owner, instigator);

            Assert.AreSame(owner, context.Owner);
            Assert.AreSame(instigator, context.Instigator);
            Assert.AreEqual(7, ((TestPayload)context.Payload).Version);
            Assert.AreEqual(new AbilityEntryKey("Q"), context.EntryKey);
            Assert.AreEqual(new AbilityKey("skill"), context.Ability.Key);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 Activation 결과가 성공으로 판정되지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityActivationResult_기본값은성공하지않음()
        {
            Assert.IsFalse(default(AbilityActivationResult).Succeeded);
        }

    #endregion

    }
}
