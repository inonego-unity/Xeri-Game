/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : StatusSpec.cs
수정일 : 2026-11-20

# 설명
Status 한 종류의 스택·지속 시간·출처·초과 규칙을 정의한다.
값 동등성으로 같은 Key의 규칙이 일치하는지 비교하고, 적용 시점의 복사본을 만든다.
동등성은 동작에 쓰이는 값만 비교한다. 지속 시간이 없으면 Duration을, Independent가 아니면 Overflow를 비교하지 않는다.

# 특이사항, 제약사항
Unity 직렬화로 채워진 값은 생성자 검사를 거치지 않으므로 사용 전에 Validate로 검사한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 같은 Status에 스택이 추가될 때의 시간 규칙.
    /// </summary>
    // ============================================================
    public enum StatusStacking { Independent, SharedRenew, SharedKeep }

    // ============================================================
    /// <summary>
    /// 같은 Key의 Status를 출처별로 나눌지 여부.
    /// </summary>
    // ============================================================
    public enum StatusSourcing { PerOrigin, Shared }

    // ============================================================
    /// <summary>
    /// 독립 만료 스택이 최대치에 도달했을 때의 처리.
    /// </summary>
    // ============================================================
    public enum StatusOverflow { Reject, ReplaceEarliest }

    // ============================================================
    /// <summary>
    /// Status 한 종류의 스택·지속 시간·출처·초과 규칙.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class StatusSpec : IEquatable<StatusSpec>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Status 종류를 구분하는 Key.
        /// </summary>
        // ------------------------------------------------------------
        public StatusKey Key => key;

        [SerializeField]
        private StatusKey key;

        // ------------------------------------------------------------
        /// <summary>
        /// 한 Status 인스턴스가 가질 수 있는 최대 스택 수.
        /// </summary>
        // ------------------------------------------------------------
        public int MaxStacks => maxStacks;

        [SerializeField]
        private int maxStacks = 1;

        // ------------------------------------------------------------
        /// <summary>
        /// 시간이 지나면 스택이 만료되는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasDuration => hasDuration;

        [SerializeField]
        private bool hasDuration = false;

        // ------------------------------------------------------------
        /// <summary>
        /// 스택 하나의 지속 시간. HasDuration일 때만 의미가 있다.
        /// </summary>
        // ------------------------------------------------------------
        public float Duration => duration;

        [SerializeField]
        private float duration = 0f;

        // ------------------------------------------------------------
        /// <summary>
        /// 스택이 추가될 때의 시간 규칙.
        /// </summary>
        // ------------------------------------------------------------
        public StatusStacking Stacking => stacking;

        [SerializeField]
        private StatusStacking stacking = StatusStacking.Independent;

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 Key의 Status를 출처별로 나눌지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public StatusSourcing Sourcing => sourcing;

        [SerializeField]
        private StatusSourcing sourcing = StatusSourcing.PerOrigin;

        // ------------------------------------------------------------
        /// <summary>
        /// 독립 만료 스택이 최대치에 도달했을 때의 처리.
        /// </summary>
        // ------------------------------------------------------------
        public StatusOverflow Overflow => overflow;

        [SerializeField]
        private StatusOverflow overflow = StatusOverflow.Reject;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 검사한 규칙으로 Status Spec을 생성한다.
        /// <br/> duration이 null이면 시간으로 만료되지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public StatusSpec
        (
            StatusKey key,
            int maxStacks,
            float? duration,
            StatusStacking stacking,
            StatusSourcing sourcing,
            StatusOverflow overflow = StatusOverflow.Reject
        )
        {
            this.key         = key;
            this.maxStacks   = maxStacks;
            this.hasDuration = duration.HasValue;
            this.duration    = duration ?? 0f;
            this.stacking    = stacking;
            this.sourcing    = sourcing;
            this.overflow    = overflow;

            Validate();
        }

        private StatusSpec(StatusSpec source)
        {
            key         = source.key;
            maxStacks   = source.maxStacks;
            hasDuration = source.hasDuration;
            duration    = source.duration;
            stacking    = source.stacking;
            sourcing    = source.sourcing;
            overflow    = source.overflow;
        }

    #endregion

    #region 검사와 복사

        // ------------------------------------------------------------
        /// <summary>
        /// 규칙 값이 유효하지 않으면 ArgumentException을 던진다.
        /// </summary>
        // ------------------------------------------------------------
        public void Validate()
        {
            if (!key.IsValid)
            {
                throw new ArgumentException("유효한 Status Key가 필요합니다.");
            }

            if (maxStacks < 1)
            {
                throw new ArgumentException("최대 스택 수는 1 이상이어야 합니다.");
            }

            var isDurationValid = !float.IsNaN(duration) && !float.IsInfinity(duration) && duration > 0f;

            if (hasDuration && !isDurationValid)
            {
                throw new ArgumentException("지속 시간은 0보다 큰 유한한 값이어야 합니다.");
            }

            // 적용마다 검사하므로 박싱하는 Enum.IsDefined 대신 값을 직접 비교한다.
            var isStackingDefined = stacking is StatusStacking.Independent or StatusStacking.SharedRenew or StatusStacking.SharedKeep;
            var isSourcingDefined = sourcing is StatusSourcing.PerOrigin or StatusSourcing.Shared;
            var isOverflowDefined = overflow is StatusOverflow.Reject or StatusOverflow.ReplaceEarliest;

            if (!isStackingDefined || !isSourcingDefined || !isOverflowDefined)
            {
                throw new ArgumentException("정의되지 않은 Status 규칙 값입니다.");
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 적용 시점의 규칙을 보존할 독립 복사본을 만든다.
        /// </summary>
        // ------------------------------------------------------------
        internal StatusSpec Copy() => new(this);

    #endregion

    #region 동등성

        // ------------------------------------------------------------
        /// <summary>
        /// 다른 Spec과 동작에 쓰이는 규칙 값이 모두 같은지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Equals(StatusSpec other)
        {
            if (other is null)
            {
                return false;
            }

            return
                key == other.key &&
                maxStacks == other.maxStacks &&
                hasDuration == other.hasDuration &&
                (!hasDuration || duration.Equals(other.duration)) &&
                stacking == other.stacking &&
                sourcing == other.sourcing &&
                (stacking != StatusStacking.Independent || overflow == other.overflow);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 객체가 같은 규칙의 Spec인지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool Equals(object obj) => obj is StatusSpec other && Equals(other);

        // ------------------------------------------------------------
        /// <summary>
        /// 동작에 쓰이는 규칙 값으로 해시 코드를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public override int GetHashCode() => HashCode.Combine
        (
            key, maxStacks, hasDuration, hasDuration ? duration : 0f, stacking, sourcing,
            stacking == StatusStacking.Independent ? overflow : StatusOverflow.Reject
        );

    #endregion

    }
}
