/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityActivation.cs
수정일 : 2026-11-17

# 설명
Requirement와 Cost를 전부 검사한 뒤 비용을 소비하고, 실행 시작 실패 시 역순 환급하는 Ability Activation 계약을 검증한다.
비용 소비 과정의 원자성은 해당 Resource Owner가 책임진다.

# 테스트 구성
 A: 사용 조건·비용 검사·소비
 U: 선택 현재성·사용 수명·상태 전이
 R: 독립 Resource Owner와 비용 소유권
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

        private readonly struct TestPayload
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 테스트 요청 맥락의 버전을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int Version { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// 버전으로 테스트 요청 맥락을 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public TestPayload(int version) => Version = version;
        }

        private sealed class CallbackRequirement :
            IAbilityActivationRequirement<TestDefinition>
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
                in AbilityActivationContext<TestDefinition> context
            ) => evaluate();
        }

        private sealed class CallbackCost : IAbilityActivationCost<TestDefinition>
        {
            private readonly Func<AbilityActivationCheck> evaluate;
            private readonly Action pay;
            private readonly Action refund;

            // ------------------------------------------------------------
            /// <summary>
            /// 평가·지불·환급 콜백으로 Cost를 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public CallbackCost(Func<AbilityActivationCheck> evaluate, Action pay, Action refund = null)
            {
                this.evaluate = evaluate;
                this.pay = pay;
                this.refund = refund;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 콜백으로 지불 가능 여부를 평가한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityActivationCheck Evaluate
            (
                in AbilityActivationContext<TestDefinition> context
            ) => evaluate();

            // ------------------------------------------------------------
            /// <summary>
            /// 콜백으로 비용을 지불하고 영수증을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityCostReceipt Pay
            (
                in AbilityActivationContext<TestDefinition> context
            )
            {
                pay();
                return refund == null
                    ? AbilityCostReceipt.NoChange
                    : new AbilityCostReceipt(refund);
            }
        }

        private sealed class TestResourceOwner
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 테스트 자원의 현재 수량을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int Current { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 시작 수량을 지정하여 테스트 자원 소유자를 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public TestResourceOwner(int current) => Current = current;

            // ------------------------------------------------------------
            /// <summary>
            /// 테스트 자원의 현재 수량을 지정한다.
            /// </summary>
            // ------------------------------------------------------------
            public void SetCurrent(int current)
            {
                Current = current;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 지정한 수량을 자원에서 차감한다.
            /// </summary>
            // ------------------------------------------------------------
            public void Pay(int amount)
            {
                if (Current < amount)
                {
                    throw new InvalidOperationException("외부 Resource가 변경됐습니다.");
                }

                Current -= amount;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 지정한 수량을 자원에 되돌린다.
            /// </summary>
            // ------------------------------------------------------------
            public void Refund(int amount)
            {
                Current += amount;
            }
        }

        private sealed class TestResourceCost : IAbilityActivationCost<TestDefinition>
        {
            private readonly TestResourceOwner owner;
            private readonly int amount;

            // ------------------------------------------------------------
            /// <summary>
            /// 소유자와 수량으로 테스트 Cost를 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public TestResourceCost(TestResourceOwner owner, int amount)
            {
                this.owner = owner;
                this.amount = amount;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 자원 수량이 충분한지 평가한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityActivationCheck Evaluate
            (
                in AbilityActivationContext<TestDefinition> context
            )
            {
                return owner.Current >= amount
                    ? AbilityActivationCheck.Allow()
                    : AbilityActivationCheck.Reject("외부 Resource 부족");
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 자원을 차감하고 영수증을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityCostReceipt Pay
            (
                in AbilityActivationContext<TestDefinition> context
            )
            {
                owner.Pay(amount);
                return new AbilityCostReceipt(() => owner.Refund(amount));
            }
        }

        private static AbilityActivationContext<TestDefinition> CreateContext
        (
            object owner = null,
            object instigator = null
        ) => CreateContextWithPayload(new TestPayload(7), owner, instigator);

        private static AbilityActivationContext<TestDefinition> CreateContextWithPayload
        (
            object payload,
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
            return new AbilityActivationContext<TestDefinition>
            (
                owner, instigator, binding, payload
            );
        }

        private static AbilityBindings<TestDefinition> CreateBindings() => new();

        private static AbilityUseSelector<TestDefinition, TestPayload, string> CreateSelector
        (
            AbilityActivationPolicy<TestDefinition> policy,
            Func<bool> isCurrent,
            Action transition = null
        )
        {
            return new AbilityUseSelector<TestDefinition, TestPayload, string>
            (
                new Func<AbilityActivationContext<TestDefinition>, TestPayload,
                    AbilityUseChoice<TestDefinition, string>>[]
                {
                    (context, payload) => new AbilityUseChoice<TestDefinition, string>
                    (
                        "action", policy, isCurrent, transition
                    ),
                }
            );
        }

        private static bool Accept(in AbilityUse<string> use) => true;

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
            var policy = new AbilityActivationPolicy<TestDefinition>
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
            var policy = new AbilityActivationPolicy<TestDefinition>
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
            var policy = new AbilityActivationPolicy<TestDefinition>
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
            var policy = new AbilityActivationPolicy<TestDefinition>
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
            var policy = new AbilityActivationPolicy<TestDefinition>
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

        // ----------------------------------------------------------------------
        /// <summary>
        /// 정책 단독 실행은 후속 비용의 소비 예외가 이전 비용을 자동 복원하지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityActivationPolicy_소비예외시이전소비는자동복원하지않음()
        {
            var owner = new TestResourceOwner(2);
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new IAbilityActivationCost<TestDefinition>[]
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new IAbilityActivationCost<TestDefinition>[]
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
            var policy = new AbilityActivationPolicy<TestDefinition>();
            var payloadVersion = 0;
            var selector = new AbilityUseSelector<TestDefinition, TestPayload, string>
            (
                new Func<AbilityActivationContext<TestDefinition>, TestPayload,
                    AbilityUseChoice<TestDefinition, string>>[]
                {
                    (_, _) => null,
                    (_, payload) =>
                    {
                        payloadVersion = payload.Version;
                        return new AbilityUseChoice<TestDefinition, string>("Q2", policy, () => true);
                    },
                    (_, _) => new AbilityUseChoice<TestDefinition, string>("other", policy, () => true),
                }
            );

            Assert.IsTrue(selector.TrySelect(CreateBindings(), CreateContext(), out var selection, out _));
            Assert.AreEqual("Q2", selection.Execution);
            Assert.AreEqual(7, payloadVersion);
            Assert.IsTrue(selection.TryActivate(Accept, out _, out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 일치 규칙이 없을 때 선택 결과와 거부 사유를 반환하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelector_일치규칙없으면선택하지않음()
        {
            var selector = new AbilityUseSelector<TestDefinition, TestPayload, string>
            (
                new Func<AbilityActivationContext<TestDefinition>, TestPayload,
                    AbilityUseChoice<TestDefinition, string>>[]
                {
                    (_, _) => null,
                }
            );

            Assert.IsFalse(selector.TrySelect(CreateBindings(), CreateContext(), out var selection, out var reason));
            Assert.IsNull(selection);
            Assert.IsNotEmpty(reason);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Context의 Payload가 규칙의 Payload 형식이면
        /// <br/> 값을 그대로 전달하고 아니면 선택 전에 거부하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelector_Payload를규칙형식으로변환하고맞지않으면예외()
        {
            var policy = new AbilityActivationPolicy<TestDefinition>();
            var invoked = 0;
            string received = "unset";
            int? receivedNullable = 5;

            var referenceSelector = new AbilityUseSelector<TestDefinition, string, string>
            (
                new Func<AbilityActivationContext<TestDefinition>, string,
                    AbilityUseChoice<TestDefinition, string>>[]
                {
                    (_, payload) =>
                    {
                        invoked++;
                        received = payload;
                        return new AbilityUseChoice<TestDefinition, string>("action", policy);
                    },
                }
            );

            // 참조 형식은 null Payload를 허용한다.
            Assert.IsTrue(referenceSelector.TrySelect(CreateBindings(), CreateContextWithPayload(null), out _, out _));
            Assert.IsNull(received);
            Assert.IsTrue(referenceSelector.TrySelect(CreateBindings(), CreateContextWithPayload("값"), out _, out _));
            Assert.AreEqual("값", received);
            Assert.AreEqual(2, invoked);

            // 형식이 맞지 않으면 규칙을 호출하기 전에 예외를 전달한다.
            Assert.Throws<ArgumentException>
            (
                () => referenceSelector.TrySelect(CreateBindings(), CreateContextWithPayload(3), out _, out _)
            );
            Assert.AreEqual(2, invoked);

            // 값 형식은 null Payload를 허용하지 않지만 Nullable은 허용한다.
            var valueSelector = new AbilityUseSelector<TestDefinition, TestPayload, string>
            (
                new Func<AbilityActivationContext<TestDefinition>, TestPayload,
                    AbilityUseChoice<TestDefinition, string>>[]
                {
                    (_, _) => throw new InvalidOperationException("호출 금지"),
                }
            );
            Assert.Throws<ArgumentException>
            (
                () => valueSelector.TrySelect(CreateBindings(), CreateContextWithPayload(null), out _, out _)
            );

            var nullableSelector = new AbilityUseSelector<TestDefinition, int?, string>
            (
                new Func<AbilityActivationContext<TestDefinition>, int?,
                    AbilityUseChoice<TestDefinition, string>>[]
                {
                    (_, payload) =>
                    {
                        receivedNullable = payload;
                        return new AbilityUseChoice<TestDefinition, string>("action", policy);
                    },
                }
            );
            Assert.IsTrue(nullableSelector.TrySelect(CreateBindings(), CreateContextWithPayload(null), out _, out _));
            Assert.IsNull(receivedNullable);
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new IAbilityActivationCost<TestDefinition>[]
                {
                    new TestResourceCost(owner, 1),
                }
            );
            var selector = CreateSelector(policy, () => true);

            Assert.IsTrue(selector.TrySelect(CreateBindings(), CreateContext(), out var selection, out _));
            Assert.IsTrue(selection.Evaluate().Succeeded);
            Assert.IsTrue(selection.Evaluate().Succeeded);
            Assert.AreEqual(2, owner.Current);

            Assert.IsTrue(selection.TryActivate(Accept, out var result, out _));
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => paid++),
                }
            );
            var selector = CreateSelector(policy, () => false);

            Assert.IsTrue(selector.TrySelect(CreateBindings(), CreateContext(), out var selection, out _));
            Assert.IsFalse(selection.TryActivate(Accept, out var result, out _));
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new CallbackCost(() => TraceThenAllow(() => current = false), () => paid++),
                }
            );
            var selector = CreateSelector(policy, () => current);

            Assert.IsTrue(selector.TrySelect(CreateBindings(), CreateContext(), out var selection, out _));
            Assert.IsFalse(selection.TryActivate(Accept, out var result, out _));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, result.Failure);
            Assert.AreEqual(0, paid);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 선택 뒤 State가 바뀌면 현재성 토큰이 사전 평가와 사용을 모두 거부하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_선택뒤State변경은현재성토큰이거부()
        {
            var paid = 0;
            var begun = 0;
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => paid++),
                }
            );
            var selector = CreateSelector(policy, null);
            var context = CreateContext();

            Assert.IsTrue(selector.TrySelect(CreateBindings(), context, out var selection, out _));
            Assert.IsTrue(selection.Evaluate().Succeeded);

            Assert.IsTrue(context.Ability.State.Control.TryBeginCooldown(1f));

            Assert.AreEqual(AbilityActivationFailure.InvalidState, selection.Evaluate().Failure);
            Assert.IsFalse
            (
                selection.TryActivate
                (
                    (in AbilityUse<string> use) =>
                    {
                        begun++;
                        return true;
                    },
                    out var result,
                    out _
                )
            );
            Assert.AreEqual(AbilityActivationFailure.InvalidState, result.Failure);
            Assert.AreEqual(0, paid);
            Assert.AreEqual(0, begun);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 선택 뒤 State를 직렬화로 복원하면 현재성 토큰이 사용을 거부하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_선택뒤직렬화복원은현재성토큰이거부()
        {
            var begun = 0;
            var selector = CreateSelector(new AbilityActivationPolicy<TestDefinition>(), null);
            var context = CreateContext();
            var state = context.Ability.State;

            Assert.IsTrue(selector.TrySelect(CreateBindings(), context, out var selection, out _));
            Assert.IsTrue(selection.Evaluate().Succeeded);

            UnityEngine.JsonUtility.FromJsonOverwrite(UnityEngine.JsonUtility.ToJson(state), state);

            Assert.AreEqual(AbilityActivationFailure.InvalidState, selection.Evaluate().Failure);
            Assert.IsFalse
            (
                selection.TryActivate
                (
                    (in AbilityUse<string> use) =>
                    {
                        begun++;
                        return true;
                    },
                    out var result,
                    out _
                )
            );
            Assert.AreEqual(AbilityActivationFailure.InvalidState, result.Failure);
            Assert.AreEqual(0, begun);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 선택 뒤 Binding이 바뀌면 현재성 토큰이 사용을 거부하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_선택뒤Binding변경은현재성토큰이거부()
        {
            var paid = 0;
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => paid++),
                }
            );
            var selector = CreateSelector(policy, null);
            var context = CreateContext();
            var bindings = CreateBindings();

            Assert.IsTrue(selector.TrySelect(bindings, context, out var acquired, out _));
            var other = new AbilityInstance<TestDefinition>
            (
                new TestDefinition(),
                new AbilityState(new AbilityKey("other"))
            );
            var lease = bindings.AcquireOverride(new AbilityEntryKey("Q"), other, 1);
            Assert.IsFalse(acquired.TryActivate(Accept, out var acquiredResult, out _));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, acquiredResult.Failure);

            // 해제도 Binding 변경이므로 이전 선택은 거부된다.
            Assert.IsTrue(selector.TrySelect(bindings, context, out var released, out _));
            lease.Release();
            Assert.IsFalse(released.TryActivate(Accept, out var releasedResult, out _));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, releasedResult.Failure);
            Assert.AreEqual(0, paid);

            // 변경 뒤 새로 선택하면 사용할 수 있다.
            Assert.IsTrue(selector.TrySelect(bindings, context, out var fresh, out _));
            Assert.IsTrue(fresh.TryActivate(Accept, out _, out _));
            Assert.AreEqual(1, paid);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시간 진행만으로는 현재성 토큰이 바뀌지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_시간진행만으로는현재성토큰이유지()
        {
            var policy = new AbilityActivationPolicy<TestDefinition>();
            var selector = CreateSelector(policy, null);
            var context = CreateContext();
            Assert.IsTrue(context.Ability.State.Control.TryBeginCooldown(1f));

            Assert.IsTrue(selector.TrySelect(CreateBindings(), context, out var selection, out _));
            context.Ability.State.Advance(2f);

            Assert.IsTrue(selection.Evaluate().Succeeded);
            Assert.IsTrue(selection.TryActivate(Accept, out _, out _));
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => trace.Add("pay")),
                }
            );
            var selector = CreateSelector(policy, () => true, () => trace.Add("transition"));

            Assert.IsTrue(selector.TrySelect(CreateBindings(), CreateContext(), out var selection, out _));
            Assert.IsTrue
            (
                selection.TryActivate
                (
                    (in AbilityUse<string> use) =>
                    {
                        trace.Add("begin");
                        return true;
                    },
                    out var result,
                    out var started
                )
            );
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("action", started.Execution);
            Assert.AreEqual(new AbilityKey("skill"), started.Origin.AbilityKey);
            Assert.AreEqual(new AbilityEntryKey("Q"), started.Origin.EntryKey);
            Assert.IsFalse(selection.TryActivate(Accept, out var again, out var againUse));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, again.Failure);
            Assert.IsNull(againUse.Execution);
            Assert.AreEqual("pay,begin,transition", string.Join(",", trace));
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new TestResourceCost(owner, 1),
                }
            );
            var selector = CreateSelector(policy, () => true, () => throw new InvalidOperationException("transition"));
            var context = CreateContext();

            Assert.IsTrue(selector.TrySelect(CreateBindings(), context, out var selection, out _));
            Assert.Throws<InvalidOperationException>(() => selection.TryActivate(Accept, out _, out _));
            Assert.AreEqual(1, owner.Current);
            Assert.IsFalse(selection.TryActivate(Accept, out _, out _));

            // 전이 예외 뒤에도 사용 중 상태가 남지 않아 새 선택은 다시 사용할 수 있다.
            var okPolicy = new AbilityActivationPolicy<TestDefinition>();
            Assert.IsTrue(CreateSelector(okPolicy, null).TrySelect(CreateBindings(), context, out var fresh, out _));
            Assert.IsTrue(fresh.TryActivate(Accept, out _, out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 비용 소비 콜백의 재진입이 중복 소비를 발생시키지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_소비콜백의재진입은중복소비하지않음()
        {
            AbilityUseSelection<TestDefinition, string> selection = null;
            var paid = 0;
            var nestedRejected = false;
            void PayCost()
            {
                paid++;
                nestedRejected = !selection.TryActivate(Accept, out var nested, out _) &&
                    nested.Failure == AbilityActivationFailure.InvalidState;
            }

            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, PayCost),
                }
            );

            var selector = CreateSelector(policy, () => true);

            Assert.IsTrue(selector.TrySelect(CreateBindings(), CreateContext(), out selection, out _));
            Assert.IsTrue(selection.TryActivate(Accept, out _, out _));
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
            var policy = new AbilityActivationPolicy<TestDefinition>();
            var selector = CreateSelector(policy, () => current);
            var context = CreateContext();
            var bindings = CreateBindings();

            Assert.IsTrue(selector.TrySelect(bindings, context, out var selection, out _));
            Assert.IsFalse(selection.TryActivate(Accept, out _, out _));
            current = true;
            Assert.IsFalse(selection.TryActivate(Accept, out _, out _));
            Assert.IsTrue(selector.TrySelect(bindings, context, out var fresh, out _));
            Assert.IsTrue(fresh.TryActivate(Accept, out _, out _));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 시작 콜백이 없으면 어떤 비용도 소비하지 않고 예외를 전달하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_시작콜백이없으면소비하지않음()
        {
            var paid = 0;
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => paid++),
                }
            );
            var selector = CreateSelector(policy, null);

            Assert.IsTrue(selector.TrySelect(CreateBindings(), CreateContext(), out var selection, out _));
            Assert.Throws<ArgumentNullException>(() => selection.TryActivate(null, out _, out _));
            Assert.AreEqual(0, paid);
            Assert.IsTrue(selection.TryActivate(Accept, out _, out _));
            Assert.AreEqual(1, paid);
        }

    #endregion

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
            var policy = new AbilityActivationPolicy<TestDefinition>
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

            Assert.IsTrue(selector.TrySelect(CreateBindings(), context, out var selection, out _));
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new IAbilityActivationCost<TestDefinition>[]
                {
                    new TestResourceCost(owner, 2),
                    new CallbackCost(Allow, () => trace.Add("B.Pay"), () => trace.Add("B.Refund")),
                }
            );
            var selector = CreateSelector(policy, null, () => transitionCount++);

            Assert.IsTrue(selector.TrySelect(CreateBindings(), CreateContext(), out var selection, out _));
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
            var policy = new AbilityActivationPolicy<TestDefinition>
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

            Assert.IsTrue(selector.TrySelect(CreateBindings(), CreateContext(), out var selection, out _));
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => { }, () => { trace.Add("A.Refund"); throw refundA; }),
                    new CallbackCost(Allow, () => { }, () => trace.Add("C.Refund")),
                    new CallbackCost(Allow, () => { }, () => { trace.Add("B.Refund"); throw refundB; }),
                }
            );
            var selector = CreateSelector(policy, null);

            Assert.IsTrue(selector.TrySelect(CreateBindings(), CreateContext(), out var selection, out _));
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
            var single = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => { }, () => throw refundA),
                    new CallbackCost(Allow, () => { }),
                }
            );

            Assert.IsTrue(CreateSelector(single, null).TrySelect(CreateBindings(), CreateContext(), out var first, out _));
            var thrown = Assert.Throws<InvalidOperationException>
            (
                () => first.TryActivate((in AbilityUse<string> use) => false, out _, out _)
            );
            Assert.AreSame(refundA, thrown);

            var both = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => { }, () => throw new InvalidOperationException("A")),
                    new CallbackCost(Allow, () => { }, () => throw new InvalidOperationException("B")),
                }
            );

            Assert.IsTrue(CreateSelector(both, null).TrySelect(CreateBindings(), CreateContext(), out var second, out _));
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => payCount++, () => refunds++),
                }
            );
            var context = CreateContext();

            // 시작이 거부되면 전이는 실행되지 않는다.
            var rejected = CreateSelector(policy, null, () => transitionCount++);
            Assert.IsTrue(rejected.TrySelect(CreateBindings(), context, out var selection, out _));
            Assert.IsFalse(selection.TryActivate((in AbilityUse<string> use) => false, out _, out _));
            Assert.AreEqual(0, transitionCount);
            Assert.AreEqual(1, refunds);

            // 시작 성공 뒤 전이 예외는 비용을 유지한 채 그대로 전달된다.
            var failing = CreateSelector(policy, null, () => throw new InvalidOperationException("transition"));
            Assert.IsTrue(failing.TrySelect(CreateBindings(), context, out var failed, out _));
            Assert.Throws<InvalidOperationException>(() => failed.TryActivate(Accept, out _, out _));
            Assert.AreEqual(2, payCount);
            Assert.AreEqual(1, refunds);

            // 사용 중 상태가 해제돼 같은 State에서 새 선택을 사용할 수 있다.
            Assert.IsTrue(rejected.TrySelect(CreateBindings(), context, out var fresh, out _));
            Assert.IsTrue(fresh.TryActivate(Accept, out _, out _));
            Assert.AreEqual(1, transitionCount);
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new IAbilityActivationCost<TestDefinition>[]
                {
                    new TestResourceCost(owner, 1),
                }
            );
            var selector = CreateSelector(policy, null);
            var context = CreateContext();
            var bindings = CreateBindings();

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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => paid++),
                }
            );
            var selector = CreateSelector(policy, null);
            AbilityUseSelection<TestDefinition, string> selection = null;
            AbilityActivationFailure nested = AbilityActivationFailure.None;

            Assert.IsTrue(selector.TrySelect(CreateBindings(), CreateContext(), out selection, out _));
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
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new IAbilityActivationCost<TestDefinition>[]
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

            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new IAbilityActivationCost<TestDefinition>[]
                {
                    new CallbackCost(Allow, ChangeOwner),
                    new TestResourceCost(owner, 1),
                }
            );

            Assert.Throws<InvalidOperationException>(() => policy.TryActivate(CreateContext(), out _));
            Assert.AreEqual(1, paid);
            Assert.AreEqual(0, owner.Current);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 영수증은 환급 동작을 요구하고 NoChange는 환급할 내용이 없는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityCostReceipt_환급동작을요구하고NoChange는환급하지않음()
        {
            Assert.Throws<ArgumentNullException>(() => new AbilityCostReceipt(null));

            // NoChange만 지불한 사용은 시작이 거부돼도 환급 예외 없이 거부 결과를 반환한다.
            var policy = new AbilityActivationPolicy<TestDefinition>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => { }),
                }
            );
            Assert.IsTrue(CreateSelector(policy, null).TrySelect(CreateBindings(), CreateContext(), out var selection, out _));
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
