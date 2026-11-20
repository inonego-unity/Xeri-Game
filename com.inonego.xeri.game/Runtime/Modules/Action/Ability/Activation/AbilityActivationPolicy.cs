/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityActivationPolicy.cs
수정일 : 2026-11-19

# 설명
Requirement와 모든 Cost를 상태 변경 없이 검사한 뒤 등록 순서대로 비용을 소비한다.
Pay는 소비를 되돌리는 AbilityCostReceipt를 반환한다. 소비 도중 예외가 나면 정책이 앞서 소비한 비용을 역순으로 환급하고
예외를 다시 던지므로 지불 단계의 사후 조건은 호출 경로와 무관하게 같다. 실행 시작 실패의 환급은 Selection이 맡는다.
영수증으로 되돌리지 않는 소비(NoChange)와 한 Cost 안의 여러 자원 원자성은 Resource Owner가 책임진다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime;
using System.Runtime.ExceptionServices;

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

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 영수증을 역순으로 모두 환급한다. 환급 실패가 없으면 그대로 반환해
        /// <br/> 호출자가 원래 예외를 다시 던지게 한다. 환급 실패가 있으면 원래 예외를
        /// <br/> 첫 항목으로 한 오류들을 하나면 그대로, 여럿이면 묶어서 던진다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal static void RefundAll(IReadOnlyList<AbilityCostReceipt> receipts, Exception original)
        {
            List<Exception> errors = null;
            if (original != null)
            {
                (errors ??= new List<Exception>()).Add(original);
            }

            var failed = false;
            for (var index = receipts.Count - 1; index >= 0; index--)
            {
                try
                {
                    receipts[index].Refund();
                }
                catch (Exception exception)
                {
                    failed = true;
                    (errors ??= new List<Exception>()).Add(exception);
                }
            }

            if (!failed)
            {
                return;
            }

            if (errors.Count == 1)
            {
                ExceptionDispatchInfo.Capture(errors[0]).Throw();
            }

            throw new AggregateException("Ability 사용 비용 환급 중 오류가 발생했습니다.", errors);
        }
    }

    // ============================================================
    /// <summary>
    /// Ability 사용 조건을 상태 변경 없이 평가한다.
    /// </summary>
    // ============================================================
    public interface IAbilityActivationRequirement<TSpec>
    where TSpec : class
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 현재 요청 맥락으로 사용 가능 여부를 평가한다.
        /// </summary>
        // ------------------------------------------------------------
        AbilityActivationCheck Evaluate
        (
            in AbilityActivationContext<TSpec> context
        );
    }

    // ============================================================
    /// <summary>
    /// 일반 사용의 Cooldown 완료 여부를 검사한다.
    /// </summary>
    // ============================================================
    public sealed class AbilityCooldownRequirement<TSpec> :
        IAbilityActivationRequirement<TSpec>
    where TSpec : class
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Ability의 Cooldown 기준 사용 가능 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationCheck Evaluate
        (
            in AbilityActivationContext<TSpec> context
        )
        {
            return context.Ability?.State?.IsCooldownReady == true
                ? AbilityActivationCheck.Allow()
                : AbilityActivationCheck.Reject("Cooldown이 진행 중입니다.");
        }
    }

    // ================================================================================
    /// <summary>
    /// <br/> 비용의 순수 검사와 실제 소비를 Resource Owner에 위임한다.
    /// <br/> 검사를 통과한 Pay는 정상 상황에서 실패하지 않아야 한다.
    /// <br/> Pay가 반환한 영수증으로 뒤 비용의 소비 실패나 실행 시작 실패 시 소비를 되돌린다.
    /// </summary>
    // ================================================================================
    public interface IAbilityActivationCost<TSpec>
    where TSpec : class
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 현재 비용을 소비할 수 있는지 상태 변경 없이 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        AbilityActivationCheck Evaluate
        (
            in AbilityActivationContext<TSpec> context
        );

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 검사된 비용을 Resource Owner의 책임 아래 소비하고 되돌릴 영수증을 반환한다.
        /// <br/> 여러 자원을 반드시 함께 적용해야 하면 해당 Owner가 원자성을 보장한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        AbilityCostReceipt Pay
        (
            in AbilityActivationContext<TSpec> context
        );
    }

    // ============================================================
    /// <summary>
    /// 모든 사용 조건과 비용을 검사한 뒤 실제 비용을 소비하는 정책.
    /// </summary>
    // ============================================================
    public sealed class AbilityActivationPolicy<TSpec>
    where TSpec : class
    {

    #region 정책 구성

        private readonly IAbilityActivationRequirement<TSpec>[] requirement;
        private readonly IAbilityActivationCost<TSpec>[] cost;

        // ------------------------------------------------------------
        /// <summary>
        /// 사용 조건과 비용을 등록 순서대로 복사한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationPolicy
        (
            IReadOnlyList<IAbilityActivationRequirement<TSpec>> requirement = null,
            IReadOnlyList<IAbilityActivationCost<TSpec>> cost = null
        )
        {
            this.requirement = CopyEntries(requirement, nameof(requirement));
            this.cost = CopyEntries(cost, nameof(cost));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 등록 목록의 항목 참조를 배열로 옮겨 이후 원본 목록 변경과 분리한다.
        /// <br/> 항목 객체는 복제하지 않으며 null 항목은 거부한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private static T[] CopyEntries<T>(IReadOnlyList<T> source, string parameterName)
        where T : class
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<T>();
            }

            var result = new T[source.Count];
            for (var index = 0; index < source.Count; index++)
            {
                result[index] = source[index] ?? throw new ArgumentException
                (
                    "null 항목은 등록할 수 없습니다.", parameterName
                );
            }

            return result;
        }

    #endregion

    #region 조건 검사 및 소비

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 모든 Requirement와 Cost를 평가하고 선택의 현재성까지 확인하되
        /// <br/> 어떤 비용도 소비하지 않는다. 공개 검사 진입점은
        /// <br/> AbilityUseSelection.Evaluate이며 이 메서드는 그 내부 구현이다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal AbilityActivationResult Evaluate
        (
            in AbilityActivationContext<TSpec> context,
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

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 모든 비용을 다시 검사한 뒤 순서대로 소비하며 영수증을 소비 순서대로 담는다.
        /// <br/> 소비 도중 예외가 나면 이 호출에서 담은 영수증을 역순으로 환급하고 예외를 다시 던진다.
        /// <br/> 성공하면 이후 단계의 환급을 위해 담은 영수증을 호출자에게 넘긴다.
        /// <br/> 공개 사용 진입점은 AbilityUseSelection.TryActivate이며
        /// <br/> 이 메서드는 그 내부 구현이다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal bool TryActivate
        (
            in AbilityActivationContext<TSpec> context,
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

            try
            {
                for (var index = 0; index < cost.Length; index++)
                {
                    receipt.Add(cost[index].Pay(context));
                }
            }
            catch (Exception exception)
            {
                // 환급이 예외를 던지면 원래 예외를 첫 항목으로 포함해 대신 전달한다.
                AbilityCostReceipt.RefundAll(receipt, exception);
                throw;
            }

            return true;
        }

    #endregion

    }
}
