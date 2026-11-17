/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_GameplayOperation.cs
수정일 : 2026-11-12

# 설명
GameplayOperation의 direct invocation, no-op/reject 결과와 AbilityState feedback 조합을 검증한다.
Cooldown·Charge·Owner 후속 단계가 이후 activation 평가에 반영되는 경계를 확인한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._GameplayOperation
{
    using Xeri;
    using Xeri.Game;

    // ======================================================================
    /// <summary>
    /// GameplayOperation의 적용 결과와 Ability 연동 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ======================================================================
    public sealed class TEST_GameplayOperation
    {

    #region 헬퍼

        private sealed class Counter
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 테스트 대상의 값을 읽고 설정한다.
            /// </summary>
            // ------------------------------------------------------------
            public int Value { get; set; }
        }

        private sealed class AddOperation : IGameplayOperation<Counter, int>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 전달받은 Context로 Operation을 실행하고 결과를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public GameplayOperationResult Invoke
            (
                in GameplayOperationContext<Counter, int> context
            )
            {
                if (context.Recipient == null)
                {
                    return GameplayOperationResult.Reject("recipient missing");
                }

                if (context.Payload == 0)
                {
                    return GameplayOperationResult.NoOp("zero delta");
                }

                context.Recipient.Value += context.Payload;
                return GameplayOperationResult.Applied();
            }
        }

        private sealed class FollowupStageRequirement :
            IAbilityActivationRequirement<object>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// Operation 적용 가능 여부를 평가한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityActivationCheck Evaluate
            (
                in AbilityActivationContext<object> context
            )
            {
                return context.Ability.State.Values.GetI("FollowupStage") >= 2
                    ? AbilityActivationCheck.Allow()
                    : AbilityActivationCheck.Reject("followup stage missing");
            }
        }

        private sealed class AbilityFeedback
        {
            // ------------------------------------------------------------
            /// <summary>
            /// AbilityFeedback의 Cooldown 감소량을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public float CooldownReduction { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// AbilityFeedback의 Charge 복원량을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int ChargeRestore { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// AbilityFeedback의 후속 단계 값을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int FollowupStage { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// Cooldown 감소, Charge 복원, 후속 단계를 담은 피드백을 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityFeedback
            (
                float cooldownReduction,
                int chargeRestore,
                int followupStage = 0
            )
            {
                CooldownReduction = cooldownReduction;
                ChargeRestore = chargeRestore;
                FollowupStage = followupStage;
            }
        }

        private sealed class AbilityFeedbackOperation :
            IGameplayOperation<AbilityState, AbilityFeedback>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// Operation을 실행하고 결과를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public GameplayOperationResult Invoke
            (
                in GameplayOperationContext<AbilityState, AbilityFeedback> context
            )
            {
                if (context.Recipient == null)
                {
                    return GameplayOperationResult.Reject("ability state missing");
                }

                if (context.Payload.CooldownReduction > 0f)
                {
                    context.Recipient.Control.ReduceCooldown
                    (
                        context.Payload.CooldownReduction
                    );
                }

                if (context.Payload.ChargeRestore > 0)
                {
                    context.Recipient.Control.RestoreCharge
                    (
                        context.Payload.ChargeRestore
                    );
                }

                if (context.Payload.FollowupStage > 0)
                {
                    context.Recipient.Values.SetI("FollowupStage", context.Payload.FollowupStage);
                }

                return GameplayOperationResult.Applied();
            }
        }

    #endregion

    #region O-1: 호출 결과와 기본값

        // ------------------------------------------------------------
        /// <summary>
        /// 미초기화 결과의 기본값이 적용 성공이 아닌지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_GameplayOperationResult_미초기화기본값은적용성공이아님()
        {
            var result = default(GameplayOperationResult);
            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.IsApplied);
            Assert.AreEqual(GameplayOperationStatus.Rejected, result.Status);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 직접 호출이 Recipient 상태를 변경하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_GameplayOperation_DirectInvocation이Recipient를변경()
        {
            var owner = new object();
            var instigator = new object();
            var origin = new object();
            var counter = new Counter();
            var context = new GameplayOperationContext<Counter, int>
            (
                owner,
                instigator,
                origin,
                counter,
                3
            );

            var result = GameplayOperationRunner.Invoke
            (
                new AddOperation(),
                context
            );

            Assert.IsTrue(result.IsApplied);
            Assert.AreEqual(3, counter.Value);
            Assert.AreSame(owner, context.Owner);
            Assert.AreSame(instigator, context.Instigator);
            Assert.AreSame(origin, context.Origin);
        }

    #endregion

    #region O-2: NoOp / Reject

        // ------------------------------------------------------------
        /// <summary>
        /// NoOp과 Reject가 서로 구분되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_GameplayOperation_NoOp과Reject를구분()
        {
            var noOp = GameplayOperationRunner.Invoke
            (
                new AddOperation(),
                new GameplayOperationContext<Counter, int>
                (
                    null,
                    null,
                    null,
                    new Counter(),
                    0
                )
            );
            var rejected = GameplayOperationRunner.Invoke<Counter, int>
            (
                null,
                default
            );

            Assert.IsTrue(noOp.Succeeded);
            Assert.IsTrue(noOp.IsNoOp);
            Assert.IsFalse(rejected.Succeeded);
            Assert.AreEqual(GameplayOperationStatus.Rejected, rejected.Status);
        }

    #endregion

    #region O-3: Ability 상태 Feedback

        // ----------------------------------------------------------------------
        /// <summary>
        /// Operation을 통해 Ability의 Cooldown을 감소시키고 Charge를 복구한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_GameplayOperation_AbilityStateControl로CooldownCharge변경()
        {
            var state = new AbilityState(new AbilityKey("Feedback"));
            state.Control.ConfigureCharge(3, 1f);
            state.Control.TryConsumeCharge(2);
            state.Control.TryBeginCooldown(5f);
            var context = new GameplayOperationContext
            <
                AbilityState,
                AbilityFeedback
            >
            (
                null,
                null,
                null,
                state,
                new AbilityFeedback(2f, 1)
            );

            var result = GameplayOperationRunner.Invoke
            (
                new AbilityFeedbackOperation(),
                context
            );

            Assert.IsTrue(result.IsApplied);
            Assert.AreEqual(3f, state.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(2, state.Charge.Current);
        }

    #endregion

    #region O-4: Owner 후속 단계와 Activation 평가

        // ------------------------------------------------------------
        /// <summary>
        /// Owner의 후속 단계가 다음 Activation 평가에 반영되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_GameplayOperation_Owner후속단계가다음Activation평가에반영()
        {
            var state = new AbilityState(new AbilityKey("Feedback"));
            var ability = new AbilityInstance<object>
            (
                new object(),
                state
            );
            var binding = new AbilityBinding<object>
            (
                new AbilityEntryKey("Skill"),
                ability
            );
            var activationContext = new AbilityActivationContext<object>
            (
                null,
                null,
                binding,
                0
            );
            var policy = new AbilityActivationPolicy<object>
            (
                new IAbilityActivationRequirement<object>[]
                {
                    new FollowupStageRequirement()
                }
            );

            Assert.IsFalse(policy.TryActivate(activationContext, out _));

            var feedback = new AbilityFeedback(0f, 0, 2);
            var operationContext = new GameplayOperationContext
            <
                AbilityState,
                AbilityFeedback
            >
            (
                null,
                null,
                null,
                state,
                feedback
            );

            var result = GameplayOperationRunner.Invoke
            (
                new AbilityFeedbackOperation(),
                operationContext
            );

            Assert.IsTrue(result.IsApplied);
            Assert.AreEqual(2, state.Values.GetI("FollowupStage"));
            Assert.IsTrue
            (
                policy.TryActivate
                (
                    activationContext,
                    out var activationResult
                )
            );
            Assert.IsTrue(activationResult.Succeeded);

            state.Values.Remove("FollowupStage");
            Assert.IsFalse(policy.TryActivate(activationContext, out _));
        }

    #endregion

    }
}
