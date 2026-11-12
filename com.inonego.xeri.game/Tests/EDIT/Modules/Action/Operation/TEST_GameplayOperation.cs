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

    public sealed class TEST_GameplayOperation
    {

    #region 헬퍼

        private sealed class Counter
        {
            public int Value { get; set; }
        }

        private sealed class AddOperation : IGameplayOperation<Counter, int>
        {
            public GameplayOperationResult Invoke
            (
                in GameplayOperationContext<Counter, int> context
            )
            {
                if (context.Recipient == null)
                {
                    return GameplayOperationResult.Reject("recipient missing");
                }

                if (context.Project == 0)
                {
                    return GameplayOperationResult.NoOp("zero delta");
                }

                context.Recipient.Value += context.Project;
                return GameplayOperationResult.Applied();
            }
        }

        private sealed class FollowupStageRequirement :
            IAbilityActivationRequirement<object, int>
        {
            public AbilityActivationCheck Evaluate
            (
                in AbilityActivationContext<object, int> context
            )
            {
                return context.Ability.State.Values.GetI("FollowupStage") >= 2
                    ? AbilityActivationCheck.Allow()
                    : AbilityActivationCheck.Reject("followup stage missing");
            }
        }

        private sealed class AbilityFeedback
        {
            public float CooldownReduction { get; }
            public int ChargeRestore { get; }
            public int FollowupStage { get; }

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
            public GameplayOperationResult Invoke
            (
                in GameplayOperationContext<AbilityState, AbilityFeedback> context
            )
            {
                if (context.Recipient == null)
                {
                    return GameplayOperationResult.Reject("ability state missing");
                }

                if (context.Project.CooldownReduction > 0f)
                {
                    context.Recipient.Control.ReduceCooldown
                    (
                        context.Project.CooldownReduction
                    );
                }

                if (context.Project.ChargeRestore > 0)
                {
                    context.Recipient.Control.RestoreCharge
                    (
                        context.Project.ChargeRestore
                    );
                }

                if (context.Project.FollowupStage > 0)
                {
                    context.Recipient.Values.SetI("FollowupStage", context.Project.FollowupStage);
                }

                return GameplayOperationResult.Applied();
            }
        }

    #endregion

    #region O-1: 호출 결과와 기본값

        [Test]
        public void TEST_GameplayOperationResult_미초기화기본값은적용성공이아님()
        {
            var result = default(GameplayOperationResult);
            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.IsApplied);
            Assert.AreEqual(GameplayOperationStatus.Rejected, result.Status);
        }

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
            var activationContext = new AbilityActivationContext<object, int>
            (
                null,
                null,
                binding,
                0
            );
            var policy = new AbilityActivationPolicy<object, int>
            (
                new IAbilityActivationRequirement<object, int>[]
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
