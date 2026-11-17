/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityActivationPolicy.cs
수정일 : 2026-11-17

# 설명
Requirement와 모든 Cost를 상태 변경 없이 검사한 뒤 등록 순서대로 비용을 소비한다.
Pay는 소비를 되돌리는 AbilityCostReceipt를 반환하며, 실행 시작이 실패하면 호출자가 역순으로 환급한다.
서로 독립된 Owner의 Pay를 Xeri가 원자적으로 처리한다고 보장하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 소비한 비용을 되돌리는 동작을 담아 호출자에게 전달하는 영수증.
    /// </summary>
    // ============================================================
    public readonly struct AbilityCostReceipt
    {
        private readonly Action refund;

        // ------------------------------------------------------------
        /// <summary>
        /// 소비한 것이 없어 환급할 내용이 없는 영수증을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public static AbilityCostReceipt NoChange => default;

        // ------------------------------------------------------------
        /// <summary>
        /// 환급 동작을 가진 영수증을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityCostReceipt(Action refund)
        {
            this.refund = refund ?? throw new ArgumentNullException(nameof(refund));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 소비한 비용을 되돌리며 환급할 내용이 없으면 아무 일도 하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        internal void Refund() => refund?.Invoke();
    }

    // ============================================================
    /// <summary>
    /// Ability 사용 조건을 상태 변경 없이 평가한다.
    /// </summary>
    // ============================================================
    public interface IAbilityActivationRequirement<TDefinition>
    where TDefinition : class
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 현재 요청 맥락으로 사용 가능 여부를 평가한다.
        /// </summary>
        // ------------------------------------------------------------
        AbilityActivationCheck Evaluate
        (
            in AbilityActivationContext<TDefinition> context
        );
    }

    // ============================================================
    /// <summary>
    /// 일반 사용의 Cooldown 완료 여부를 검사한다.
    /// </summary>
    // ============================================================
    public sealed class AbilityCooldownRequirement<TDefinition> :
        IAbilityActivationRequirement<TDefinition>
    where TDefinition : class
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Ability의 Cooldown 기준 사용 가능 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationCheck Evaluate
        (
            in AbilityActivationContext<TDefinition> context
        )
        {
            return context.Ability?.State?.IsCooldownReady == true
                ? AbilityActivationCheck.Allow()
                : AbilityActivationCheck.Reject("Cooldown이 진행 중입니다.");
        }
    }

    // ======================================================================
    /// <summary>
    /// <br/> 비용의 순수 검사와 실제 소비를 Resource Owner에 위임한다.
    /// <br/> 검사를 통과한 Pay는 정상 상황에서 실패하지 않아야 한다.
    /// <br/> Pay가 반환한 영수증으로 이후 실행 시작 실패 시 소비를 되돌린다.
    /// </summary>
    // ======================================================================
    public interface IAbilityActivationCost<TDefinition>
    where TDefinition : class
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 현재 비용을 소비할 수 있는지 상태 변경 없이 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        AbilityActivationCheck Evaluate
        (
            in AbilityActivationContext<TDefinition> context
        );

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 검사된 비용을 Resource Owner의 책임 아래 소비하고 되돌릴 영수증을 반환한다.
        /// <br/> 여러 자원을 반드시 함께 적용해야 하면 해당 Owner가 원자성을 보장한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        AbilityCostReceipt Pay
        (
            in AbilityActivationContext<TDefinition> context
        );
    }

    // ============================================================
    /// <summary>
    /// 모든 사용 조건과 비용을 검사한 뒤 실제 비용을 소비하는 정책.
    /// </summary>
    // ============================================================
    public sealed class AbilityActivationPolicy<TDefinition>
    where TDefinition : class
    {

    #region 정책 구성

        private readonly IAbilityActivationRequirement<TDefinition>[] requirement;
        private readonly IAbilityActivationCost<TDefinition>[] cost;

        // ------------------------------------------------------------
        /// <summary>
        /// 사용 조건과 비용을 등록 순서대로 복사한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationPolicy
        (
            IReadOnlyList<IAbilityActivationRequirement<TDefinition>> requirement = null,
            IReadOnlyList<IAbilityActivationCost<TDefinition>> cost = null
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
            in AbilityActivationContext<TDefinition> context,
            out AbilityActivationResult result
        ) => TryActivate(context, null, null, out result);

        // ------------------------------------------------------------
        /// <summary>
        /// 비용 소비 없이 모든 Requirement와 Cost를 평가한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationResult Evaluate
        (
            in AbilityActivationContext<TDefinition> context
        ) => Evaluate(context, null);

        // ------------------------------------------------------------
        /// <summary>
        /// 선택의 현재성까지 확인하되 어떤 비용도 소비하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        internal AbilityActivationResult Evaluate
        (
            in AbilityActivationContext<TDefinition> context,
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

        // ----------------------------------------------------------------------
        /// <summary>
        /// 모든 비용을 다시 검사한 뒤 순서대로 소비하며 영수증이 있으면 소비 순서대로 담는다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal bool TryActivate
        (
            in AbilityActivationContext<TDefinition> context,
            Func<bool> isCurrent,
            List<AbilityCostReceipt> receipt,
            out AbilityActivationResult result
        )
        {
            result = Evaluate(context, isCurrent);
            if (!result.Succeeded)
            {
                return false;
            }

            // 비용 소비 중 예외가 발생하면 이미 담은 영수증의 환급은 호출자가 맡는다.
            for (var index = 0; index < cost.Length; index++)
            {
                var paid = cost[index].Pay(context);
                receipt?.Add(paid);
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
        private static IAbilityActivationRequirement<TDefinition>[] CopyRequirement
        (
            IReadOnlyList<IAbilityActivationRequirement<TDefinition>> source
        )
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<IAbilityActivationRequirement<TDefinition>>();
            }

            var result = new IAbilityActivationRequirement<TDefinition>[source.Count];
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
        private static IAbilityActivationCost<TDefinition>[] CopyCost
        (
            IReadOnlyList<IAbilityActivationCost<TDefinition>> source
        )
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<IAbilityActivationCost<TDefinition>>();
            }

            var result = new IAbilityActivationCost<TDefinition>[source.Count];
            for (var index = 0; index < source.Count; index++)
            {
                result[index] = source[index] ?? throw new ArgumentException(nameof(source));
            }

            return result;
        }

    #endregion

    }
}
