/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityActivationTestBase.cs
수정일 : 2026-11-19

# 설명
Activation 테스트가 공유하는 콜백 Requirement·Cost, 외부 Resource Owner와 선택 도우미를 제공한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.TEST._Ability.Fixture
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// Activation 테스트가 공유하는 검증용 조건·비용과 선택 도우미이다.
    /// </summary>
    // ============================================================
    public abstract class AbilityActivationTestBase : AbilityTestBase
    {

    #region 검증용 조건과 선택 도우미

        protected sealed class CallbackCost : IAbilityActivationCost<TestSpec>
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
                in AbilityActivationContext<TestSpec> context
            ) => evaluate();

            // ------------------------------------------------------------
            /// <summary>
            /// 콜백으로 비용을 지불하고 영수증을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityCostReceipt Pay
            (
                in AbilityActivationContext<TestSpec> context
            )
            {
                pay();
                return refund == null
                    ? AbilityCostReceipt.NoChange
                    : new AbilityCostReceipt(refund);
            }
        }

        protected sealed class TestResourceOwner
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

        protected sealed class TestResourceCost : IAbilityActivationCost<TestSpec>
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
                in AbilityActivationContext<TestSpec> context
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
                in AbilityActivationContext<TestSpec> context
            )
            {
                owner.Pay(amount);
                return new AbilityCostReceipt(() => owner.Refund(amount));
            }
        }

        protected static AbilityActivationContext<TestSpec> CreateContext
        (
            object owner = null,
            object instigator = null
        ) => CreateContextWithPayload(new TestPayload(7), owner, instigator);

        protected static AbilityActivationContext<TestSpec> CreateContextWithPayload
        (
            object payload,
            object owner = null,
            object instigator = null
        )
        {
            var instance = new AbilityInstance<TestSpec>
            (
                new TestSpec(),
                new AbilityState(new AbilityKey("skill"))
            );
            var binding = new AbilityBinding<TestSpec>(new AbilityEntryKey("Q"), instance);
            return new AbilityActivationContext<TestSpec>
            (
                owner, instigator, binding, payload
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Context의 Entry가 Context의 Instance를 현재 연결로
        /// <br/> 해석하는 Bindings를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        protected static AbilityBindings<TestSpec> CreateBindings
        (
            in AbilityActivationContext<TestSpec> context
        )
        {
            var layer = new AbilityBindingLayer<TestSpec>(0);
            layer.Set(context.EntryKey, context.Ability);

            var bindings = new AbilityBindings<TestSpec>();
            bindings.AcquireLayer(layer);
            return bindings;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Context와 일치하는 현재 Binding으로 Selector가 선택하게 한다.
        /// </summary>
        // ------------------------------------------------------------
        protected static bool Select<TPayload>
        (
            AbilityUseSelector<TestSpec, TPayload, string> selector,
            in AbilityActivationContext<TestSpec> context,
            out AbilityUseSelection<TestSpec, string> selection,
            out string reason
        )
        {
            return selector.TrySelect(CreateBindings(context), context, out selection, out reason);
        }

        protected static AbilityUseSelector<TestSpec, TestPayload, string> CreateSelector
        (
            AbilityActivationPolicy<TestSpec> policy,
            Func<bool> isCurrent,
            Action transition = null
        )
        {
            return new AbilityUseSelector<TestSpec, TestPayload, string>
            (
                new Func<AbilityActivationContext<TestSpec>, TestPayload,
                    AbilityUseChoice<TestSpec, string>>[]
                {
                    (context, payload) => new AbilityUseChoice<TestSpec, string>
                    (
                        "action", policy, isCurrent, transition
                    ),
                }
            );
        }

        protected static bool Accept(in AbilityUse<string> use) => true;

        protected static AbilityActivationCheck TraceThenAllow(Action action)
        {
            action();
            return AbilityActivationCheck.Allow();
        }

        protected static AbilityActivationCheck Allow() => AbilityActivationCheck.Allow();

    #endregion

    }
}
