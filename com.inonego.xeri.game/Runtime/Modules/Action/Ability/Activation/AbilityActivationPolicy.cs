/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityActivationPolicy.cs
수정일 : 2026-11-09

# 설명
Requirement와 모든 Cost를 상태 변경 없이 검사한 뒤 등록 순서대로 비용을 소비한다.
비용 소비와 외부 알림의 사후 복구는 Resource Owner·게임 정책의 책임이다.
서로 독립된 Owner의 Pay를 Xeri가 원자적으로 처리한다고 보장하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Ability 사용 조건을 상태 변경 없이 평가한다.
    /// </summary>
    // ============================================================
    public interface IAbilityActivationRequirement<TDefinition, TProjectContext>
    where TDefinition : class
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 현재 요청 맥락으로 사용 가능 여부를 평가한다.
        /// </summary>
        // ------------------------------------------------------------
        AbilityActivationCheck Evaluate
        (
            in AbilityActivationContext<TDefinition, TProjectContext> context
        );
    }

    // ============================================================
    /// <summary>
    /// 일반 사용의 Cooldown 완료 여부를 검사한다.
    /// </summary>
    // ============================================================
    public sealed class AbilityCooldownRequirement<TDefinition, TProjectContext> :
        IAbilityActivationRequirement<TDefinition, TProjectContext>
    where TDefinition : class
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Ability의 Cooldown 기준 사용 가능 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationCheck Evaluate
        (
            in AbilityActivationContext<TDefinition, TProjectContext> context
        )
        {
            return context.Ability?.State?.IsCooldownReady == true
                ? AbilityActivationCheck.Allow()
                : AbilityActivationCheck.Reject("Cooldown이 진행 중입니다.");
        }
    }

    // ============================================================
    /// <summary>
    /// <br/> 비용의 순수 검사와 실제 소비를 Resource Owner에 위임한다.
    /// <br/> 검사를 통과한 Pay는 정상 상황에서 실패하지 않아야 한다.
    /// <br/> 예외가 발생해도 이미 적용된 비용은 자동으로 복원하지 않는다.
    /// </summary>
    // ============================================================
    public interface IAbilityActivationCost<TDefinition, TProjectContext>
    where TDefinition : class
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 현재 비용을 소비할 수 있는지 상태 변경 없이 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        AbilityActivationCheck Evaluate
        (
            in AbilityActivationContext<TDefinition, TProjectContext> context
        );

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 검사된 비용을 Resource Owner의 책임 아래 소비한다.
        /// <br/> 여러 자원을 반드시 함께 적용해야 하면 해당 Owner가 원자성을 보장한다.
        /// </summary>
        // ----------------------------------------------------------------------
        void Pay
        (
            in AbilityActivationContext<TDefinition, TProjectContext> context
        );
    }

    // ============================================================
    /// <summary>
    /// 모든 사용 조건과 비용을 검사한 뒤 실제 비용을 소비하는 정책.
    /// </summary>
    // ============================================================
    public sealed class AbilityActivationPolicy<TDefinition, TProjectContext>
    where TDefinition : class
    {

    #region 정책 구성

        private readonly IAbilityActivationRequirement<TDefinition, TProjectContext>[] requirement;
        private readonly IAbilityActivationCost<TDefinition, TProjectContext>[] cost;

        // ------------------------------------------------------------
        /// <summary>
        /// 사용 조건과 비용을 등록 순서대로 복사한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationPolicy
        (
            IReadOnlyList<IAbilityActivationRequirement<TDefinition, TProjectContext>> requirement = null,
            IReadOnlyList<IAbilityActivationCost<TDefinition, TProjectContext>> cost = null
        )
        {
            this.requirement = CopyRequirement(requirement);
            this.cost = CopyCost(cost);
        }

    #endregion

    #region 조건 검사 및 소비

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 조건과 비용을 먼저 검사하고 성공하면 비용을 순서대로 소비한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryActivate
        (
            in AbilityActivationContext<TDefinition, TProjectContext> context,
            out AbilityActivationResult result
        ) => TryActivate(context, null, out result);

        // ------------------------------------------------------------
        /// <summary>
        /// 비용 소비 없이 모든 Requirement와 Cost를 평가한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationResult Evaluate
        (
            in AbilityActivationContext<TDefinition, TProjectContext> context
        ) => Evaluate(context, null);

        // ------------------------------------------------------------
        /// <summary>
        /// 선택의 현재성까지 확인하되 어떤 비용도 소비하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        internal AbilityActivationResult Evaluate
        (
            in AbilityActivationContext<TDefinition, TProjectContext> context,
            Func<bool> isCurrent
        )
        {
            if (context.Ability?.State == null || (isCurrent != null && !isCurrent()))
            {
                return AbilityActivationResult.Reject
                (
                    AbilityActivationFailure.InvalidState,
                    reason: "Ability 사용 선택이 유효하지 않습니다."
                );
            }

            for (var index = 0; index < requirement.Length; index++)
            {
                var check = requirement[index].Evaluate(context);
                if (!check.IsAllowed)
                {
                    return AbilityActivationResult.Reject
                    (
                        AbilityActivationFailure.RequirementRejected, index, check.Reason
                    );
                }
            }

            for (var index = 0; index < cost.Length; index++)
            {
                var check = cost[index].Evaluate(context);
                if (!check.IsAllowed)
                {
                    return AbilityActivationResult.Reject
                    (
                        AbilityActivationFailure.CostRejected, index, check.Reason
                    );
                }
            }

            if (isCurrent != null && !isCurrent())
            {
                return AbilityActivationResult.Reject
                (
                    AbilityActivationFailure.InvalidState,
                    reason: "검사 중 Ability 사용 선택이 변경됐습니다."
                );
            }

            return AbilityActivationResult.Success();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 비용을 다시 검사한 뒤 순서대로 소비한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool TryActivate
        (
            in AbilityActivationContext<TDefinition, TProjectContext> context,
            Func<bool> isCurrent,
            out AbilityActivationResult result
        )
        {
            result = Evaluate(context, isCurrent);
            if (!result.Succeeded)
            {
                return false;
            }

            // 비용 소비 중 예외가 발생하면 이미 적용된 소비는 복원하지 않는다.
            for (var index = 0; index < cost.Length; index++)
            {
                cost[index].Pay(context);
            }

            return true;
        }

    #endregion

    #region 목록 복사

        // ------------------------------------------------------------
        /// <summary>
        /// Requirement 등록 목록을 검증하고 보관한다.
        /// </summary>
        // ------------------------------------------------------------
        private static IAbilityActivationRequirement<TDefinition, TProjectContext>[] CopyRequirement
        (
            IReadOnlyList<IAbilityActivationRequirement<TDefinition, TProjectContext>> source
        )
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<IAbilityActivationRequirement<TDefinition, TProjectContext>>();
            }

            var result = new IAbilityActivationRequirement<TDefinition, TProjectContext>[source.Count];
            for (var index = 0; index < source.Count; index++)
            {
                result[index] = source[index] ?? throw new ArgumentException(nameof(source));
            }

            return result;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Cost 등록 목록을 검증하고 보관한다.
        /// </summary>
        // ------------------------------------------------------------
        private static IAbilityActivationCost<TDefinition, TProjectContext>[] CopyCost
        (
            IReadOnlyList<IAbilityActivationCost<TDefinition, TProjectContext>> source
        )
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<IAbilityActivationCost<TDefinition, TProjectContext>>();
            }

            var result = new IAbilityActivationCost<TDefinition, TProjectContext>[source.Count];
            for (var index = 0; index < source.Count; index++)
            {
                result[index] = source[index] ?? throw new ArgumentException(nameof(source));
            }

            return result;
        }

    #endregion

    }
}
