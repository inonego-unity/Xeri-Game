/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionStepStatuses.cs
수정일 : 2026-11-22

# 설명
입력 처리 결과를 받는 호출자 소유 버퍼와 입력별 결과(MotionInputStatus)를 정의한다.

# 특이사항, 제약사항
비운 버퍼에는 결과를 기록하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// 입력별 처리 결과를 받는 호출자 소유 버퍼.
    /// </summary>
    // ============================================================
    public readonly ref struct MotionStepStatuses
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 의도 결과. 입력과 같은 순서다.
        /// </summary>
        // ------------------------------------------------------------
        public Span<MotionInputStatus> Intents { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Modifier 명령 결과. 입력과 같은 순서다.
        /// </summary>
        // ------------------------------------------------------------
        public Span<MotionInputStatus> Modifiers { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 결과 버퍼를 묶는다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionStepStatuses
        (
            Span<MotionInputStatus> intents = default,
            Span<MotionInputStatus> modifiers = default
        )
        {
            Intents = intents;
            Modifiers = modifiers;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 입력 하나의 처리 결과.
    /// </summary>
    // ============================================================
    public enum MotionInputStatus
    {
        Accepted,
        Updated,
        InvalidInput,
        StaleGeneration,
        NotFound,
        CapacityReached
    }
}
