/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityActivation.cs
수정일 : 2026-11-12

# 설명
Requirement와 Cost를 전부 검사한 뒤 비용을 소비하는 최소 Ability Activation 계약을 검증한다.
비용 소비 과정의 원자성은 해당 Resource Owner가 책임진다.

# 테스트 구성
 A: 사용 조건·비용 검사·소비
 U: 선택 현재성·사용 수명·상태 전이
 R: 독립 Resource Owner와 비용 소유권
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

    // ============================================================
    /// <summary>
    /// 상태 기반 선택과 검증 후 비용 소비의 책임을 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityActivation
    {

    #region 검증용 정의와 조건

        private sealed class TestDefinition
        {
            // NONE
        }

        private readonly struct TestProjectContext
        {
            public int Version { get; }

            public TestProjectContext(int version) => Version = version;
        }

        private sealed class CallbackRequirement :
            IAbilityActivationRequirement<TestDefinition, TestProjectContext>
        {
            private readonly Func<AbilityActivationCheck> evaluate;

            public CallbackRequirement(Func<AbilityActivationCheck> evaluate) => this.evaluate = evaluate;

            public AbilityActivationCheck Evaluate
            (
                in AbilityActivationContext<TestDefinition, TestProjectContext> context
            ) => evaluate();
        }

        private sealed class CallbackCost : IAbilityActivationCost<TestDefinition, TestProjectContext>
        {
            private readonly Func<AbilityActivationCheck> evaluate;
            private readonly Action pay;

            public CallbackCost(Func<AbilityActivationCheck> evaluate, Action pay)
            {
                this.evaluate = evaluate;
                this.pay = pay;
            }

            public AbilityActivationCheck Evaluate
            (
                in AbilityActivationContext<TestDefinition, TestProjectContext> context
            ) => evaluate();

            public void Pay
            (
                in AbilityActivationContext<TestDefinition, TestProjectContext> context
            ) => pay();
        }

        private sealed class TestResourceOwner
        {
            public int Current { get; private set; }

            public TestResourceOwner(int current) => Current = current;

            public void SetCurrent(int current)
            {
                Current = current;
            }

            public void Pay(int amount)
            {
                if (Current < amount)
                {
                    throw new InvalidOperationException("외부 Resource가 변경됐습니다.");
                }

                Current -= amount;
            }
        }

        private sealed class TestResourceCost : IAbilityActivationCost<TestDefinition, TestProjectContext>
        {
            private readonly TestResourceOwner owner;
            private readonly int amount;

            public TestResourceCost(TestResourceOwner owner, int amount)
            {
                this.owner = owner;
                this.amount = amount;
            }

            public AbilityActivationCheck Evaluate
            (
                in AbilityActivationContext<TestDefinition, TestProjectContext> context
            )
            {
                return owner.Current >= amount
                    ? AbilityActivationCheck.Allow()
                    : AbilityActivationCheck.Reject("외부 Resource 부족");
            }

            public void Pay
            (
                in AbilityActivationContext<TestDefinition, TestProjectContext> context
            ) => owner.Pay(amount);
        }

        private static AbilityActivationContext<TestDefinition, TestProjectContext> CreateContext
        (
            object owner = null,
            object instigator = null
        )
        {
            var instance = new AbilityInstance<TestDefinition>
            (
                new TestDefinition(),
                new AbilityState(new AbilityKey("skill"))
            );
            var binding = new AbilityBinding<TestDefinition>(new AbilityEntryKey("Q"), instance);
            return new AbilityActivationContext<TestDefinition, TestProjectContext>
            (
                owner, instigator, binding, new TestProjectContext(7)
            );
        }

        private static AbilityUseSelector<TestDefinition, TestProjectContext, string> CreateSelector
        (
            AbilityActivationPolicy<TestDefinition, TestProjectContext> policy,
            Func<bool> isCurrent,
            Action transition = null
        )
        {
            return new AbilityUseSelector<TestDefinition, TestProjectContext, string>
            (
                new Func<AbilityActivationContext<TestDefinition, TestProjectContext>,
                    AbilityUseChoice<TestDefinition, TestProjectContext, string>>[]
                {
                    context => new AbilityUseChoice<TestDefinition, TestProjectContext, string>
                    (
                        "action", policy, isCurrent, transition
                    ),
                }
            );
        }

        private static AbilityActivationCheck TraceThenAllow(Action action)
        {
            action();
            return AbilityActivationCheck.Allow();
        }

        private static AbilityActivationCheck Allow() => AbilityActivationCheck.Allow();
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
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
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

            Assert.IsFalse(policy.TryActivate(CreateContext(), out var result));
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
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new[]
                {
                    new CallbackCost(() => TraceThenAllow(() => calls.Add("A.Check")), () => calls.Add("A.Pay")),
                    new CallbackCost(() => TraceThenAllow(() => calls.Add("B.Check")), () => calls.Add("B.Pay")),
                }
            );

            Assert.IsTrue(policy.TryActivate(CreateContext(), out var result));
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
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new[]
                {
                    new CallbackCost(() => TraceThenAllow(() => trace.Add("A.Check")), () => trace.Add("A.Pay")),
                    new CallbackCost(() => { trace.Add("B.Check"); return Deny(); }, () => trace.Add("B.Pay")),
                }
            );

            Assert.IsFalse(policy.TryActivate(CreateContext(), out var result));
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
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => paid = true),
                    new CallbackCost(() => throw new InvalidOperationException("check"), () => paid = true),
                }
            );

            Assert.Throws<InvalidOperationException>(() => policy.TryActivate(CreateContext(), out _));
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
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
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

            Assert.IsTrue(policy.TryActivate(context, out _));
            current = false;
            Assert.IsFalse(policy.TryActivate(context, out var rejected));
            Assert.AreEqual(AbilityActivationFailure.RequirementRejected, rejected.Failure);
            Assert.AreEqual(1, payCount);
        }

    #endregion

    #region A-2: 소비 예외와 사후 상태

        // ------------------------------------------------------------
        /// <summary>
        /// 후속 비용의 소비 예외가 이전 비용을 자동 복원하지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityActivationPolicy_소비예외시이전소비는자동복원하지않음()
        {
            var owner = new TestResourceOwner(2);
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new IAbilityActivationCost<TestDefinition, TestProjectContext>[]
                {
                    new TestResourceCost(owner, 1),
                    new CallbackCost(Allow, () => throw new InvalidOperationException("pay")),
                }
            );

            Assert.Throws<InvalidOperationException>(() => policy.TryActivate(CreateContext(), out _));
            Assert.AreEqual(1, owner.Current);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 서로 다른 Owner의 정상 소비가 추가 복구 절차 없이 완료되는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityActivationPolicy_이미소비한비용은후속Cost가임의로복구하지않음()
        {
            var owner = new TestResourceOwner(2);
            var other = new TestResourceOwner(1);
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new IAbilityActivationCost<TestDefinition, TestProjectContext>[]
                {
                    new TestResourceCost(owner, 1),
                    new CallbackCost(Allow, () => other.Pay(1)),
                }
            );

            Assert.IsTrue(policy.TryActivate(CreateContext(), out _));
            Assert.AreEqual(1, owner.Current);
            Assert.AreEqual(0, other.Current);
        }

    #endregion

    #region U-1: 선택과 현재성

        // ------------------------------------------------------------
        /// <summary>
        /// 여러 후보 중 첫 번째 일치 규칙을 선택하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelector_첫번째일치규칙을선택()
        {
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>();
            var selector = new AbilityUseSelector<TestDefinition, TestProjectContext, string>
            (
                new Func<AbilityActivationContext<TestDefinition, TestProjectContext>,
                    AbilityUseChoice<TestDefinition, TestProjectContext, string>>[]
                {
                    _ => null,
                    _ => new AbilityUseChoice<TestDefinition, TestProjectContext, string>("Q2", policy, () => true),
                    _ => new AbilityUseChoice<TestDefinition, TestProjectContext, string>("other", policy, () => true),
                }
            );

            Assert.IsTrue(selector.TrySelect(CreateContext(), out var selection, out _));
            Assert.AreEqual("Q2", selection.Execution);
            Assert.IsTrue(selection.TryActivate(out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 일치 규칙이 없을 때 선택 결과와 거부 사유를 반환하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelector_일치규칙없으면선택하지않음()
        {
            var selector = new AbilityUseSelector<TestDefinition, TestProjectContext, string>
            (
                new Func<AbilityActivationContext<TestDefinition, TestProjectContext>,
                    AbilityUseChoice<TestDefinition, TestProjectContext, string>>[]
                {
                    _ => null,
                }
            );

            Assert.IsFalse(selector.TrySelect(CreateContext(), out var selection, out var reason));
            Assert.IsNull(selection);
            Assert.IsNotEmpty(reason);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 사전 평가는 비용을 소비하지 않고 실제 Activation만 한 번 실행하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_사전평가는비용을소비하지않음()
        {
            var owner = new TestResourceOwner(2);
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new IAbilityActivationCost<TestDefinition, TestProjectContext>[]
                {
                    new TestResourceCost(owner, 1),
                }
            );
            var selector = CreateSelector(policy, () => true);

            Assert.IsTrue(selector.TrySelect(CreateContext(), out var selection, out _));
            Assert.IsTrue(selection.Evaluate().Succeeded);
            Assert.IsTrue(selection.Evaluate().Succeeded);
            Assert.AreEqual(2, owner.Current);

            Assert.IsTrue(selection.TryActivate(out var result));
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(1, owner.Current);
            Assert.AreEqual(AbilityActivationFailure.InvalidState, selection.Evaluate().Failure);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 선택이 무효하면 사용 전에 비용을 소비하지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_사용전현재성실패는소비하지않음()
        {
            var paid = 0;
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => paid++),
                }
            );
            var selector = CreateSelector(policy, () => false);

            Assert.IsTrue(selector.TrySelect(CreateContext(), out var selection, out _));
            Assert.IsFalse(selection.TryActivate(out var result));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, result.Failure);
            Assert.AreEqual(0, paid);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 비용 검사 과정에서 선택이 변경되면 소비하지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_검사중선택변경시소비하지않음()
        {
            var current = true;
            var paid = 0;
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new[]
                {
                    new CallbackCost(() => TraceThenAllow(() => current = false), () => paid++),
                }
            );
            var selector = CreateSelector(policy, () => current);

            Assert.IsTrue(selector.TrySelect(CreateContext(), out var selection, out _));
            Assert.IsFalse(selection.TryActivate(out var result));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, result.Failure);
            Assert.AreEqual(0, paid);
        }

    #endregion

    #region U-2: 단일 사용과 전이

        // ------------------------------------------------------------
        /// <summary>
        /// 비용 소비 후 상태 전이를 한 번만 적용하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_비용소비후전이는한번실행()
        {
            var trace = new List<string>();
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => trace.Add("pay")),
                }
            );
            var selector = CreateSelector(policy, () => true, () => trace.Add("transition"));

            Assert.IsTrue(selector.TrySelect(CreateContext(), out var selection, out _));
            Assert.IsTrue(selection.TryActivate(out var result));
            Assert.IsTrue(result.Succeeded);
            Assert.IsFalse(selection.TryActivate(out var again));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, again.Failure);
            Assert.AreEqual("pay,transition", string.Join(",", trace));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 상태 전이 예외 후 이미 소비한 비용을 복원하지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_전이예외후이미소비한비용은유지()
        {
            var owner = new TestResourceOwner(2);
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new[]
                {
                    new TestResourceCost(owner, 1),
                }
            );
            var selector = CreateSelector(policy, () => true, () => throw new InvalidOperationException("transition"));

            Assert.IsTrue(selector.TrySelect(CreateContext(), out var selection, out _));
            Assert.Throws<InvalidOperationException>(() => selection.TryActivate(out _));
            Assert.AreEqual(1, owner.Current);
            Assert.IsFalse(selection.TryActivate(out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 비용 소비 콜백의 재진입이 중복 소비를 발생시키지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_소비콜백의재진입은중복소비하지않음()
        {
            AbilityUseSelection<TestDefinition, TestProjectContext, string> selection = null;
            var paid = 0;
            var nestedRejected = false;
            void PayCost()
            {
                paid++;
                nestedRejected = !selection.TryActivate(out var nested) &&
                    nested.Failure == AbilityActivationFailure.InvalidState;
            }

            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, PayCost),
                }
            );

            var selector = CreateSelector(policy, () => true);

            Assert.IsTrue(selector.TrySelect(CreateContext(), out selection, out _));
            Assert.IsTrue(selection.TryActivate(out _));
            Assert.IsTrue(nestedRejected);
            Assert.AreEqual(1, paid);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 거부되거나 이미 사용한 선택은 재사용하지 않고 다시 선택하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_거부된선택은다시사용하지않음()
        {
            var current = false;
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>();
            var selector = CreateSelector(policy, () => current);

            Assert.IsTrue(selector.TrySelect(CreateContext(), out var selection, out _));
            Assert.IsFalse(selection.TryActivate(out _));
            current = true;
            Assert.IsFalse(selection.TryActivate(out _));
            Assert.IsTrue(selector.TrySelect(CreateContext(), out var fresh, out _));
            Assert.IsTrue(fresh.TryActivate(out _));
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
            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new IAbilityActivationCost<TestDefinition, TestProjectContext>[]
                {
                    new TestResourceCost(ownerA, 2),
                    new TestResourceCost(ownerB, 4),
                }
            );

            Assert.IsTrue(policy.TryActivate(CreateContext(), out _));
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

            var policy = new AbilityActivationPolicy<TestDefinition, TestProjectContext>
            (
                cost: new IAbilityActivationCost<TestDefinition, TestProjectContext>[]
                {
                    new CallbackCost(Allow, ChangeOwner),
                    new TestResourceCost(owner, 1),
                }
            );

            Assert.Throws<InvalidOperationException>(() => policy.TryActivate(CreateContext(), out _));
            Assert.AreEqual(1, paid);
            Assert.AreEqual(0, owner.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Owner·Instigator·Project Context를 각각 보존하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityActivationContext_Owner와Instigator및Project를보존()
        {
            var owner = new object();
            var instigator = new object();
            var context = CreateContext(owner, instigator);

            Assert.AreSame(owner, context.Owner);
            Assert.AreSame(instigator, context.Instigator);
            Assert.AreEqual(7, context.Project.Version);
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
