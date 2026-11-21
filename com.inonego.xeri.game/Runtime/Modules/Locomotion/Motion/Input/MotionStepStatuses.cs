/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionStepStatuses.cs
수정일 : 2026-11-21

# 설명
층별 입력 처리 결과를 받는 호출자 소유 버퍼와 입력별 결과(MotionInputStatus)를 정의한다.

# 특이사항, 제약사항
비운 층은 결과를 기록하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// <br/> 층별 입력 처리 결과를 받을 호출자 소유 버퍼.
    /// <br/> 비운 층은 결과를 기록하지 않는다.
    /// </summary>
    // ============================================================
    public readonly ref struct MotionStepStatuses
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 의도 결과.
        /// </summary>
        // ------------------------------------------------------------
        public Span<MotionInputStatus> Intents { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Override 명령 결과.
        /// </summary>
        // ------------------------------------------------------------
        public Span<MotionInputStatus> Overrides { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 제어 배율 명령 결과.
        /// </summary>
        // ------------------------------------------------------------
        public Span<MotionInputStatus> Scales { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 속도 명령 결과.
        /// </summary>
        // ------------------------------------------------------------
        public Span<MotionInputStatus> Externals { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 층별 결과 버퍼를 묶는다. 생략한 층은 기록하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionStepStatuses
        (
            Span<MotionInputStatus> intents = default,
            Span<MotionInputStatus> overrides = default,
            Span<MotionInputStatus> scales = default,
            Span<MotionInputStatus> externals = default
        )
        {
            Intents = intents;
            Overrides = overrides;
            Scales = scales;
            Externals = externals;
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
        InvalidInput,
        StaleGeneration,
        Duplicate,
        NotFound,
        CapacityReached,
        UnknownTrack
    }
}
