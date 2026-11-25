/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityUseBegin.cs
수정일 : 2026-11-19

# 설명
실행 시작이 실패하면 소비한 비용을 역순 환급하고 사용 중 재진입이 같은 선택을 다시 소비하지 않는 계약을 검증한다.

# 테스트 구성
 B: 실행 시작·환급·재진입
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
    /// 실행 시작 실패의 환급과 사용 중 재진입 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityUseBegin : AbilityActivationTestBase
    {

    #region B-1: 실행 시작 실패와 환급

        // ------------------------------------------------------------
        /// <summary>
        /// 시작이 거부되면 소비한 비용을 지불의 역순으로 환급하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_시작거부는소비한비용을역순환급()
        {
            var trace = new List<string>();
            var transitionCount = 0;
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => trace.Add("A.Pay"), () => trace.Add("A.Refund")),
                    new CallbackCost(Allow, () => trace.Add("B.Pay"), () => trace.Add("B.Refund")),
                    new CallbackCost(Allow, () => trace.Add("C.Pay")),
                }
            );
            var selector = CreateSelector(policy, null, () => transitionCount++);
            var context = CreateContext();

            Assert.IsTrue(Select(selector, context, out var selection, out _));
            Assert.IsFalse
            (
                selection.TryActivate
                (
                    (in AbilityUse<string> use) =>
                    {
                        trace.Add("Begin");
                        return false;
                    },
                    out var result,
                    out var started
                )
            );

            Assert.AreEqual(AbilityActivationFailure.StartRejected, result.Failure);
            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(started.Execution);
            Assert.AreEqual("A.Pay,B.Pay,C.Pay,Begin,B.Refund,A.Refund", string.Join(",", trace));
            Assert.AreEqual(0, transitionCount);
            Assert.IsFalse(selection.TryActivate(Accept, out var repeated, out _));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, repeated.Failure);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 시작 예외도 소비한 비용을 역순으로 환급하고 같은 예외를 전달하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_시작예외는소비한비용을역순환급하고원래예외전달()
        {
            var owner = new TestResourceOwner(5);
            var trace = new List<string>();
            var transitionCount = 0;
            var original = new InvalidOperationException("begin");
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new IAbilityActivationCost<TestSpec>[]
                {
                    new TestResourceCost(owner, 2),
                    new CallbackCost(Allow, () => trace.Add("B.Pay"), () => trace.Add("B.Refund")),
                }
            );
            var selector = CreateSelector(policy, null, () => transitionCount++);

            Assert.IsTrue(Select(selector, CreateContext(), out var selection, out _));
            var thrown = Assert.Throws<InvalidOperationException>
            (
                () => selection.TryActivate
                (
                    (in AbilityUse<string> use) =>
                    {
                        Assert.AreEqual(3, owner.Current);
                        throw original;
                    },
                    out _,
                    out _
                )
            );

            Assert.AreSame(original, thrown);
            Assert.AreEqual(5, owner.Current);
            Assert.AreEqual("B.Pay,B.Refund", string.Join(",", trace));
            Assert.AreEqual(0, transitionCount);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 지불 중 예외가 나면 이미 지불한 비용만 역순 환급하고 시작은 호출하지 않는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_지불예외는앞선비용만역순환급()
        {
            var trace = new List<string>();
            var begun = 0;
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => trace.Add("A.Pay"), () => trace.Add("A.Refund")),
                    new CallbackCost(Allow, () => trace.Add("B.Pay"), () => trace.Add("B.Refund")),
                    new CallbackCost
                    (
                        Allow,
                        () => throw new InvalidOperationException("pay"),
                        () => trace.Add("C.Refund")
                    ),
                }
            );
            var selector = CreateSelector(policy, null);

            Assert.IsTrue(Select(selector, CreateContext(), out var selection, out _));
            Assert.Throws<InvalidOperationException>
            (
                () => selection.TryActivate
                (
                    (in AbilityUse<string> use) =>
                    {
                        begun++;
                        return true;
                    },
                    out _,
                    out _
                )
            );

            Assert.AreEqual(0, begun);
            Assert.AreEqual("A.Pay,B.Pay,B.Refund,A.Refund", string.Join(",", trace));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 환급 중 예외가 나도 나머지 환급을 계속하고 원래 예외를 첫
        /// <br/> 항목으로 AggregateException에 담는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_환급예외는원래예외를첫항목으로집계()
        {
            var trace = new List<string>();
            var original = new InvalidOperationException("begin");
            var refundB = new InvalidOperationException("B.Refund");
            var refundA = new InvalidOperationException("A.Refund");
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => { }, () => { trace.Add("A.Refund"); throw refundA; }),
                    new CallbackCost(Allow, () => { }, () => trace.Add("C.Refund")),
                    new CallbackCost(Allow, () => { }, () => { trace.Add("B.Refund"); throw refundB; }),
                }
            );
            var selector = CreateSelector(policy, null);

            Assert.IsTrue(Select(selector, CreateContext(), out var selection, out _));
            var aggregate = Assert.Throws<AggregateException>
            (
                () => selection.TryActivate
                (
                    (in AbilityUse<string> use) => throw original,
                    out _,
                    out _
                )
            );

            Assert.AreEqual(3, aggregate.InnerExceptions.Count);
            Assert.AreSame(original, aggregate.InnerExceptions[0]);
            Assert.AreSame(refundB, aggregate.InnerExceptions[1]);
            Assert.AreSame(refundA, aggregate.InnerExceptions[2]);
            Assert.AreEqual("B.Refund,C.Refund,A.Refund", string.Join(",", trace));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 시작 거부 뒤 환급 실패가 하나뿐이면 그 예외를, 둘 이상이면
        /// <br/> AggregateException을 전달하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_시작거부뒤환급예외를전달()
        {
            var refundA = new InvalidOperationException("A.Refund");
            var single = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => { }, () => throw refundA),
                    new CallbackCost(Allow, () => { }),
                }
            );

            Assert.IsTrue(Select(CreateSelector(single, null), CreateContext(), out var first, out _));
            var thrown = Assert.Throws<InvalidOperationException>
            (
                () => first.TryActivate((in AbilityUse<string> use) => false, out _, out _)
            );
            Assert.AreSame(refundA, thrown);

            var both = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => { }, () => throw new InvalidOperationException("A")),
                    new CallbackCost(Allow, () => { }, () => throw new InvalidOperationException("B")),
                }
            );

            Assert.IsTrue(Select(CreateSelector(both, null), CreateContext(), out var second, out _));
            var aggregate = Assert.Throws<AggregateException>
            (
                () => second.TryActivate((in AbilityUse<string> use) => false, out _, out _)
            );
            Assert.AreEqual(2, aggregate.InnerExceptions.Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 시작 성공 뒤에만 전이를 실행하고 전이 예외는 환급
        /// <br/> 없이 전달하며 사용 중 상태를 해제하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_전이는시작성공뒤에만실행하고전이예외는환급하지않음()
        {
            var refunds = 0;
            var transitionCount = 0;
            var payCount = 0;
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => payCount++, () => refunds++),
                }
            );
            var context = CreateContext();

            // 시작이 거부되면 전이는 실행되지 않는다.
            var rejected = CreateSelector(policy, null, () => transitionCount++);
            Assert.IsTrue(Select(rejected, context, out var selection, out _));
            Assert.IsFalse(selection.TryActivate((in AbilityUse<string> use) => false, out _, out _));
            Assert.AreEqual(0, transitionCount);
            Assert.AreEqual(1, refunds);

            // 시작 성공 뒤 전이 예외는 비용을 유지한 채 그대로 전달된다.
            var failing = CreateSelector(policy, null, () => throw new InvalidOperationException("transition"));
            Assert.IsTrue(Select(failing, context, out var failed, out _));
            Assert.Throws<InvalidOperationException>(() => failed.TryActivate(Accept, out _, out _));
            Assert.AreEqual(2, payCount);
            Assert.AreEqual(1, refunds);

            // 사용 중 상태가 해제돼 같은 State에서 새 선택을 사용할 수 있다.
            Assert.IsTrue(Select(rejected, context, out var fresh, out _));
            Assert.IsTrue(fresh.TryActivate(Accept, out _, out _));
            Assert.AreEqual(1, transitionCount);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 실제 Charge 비용을 하나 또는 둘 지불한 뒤 실행 시작이 거부되면
        /// <br/> 수량과 충전 시계를 지불 전 상태로 되돌리는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [TestCase(1)]
        [TestCase(2)]
        public void TEST_AbilityUseSelection_시작거부는실제Charge비용을지불전상태로환급(int costCount)
        {
            var context = CreateContext();
            var state = context.Ability.State;
            state.ConfigureCharge(3, 1f);
            var costs = new List<IAbilityActivationCost<TestSpec>>();
            for (var index = 0; index < costCount; index++)
            {
                costs.Add(new AbilityChargeCost<TestSpec>());
            }

            var selector = CreateSelector(new AbilityActivationPolicy<TestSpec>(cost: costs), null);
            Assert.IsTrue(Select(selector, context, out var selection, out _));
            var before = UnityEngine.JsonUtility.ToJson(state);
            var revision = state.Revision;

            var result = default(AbilityActivationResult);
            Assert.IsFalse
            (
                selection.TryActivate
                (
                    (in AbilityUse<string> use) =>
                    {
                        // 시작을 시도하는 시점에는 비용이 실제로 지불된 상태다.
                        Assert.AreEqual(3 - costCount, state.Charge.Current);
                        return false;
                    },
                    out result,
                    out _
                )
            );

            Assert.AreEqual(AbilityActivationFailure.StartRejected, result.Failure);
            Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(state));
            Assert.AreEqual(3, state.Charge.Current);
            Assert.IsFalse(state.Charge.Recharge.IsRunning);
            Assert.IsTrue(state.Revision > revision);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 같은 Charge를 소비하는 두 비용의 합계가 부족하면 Selection 경로에서는
        /// <br/> 먼저 지불한 비용을 환급하고 지불 실패 예외를 전달하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_같은Charge합계부족은먼저지불한비용을환급하고예외를전달()
        {
            var context = CreateContext();
            var state = context.Ability.State;
            state.ConfigureCharge(1, 1f);
            var costs = new List<IAbilityActivationCost<TestSpec>>
            {
                new AbilityChargeCost<TestSpec>(),
                new AbilityChargeCost<TestSpec>(),
            };

            var selector = CreateSelector(new AbilityActivationPolicy<TestSpec>(cost: costs), null);
            Assert.IsTrue(Select(selector, context, out var selection, out _));
            var before = UnityEngine.JsonUtility.ToJson(state);

            Assert.Throws<InvalidOperationException>(() => selection.TryActivate(Accept, out _, out _));

            Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(state));
            Assert.AreEqual(1, state.Charge.Current);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 시작이 지불 뒤 Charge를 바꿔 환급이 거부되면 환급 실패를 전달하고
        /// <br/> 시작 예외가 있었다면 첫 항목으로 함께 전달하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityUseSelection_시작중State변경으로환급이거부되면그결과를전달(bool beginThrows)
        {
            var context = CreateContext();
            var state = context.Ability.State;
            state.ConfigureCharge(3, 1f);
            var costs = new List<IAbilityActivationCost<TestSpec>>
            {
                new AbilityChargeCost<TestSpec>(),
            };

            var selector = CreateSelector(new AbilityActivationPolicy<TestSpec>(cost: costs), null);
            Assert.IsTrue(Select(selector, context, out var selection, out _));

            AbilityBegin<string> begin = (in AbilityUse<string> use) =>
            {
                state.AddCharge(1);
                if (beginThrows)
                {
                    throw new InvalidOperationException("시작 실패");
                }

                return false;
            };

            if (beginThrows)
            {
                var aggregate = Assert.Throws<AggregateException>
                (
                    () => selection.TryActivate(begin, out _, out _)
                );
                Assert.AreEqual(2, aggregate.InnerExceptions.Count);
                Assert.AreEqual("시작 실패", aggregate.InnerExceptions[0].Message);
            }
            else
            {
                var exception = Assert.Throws<InvalidOperationException>
                (
                    () => selection.TryActivate(begin, out _, out _)
                );
                StringAssert.Contains("환급", exception.Message);
            }

            // 시작이 되돌려 놓은 수량이 남고 환급은 적용되지 않는다.
            Assert.AreEqual(3, state.Charge.Current);
        }

    #endregion

    #region B-2: 사용 중 재진입

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 시작 콜백에서 같은 State의 다른 선택을 사용하면
        /// <br/> 재진입으로 거절돼 환급되고 사용 중 상태가 해제되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_시작콜백의다른선택재진입은거절되고환급()
        {
            var owner = new TestResourceOwner(2);
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new IAbilityActivationCost<TestSpec>[]
                {
                    new TestResourceCost(owner, 1),
                }
            );
            var selector = CreateSelector(policy, null);
            var context = CreateContext();
            var bindings = CreateBindings(context);

            Assert.IsTrue(selector.TrySelect(bindings, context, out var outer, out _));
            Assert.IsTrue(selector.TrySelect(bindings, context, out var inner, out _));

            Assert.Throws<InvalidOperationException>
            (
                () => outer.TryActivate
                (
                    (in AbilityUse<string> use) =>
                    {
                        inner.TryActivate(Accept, out _, out _);
                        return true;
                    },
                    out _,
                    out _
                )
            );

            // 재진입은 아무 것도 소비하지 않았고 바깥 사용의 비용은 환급됐다.
            Assert.AreEqual(2, owner.Current);

            // 사용 중 상태가 해제돼 새 선택은 정상적으로 사용할 수 있다.
            Assert.IsTrue(selector.TrySelect(bindings, context, out var fresh, out _));
            Assert.IsTrue(fresh.TryActivate(Accept, out _, out _));
            Assert.AreEqual(1, owner.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 시작 콜백에서 같은 선택을 다시 사용하면 InvalidState로
        /// <br/> 거절되고 바깥 사용은 정상 완료되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_시작콜백의같은선택재진입은InvalidState()
        {
            var paid = 0;
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => paid++),
                }
            );
            var selector = CreateSelector(policy, null);
            AbilityUseSelection<TestSpec, string> selection = null;
            AbilityActivationFailure nested = AbilityActivationFailure.None;

            Assert.IsTrue(Select(selector, CreateContext(), out selection, out _));
            Assert.IsTrue
            (
                selection.TryActivate
                (
                    (in AbilityUse<string> use) =>
                    {
                        selection.TryActivate(Accept, out var nestedResult, out _);
                        nested = nestedResult.Failure;
                        return true;
                    },
                    out _,
                    out _
                )
            );

            Assert.AreEqual(AbilityActivationFailure.InvalidState, nested);
            Assert.AreEqual(1, paid);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 비용 소비 콜백의 재진입이 중복 소비를 발생시키지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_소비콜백의재진입은중복소비하지않음()
        {
            AbilityUseSelection<TestSpec, string> selection = null;
            var paid = 0;
            var nestedRejected = false;
            void PayCost()
            {
                paid++;
                nestedRejected = !selection.TryActivate(Accept, out var nested, out _) &&
                    nested.Failure == AbilityActivationFailure.InvalidState;
            }

            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, PayCost),
                }
            );

            var selector = CreateSelector(policy, () => true);

            Assert.IsTrue(Select(selector, CreateContext(), out selection, out _));
            Assert.IsTrue(selection.TryActivate(Accept, out _, out _));
            Assert.IsTrue(nestedRejected);
            Assert.AreEqual(1, paid);
        }

    #endregion

    }
}
