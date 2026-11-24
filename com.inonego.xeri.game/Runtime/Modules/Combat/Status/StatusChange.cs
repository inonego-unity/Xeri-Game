/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : StatusChange.cs
수정일 : 2026-11-20

# 설명
Status 적용 결과, 변경 알림 값과 읽기 전용 Status 계약을 정의한다.
StacksRemoved는 Status가 남는 감소를, Removed는 Status가 사라지는 변경을 뜻한다.
========================================================================= BLOCK_HEADER_END */

namespace inonego.Xeri.Game
{
    using Xeri;
    using Xeri.Utility;

    // ============================================================
    /// <summary>
    /// Status 변경 알림의 종류.
    /// </summary>
    // ============================================================
    public enum StatusChangeKind { Added, Stacked, Renewed, StacksRemoved, Removed }

    // ============================================================
    /// <summary>
    /// Status 스택이 줄거나 Status가 사라진 이유.
    /// </summary>
    // ============================================================
    public enum StatusRemoveReason { None, Expired, Consumed, Overflowed, Removed, Cleared }

    // ============================================================
    /// <summary>
    /// StatusContainer가 보내는 변경 한 건.
    /// </summary>
    // ============================================================
    public readonly struct StatusChange<TOrigin>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 변경의 종류.
        /// </summary>
        // ------------------------------------------------------------
        public StatusChangeKind Kind { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 변경된 Status의 Key.
        /// </summary>
        // ------------------------------------------------------------
        public StatusKey Key { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 변경된 Status의 출처.
        /// </summary>
        // ------------------------------------------------------------
        public TOrigin Origin { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 스택 변화량. 늘면 양수, 줄면 음수, 갱신만 했으면 0이다.
        /// </summary>
        // ------------------------------------------------------------
        public int StackDelta { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 변경 뒤 스택 수. Removed면 0이다.
        /// </summary>
        // ------------------------------------------------------------
        public int Stacks { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// StacksRemoved·Removed의 이유. 그 밖의 변경이면 None이다.
        /// </summary>
        // ------------------------------------------------------------
        public StatusRemoveReason Reason { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 변경 한 건의 값을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public StatusChange
        (
            StatusChangeKind kind,
            StatusKey key,
            TOrigin origin,
            int stackDelta,
            int stacks,
            StatusRemoveReason reason
        )
        {
            Kind       = kind;
            Key        = key;
            Origin     = origin;
            StackDelta = stackDelta;
            Stacks     = stacks;
            Reason     = reason;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// StatusContainer가 보유한 Status 하나의 읽기 전용 상태.
    /// </summary>
    // ============================================================
    public interface IReadOnlyStatus<TOrigin>
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 적용 시점에 복사한 Status 규칙.
        /// </summary>
        // ------------------------------------------------------------
        public StatusSpec Spec { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Status의 출처. Shared Status는 가장 최근 적용자다.
        /// </summary>
        // ------------------------------------------------------------
        public TOrigin Origin { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 스택 수.
        /// </summary>
        // ------------------------------------------------------------
        public int Stacks { get; }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 적용 순서상 index번째 스택의 시간 진행을 반환한다.
        /// <br/> 지속 시간이 없는 Status의 스택은 시작되지 않은 Timer를 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public ITimerProgress GetStackTimer(int index);
    }
}
