/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityActivation.cs
수정일 : 2026-11-17

# 설명
Ability 사용 요청의 Owner·Instigator·Binding 및 Payload를 보존한다.
선택한 실행과 승인 정책을 한 번의 사용 시도로 묶고, 실행 시작이 실패하면 소비한 비용을 역순으로 환급한다.
선택 후 Binding·State가 바뀌면 현재성 토큰으로 거부하며 Payload는 형식을 정하지 않는 object로 전달한다.
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
        StartRejected = 5,
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
    public readonly struct AbilityActivationContext<TDefinition>
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
        /// 게임이 요청에 덧붙인 Payload를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public object Payload { get; }

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
            object payload
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
            Payload = payload;
        }
    }

    // ============================================================
    /// <summary>
    /// 사용의 출처인 Ability Key와 입력 Entry Key를 묶는 값.
    /// </summary>
    // ============================================================
    public readonly struct AbilityUseOrigin
    {

        // ------------------------------------------------------------
        /// <summary>
        /// 사용한 Ability의 Key를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityKey AbilityKey { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 사용을 요청한 입력 Entry Key를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityEntryKey EntryKey { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Ability Key와 Entry Key의 출처를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityUseOrigin
        (
            AbilityKey abilityKey,
            AbilityEntryKey entryKey
        )
        {
            AbilityKey = abilityKey;
            EntryKey = entryKey;
        }
    }

    // ============================================================
    /// <summary>
    /// 승인된 한 번의 사용을 출처와 선택된 실행으로 전달하는 값.
    /// </summary>
    // ============================================================
    public readonly struct AbilityUse<TExecution>
    {

        // ------------------------------------------------------------
        /// <summary>
        /// 사용의 출처를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityUseOrigin Origin { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 선택된 실행 정의를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public TExecution Execution { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 출처와 실행 정의를 묶은 사용을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityUse
        (
            AbilityUseOrigin origin,
            TExecution execution
        )
        {
            Origin = origin;
            Execution = execution;
        }
    }

    // ============================================================
    /// <summary>
    /// <br/> 승인된 사용으로 실제 실행을 시작하고 시작 성공 여부를 반환한다.
    /// <br/> false를 반환하거나 예외를 던지면 이미 소비한 비용을 환급한다.
    /// </summary>
    // ============================================================
    public delegate bool AbilityBegin<TExecution>(in AbilityUse<TExecution> use);

    // ============================================================
    /// <summary>
    /// 한 규칙이 선택한 실행, 승인 정책과 한 번 적용할 상태 전이를 보관한다.
    /// </summary>
    // ============================================================
    public sealed class AbilityUseChoice<TDefinition, TExecution>
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
        public AbilityActivationPolicy<TDefinition> Policy { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 선택이 여전히 유효한지 확인하는 추가 조건을 반환하며 없으면 null이다.
        /// </summary>
        // ------------------------------------------------------------
        internal Func<bool> IsCurrent { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 실행 시작 성공 후 한 번 실행할 상태 전이를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal Action Transition { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 실행 정의, 승인 정책과 선택적 현재성 조건을 하나의 선택으로 묶는다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityUseChoice
        (
            TExecution execution,
            AbilityActivationPolicy<TDefinition> policy,
            Func<bool> isCurrent = null,
            Action transition = null
        )
        {
            Execution = execution;
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            IsCurrent = isCurrent;
            Transition = transition;
        }
    }

    // ======================================================================
    /// <summary>
    /// 상태를 바꾸지 않는 규칙을 순서대로 평가해 첫 번째 사용 후보를 선택한다.
    /// </summary>
    // ======================================================================
    public sealed class AbilityUseSelector<TDefinition, TPayload, TExecution>
    where TDefinition : class
    {

    #region 규칙 구성

        private readonly Func<AbilityActivationContext<TDefinition>, TPayload,
            AbilityUseChoice<TDefinition, TExecution>>[] rules;

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 선택 규칙을 순서대로 복사해 보관한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityUseSelector
        (
            IReadOnlyList<Func<AbilityActivationContext<TDefinition>, TPayload,
                AbilityUseChoice<TDefinition, TExecution>>> rules
        )
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            this.rules = new Func<AbilityActivationContext<TDefinition>, TPayload,
                AbilityUseChoice<TDefinition, TExecution>>[rules.Count];
            for (var index = 0; index < rules.Count; index++)
            {
                this.rules[index] = rules[index] ?? throw new ArgumentException(nameof(rules));
            }
        }

    #endregion

    #region 사용 선택

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 첫 번째 일치 규칙을 선택하고 실행 및 승인 정책을 반환한다.
        /// <br/> 선택 당시의 Binding·State Revision을 기록해 이후 현재성 판정에 사용한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool TrySelect
        (
            AbilityBindings<TDefinition> bindings,
            in AbilityActivationContext<TDefinition> context,
            out AbilityUseSelection<TDefinition, TExecution> selection,
            out string reason
        )
        {
            if (bindings == null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }

            if (context.Ability == null)
            {
                selection = null;
                reason = "유효한 Ability Binding이 필요합니다.";
                return false;
            }

            var payload = ConvertPayload(context.Payload);

            foreach (var rule in rules)
            {
                var choice = rule(context, payload);
                if (choice != null)
                {
                    selection = new AbilityUseSelection<TDefinition, TExecution>(bindings, context, choice);
                    reason = string.Empty;
                    return true;
                }
            }

            selection = null;
            reason = "일치하는 Ability 사용 규칙이 없습니다.";
            return false;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// Context의 Payload 값을 규칙이 받는 형식으로 변환하며 맞지 않으면 예외를 전달한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private static TPayload ConvertPayload(object payload)
        {
            if (payload is TPayload value)
            {
                return value;
            }

            if (payload == null &&
                (!typeof(TPayload).IsValueType || Nullable.GetUnderlyingType(typeof(TPayload)) != null))
            {
                return default;
            }

            throw new ArgumentException
            (
                $"Payload를 {typeof(TPayload).Name} 형식으로 변환할 수 없습니다.",
                nameof(payload)
            );
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 선택한 실행과 정책을 한 번 사용하며 실행 시작 후 상태 전이를 적용한다.
    /// </summary>
    // ============================================================
    public sealed class AbilityUseSelection<TDefinition, TExecution>
    where TDefinition : class
    {

    #region 선택 결과

        // ------------------------------------------------------------
        /// <summary>
        /// 선택 당시의 요청 Context를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationContext<TDefinition> Context { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 선택된 실행 정의를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public TExecution Execution => choice.Execution;

        private readonly AbilityUseChoice<TDefinition, TExecution> choice;
        private readonly AbilityBindings<TDefinition> bindings;
        private readonly AbilityInstance<TDefinition> instance;
        private readonly ulong bindingsRevision;
        private readonly ulong stateRevision;
        private bool attempted;

        // ------------------------------------------------------------
        /// <summary>
        /// 선택 규칙과 요청 Context, 선택 당시의 Revision을 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        internal AbilityUseSelection
        (
            AbilityBindings<TDefinition> bindings,
            in AbilityActivationContext<TDefinition> context,
            AbilityUseChoice<TDefinition, TExecution> choice
        )
        {
            Context = context;
            this.choice = choice;
            this.bindings = bindings;
            instance = context.Ability;
            bindingsRevision = bindings.Revision;
            stateRevision = instance.State.Revision;
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
                : choice.Policy.Evaluate(Context, IsCurrent);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 단 한 번 비용을 검사·소비하고 실행을 시작한 뒤 상태 전이를 적용한다.
        /// <br/> 실행 시작이 거부되거나 예외로 끝나면 소비한 비용을 역순으로 환급한다.
        /// <br/> 시작 성공 후의 전이 예외로는 소비한 비용을 되돌리지 않는다.
        /// <br/> 거부되더라도 이 선택은 재사용하지 않고 새로 선택해야 한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool TryActivate
        (
            AbilityBegin<TExecution> begin,
            out AbilityActivationResult result,
            out AbilityUse<TExecution> use
        )
        {
            if (begin == null)
            {
                throw new ArgumentNullException(nameof(begin));
            }

            use = default;

            if (attempted)
            {
                result = AbilityActivationResult.Reject
                (
                    AbilityActivationFailure.InvalidState,
                    reason: "이미 시도한 사용 선택입니다."
                );
                return false;
            }

            var state = instance.State;
            state.EnterUse();
            try
            {
                // Cost·Begin·Transition 콜백의 재진입에도 같은 선택을 다시 소비하지 않는다.
                attempted = true;

                var receipt = new List<AbilityCostReceipt>();
                var candidate = default(AbilityUse<TExecution>);
                bool started;
                try
                {
                    if (!choice.Policy.TryActivate(Context, IsCurrent, receipt, out result))
                    {
                        return false;
                    }

                    candidate = new AbilityUse<TExecution>
                    (
                        new AbilityUseOrigin(instance.Key, Context.EntryKey),
                        choice.Execution
                    );

                    started = begin(candidate);
                }
                catch (Exception exception)
                {
                    // Refund가 예외를 던지면 원래 예외를 첫 항목으로 포함해 대신 전달한다.
                    Refund(receipt, exception);
                    throw;
                }

                if (!started)
                {
                    result = AbilityActivationResult.Reject
                    (
                        AbilityActivationFailure.StartRejected,
                        reason: "Ability 실행 시작이 거부됐습니다."
                    );
                    Refund(receipt, null);
                    return false;
                }

                // 시작 성공 후의 전이 예외로는 이미 소비한 비용을 환급하지 않는다.
                choice.Transition?.Invoke();

                use = candidate;
                result = AbilityActivationResult.Success();
                return true;
            }
            finally
            {
                state.ExitUse();
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 선택 당시의 Binding·State Revision과 추가 현재성 조건이 유지되는지 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private bool IsCurrent()
        {
            return bindings.Revision == bindingsRevision
                && instance.State.Revision == stateRevision
                && (choice.IsCurrent == null || choice.IsCurrent());
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 소비한 비용을 역순으로 환급하고 환급 실패는 원래 예외와 함께 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        private static void Refund
        (
            List<AbilityCostReceipt> receipt,
            Exception original
        )
        {
            List<Exception> errors = null;
            if (original != null)
            {
                (errors ??= new List<Exception>()).Add(original);
            }

            var failed = false;
            for (var index = receipt.Count - 1; index >= 0; index--)
            {
                try
                {
                    receipt[index].Refund();
                }
                catch (Exception exception)
                {
                    failed = true;
                    (errors ??= new List<Exception>()).Add(exception);
                }
            }

            // 환급 실패가 없으면 원래 예외는 호출자가 그대로 다시 던진다.
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

    #endregion

    }

}
