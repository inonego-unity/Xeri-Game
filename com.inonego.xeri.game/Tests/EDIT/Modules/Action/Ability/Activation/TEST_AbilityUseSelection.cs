/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityUseSelection.cs
수정일 : 2026-11-19

# 설명
규칙 선택, 비용을 소비하지 않는 사전 평가, 현재성 토큰과 단일 사용·상태 전이 계약을 검증한다.
같은 Entry가 선택 시점의 State(Charge 잔량, 콤보 단계 Values)에 따라 다른 실행·비용·전이를 고르는 규칙을 검증한다.

# 테스트 구성
 U: 규칙 선택·사전 평가·현재성 토큰·단일 사용과 상태 전이·상태 기반 선택
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
    /// 상태 기반 선택과 현재성 토큰, 단일 사용 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityUseSelection : AbilityActivationTestBase
    {

    #region U-1: 규칙 선택

        // ------------------------------------------------------------
        /// <summary>
        /// 여러 후보 중 첫 번째 일치 규칙을 선택하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelector_첫번째일치규칙을선택()
        {
            var policy = new AbilityActivationPolicy<TestSpec>();
            var payloadVersion = 0;
            var selector = new AbilityUseSelector<TestSpec, TestPayload, string>
            (
                new Func<AbilityActivationContext<TestSpec>, TestPayload,
                    AbilityUseChoice<TestSpec, string>>[]
                {
                    (_, _) => null,
                    (_, payload) =>
                    {
                        payloadVersion = payload.Version;
                        return new AbilityUseChoice<TestSpec, string>("Q2", policy, () => true);
                    },
                    (_, _) => new AbilityUseChoice<TestSpec, string>("other", policy, () => true),
                }
            );

            Assert.IsTrue(Select(selector, CreateContext(), out var selection, out _));
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
            var selector = new AbilityUseSelector<TestSpec, TestPayload, string>
            (
                new Func<AbilityActivationContext<TestSpec>, TestPayload,
                    AbilityUseChoice<TestSpec, string>>[]
                {
                    (_, _) => null,
                }
            );

            Assert.IsFalse(Select(selector, CreateContext(), out var selection, out var reason));
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
            var policy = new AbilityActivationPolicy<TestSpec>();
            var invoked = 0;
            string received = "unset";
            int? receivedNullable = 5;

            var referenceSelector = new AbilityUseSelector<TestSpec, string, string>
            (
                new Func<AbilityActivationContext<TestSpec>, string,
                    AbilityUseChoice<TestSpec, string>>[]
                {
                    (_, payload) =>
                    {
                        invoked++;
                        received = payload;
                        return new AbilityUseChoice<TestSpec, string>("action", policy);
                    },
                }
            );

            // 참조 형식은 null Payload를 허용한다.
            Assert.IsTrue(Select(referenceSelector, CreateContextWithPayload(null), out _, out _));
            Assert.IsNull(received);
            Assert.IsTrue(Select(referenceSelector, CreateContextWithPayload("값"), out _, out _));
            Assert.AreEqual("값", received);
            Assert.AreEqual(2, invoked);

            // 형식이 맞지 않으면 규칙을 호출하기 전에 예외를 전달한다.
            Assert.Throws<ArgumentException>
            (
                () => Select(referenceSelector, CreateContextWithPayload(3), out _, out _)
            );
            Assert.AreEqual(2, invoked);

            // 값 형식은 null Payload를 허용하지 않지만 Nullable은 허용한다.
            var valueSelector = new AbilityUseSelector<TestSpec, TestPayload, string>
            (
                new Func<AbilityActivationContext<TestSpec>, TestPayload,
                    AbilityUseChoice<TestSpec, string>>[]
                {
                    (_, _) => throw new InvalidOperationException("호출 금지"),
                }
            );
            Assert.Throws<ArgumentException>
            (
                () => Select(valueSelector, CreateContextWithPayload(null), out _, out _)
            );

            var nullableSelector = new AbilityUseSelector<TestSpec, int?, string>
            (
                new Func<AbilityActivationContext<TestSpec>, int?,
                    AbilityUseChoice<TestSpec, string>>[]
                {
                    (_, payload) =>
                    {
                        receivedNullable = payload;
                        return new AbilityUseChoice<TestSpec, string>("action", policy);
                    },
                }
            );
            Assert.IsTrue(Select(nullableSelector, CreateContextWithPayload(null), out _, out _));
            Assert.IsNull(receivedNullable);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 현재 Entry 연결과 다른 Instance의 Context는 선택하지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelector_현재Binding과다른Context는선택을거부()
        {
            var selector = CreateSelector(new AbilityActivationPolicy<TestSpec>(), null);
            var context = CreateContext();
            var bindings = CreateBindings(context);
            var replacement = new AbilityInstance<TestSpec>
            (
                new TestSpec(),
                new AbilityState(new AbilityKey("other"))
            );

            using (bindings.AcquireOverride(context.EntryKey, replacement, 1))
            {
                Assert.IsFalse(selector.TrySelect(bindings, context, out var stale, out var reason));
                Assert.IsNull(stale);
                Assert.IsNotEmpty(reason);
            }

            Assert.IsTrue(selector.TrySelect(bindings, context, out _, out _));
            Assert.IsFalse(selector.TrySelect(new AbilityBindings<TestSpec>(), context, out _, out _));
        }

    #endregion

    #region U-2: 사전 평가와 현재성 토큰

        // ----------------------------------------------------------------------
        /// <summary>
        /// 사전 평가는 비용을 소비하지 않고 실제 Activation만 한 번 실행하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_사전평가는비용을소비하지않음()
        {
            var owner = new TestResourceOwner(2);
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new IAbilityActivationCost<TestSpec>[]
                {
                    new TestResourceCost(owner, 1),
                }
            );
            var selector = CreateSelector(policy, () => true);

            Assert.IsTrue(Select(selector, CreateContext(), out var selection, out _));
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
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => paid++),
                }
            );
            var selector = CreateSelector(policy, () => false);

            Assert.IsTrue(Select(selector, CreateContext(), out var selection, out _));
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
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(() => TraceThenAllow(() => current = false), () => paid++),
                }
            );
            var selector = CreateSelector(policy, () => current);

            Assert.IsTrue(Select(selector, CreateContext(), out var selection, out _));
            Assert.IsFalse(selection.TryActivate(Accept, out var result, out _));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, result.Failure);
            Assert.AreEqual(0, paid);
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
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => paid++),
                }
            );
            var selector = CreateSelector(policy, null);
            var context = CreateContext();
            var bindings = CreateBindings(context);

            Assert.IsTrue(selector.TrySelect(bindings, context, out var acquired, out _));
            var other = new AbilityInstance<TestSpec>
            (
                new TestSpec(),
                new AbilityState(new AbilityKey("other"))
            );
            var lease = bindings.AcquireOverride(new AbilityEntryKey("Q"), other, 1);
            Assert.IsFalse(acquired.TryActivate(Accept, out var acquiredResult, out _));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, acquiredResult.Failure);

            // 해제도 Binding 변경이므로 Override 중에 한 선택은 해제 뒤 거부된다.
            Assert.IsTrue(bindings.TryResolve(context.EntryKey, out var overridden));
            var overriddenContext = new AbilityActivationContext<TestSpec>
            (
                context.Owner, context.Instigator, overridden, context.Payload
            );
            Assert.IsTrue(selector.TrySelect(bindings, overriddenContext, out var released, out _));
            lease.Release();
            Assert.IsFalse(released.TryActivate(Accept, out var releasedResult, out _));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, releasedResult.Failure);
            Assert.AreEqual(0, paid);

            // 변경 뒤 새로 선택하면 사용할 수 있다.
            Assert.IsTrue(selector.TrySelect(bindings, context, out var fresh, out _));
            Assert.IsTrue(fresh.TryActivate(Accept, out _, out _));
            Assert.AreEqual(1, paid);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 선택 규칙이 실행 중 Binding을 바꾸면 그 변경을 선택 이후의 변경으로 보고
        /// <br/> 사용을 거부하며 비용을 소비하지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelector_규칙이바꾼Binding은선택뒤변경으로거부()
        {
            var paid = 0;
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => paid++),
                }
            );
            var context = CreateContext();
            var bindings = CreateBindings(context);
            var other = new AbilityInstance<TestSpec>
            (
                new TestSpec(),
                new AbilityState(new AbilityKey("other"))
            );
            var choice = new AbilityUseChoice<TestSpec, string>("action", policy);
            var selector = new AbilityUseSelector<TestSpec, TestPayload, string>
            (
                new Func<AbilityActivationContext<TestSpec>, TestPayload,
                    AbilityUseChoice<TestSpec, string>>[]
                {
                    (_, _) =>
                    {
                        bindings.AcquireOverride(context.EntryKey, other, 1);
                        return choice;
                    },
                }
            );

            Assert.IsTrue(selector.TrySelect(bindings, context, out var selection, out _));
            Assert.IsFalse(selection.TryActivate(Accept, out var result, out _));

            Assert.AreEqual(AbilityActivationFailure.InvalidState, result.Failure);
            Assert.AreEqual(0, paid);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시간 진행만으로는 현재성 토큰이 바뀌지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_시간진행만으로는현재성토큰이유지()
        {
            var policy = new AbilityActivationPolicy<TestSpec>();
            var selector = CreateSelector(policy, null);
            var context = CreateContext();
            Assert.IsTrue(context.Ability.State.TryBeginCooldown(1f));

            Assert.IsTrue(Select(selector, context, out var selection, out _));
            context.Ability.State.Advance(2f);

            Assert.IsTrue(selection.Evaluate().Succeeded);
            Assert.IsTrue(selection.TryActivate(Accept, out _, out _));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> State를 바꾸는 모든 경로(Control·Values·직렬화 복원·메모리 복제)가
        /// <br/> Revision을 올려 선택을 무효화하고 비용을 소비하지 않는지
        /// <br/> 선택 이후 변경의 종류별로 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [TestCaseSource(nameof(StateMutationCases))]
        public void TEST_AbilityUseSelection_선택뒤State변경은종류와무관하게현재성토큰이거부
        (
            Action<AbilityState> setup,
            Action<AbilityState> mutate
        )
        {
            var paid = 0;
            var begun = 0;
            var context = CreateContext();
            var state = context.Ability.State;
            setup(state);

            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => paid++),
                }
            );
            var selector = CreateSelector(policy, null);
            Assert.IsTrue(Select(selector, context, out var selection, out _));
            Assert.IsTrue(selection.Evaluate().Succeeded);

            var revision = state.Revision;
            mutate(state);

            Assert.IsTrue(state.Revision > revision);
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

        // ------------------------------------------------------------
        /// <summary>
        /// 선택 뒤 변경 종류별로 선택 전 준비와 선택 뒤 변경을 짝지어 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        private static IEnumerable<TestCaseData> StateMutationCases()
        {
            Action<AbilityState> none = _ => { };
            Action<AbilityState> beginCooldown = state => state.TryBeginCooldown(5f);

            yield return new TestCaseData
            (
                none,
                (Action<AbilityState>)(state => state.Values.SetI("Key", 1))
            ).SetName("Values변경");

            yield return new TestCaseData
            (
                none,
                (Action<AbilityState>)(state => state.TryBeginCooldown(1f))
            ).SetName("Cooldown시작");

            yield return new TestCaseData
            (
                beginCooldown,
                (Action<AbilityState>)(state => state.PauseCooldown())
            ).SetName("Cooldown일시정지");

            yield return new TestCaseData
            (
                (Action<AbilityState>)(state =>
                {
                    state.TryBeginCooldown(5f);
                    state.PauseCooldown();
                }),
                (Action<AbilityState>)(state => state.ResumeCooldown())
            ).SetName("Cooldown재개");

            yield return new TestCaseData
            (
                beginCooldown,
                (Action<AbilityState>)(state => state.ResetCooldown())
            ).SetName("Cooldown초기화");

            yield return new TestCaseData
            (
                beginCooldown,
                (Action<AbilityState>)(state => state.ReduceCooldown(1f))
            ).SetName("Cooldown감소");

            yield return new TestCaseData
            (
                (Action<AbilityState>)(state => state.ConfigureCharge(2, 1f)),
                (Action<AbilityState>)(state => state.ConfigureCharge(3, 1f))
            ).SetName("Charge재구성");

            yield return new TestCaseData
            (
                (Action<AbilityState>)(state => state.ConfigureCharge(2, 1f)),
                (Action<AbilityState>)(state => state.TryConsumeCharge(1))
            ).SetName("Charge소비");

            yield return new TestCaseData
            (
                (Action<AbilityState>)(state =>
                {
                    state.ConfigureCharge(2, 1f);
                    state.TryConsumeCharge(1);
                }),
                (Action<AbilityState>)(state => state.AddCharge(1))
            ).SetName("Charge복원");

            yield return new TestCaseData
            (
                none,
                (Action<AbilityState>)(state =>
                    UnityEngine.JsonUtility.FromJsonOverwrite(UnityEngine.JsonUtility.ToJson(state), state))
            ).SetName("직렬화복원");

            yield return new TestCaseData
            (
                none,
                (Action<AbilityState>)(state => state.CloneFrom(state.Clone()))
            ).SetName("메모리복제");
        }

    #endregion

    #region U-3: 단일 사용과 전이

        // ------------------------------------------------------------
        /// <summary>
        /// 비용 소비 후 상태 전이를 한 번만 적용하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelection_비용소비후전이는한번실행()
        {
            var trace = new List<string>();
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => trace.Add("pay")),
                }
            );
            var selector = CreateSelector(policy, () => true, () => trace.Add("transition"));

            Assert.IsTrue(Select(selector, CreateContext(), out var selection, out _));
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
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new TestResourceCost(owner, 1),
                }
            );
            var selector = CreateSelector(policy, () => true, () => throw new InvalidOperationException("transition"));
            var context = CreateContext();

            Assert.IsTrue(Select(selector, context, out var selection, out _));
            Assert.Throws<InvalidOperationException>(() => selection.TryActivate(Accept, out _, out _));
            Assert.AreEqual(1, owner.Current);
            Assert.IsFalse(selection.TryActivate(Accept, out _, out _));

            // 전이 예외 뒤에도 사용 중 상태가 남지 않아 새 선택은 다시 사용할 수 있다.
            var okPolicy = new AbilityActivationPolicy<TestSpec>();
            Assert.IsTrue(Select(CreateSelector(okPolicy, null), context, out var fresh, out _));
            Assert.IsTrue(fresh.TryActivate(Accept, out _, out _));
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
            var policy = new AbilityActivationPolicy<TestSpec>();
            var selector = CreateSelector(policy, () => current);
            var context = CreateContext();
            var bindings = CreateBindings(context);

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
            var policy = new AbilityActivationPolicy<TestSpec>
            (
                cost: new[]
                {
                    new CallbackCost(Allow, () => paid++),
                }
            );
            var selector = CreateSelector(policy, null);

            Assert.IsTrue(Select(selector, CreateContext(), out var selection, out _));
            Assert.Throws<ArgumentNullException>(() => selection.TryActivate(null, out _, out _));
            Assert.AreEqual(0, paid);
            Assert.IsTrue(selection.TryActivate(Accept, out _, out _));
            Assert.AreEqual(1, paid);
        }

    #endregion

    #region U-4: 상태 기반 선택

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 같은 Entry에서 선택 시점의 Charge가 마지막 하나면 다른 실행과 비용을 고르고
        /// <br/> 그 외에는 일반 실행을 고르는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelector_마지막Charge는다른실행과비용을선택()
        {
            var context = CreateContext();
            var state = context.Ability.State;
            state.ConfigureCharge(3, 10f);
            var finisherPaid = 0;
            var normal = new AbilityActivationPolicy<TestSpec>
            (
                cost: new IAbilityActivationCost<TestSpec>[]
                {
                    new AbilityChargeCost<TestSpec>(),
                }
            );
            var finisher = new AbilityActivationPolicy<TestSpec>
            (
                cost: new IAbilityActivationCost<TestSpec>[]
                {
                    new AbilityChargeCost<TestSpec>(),
                    new CallbackCost(Allow, () => finisherPaid++),
                }
            );
            var selector = new AbilityUseSelector<TestSpec, TestPayload, string>
            (
                new Func<AbilityActivationContext<TestSpec>, TestPayload,
                    AbilityUseChoice<TestSpec, string>>[]
                {
                    (request, _) => request.Ability.State.Charge.Current == 1
                        ? new AbilityUseChoice<TestSpec, string>("LastShot", finisher)
                        : null,
                    (_, _) => new AbilityUseChoice<TestSpec, string>("Shot", normal),
                }
            );

            var executions = new List<string>();
            for (var shot = 0; shot < 3; shot++)
            {
                Assert.IsTrue(Select(selector, context, out var selection, out _));
                Assert.IsTrue(selection.TryActivate(Accept, out _, out var use));
                executions.Add(use.Execution);
            }

            Assert.AreEqual("Shot,Shot,LastShot", string.Join(",", executions));
            Assert.AreEqual(1, finisherPaid);
            Assert.AreEqual(0, state.Charge.Current);

            // 탄이 없으면 일반 규칙을 고르지만 비용 검사에서 거부한다.
            Assert.IsTrue(Select(selector, context, out var empty, out _));
            Assert.AreEqual("Shot", empty.Execution);
            Assert.IsFalse(empty.TryActivate(Accept, out var rejected, out _));
            Assert.AreEqual(AbilityActivationFailure.CostRejected, rejected.Failure);
            Assert.AreEqual(1, finisherPaid);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 콤보 단계 값에 따라 같은 Entry가 다음 실행을 고르고 전이가 단계를 올리며,
        /// <br/> 시작이 거부되면 단계가 그대로이고, 먼저 고른 다른 선택은
        /// <br/> 단계가 바뀌면 거부되는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityUseSelector_콤보단계에따라다음실행을고르고전이로단계를올림()
        {
            var context = CreateContext();
            var state = context.Ability.State;
            var policy = new AbilityActivationPolicy<TestSpec>();
            var selector = new AbilityUseSelector<TestSpec, TestPayload, string>
            (
                new Func<AbilityActivationContext<TestSpec>, TestPayload,
                    AbilityUseChoice<TestSpec, string>>[]
                {
                    (request, _) =>
                    {
                        var values = request.Ability.State.Values;
                        var stage = values.GetI("Stage");
                        return new AbilityUseChoice<TestSpec, string>
                        (
                            "Q" + (stage + 1),
                            policy,
                            transition: () => values.SetI("Stage", (stage + 1) % 3)
                        );
                    },
                }
            );

            var executions = new List<string>();
            for (var step = 0; step < 4; step++)
            {
                Assert.IsTrue(Select(selector, context, out var selection, out _));
                Assert.IsTrue(selection.TryActivate(Accept, out _, out var use));
                executions.Add(use.Execution);
            }

            Assert.AreEqual("Q1,Q2,Q3,Q1", string.Join(",", executions));
            Assert.AreEqual(1, state.Values.GetI("Stage"));

            // 시작이 거부되면 전이를 적용하지 않아 같은 단계를 다시 고른다.
            Assert.IsTrue(Select(selector, context, out var refused, out _));
            Assert.IsFalse(refused.TryActivate((in AbilityUse<string> use) => false, out var startRejected, out _));
            Assert.AreEqual(AbilityActivationFailure.StartRejected, startRejected.Failure);
            Assert.AreEqual(1, state.Values.GetI("Stage"));

            // 같은 단계에서 먼저 고른 두 선택 중 하나가 단계를 올리면 나머지는 낡은 선택으로 거부한다.
            Assert.IsTrue(Select(selector, context, out var first, out _));
            Assert.IsTrue(Select(selector, context, out var second, out _));
            Assert.AreEqual("Q2", first.Execution);
            Assert.AreEqual("Q2", second.Execution);
            Assert.IsTrue(first.TryActivate(Accept, out _, out _));

            var begun = false;
            Assert.IsFalse
            (
                second.TryActivate
                (
                    (in AbilityUse<string> use) =>
                    {
                        begun = true;
                        return true;
                    },
                    out var stale,
                    out _
                )
            );
            Assert.AreEqual(AbilityActivationFailure.InvalidState, stale.Failure);
            Assert.IsFalse(begun);
            Assert.AreEqual(2, state.Values.GetI("Stage"));
        }

    #endregion

    }
}
