/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityUseSelection.cs
수정일 : 2026-11-19

# 설명
사용 출처(Origin)·사용(Use)·시작 대리자와, 규칙으로 사용 후보(Choice)를 고르는 Selector·Selection을 제공한다.
선택한 실행과 승인 정책을 한 번의 사용 시도로 묶고, 실행 시작이 실패하면 소비한 비용을 역순으로 환급한다.
소비 도중 예외의 환급은 정책(AbilityActivationPolicy)이 맡아 두 사용 경로의 지불 단계 사후 조건이 같다.
선택 후 Binding·State가 바뀌면 현재성 토큰으로 거부한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
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
        /// 선택된 실행 Spec을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public TExecution Execution { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 출처와 실행 Spec을 묶은 사용을 생성한다.
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
    public sealed class AbilityUseChoice<TSpec, TExecution>
    where TSpec : class
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 선택한 실행 Spec을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public TExecution Execution { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 사용 승인과 비용 소비를 담당하는 정책을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationPolicy<TSpec> Policy { get; }

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
        /// 실행 Spec, 승인 정책과 선택적 현재성 조건을 하나의 선택으로 묶는다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityUseChoice
        (
            TExecution execution,
            AbilityActivationPolicy<TSpec> policy,
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
    public sealed class AbilityUseSelector<TSpec, TPayload, TExecution>
    where TSpec : class
    {

    #region 규칙 구성

        private readonly Func<AbilityActivationContext<TSpec>, TPayload,
            AbilityUseChoice<TSpec, TExecution>>[] rules;

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 선택 규칙을 순서대로 복사해 보관한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityUseSelector
        (
            IReadOnlyList<Func<AbilityActivationContext<TSpec>, TPayload,
                AbilityUseChoice<TSpec, TExecution>>> rules
        )
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            this.rules = new Func<AbilityActivationContext<TSpec>, TPayload,
                AbilityUseChoice<TSpec, TExecution>>[rules.Count];
            for (var index = 0; index < rules.Count; index++)
            {
                this.rules[index] = rules[index] ?? throw new ArgumentException("선택 규칙에 null이 있습니다.", nameof(rules));
            }
        }

    #endregion

    #region 사용 선택

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 첫 번째 일치 규칙을 선택하고 실행 및 승인 정책을 반환한다.
        /// <br/> Context의 Binding이 bindings의 현재 Entry 연결과 같은 Instance여야 선택한다.
        /// <br/> Binding·State Revision은 규칙 실행 전에 기록해 이후 현재성 판정에 사용한다.
        /// <br/> 규칙이 실행 중 바꾼 Binding과 State는 선택 이후의 변경으로 취급한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool TrySelect
        (
            AbilityBindings<TSpec> bindings,
            in AbilityActivationContext<TSpec> context,
            out AbilityUseSelection<TSpec, TExecution> selection,
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

            if
            (
                !bindings.TryResolve(context.EntryKey, out var current) ||
                !ReferenceEquals(current.Instance, context.Ability)
            )
            {
                selection = null;
                reason = "Context의 Binding이 현재 Entry 연결과 일치하지 않습니다.";
                return false;
            }

            var payload = ConvertPayload(context.Payload);
            var bindingsRevision = bindings.Revision;
            var stateRevision = context.Ability.State.Revision;

            foreach (var rule in rules)
            {
                var choice = rule(context, payload);
                if (choice != null)
                {
                    selection = new AbilityUseSelection<TSpec, TExecution>
                    (
                        bindings, context, choice, bindingsRevision, stateRevision
                    );
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
    public sealed class AbilityUseSelection<TSpec, TExecution>
    where TSpec : class
    {

    #region 선택 결과

        // ------------------------------------------------------------
        /// <summary>
        /// 선택 당시의 요청 Context를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationContext<TSpec> Context { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 선택된 실행 Spec을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public TExecution Execution => choice.Execution;

        private readonly AbilityUseChoice<TSpec, TExecution> choice;
        private readonly AbilityBindings<TSpec> bindings;
        private readonly AbilityInstance<TSpec> instance;
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
            AbilityBindings<TSpec> bindings,
            in AbilityActivationContext<TSpec> context,
            AbilityUseChoice<TSpec, TExecution> choice,
            ulong bindingsRevision,
            ulong stateRevision
        )
        {
            Context = context;
            this.choice = choice;
            this.bindings = bindings;
            instance = context.Ability;
            this.bindingsRevision = bindingsRevision;
            this.stateRevision = stateRevision;
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
        /// <br/> 소비 도중 예외는 정책이, 실행 시작 거부·예외는 이 선택이
        /// <br/> 소비한 비용을 역순으로 환급한다.
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

                // 지불 단계의 실패와 그 환급은 정책이 맡는다. 여기서는 시작 단계의 환급만 맡는다.
                var receipt = new List<AbilityCostReceipt>();
                if (!choice.Policy.TryActivate(Context, IsCurrent, receipt, out result))
                {
                    return false;
                }

                var candidate = new AbilityUse<TExecution>
                (
                    new AbilityUseOrigin(instance.Key, Context.EntryKey),
                    choice.Execution
                );

                bool started;
                try
                {
                    started = begin(candidate);
                }
                catch (Exception exception)
                {
                    // 환급이 예외를 던지면 원래 예외를 첫 항목으로 포함해 대신 전달한다.
                    AbilityCostReceipt.RefundAll(receipt, exception);
                    throw;
                }

                if (!started)
                {
                    result = AbilityActivationResult.Reject
                    (
                        AbilityActivationFailure.StartRejected,
                        reason: "Ability 실행 시작이 거부됐습니다."
                    );
                    AbilityCostReceipt.RefundAll(receipt, null);
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

    #endregion

    }
}
