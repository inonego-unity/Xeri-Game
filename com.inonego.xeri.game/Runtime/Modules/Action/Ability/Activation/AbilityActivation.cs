/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityActivation.cs
수정일 : 2026-11-23

# 설명
Ability 사용 시도의 실패 분류, 조건 검사 결과와 사용 결과를 표현한다.
사용 요청의 Owner·Instigator·Binding 및 Payload를 보존하며 Payload는 형식을 정하지 않는 object로 전달한다.
거부에는 사람이 읽는 Reason과 별개로 분기·현지화에 쓰는 안정적인 Code(문자열)를 선택적으로 붙인다. Code가 없으면 빈 문자열이다.
========================================================================= BLOCK_HEADER_END */

using System;

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

    // ======================================================================
    /// <summary>
    /// <br/> 내장 Requirement·Cost와 사용 시도가 거부할 때 붙이는 Code 값.
    /// <br/> 프로젝트의 Requirement·Cost는 자기 Code를 자유롭게 정하며
    /// <br/> 이 값과 겹치지 않게 한다.
    /// </summary>
    // ======================================================================
    public static class AbilityActivationCodes
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Cooldown이 진행 중이거나 일시정지 상태여서 거부됐다.
        /// </summary>
        // ------------------------------------------------------------
        public const string CooldownActive = "Ability.CooldownActive";

        // ------------------------------------------------------------
        /// <summary>
        /// Charge가 비용보다 적어 거부됐다.
        /// </summary>
        // ------------------------------------------------------------
        public const string ChargeShortage = "Ability.ChargeShortage";

        // ------------------------------------------------------------
        /// <summary>
        /// Ability 사용 선택이 유효하지 않아 거부됐다.
        /// </summary>
        // ------------------------------------------------------------
        public const string SelectionInvalid = "Ability.SelectionInvalid";

        // ------------------------------------------------------------
        /// <summary>
        /// 검사 중 Ability 사용 선택이 변경돼 거부됐다.
        /// </summary>
        // ------------------------------------------------------------
        public const string SelectionChanged = "Ability.SelectionChanged";

        // ------------------------------------------------------------
        /// <summary>
        /// 이미 시도한 사용 선택이어서 거부됐다.
        /// </summary>
        // ------------------------------------------------------------
        public const string SelectionAttempted = "Ability.SelectionAttempted";

        // ------------------------------------------------------------
        /// <summary>
        /// 실행 시작이 거부됐다.
        /// </summary>
        // ------------------------------------------------------------
        public const string StartRejected = "Ability.StartRejected";
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

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 거부를 분기·현지화에 쓰는 안정적인 Code를 반환하며 허용 시와 Code가 없으면 빈 문자열이다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public string Code { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 요청 평가 결과를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        private AbilityActivationCheck(bool isAllowed, string reason, string code)
        {
            IsAllowed = isAllowed;
            Reason = reason ?? string.Empty;
            Code = code ?? string.Empty;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 사용을 허용하는 평가 결과를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public static AbilityActivationCheck Allow() => new(true, string.Empty, string.Empty);

        // ------------------------------------------------------------
        /// <summary>
        /// 사유와 선택적 Code가 포함된 사용 거부 결과를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public static AbilityActivationCheck Reject(string reason = null, string code = null) => new(false, reason, code);
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

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 거부를 분기·현지화에 쓰는 안정적인 Code를 반환하며 성공 시와 Code가 없으면 빈 문자열이다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public string Code { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 성공 또는 실패 결과를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        private AbilityActivationResult
        (
            AbilityActivationFailure failure,
            int policyIndex,
            string reason,
            string code
        )
        {
            Failure = failure;
            PolicyIndex = policyIndex;
            Reason = reason ?? string.Empty;
            Code = code ?? string.Empty;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 성공한 Activation 결과를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public static AbilityActivationResult Success() => new(AbilityActivationFailure.None, -1, string.Empty, string.Empty);

        // ------------------------------------------------------------
        /// <summary>
        /// 실패 결과를 생성하며 None을 실패 사유로 사용하는 요청은 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        public static AbilityActivationResult Reject
        (
            AbilityActivationFailure failure,
            int policyIndex = -1,
            string reason = null,
            string code = null
        )
        {
            if (failure == AbilityActivationFailure.None
                || failure == AbilityActivationFailure.Uninitialized)
            {
                throw new ArgumentOutOfRangeException(nameof(failure));
            }

            return new AbilityActivationResult(failure, policyIndex, reason, code);
        }
    }

    // ================================================================================
    /// <summary>
    /// Owner와 Instigator, 선택된 Binding과 프로젝트 정보를 함께 전달하는 요청 맥락.
    /// </summary>
    // ================================================================================
    public readonly struct AbilityActivationContext<TSpec>
    where TSpec : class
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
        public AbilityBinding<TSpec> Binding { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 입력 Entry가 아니라 실제로 해석된 Ability Instance를 제공한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityInstance<TSpec> Ability => Binding.Instance;

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
            in AbilityBinding<TSpec> binding,
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
}
