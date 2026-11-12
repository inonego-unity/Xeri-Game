/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityActivation.cs
수정일 : 2026-11-09

# 설명
Ability 사용 요청의 Owner·Instigator·Binding 및 Project Context를 보존한다.
선택한 실행과 승인 정책을 한 번의 사용 시도로 묶으며 비용의 사후 복구를 소유하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Ability activation 실패 원인을 호출자에게 전달하는 분류.
    /// </summary>
    // ============================================================
    public enum AbilityActivationFailure
    {
        Uninitialized = 0,
        None = 1,
        InvalidState = 2,
        RequirementRejected = 3,
        CostRejected = 4,
    }

    // ============================================================
    /// <summary>
    /// Requirement가 허용 여부와 거부 사유를 반환하는 읽기 전용 결과.
    /// </summary>
    // ============================================================
    public readonly struct AbilityActivationCheck
    {

        // ------------------------------------------------------------
        /// <summary>
        /// Requirement 결과가 사용을 허용하는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsAllowed { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 거부 이유를 반환하며 허용 시 빈 문자열이다.
        /// </summary>
        // ------------------------------------------------------------
        public string Reason { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 요청 평가 결과를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        private AbilityActivationCheck(bool isAllowed, string reason)
        {
            IsAllowed = isAllowed;
            Reason = reason ?? string.Empty;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 사용을 허용하는 평가 결과를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public static AbilityActivationCheck Allow() => new(true, string.Empty);

        // ------------------------------------------------------------
        /// <summary>
        /// 사유가 포함된 사용 거부 결과를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public static AbilityActivationCheck Reject(string reason = null) => new(false, reason);
    }

    // ======================================================================
    /// <summary>
    /// Activation 조건 평가와 비용 소비의 성공 또는 실패 위치를 전달하는 결과.
    /// </summary>
    // ======================================================================
    public readonly struct AbilityActivationResult
    {

        // ------------------------------------------------------------
        /// <summary>
        /// Ability 사용 승인이 완료됐는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Succeeded => Failure == AbilityActivationFailure.None;

        // ------------------------------------------------------------
        /// <summary>
        /// 실패 위치를 나타내는 분류를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationFailure Failure { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 거부된 Requirement 또는 Cost의 인덱스를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public int PolicyIndex { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 상세한 실패 사유를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public string Reason { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 성공 또는 실패 결과를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        private AbilityActivationResult
        (
            AbilityActivationFailure failure,
            int policyIndex,
            string reason
        )
        {
            Failure = failure;
            PolicyIndex = policyIndex;
            Reason = reason ?? string.Empty;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 성공한 Activation 결과를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public static AbilityActivationResult Success() => new(AbilityActivationFailure.None, -1, string.Empty);

        // ------------------------------------------------------------
        /// <summary>
        /// 실패 결과를 생성하며 None을 실패 사유로 사용하는 요청은 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        public static AbilityActivationResult Reject
        (
            AbilityActivationFailure failure,
            int policyIndex = -1,
            string reason = null
        )
        {
            if (failure == AbilityActivationFailure.None
                || failure == AbilityActivationFailure.Uninitialized)
            {
                throw new ArgumentOutOfRangeException(nameof(failure));
            }

            return new AbilityActivationResult(failure, policyIndex, reason);
        }
    }

    // ================================================================================
    /// <summary>
    /// Owner와 Instigator, 선택된 Binding과 프로젝트 정보를 함께 전달하는 요청 맥락.
    /// </summary>
    // ================================================================================
    public readonly struct AbilityActivationContext<TDefinition, TProjectContext>
    where TDefinition : class
    {

        // ------------------------------------------------------------
        /// <summary>
        /// 사용 상태를 소유한 Owner를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public object Owner { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 사용을 발생시킨 Instigator를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public object Instigator { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 입력 Entry와 해석된 인스턴스의 연결을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityBinding<TDefinition> Binding { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 입력 Entry가 아니라 실제로 해석된 Ability Instance를 제공한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityInstance<TDefinition> Ability => Binding.Instance;

        // ------------------------------------------------------------
        /// <summary>
        /// 해석에 사용한 입력 Entry Key를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityEntryKey EntryKey => Binding.EntryKey;

        // ------------------------------------------------------------
        /// <summary>
        /// 게임에서 제공한 요청 Context를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public TProjectContext Project { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Owner와 Binding을 연결한 사용 요청 Context를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationContext
        (
            object owner,
            object instigator,
            in AbilityBinding<TDefinition> binding,
            TProjectContext project
        )
        {
            if (binding.Instance == null)
            {
                throw new ArgumentException
                (
                    "유효한 Ability Binding이 필요합니다.",
                    nameof(binding)
                );
            }

            Owner = owner;
            Instigator = instigator;
            Binding = binding;
            Project = project;
        }
    }

    // ============================================================
    /// <summary>
    /// 한 규칙이 선택한 실행, 승인 정책과 한 번 적용할 상태 전이를 보관한다.
    /// </summary>
    // ============================================================
    public sealed class AbilityUseChoice<TDefinition, TProjectContext, TExecution>
    where TDefinition : class
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 선택한 실행 정의를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public TExecution Execution { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 사용 승인과 비용 소비를 담당하는 정책을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationPolicy<TDefinition, TProjectContext> Policy { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 선택이 여전히 유효한지 확인하는 조건을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal Func<bool> IsCurrent { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 비용 소비 후 한 번 실행할 상태 전이를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal Action Transition { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 실행 정의, 승인 정책과 현재성 조건을 하나의 선택으로 묶는다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityUseChoice
        (
            TExecution execution,
            AbilityActivationPolicy<TDefinition, TProjectContext> policy,
            Func<bool> isCurrent,
            Action transition = null
        )
        {
            Execution = execution;
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            IsCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
            Transition = transition;
        }
    }

    // ======================================================================
    /// <summary>
    /// 상태를 바꾸지 않는 규칙을 순서대로 평가해 첫 번째 사용 후보를 선택한다.
    /// </summary>
    // ======================================================================
    public sealed class AbilityUseSelector<TDefinition, TProjectContext, TExecution>
    where TDefinition : class
    {

    #region 규칙 구성

        private readonly Func<AbilityActivationContext<TDefinition, TProjectContext>,
            AbilityUseChoice<TDefinition, TProjectContext, TExecution>>[] rules;

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 선택 규칙을 순서대로 복사해 보관한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityUseSelector
        (
            IReadOnlyList<Func<AbilityActivationContext<TDefinition, TProjectContext>,
                AbilityUseChoice<TDefinition, TProjectContext, TExecution>>> rules
        )
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            this.rules = new Func<AbilityActivationContext<TDefinition, TProjectContext>,
                AbilityUseChoice<TDefinition, TProjectContext, TExecution>>[rules.Count];
            for (var index = 0; index < rules.Count; index++)
            {
                this.rules[index] = rules[index] ?? throw new ArgumentException(nameof(rules));
            }
        }

    #endregion

    #region 사용 선택

        // ------------------------------------------------------------
        /// <summary>
        /// 첫 번째 일치 규칙을 선택하고 실행 및 승인 정책을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TrySelect
        (
            in AbilityActivationContext<TDefinition, TProjectContext> context,
            out AbilityUseSelection<TDefinition, TProjectContext, TExecution> selection,
            out string reason
        )
        {
            if (context.Ability == null)
            {
                selection = null;
                reason = "유효한 Ability Binding이 필요합니다.";
                return false;
            }

            foreach (var rule in rules)
            {
                var choice = rule(context);
                if (choice != null)
                {
                    selection = new AbilityUseSelection<TDefinition, TProjectContext, TExecution>(context, choice);
                    reason = string.Empty;
                    return true;
                }
            }

            selection = null;
            reason = "일치하는 Ability 사용 규칙이 없습니다.";
            return false;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 선택한 실행과 정책을 한 번 사용하며 비용 소비 후 상태 전이를 적용한다.
    /// </summary>
    // ============================================================
    public sealed class AbilityUseSelection<TDefinition, TProjectContext, TExecution>
    where TDefinition : class
    {

    #region 선택 결과

        // ------------------------------------------------------------
        /// <summary>
        /// 선택 당시의 요청 Context를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationContext<TDefinition, TProjectContext> Context { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 선택된 실행 정의를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public TExecution Execution => choice.Execution;

        private readonly AbilityUseChoice<TDefinition, TProjectContext, TExecution> choice;
        private bool attempted;

        // ------------------------------------------------------------
        /// <summary>
        /// 선택 규칙과 요청 Context를 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        internal AbilityUseSelection
        (
            in AbilityActivationContext<TDefinition, TProjectContext> context,
            AbilityUseChoice<TDefinition, TProjectContext, TExecution> choice
        )
        {
            Context = context;
            this.choice = choice;
        }

    #endregion

    #region 사용 확정

        // ------------------------------------------------------------
        /// <summary>
        /// 선택의 현재성과 모든 비용을 소비 없이 평가한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationResult Evaluate()
        {
            return attempted
                ? AbilityActivationResult.Reject
                (
                    AbilityActivationFailure.InvalidState,
                    reason: "이미 시도한 사용 선택입니다."
                )
                : choice.Policy.Evaluate(Context, choice.IsCurrent);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 단 한 번 비용을 검사·소비하고 상태 전이를 적용한다.
        /// <br/> 이후 실행이나 알림 예외로 이미 소비한 비용을 되돌리지 않는다.
        /// <br/> 거부되더라도 이 선택은 재사용하지 않고 새로 선택해야 한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryActivate(out AbilityActivationResult result)
        {
            if (attempted)
            {
                result = AbilityActivationResult.Reject
                (
                    AbilityActivationFailure.InvalidState,
                    reason: "이미 시도한 사용 선택입니다."
                );
                return false;
            }

            // Cost·Transition 콜백의 재진입에도 같은 선택을 다시 소비하지 않는다.
            attempted = true;
            if (!choice.Policy.TryActivate(Context, choice.IsCurrent, out result))
            {
                return false;
            }

            choice.Transition?.Invoke();
            return true;
        }

    #endregion

    }

}
